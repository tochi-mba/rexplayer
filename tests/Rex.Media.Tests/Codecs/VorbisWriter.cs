namespace Rex.Media.Tests.Codecs;

/// <summary>Writes bits least significant first, as Vorbis packs them, for crafting streams in tests.</summary>
internal sealed class VorbisWriter
{
    private readonly List<byte> _bytes = [];
    private int _bit;

    public VorbisWriter Bits(long value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_bit == 0)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                _bytes[^1] |= (byte)(1 << _bit);
            }

            _bit = (_bit + 1) & 7;
        }

        return this;
    }

    public VorbisWriter Flag(bool value) => Bits(value ? 1 : 0, 1);

    /// <summary>A header's packet type and the word "vorbis".</summary>
    public VorbisWriter Header(int type)
    {
        Bits(type, 8);
        foreach (var c in "vorbis")
        {
            Bits(c, 8);
        }

        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    /// <summary>The identification header: version 0, then the channels, rate and block sizes as powers of two.</summary>
    public static byte[] Identification(int channels = 1, int sampleRate = 8000, int shortExponent = 6, int longExponent = 8, int version = 0, bool framing = true) =>
        new VorbisWriter().Header(1).Bits(version, 32).Bits(channels, 8).Bits(sampleRate, 32).Bits(0, 32).Bits(0, 32).Bits(0, 32)
            .Bits(shortExponent, 4).Bits(longExponent, 4).Flag(framing).ToArray();

    public static byte[] Comment() => new VorbisWriter().Header(3).Bits(0, 32).Bits(0, 32).Flag(true).ToArray();

    /// <summary>The three headers in Xiph lacing, as Matroska's CodecPrivate carries them.</summary>
    public static byte[] Laced(params byte[][] packets)
    {
        var laced = new List<byte> { (byte)(packets.Length - 1) };
        foreach (var packet in packets[..^1])
        {
            var size = packet.Length;
            while (size >= 255)
            {
                laced.Add(255);
                size -= 255;
            }

            laced.Add((byte)size);
        }

        foreach (var packet in packets)
        {
            laced.AddRange(packet);
        }

        return [.. laced];
    }

    /// <summary>A codebook of <paramref name="lengths"/> (0 for unused), unordered, without a value lookup unless one is written after.</summary>
    public VorbisWriter Codebook(int dimensions, int[] lengths, int lookupType = 0)
    {
        Bits(0x564342, 24).Bits(dimensions, 16).Bits(lengths.Length, 24).Flag(false);
        var sparse = lengths.Any(length => length == 0);
        Flag(sparse);
        foreach (var length in lengths)
        {
            if (sparse)
            {
                Flag(length > 0);
                if (length == 0)
                {
                    continue;
                }
            }

            Bits(length - 1, 5);
        }

        return Bits(lookupType, 4);
    }
}
