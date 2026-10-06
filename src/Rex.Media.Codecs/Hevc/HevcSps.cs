// Spec: ITU-T H.265 (V8, 08/2021) sections 7.3.1.2 (NAL unit header), 7.3.2.2.1 "General sequence parameter set RBSP syntax", 7.3.3 "Profile, tier and level syntax", 7.3.4 "Scaling list data syntax", 7.3.7 "Short-term reference picture set syntax" with 7.4.8 (NumDeltaPocs of a predicted set), 7.4.3.2.1 (conformance window in chroma units, Table 6-1), Annex E section E.2.1 (VUI: default display window, timing info with one tick per picture).
using Rex.Media.Codecs.Video;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Hevc;

/// <summary>Reads an HEVC sequence parameter set (NAL unit type 33).</summary>
public static class HevcSps
{
    public const int NalType = 33;

    /// <summary>The NAL unit type in the first byte of a unit's two-byte header.</summary>
    public static int TypeOf(byte first) => (first >> 1) & 0x3F;

    /// <summary>Reads a whole SPS NAL unit, header included; false when it is not one or is cut short.</summary>
    public static bool TryParse(ReadOnlySpan<byte> unit, out SequenceInfo info)
    {
        info = new SequenceInfo();
        if (unit.Length < 4 || TypeOf(unit[0]) != NalType)
        {
            return false;
        }

        try
        {
            info = Parse(NalUnits.Unescape(unit[2..]));
            return info.Width > 0 && info.Height > 0;
        }
        catch (MediaFormatException)
        {
            // The reader ran off the end: the unit was cut short.
            return false;
        }
    }

    private static SequenceInfo Parse(byte[] rbsp)
    {
        var reader = new BitReader(rbsp);
        reader.SkipBits(4);
        var subLayers = (int)reader.ReadBits(3);
        reader.SkipBits(1);
        var (profile, level) = ProfileTierLevel(ref reader, subLayers);
        reader.ReadUnsignedExpGolomb();
        var chromaFormat = reader.ReadUnsignedExpGolomb(3, "chroma_format_idc");
        if (chromaFormat == 3)
        {
            reader.SkipBits(1);
        }

        long width = reader.ReadUnsignedExpGolomb();
        long height = reader.ReadUnsignedExpGolomb();
        var unitX = chromaFormat is 1 or 2 ? 2 : 1;
        var unitY = chromaFormat == 1 ? 2 : 1;
        if (reader.ReadBit())
        {
            width -= unitX * ((long)reader.ReadUnsignedExpGolomb() + reader.ReadUnsignedExpGolomb());
            height -= unitY * ((long)reader.ReadUnsignedExpGolomb() + reader.ReadUnsignedExpGolomb());
        }

        var bitDepth = 8 + reader.ReadUnsignedExpGolomb(8, "bit_depth_luma_minus8");
        reader.ReadUnsignedExpGolomb();
        var pocLsbBits = reader.ReadUnsignedExpGolomb(12, "log2_max_pic_order_cnt_lsb_minus4") + 4;
        for (var i = reader.ReadBit() ? 0 : subLayers; i <= subLayers; i++)
        {
            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
        }

        for (var i = 0; i < 6; i++)
        {
            reader.ReadUnsignedExpGolomb();
        }

        if (reader.ReadBit() && reader.ReadBit())
        {
            SkipScalingLists(ref reader);
        }

        reader.SkipBits(2);
        if (reader.ReadBit())
        {
            reader.SkipBits(8);
            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
            reader.SkipBits(1);
        }

        SkipReferencePictureSets(ref reader, reader.ReadUnsignedExpGolomb(64, "num_short_term_ref_pic_sets"));
        if (reader.ReadBit())
        {
            var count = reader.ReadUnsignedExpGolomb(32, "num_long_term_ref_pics_sps");
            for (var i = 0; i < count; i++)
            {
                reader.SkipBits(pocLsbBits + 1);
            }
        }

        reader.SkipBits(2);
        var info = new SequenceInfo
        {
            Profile = profile,
            Level = level,
            Width = (int)Math.Clamp(width, 0, int.MaxValue),
            Height = (int)Math.Clamp(height, 0, int.MaxValue),
            ChromaFormat = chromaFormat,
            BitDepth = bitDepth,
        };
        if (reader.ReadBit())
        {
            info = Vui.ReadCommon(ref reader, info);
            reader.SkipBits(1);
            var fieldSequence = reader.ReadBit();
            reader.SkipBits(1);
            if (reader.ReadBit())
            {
                for (var i = 0; i < 4; i++)
                {
                    reader.ReadUnsignedExpGolomb();
                }
            }

            info = info with { Interlaced = fieldSequence };
            if (reader.ReadBit())
            {
                var unitsInTick = reader.ReadBits(32);
                var timeScale = reader.ReadBits(32);
                info = info with { FrameRate = Vui.Rate(unitsInTick, timeScale, 1) };
            }
        }

        return info;
    }

    /// <summary>profile_tier_level with profilePresentFlag set: the general profile and level, then each sub-layer's.</summary>
    internal static (int Profile, int Level) ProfileTierLevel(ref BitReader reader, int subLayers)
    {
        reader.SkipBits(3);
        var profile = (int)reader.ReadBits(5);
        reader.SkipBits(32 + 48);
        var level = (int)reader.ReadBits(8);
        var profilePresent = new bool[subLayers];
        var levelPresent = new bool[subLayers];
        for (var i = 0; i < subLayers; i++)
        {
            profilePresent[i] = reader.ReadBit();
            levelPresent[i] = reader.ReadBit();
        }

        if (subLayers > 0)
        {
            reader.SkipBits(2 * (8 - subLayers));
        }

        for (var i = 0; i < subLayers; i++)
        {
            reader.SkipBits((profilePresent[i] ? 88 : 0) + (levelPresent[i] ? 8 : 0));
        }

        return (profile, level);
    }

    private static void SkipScalingLists(ref BitReader reader)
    {
        for (var size = 0; size < 4; size++)
        {
            for (var matrix = 0; matrix < 6; matrix += size == 3 ? 3 : 1)
            {
                if (!reader.ReadBit())
                {
                    reader.ReadUnsignedExpGolomb();
                    continue;
                }

                var coefficients = Math.Min(64, 1 << (4 + (size << 1)));
                if (size > 1)
                {
                    reader.ReadSignedExpGolomb();
                }

                for (var i = 0; i < coefficients; i++)
                {
                    reader.ReadSignedExpGolomb();
                }
            }
        }
    }

    /// <summary>
    /// Steps over the SPS's short-term reference picture sets. A set predicted from the one before
    /// has a flag pair per picture of that set, so each set's picture count is tracked.
    /// </summary>
    private static void SkipReferencePictureSets(ref BitReader reader, int count)
    {
        var pictures = new int[count];
        for (var set = 0; set < count; set++)
        {
            if (set > 0 && reader.ReadBit())
            {
                reader.SkipBits(1);
                reader.ReadUnsignedExpGolomb();
                var used = 0;
                for (var j = 0; j <= pictures[set - 1]; j++)
                {
                    if (reader.ReadBit() || reader.ReadBit())
                    {
                        used++;
                    }
                }

                pictures[set] = used;
                continue;
            }

            var negative = reader.ReadUnsignedExpGolomb(16, "num_negative_pics");
            var positive = reader.ReadUnsignedExpGolomb(16, "num_positive_pics");
            for (var i = 0; i < negative + positive; i++)
            {
                reader.ReadUnsignedExpGolomb();
                reader.SkipBits(1);
            }

            pictures[set] = negative + positive;
        }
    }
}
