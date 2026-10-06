// Spec: ISO/IEC 14496-3 (MPEG-4 Audio) subpart 1 sections 1.6.2.1 "AudioSpecificConfig", 1.6.3.3 and 1.6.3.4 (sampling frequency index and channel configuration tables), 1.6.5 (implicit and explicit SBR and PS signalling), 4.4.1.1 "GASpecificConfig", 4.4.1.2 "program_config_element", 4.5.1.1 (frame length).
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Aac;

/// <summary>
/// What an AAC stream's AudioSpecificConfig says: the codec layer underneath (LC, Main, LTP...), its
/// rate and channels, and whether spectral band replication or parametric stereo sit on top, which
/// double the output rate and turn one coded channel into two.
/// </summary>
public sealed record AacConfig
{
    public static readonly int[] SampleRates = [96_000, 88_200, 64_000, 48_000, 44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350];

    /// <summary>The core audio object type: 1 Main, 2 LC, 3 SSR, 4 LTP and so on.</summary>
    public int ObjectType { get; init; }

    /// <summary>The core coder's rate.</summary>
    public int SampleRate { get; init; }

    /// <summary>Coded channels; 0 when the configuration is unknown.</summary>
    public int Channels { get; init; }

    /// <summary>Spectral band replication (HE-AAC) is signalled.</summary>
    public bool Sbr { get; init; }

    /// <summary>Parametric stereo (HE-AAC v2) is signalled.</summary>
    public bool Ps { get; init; }

    /// <summary>The rate SBR raises the output to, or the core rate without SBR.</summary>
    public int OutputSampleRate { get; init; }

    /// <summary>Samples per channel in one core frame: 1024, or 960 for the short-frame variant.</summary>
    public int FrameLength { get; init; } = 1024;

    /// <summary>The channels a decoder produces: parametric stereo turns mono into stereo.</summary>
    public int OutputChannels => Ps && Channels == 1 ? 2 : Channels;

    /// <summary>Reads an AudioSpecificConfig; false when it is cut short or not AAC at all.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out AacConfig config)
    {
        try
        {
            config = Parse(data) ?? new AacConfig();
        }
        catch (MediaFormatException)
        {
            // The configuration ended inside a field.
            config = new AacConfig();
        }

        return config.SampleRate > 0;
    }

    private static AacConfig? Parse(ReadOnlySpan<byte> data)
    {
        var reader = new BitReader(data);
        if (reader.BitsRemaining < 16)
        {
            return null;
        }

        var objectType = ObjectTypeOf(ref reader);
        var rate = RateOf(ref reader);
        var channelConfiguration = (int)reader.ReadBits(4);
        var sbr = false;
        var ps = false;
        var outputRate = 0;
        if (objectType is 5 or 29)
        {
            sbr = true;
            ps = objectType == 29;
            outputRate = RateOf(ref reader);
            objectType = ObjectTypeOf(ref reader);
        }

        if (objectType is not (1 or 2 or 3 or 4 or 6 or 7 or 17 or 19 or 20 or 21 or 22 or 23) || rate <= 0)
        {
            return null;
        }

        var frameLength = reader.ReadBit() ? 960 : 1024;
        if (reader.ReadBit())
        {
            reader.SkipBits(14);
        }

        var extension = reader.ReadBit();
        var channels = channelConfiguration switch
        {
            0 => ProgramChannels(ref reader),
            >= 1 and <= 6 => channelConfiguration,
            7 => 8,
            11 => 7,
            12 or 14 => 8,
            _ => 0,
        };
        if (objectType is 6 or 20)
        {
            reader.SkipBits(3);
        }

        if (extension)
        {
            if (objectType == 22)
            {
                reader.SkipBits(16);
            }

            if (objectType is 17 or 19 or 20 or 23)
            {
                reader.SkipBits(3);
            }

            reader.SkipBits(1);
        }

        // Backward-compatible signalling: an LC config followed by a sync extension announcing SBR.
        if (!sbr && reader.BitsRemaining >= 16 && reader.ReadBits(11) == 0x2B7 && ObjectTypeOf(ref reader) == 5 && reader.BitsRemaining >= 1 && reader.ReadBit())
        {
            sbr = true;
            outputRate = RateOf(ref reader);
            ps = reader.BitsRemaining >= 12 && reader.ReadBits(11) == 0x548 && reader.ReadBit();
        }

        return new AacConfig
        {
            ObjectType = objectType,
            SampleRate = rate,
            Channels = channels,
            Sbr = sbr,
            Ps = ps,
            OutputSampleRate = sbr ? (outputRate > 0 ? outputRate : rate * 2) : rate,
            FrameLength = frameLength,
        };
    }

    /// <summary>A two-byte AudioSpecificConfig for a plain core stream, as an ADTS header describes one.</summary>
    public static byte[] Build(int objectType, int sampleRate, int channelConfiguration)
    {
        var index = Array.IndexOf(SampleRates, sampleRate);
        if (objectType is < 1 or > 30 || index < 0 || channelConfiguration is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Only the standard rates, object types and channel configurations fit a short configuration.");
        }

        var bits = (objectType << 11) | (index << 7) | (channelConfiguration << 3);
        return [(byte)(bits >> 8), (byte)bits];
    }

    private static int ObjectTypeOf(ref BitReader reader)
    {
        var type = (int)reader.ReadBits(5);
        return type == 31 ? 32 + (int)reader.ReadBits(6) : type;
    }

    private static int RateOf(ref BitReader reader)
    {
        var index = (int)reader.ReadBits(4);
        return index == 15 ? (int)reader.ReadBits(24) : index < SampleRates.Length ? SampleRates[index] : 0;
    }

    /// <summary>Counts the channels a program_config_element lays out, leaving the reader after it.</summary>
    private static int ProgramChannels(ref BitReader reader)
    {
        reader.SkipBits(4 + 2 + 4);
        var front = (int)reader.ReadBits(4);
        var side = (int)reader.ReadBits(4);
        var back = (int)reader.ReadBits(4);
        var lfe = (int)reader.ReadBits(2);
        var associated = (int)reader.ReadBits(3);
        var coupling = (int)reader.ReadBits(4);
        for (var i = 0; i < 3; i++)
        {
            // Mono, stereo and matrix mixdowns: a flag, then an element tag (or index and flag).
            if (reader.ReadBit())
            {
                reader.SkipBits(i == 2 ? 3 : 4);
            }
        }

        var channels = lfe;
        for (var i = 0; i < front + side + back; i++)
        {
            channels += reader.ReadBit() ? 2 : 1;
            reader.SkipBits(4);
        }

        reader.SkipBits((lfe * 4) + (associated * 4) + (coupling * 5));
        reader.AlignToByte();
        reader.SkipBits(8 * reader.ReadBits(8));
        return channels;
    }
}
