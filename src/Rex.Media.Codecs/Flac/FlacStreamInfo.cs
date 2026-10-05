// Spec: IETF RFC 9639 (FLAC), section 8.2 "Streaminfo".
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Flac;

/// <summary>The STREAMINFO block: the stream's fixed parameters, which every FLAC stream starts with.</summary>
public sealed record FlacStreamInfo(
    int MinBlockSize,
    int MaxBlockSize,
    int MinFrameSize,
    int MaxFrameSize,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalSamples,
    byte[] Md5)
{
    public const int Size = 34;

    /// <summary>Whether every frame (but the last) has the same number of samples.</summary>
    public bool FixedBlockSize => MinBlockSize == MaxBlockSize;

    /// <summary>The speaker order RFC 9639 section 9.1.3 assigns to each channel count.</summary>
    public static ChannelLayout Layout(int channels) => channels switch
    {
        5 => ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.BackLeft | ChannelLayout.BackRight,
        6 => ChannelLayout.Surround51Back,
        _ => ChannelLayouts.Default(channels),
    };

    public static FlacStreamInfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new MediaFormatException("The FLAC STREAMINFO block is shorter than 34 bytes.");
        }

        var reader = new BitReader(data);
        var minBlock = (int)reader.ReadBits(16);
        var maxBlock = (int)reader.ReadBits(16);
        var minFrame = (int)reader.ReadBits(24);
        var maxFrame = (int)reader.ReadBits(24);
        var rate = (int)reader.ReadBits(20);
        var channels = (int)reader.ReadBits(3) + 1;
        var bits = (int)reader.ReadBits(5) + 1;
        var total = (long)reader.ReadBits64(36);
        // A block size under 16 is forbidden (RFC 9639 section 8.2) and a FLAC stream needs at least
        // 4 bits per sample; a rate of zero is only allowed for streams with no audio to play.
        if (minBlock < 16 || maxBlock < minBlock || rate == 0 || bits < 4)
        {
            throw new MediaFormatException("The FLAC STREAMINFO block describes an impossible stream.");
        }

        return new FlacStreamInfo(minBlock, maxBlock, minFrame, maxFrame, rate, channels, bits, total, data.Slice(18, 16).ToArray());
    }

    /// <summary>The 34 bytes of this block, as an encoder writes them.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Size];
        var writer = new BitWriter(bytes);
        writer.Write((uint)MinBlockSize, 16);
        writer.Write((uint)MaxBlockSize, 16);
        writer.Write((uint)MinFrameSize, 24);
        writer.Write((uint)MaxFrameSize, 24);
        writer.Write((uint)SampleRate, 20);
        writer.Write((uint)(Channels - 1), 3);
        writer.Write((uint)(BitsPerSample - 1), 5);
        writer.Write64((ulong)TotalSamples, 36);
        Md5.AsSpan(0, 16).CopyTo(bytes.AsSpan(18));
        return bytes;
    }
}
