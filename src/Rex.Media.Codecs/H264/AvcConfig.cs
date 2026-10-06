// Spec: ISO/IEC 14496-15 section 5.3.3.1 "AVCDecoderConfigurationRecord" (configurationVersion, profile and level indications, lengthSizeMinusOne, the sequence and picture parameter set arrays).
using Rex.Media.Codecs.Video;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.H264;

/// <summary>
/// An avcC record, H.264's codec private data in MP4 and Matroska: how long each NAL unit's length
/// prefix is, and the parameter sets a decoder needs before the first frame.
/// </summary>
public sealed record AvcConfig
{
    public int Profile { get; init; }

    public int Level { get; init; }

    /// <summary>The bytes before each NAL unit in a sample: 1, 2 or 4.</summary>
    public int LengthSize { get; init; } = 4;

    public IReadOnlyList<byte[]> SequenceParameterSets { get; init; } = [];

    public IReadOnlyList<byte[]> PictureParameterSets { get; init; } = [];

    /// <summary>Every parameter set as an Annex B byte stream, SPS first, to put in front of a keyframe.</summary>
    public byte[] ParameterSetsAnnexB() => NalUnits.JoinAnnexB([.. SequenceParameterSets, .. PictureParameterSets]);

    /// <summary>What the first sequence parameter set says, or null when there is none it can read.</summary>
    public SequenceInfo? Sequence() => SequenceParameterSets.Count > 0 && H264Sps.TryParse(SequenceParameterSets[0], out var info) ? info : null;

    /// <summary>Reads an avcC record; throws when it is malformed.</summary>
    public static AvcConfig Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7 || data[0] != 1)
        {
            throw new MediaFormatException("The H.264 configuration record is not version 1.");
        }

        var lengthSize = (data[4] & 3) + 1;
        if (lengthSize == 3)
        {
            throw new MediaFormatException("The H.264 configuration record gives a 3-byte NAL unit length.");
        }

        var position = 5;
        var sequence = ReadSets(data, ref position, data[position++] & 0x1F);
        if (position >= data.Length)
        {
            throw new MediaFormatException("The H.264 configuration record ends before its picture parameter sets.");
        }

        var picture = ReadSets(data, ref position, data[position++]);
        return new AvcConfig
        {
            Profile = data[1],
            Level = data[3],
            LengthSize = lengthSize,
            SequenceParameterSets = sequence,
            PictureParameterSets = picture,
        };
    }

    private static List<byte[]> ReadSets(ReadOnlySpan<byte> data, ref int position, int count)
    {
        var sets = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            if (position + 2 > data.Length)
            {
                throw new MediaFormatException("The H.264 configuration record ends inside a parameter set.");
            }

            var length = (data[position] << 8) | data[position + 1];
            position += 2;
            if (position + length > data.Length)
            {
                throw new MediaFormatException("The H.264 configuration record ends inside a parameter set.");
            }

            sets.Add(data.Slice(position, length).ToArray());
            position += length;
        }

        return sets;
    }
}
