// Spec: Microsoft Multimedia Programming Interface and Data Specifications 1.0 (WAVEFORMATEX format tags); WAVEFORMATEXTENSIBLE (Microsoft Windows SDK documentation, mmreg.h / ksmedia.h subtypes).
using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

/// <summary>A parsed WAVEFORMATEX / WAVEFORMATEXTENSIBLE, the format block WAV and AVI share.</summary>
internal sealed record WaveFormat(
    ushort FormatTag,
    int Channels,
    int SampleRate,
    int AverageBytesPerSecond,
    int BlockAlign,
    int BitsPerSample,
    int ValidBitsPerSample,
    ChannelLayout ChannelMask,
    byte[] Extra)
{
    public const ushort Pcm = 0x0001;
    public const ushort AdpcmMs = 0x0002;
    public const ushort IeeeFloat = 0x0003;
    public const ushort Alaw = 0x0006;
    public const ushort Mulaw = 0x0007;
    public const ushort AdpcmIma = 0x0011;
    public const ushort Mpeg = 0x0050;
    public const ushort MpegLayer3 = 0x0055;
    public const ushort Aac = 0x00FF;
    public const ushort Wma = 0x0161;
    public const ushort WmaPro = 0x0162;
    public const ushort WmaLossless = 0x0163;
    public const ushort AacLatm = 0x1602;
    public const ushort Ac3 = 0x2000;
    public const ushort Dts = 0x2001;
    public const ushort Extensible = 0xFFFE;

    /// <summary>Parses a format block, resolving an extensible block to its real subformat.</summary>
    public static WaveFormat Parse(ReadOnlySpan<byte> data, bool bigEndian)
    {
        if (data.Length < 14)
        {
            throw new MediaFormatException("The audio format block is shorter than its fixed fields.");
        }

        var tag = U16(data, 0, bigEndian);
        var channels = U16(data, 2, bigEndian);
        var sampleRate = U32(data, 4, bigEndian);
        var averageBytes = U32(data, 8, bigEndian);
        var blockAlign = U16(data, 12, bigEndian);
        var bits = data.Length >= 16 ? U16(data, 14, bigEndian) : 0;
        var extraSize = data.Length >= 18 ? Math.Min(U16(data, 16, bigEndian), data.Length - 18) : 0;
        var extra = data.Length >= 18 ? data.Slice(18, extraSize).ToArray() : [];
        var validBits = bits;
        var mask = ChannelLayout.None;

        if (tag == Extensible && extra.Length >= 22)
        {
            validBits = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(extra) : BinaryPrimitives.ReadUInt16LittleEndian(extra);
            mask = (ChannelLayout)(bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(extra.AsSpan(2)) : BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(2)));
            // The subformat GUID's first two bytes are the format tag it stands for (ksmedia.h).
            tag = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(6));
            extra = extra.Length > 22 ? extra[22..] : [];
        }

        if (channels == 0 || channels > AudioFormat.MaxChannels || sampleRate == 0 || sampleRate > 768_000)
        {
            throw new MediaFormatException("The audio format declares an impossible channel count or sample rate.");
        }

        return new WaveFormat(tag, channels, (int)sampleRate, (int)Math.Min(averageBytes, int.MaxValue), blockAlign, bits, validBits == 0 ? bits : validBits, mask, extra);
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static uint U32(ReadOnlySpan<byte> data, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public CodecId Codec => FormatTag switch
    {
        Pcm or IeeeFloat => CodecId.Pcm,
        Alaw => CodecId.Alaw,
        Mulaw => CodecId.Mulaw,
        AdpcmIma => CodecId.AdpcmIma,
        AdpcmMs => CodecId.AdpcmMs,
        Mpeg => CodecId.Mp2,
        MpegLayer3 => CodecId.Mp3,
        Aac or AacLatm => CodecId.Aac,
        Wma => CodecId.Wma,
        WmaPro => CodecId.WmaPro,
        WmaLossless => CodecId.WmaLossless,
        Ac3 => CodecId.Ac3,
        Dts => CodecId.Dts,
        _ => CodecId.Unknown,
    };

    /// <summary>How PCM samples are stored, judged by the container size each sample occupies.</summary>
    public SampleFormat PcmFormat
    {
        get
        {
            if (Codec != CodecId.Pcm || Channels == 0 || BlockAlign == 0)
            {
                return SampleFormat.Unknown;
            }

            var containerBits = BlockAlign * 8 / Channels;
            if (FormatTag == IeeeFloat)
            {
                return containerBits switch
                {
                    32 => SampleFormat.F32,
                    64 => SampleFormat.F64,
                    _ => SampleFormat.Unknown,
                };
            }

            return containerBits switch
            {
                8 => SampleFormat.U8,
                16 => SampleFormat.S16,
                24 => SampleFormat.S24,
                32 => SampleFormat.S32,
                _ => SampleFormat.Unknown,
            };
        }
    }

    /// <summary>Samples per channel in one block: 1 for PCM, read from the extra bytes for ADPCM.</summary>
    public int SamplesPerBlock
    {
        get
        {
            switch (FormatTag)
            {
                case AdpcmIma when Extra.Length >= 2:
                case AdpcmMs when Extra.Length >= 2:
                    return BinaryPrimitives.ReadUInt16LittleEndian(Extra);
                case AdpcmIma when Channels > 0 && BlockAlign >= 4 * Channels:
                    return (((BlockAlign - (4 * Channels)) * 8) / (4 * Channels)) + 1;
                default:
                    return 1;
            }
        }
    }

    public TrackInfo ToTrack(int id, MediaTime duration)
    {
        var codec = Codec;
        return new TrackInfo
        {
            Id = id,
            Codec = codec,
            CodecPrivate = Extra,
            Duration = duration,
            BitRate = AverageBytesPerSecond > 0 ? AverageBytesPerSecond * 8L : null,
            Audio = new AudioTrackInfo
            {
                SampleRate = SampleRate,
                Channels = Channels,
                Layout = ChannelMask != ChannelLayout.None && ChannelMask.ChannelCount() == Channels ? ChannelMask : ChannelLayouts.Default(Channels),
                BitsPerSample = ValidBitsPerSample,
                PcmFormat = PcmFormat,
                BlockAlign = BlockAlign,
                SamplesPerBlock = SamplesPerBlock,
            },
        };
    }
}
