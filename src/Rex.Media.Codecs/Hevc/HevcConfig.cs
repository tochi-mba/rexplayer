// Spec: ISO/IEC 14496-15 section 8.3.3.1 "HEVCDecoderConfigurationRecord" (the 23-byte fixed part with general_profile_idc, general_level_idc and lengthSizeMinusOne, then arrays of VPS, SPS, PPS and SEI NAL units).
using Rex.Media.Codecs.Video;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Hevc;

/// <summary>An hvcC record, HEVC's codec private data in MP4 and Matroska.</summary>
public sealed record HevcConfig
{
    public int Profile { get; init; }

    public int Level { get; init; }

    /// <summary>The bytes before each NAL unit in a sample: 1, 2 or 4.</summary>
    public int LengthSize { get; init; } = 4;

    /// <summary>The record's NAL units in order: parameter sets (VPS, SPS, PPS) and any SEI.</summary>
    public IReadOnlyList<byte[]> Units { get; init; } = [];

    /// <summary>Every unit as an Annex B byte stream, to put in front of a keyframe.</summary>
    public byte[] ParameterSetsAnnexB() => NalUnits.JoinAnnexB(Units);

    /// <summary>What the first sequence parameter set says, or null when there is none it can read.</summary>
    public SequenceInfo? Sequence()
    {
        foreach (var unit in Units)
        {
            if (unit.Length > 0 && HevcSps.TypeOf(unit[0]) == HevcSps.NalType)
            {
                return HevcSps.TryParse(unit, out var info) ? info : null;
            }
        }

        return null;
    }

    /// <summary>Reads an hvcC record; throws when it is malformed.</summary>
    public static HevcConfig Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 23 || data[0] != 1)
        {
            throw new MediaFormatException("The HEVC configuration record is not version 1.");
        }

        var lengthSize = (data[21] & 3) + 1;
        if (lengthSize == 3)
        {
            throw new MediaFormatException("The HEVC configuration record gives a 3-byte NAL unit length.");
        }

        var units = new List<byte[]>();
        var position = 23;
        for (var array = 0; array < data[22]; array++)
        {
            if (position + 3 > data.Length)
            {
                throw new MediaFormatException("The HEVC configuration record ends inside its arrays.");
            }

            var count = (data[position + 1] << 8) | data[position + 2];
            position += 3;
            for (var i = 0; i < count; i++)
            {
                var length = position + 2 <= data.Length ? (data[position] << 8) | data[position + 1] : int.MaxValue;
                if (length > data.Length - position - 2)
                {
                    throw new MediaFormatException("The HEVC configuration record ends inside a NAL unit.");
                }

                units.Add(data.Slice(position + 2, length).ToArray());
                position += 2 + length;
            }
        }

        return new HevcConfig
        {
            Profile = data[1] & 0x1F,
            Level = data[12],
            LengthSize = lengthSize,
            Units = units,
        };
    }
}
