// Spec: ITU-T H.264 (08/2021) sections 7.3.2.1.1 "Sequence parameter set data syntax", 7.3.2.1.1.1 "Scaling list syntax", 7.4.2.1.1 (semantics: ChromaArrayType, SubWidthC and SubHeightC from Table 6-1, frame cropping in CropUnitX and CropUnitY), Annex E section E.1.1 (VUI, timing_info: two ticks per frame).
using Rex.Media.Codecs.Video;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.H264;

/// <summary>Reads an H.264 sequence parameter set (NAL unit type 7).</summary>
public static class H264Sps
{
    public const int NalType = 7;

    /// <summary>The profiles whose SPS carries chroma format, bit depths and scaling matrices.</summary>
    private static readonly int[] HighProfiles = [100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135];

    /// <summary>Reads a whole SPS NAL unit, header byte included; false when it is not one or is cut short.</summary>
    public static bool TryParse(ReadOnlySpan<byte> unit, out SequenceInfo info)
    {
        info = new SequenceInfo();
        if (unit.Length < 4 || (unit[0] & 0x1F) != NalType)
        {
            return false;
        }

        try
        {
            info = Parse(NalUnits.Unescape(unit[1..]));
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
        var profile = (int)reader.ReadBits(8);
        reader.SkipBits(8);
        var level = (int)reader.ReadBits(8);
        reader.ReadUnsignedExpGolomb();
        var chromaFormat = 1;
        var separatePlanes = false;
        var bitDepth = 8;
        if (HighProfiles.Contains(profile))
        {
            chromaFormat = reader.ReadUnsignedExpGolomb(3, "chroma_format_idc");
            if (chromaFormat == 3)
            {
                separatePlanes = reader.ReadBit();
            }

            bitDepth = 8 + reader.ReadUnsignedExpGolomb(6, "bit_depth_luma_minus8");
            reader.ReadUnsignedExpGolomb();
            reader.SkipBits(1);
            if (reader.ReadBit())
            {
                for (var i = 0; i < (chromaFormat != 3 ? 8 : 12); i++)
                {
                    if (reader.ReadBit())
                    {
                        SkipScalingList(ref reader, i < 6 ? 16 : 64);
                    }
                }
            }
        }

        reader.ReadUnsignedExpGolomb();
        var pocType = reader.ReadUnsignedExpGolomb();
        if (pocType == 0)
        {
            reader.ReadUnsignedExpGolomb();
        }
        else if (pocType == 1)
        {
            reader.SkipBits(1);
            reader.ReadSignedExpGolomb();
            reader.ReadSignedExpGolomb();
            var cycle = reader.ReadUnsignedExpGolomb(255, "num_ref_frames_in_pic_order_cnt_cycle");
            for (var i = 0; i < cycle; i++)
            {
                reader.ReadSignedExpGolomb();
            }
        }

        reader.ReadUnsignedExpGolomb();
        reader.SkipBits(1);
        var widthInMbs = (long)reader.ReadUnsignedExpGolomb() + 1;
        var heightInMapUnits = (long)reader.ReadUnsignedExpGolomb() + 1;
        var frameMbsOnly = reader.ReadBit();
        if (!frameMbsOnly)
        {
            reader.SkipBits(1);
        }

        reader.SkipBits(1);
        long cropX = 0, cropY = 0;
        var fieldFactor = frameMbsOnly ? 1 : 2;
        if (reader.ReadBit())
        {
            var chromaArrayType = separatePlanes ? 0 : chromaFormat;
            var unitX = chromaArrayType is 1 or 2 ? 2 : 1;
            var unitY = (chromaArrayType == 1 ? 2 : 1) * fieldFactor;
            cropX = unitX * ((long)reader.ReadUnsignedExpGolomb() + reader.ReadUnsignedExpGolomb());
            cropY = unitY * ((long)reader.ReadUnsignedExpGolomb() + reader.ReadUnsignedExpGolomb());
        }

        var info = new SequenceInfo
        {
            Profile = profile,
            Level = level,
            Width = (int)Math.Clamp((widthInMbs * 16) - cropX, 0, int.MaxValue),
            Height = (int)Math.Clamp((heightInMapUnits * 16 * fieldFactor) - cropY, 0, int.MaxValue),
            ChromaFormat = chromaFormat,
            BitDepth = bitDepth,
            Interlaced = !frameMbsOnly,
        };
        if (reader.ReadBit())
        {
            info = Vui.ReadCommon(ref reader, info);
            if (reader.ReadBit())
            {
                var unitsInTick = reader.ReadBits(32);
                var timeScale = reader.ReadBits(32);
                info = info with { FrameRate = Vui.Rate(unitsInTick, timeScale, 2) };
            }
        }

        return info;
    }

    private static void SkipScalingList(ref BitReader reader, int size)
    {
        var last = 8;
        var next = 8;
        for (var j = 0; j < size && next != 0; j++)
        {
            next = (last + reader.ReadSignedExpGolomb() + 256) % 256;
            last = next == 0 ? last : next;
        }
    }
}
