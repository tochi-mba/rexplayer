// Spec: ITU-T H.264 Annex E section E.1.1 and ITU-T H.265 Annex E section E.2.1 (the VUI fields both share: aspect_ratio_info, overscan_info, video_signal_type with colour_description, chroma_loc_info), Table E-1 (sample aspect ratio indicator); ITU-T H.273 for the colour code points.
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Video;

/// <summary>What a sequence parameter set says about the pictures that follow it.</summary>
public sealed record SequenceInfo
{
    public int Profile { get; init; }

    public int Level { get; init; }

    /// <summary>The displayed size: the coded size less the cropping (H.264) or conformance window (HEVC).</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>0 monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</summary>
    public int ChromaFormat { get; init; } = 1;

    public int BitDepth { get; init; } = 8;

    /// <summary>The pictures may be coded as fields (H.264 frame_mbs_only_flag clear) or are fields (HEVC field_seq_flag).</summary>
    public bool Interlaced { get; init; }

    public Rational PixelAspect { get; init; } = new(1, 1);

    public ColorInfo Color { get; init; } = ColorInfo.Unspecified;

    /// <summary>
    /// The picture rate from the timing information, or null when the stream does not say. In an
    /// HEVC field sequence each picture is one field.
    /// </summary>
    public Rational? FrameRate { get; init; }
}

/// <summary>The video usability information fields H.264 and HEVC write identically.</summary>
internal static class Vui
{
    private static readonly (int Width, int Height)[] AspectRatios =
    [
        (0, 0), (1, 1), (12, 11), (10, 11), (16, 11), (40, 33), (24, 11), (20, 11), (32, 11),
        (80, 33), (18, 11), (15, 11), (64, 33), (160, 99), (4, 3), (3, 2), (2, 1),
    ];

    /// <summary>Reads from aspect_ratio_info_present_flag up to and including chroma_loc_info.</summary>
    public static SequenceInfo ReadCommon(ref BitReader reader, SequenceInfo info)
    {
        if (reader.ReadBit())
        {
            var index = (int)reader.ReadBits(8);
            var (width, height) = index == 255
                ? ((int)reader.ReadBits(16), (int)reader.ReadBits(16))
                : index < AspectRatios.Length ? AspectRatios[index] : (0, 0);
            if (width > 0 && height > 0)
            {
                info = info with { PixelAspect = new Rational(width, height) };
            }
        }

        if (reader.ReadBit())
        {
            reader.SkipBits(1);
        }

        if (reader.ReadBit())
        {
            reader.SkipBits(3);
            var fullRange = reader.ReadBit();
            int primaries = 2, transfer = 2, matrix = 2;
            if (reader.ReadBit())
            {
                primaries = (int)reader.ReadBits(8);
                transfer = (int)reader.ReadBits(8);
                matrix = (int)reader.ReadBits(8);
            }

            info = info with { Color = ColorInfo.FromCodes(primaries, transfer, matrix, fullRange) };
        }

        if (reader.ReadBit())
        {
            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
        }

        return info;
    }

    /// <summary>A frame rate from num_units_in_tick and time_scale, where a frame is <paramref name="ticksPerFrame"/> ticks.</summary>
    public static Rational? Rate(uint unitsInTick, uint timeScale, int ticksPerFrame) =>
        unitsInTick == 0 || timeScale == 0 ? null : new Rational(timeScale, (long)unitsInTick * ticksPerFrame);
}
