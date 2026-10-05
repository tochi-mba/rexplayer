// Spec: IETF RFC 9639 (FLAC), section 9.1 "Frame header".
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Flac;

/// <summary>How a stereo frame stores its two channels (RFC 9639 section 9.1.3).</summary>
public enum FlacChannelMode
{
    /// <summary>Every channel coded on its own.</summary>
    Independent,

    /// <summary>Left, then left minus right.</summary>
    LeftSide,

    /// <summary>Left minus right, then right.</summary>
    SideRight,

    /// <summary>The average of left and right, then left minus right.</summary>
    MidSide,
}

/// <summary>
/// The header in front of every FLAC frame. Demuxers use it to find frame boundaries (a FLAC stream
/// has no length fields, only these self-checking headers), and the decoder reads its block size,
/// channel arrangement and sample size. Zero for <see cref="SampleRate"/> or
/// <see cref="BitsPerSample"/> means "as STREAMINFO says".
/// </summary>
public readonly record struct FlacFrameHeader(
    bool VariableBlockSize,
    int BlockSize,
    int SampleRate,
    FlacChannelMode ChannelMode,
    int Channels,
    int BitsPerSample,
    long CodedNumber,
    int Length)
{
    /// <summary>The longest a header can be: sync and codes, a 7-byte number, 2 + 2 optional bytes and the CRC.</summary>
    public const int MaxLength = 16;

    /// <summary>The first two bytes of any frame: the 14-bit sync code, a reserved zero bit and the blocking strategy.</summary>
    public static bool IsSync(byte first, byte second) => first == 0xFF && (second & 0xFE) == 0xF8;

    /// <summary>
    /// Reads a header at the start of <paramref name="data"/>. False when the bytes are not a valid
    /// header (bad sync, a reserved code, a bad CRC-8) or are too short to tell.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out FlacFrameHeader header)
    {
        header = default;
        if (data.Length < 6 || !IsSync(data[0], data[1]))
        {
            return false;
        }

        var variable = (data[1] & 1) != 0;
        var blockCode = data[2] >> 4;
        var rateCode = data[2] & 0x0F;
        var channelCode = data[3] >> 4;
        var sizeCode = (data[3] >> 1) & 0x07;
        if (blockCode == 0 || rateCode == 15 || channelCode > 10 || sizeCode == 3 || (data[3] & 1) != 0)
        {
            return false;
        }

        var offset = 4;
        if (!TryReadCodedNumber(data, ref offset, variable ? 7 : 6, out var number))
        {
            return false;
        }

        int blockSize;
        switch (blockCode)
        {
            case 6:
                if (offset + 1 > data.Length)
                {
                    return false;
                }

                blockSize = data[offset++] + 1;
                break;
            case 7:
                if (offset + 2 > data.Length)
                {
                    return false;
                }

                blockSize = ((data[offset] << 8) | data[offset + 1]) + 1;
                offset += 2;
                break;
            default:
                blockSize = BlockSizeFromCode(blockCode);
                break;
        }

        int sampleRate;
        switch (rateCode)
        {
            case 12:
                if (offset + 1 > data.Length)
                {
                    return false;
                }

                sampleRate = data[offset++] * 1000;
                break;
            case 13:
            case 14:
                if (offset + 2 > data.Length)
                {
                    return false;
                }

                sampleRate = ((data[offset] << 8) | data[offset + 1]) * (rateCode == 14 ? 10 : 1);
                offset += 2;
                break;
            default:
                sampleRate = SampleRateFromCode(rateCode);
                break;
        }

        if (offset + 1 > data.Length || Crc.Crc8Flac.Compute(data[..offset]) != data[offset])
        {
            return false;
        }

        var mode = channelCode switch
        {
            8 => FlacChannelMode.LeftSide,
            9 => FlacChannelMode.SideRight,
            10 => FlacChannelMode.MidSide,
            _ => FlacChannelMode.Independent,
        };
        var channels = channelCode < 8 ? channelCode + 1 : 2;
        header = new FlacFrameHeader(variable, blockSize, sampleRate, mode, channels, BitsFromCode(sizeCode), (long)number, offset + 1);
        return true;
    }

    /// <summary>The frame's first sample. A fixed-block stream numbers frames, so it needs the stream's block size.</summary>
    public long FirstSample(int streamBlockSize) => VariableBlockSize ? CodedNumber : CodedNumber * streamBlockSize;

    /// <summary>The block size field's meaning for the codes that need no extra bytes.</summary>
    internal static int BlockSizeFromCode(int code) => code switch
    {
        1 => 192,
        >= 2 and <= 5 => 576 << (code - 2),
        _ => 256 << (code - 8),
    };

    internal static int SampleRateFromCode(int code) => code switch
    {
        1 => 88200,
        2 => 176400,
        3 => 192000,
        4 => 8000,
        5 => 16000,
        6 => 22050,
        7 => 24000,
        8 => 32000,
        9 => 44100,
        10 => 48000,
        11 => 96000,
        _ => 0,
    };

    internal static int BitsFromCode(int code) => code switch
    {
        1 => 8,
        2 => 12,
        4 => 16,
        5 => 20,
        6 => 24,
        7 => 32,
        _ => 0,
    };

    /// <summary>
    /// The frame or sample number, stored the way UTF-8 stores a code point but allowed up to 36
    /// bits: a lead byte whose leading ones count the bytes, then continuation bytes of 10xxxxxx.
    /// </summary>
    private static bool TryReadCodedNumber(ReadOnlySpan<byte> data, ref int offset, int maxBytes, out ulong value)
    {
        value = 0;
        var lead = data[offset];
        var length = System.Numerics.BitOperations.LeadingZeroCount((uint)(byte)~lead) - 24;
        if (length == 1 || length > maxBytes)
        {
            return false;
        }

        if (length == 0)
        {
            value = lead;
            offset++;
            return true;
        }

        if (offset + length > data.Length)
        {
            return false;
        }

        // The lead byte keeps 7 - length payload bits; the 7-byte form (0xFE) keeps none.
        value = (ulong)(lead & ((1 << (7 - length)) - 1));
        for (var i = 1; i < length; i++)
        {
            var next = data[offset + i];
            if ((next & 0xC0) != 0x80)
            {
                return false;
            }

            value = (value << 6) | (uint)(next & 0x3F);
        }

        offset += length;
        return true;
    }
}
