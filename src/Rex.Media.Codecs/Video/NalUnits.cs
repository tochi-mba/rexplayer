// Spec: ITU-T H.264 sections 7.3.1 and 7.4.1 (NAL unit syntax, emulation prevention bytes) and Annex B (byte stream format, start codes); ITU-T H.265 sections 7.3.1 and Annex B, which define the same two; ISO/IEC 14496-15 sections 5.3.2 and 8.3.2 (length-prefixed NAL units in a sample).
namespace Rex.Media.Codecs.Video;

/// <summary>
/// The two ways H.264 and HEVC streams are cut into NAL units: start codes (Annex B, as raw .h264
/// files, transport streams and Windows' decoders use) and a big-endian length before each unit
/// (as MP4 and Matroska store them), plus removal of the bytes that stop a payload faking a start code.
/// </summary>
public static class NalUnits
{
    private static readonly byte[] StartCode = [0, 0, 0, 1];

    /// <summary>The NAL units of an Annex B byte stream, without their start codes.</summary>
    public static List<Range> SplitAnnexB(ReadOnlySpan<byte> data)
    {
        var units = new List<Range>();
        var start = -1;
        var i = 0;
        while (i + 3 <= data.Length)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                if (start >= 0)
                {
                    units.Add(new Range(start, TrimZeros(data, start, i)));
                }

                i += 3;
                start = i;
                continue;
            }

            i++;
        }

        if (start >= 0)
        {
            units.Add(new Range(start, data.Length));
        }

        return units;
    }

    /// <summary>
    /// The NAL units of a sample whose units each follow a big-endian length of
    /// <paramref name="lengthSize"/> bytes. A length running past the end is cut there.
    /// </summary>
    public static List<Range> SplitLengthPrefixed(ReadOnlySpan<byte> data, int lengthSize)
    {
        if (lengthSize is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(lengthSize), "NAL unit lengths are 1, 2 or 4 bytes.");
        }

        var units = new List<Range>();
        var position = 0;
        while (position + lengthSize <= data.Length)
        {
            long length = 0;
            for (var i = 0; i < lengthSize; i++)
            {
                length = (length << 8) | data[position + i];
            }

            position += lengthSize;
            var end = (int)Math.Min(position + length, data.Length);
            units.Add(new Range(position, end));
            position = end;
        }

        return units;
    }

    /// <summary>
    /// A length-prefixed sample as an Annex B byte stream, with <paramref name="parameterSets"/>
    /// (already in Annex B form) in front, as a decoder needs them before a keyframe.
    /// </summary>
    public static byte[] ToAnnexB(ReadOnlySpan<byte> sample, int lengthSize, ReadOnlySpan<byte> parameterSets = default)
    {
        var units = SplitLengthPrefixed(sample, lengthSize);
        var size = parameterSets.Length;
        foreach (var unit in units)
        {
            size += StartCode.Length + unit.GetOffsetAndLength(sample.Length).Length;
        }

        var output = new byte[size];
        parameterSets.CopyTo(output);
        var at = parameterSets.Length;
        foreach (var unit in units)
        {
            StartCode.CopyTo(output, at);
            at += StartCode.Length;
            var bytes = sample[unit];
            bytes.CopyTo(output.AsSpan(at));
            at += bytes.Length;
        }

        return output;
    }

    /// <summary>NAL units joined as an Annex B byte stream.</summary>
    public static byte[] JoinAnnexB(IEnumerable<byte[]> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return [.. units.SelectMany(unit => StartCode.Concat(unit))];
    }

    /// <summary>A NAL unit's payload with every emulation prevention byte (the 3 in 00 00 03) removed.</summary>
    public static byte[] Unescape(ReadOnlySpan<byte> unit)
    {
        var output = new byte[unit.Length];
        var length = 0;
        var zeros = 0;
        foreach (var b in unit)
        {
            if (zeros >= 2 && b == 3)
            {
                zeros = 0;
                continue;
            }

            zeros = b == 0 ? zeros + 1 : 0;
            output[length++] = b;
        }

        return output[..length];
    }

    /// <summary>The end of a unit that a four-byte start code follows: its trailing zero bytes belong to the code.</summary>
    private static int TrimZeros(ReadOnlySpan<byte> data, int start, int end)
    {
        while (end > start && data[end - 1] == 0)
        {
            end--;
        }

        return end;
    }
}
