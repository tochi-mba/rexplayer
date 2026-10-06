using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.TestKit;

/// <summary>Writes EBML (Matroska and WebM) element by element for demuxer tests.</summary>
public static class EbmlWriter
{
    public static byte[] Element(uint id, params byte[][] parts)
    {
        var body = Concat(parts);
        return Concat(Id(id), Size(body.Length), body);
    }

    /// <summary>An element whose size is "unknown" (all ones), as live streams write clusters.</summary>
    public static byte[] Unsized(uint id, params byte[][] parts) => Concat(Id(id), [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], Concat(parts));

    /// <summary>An element whose body is the given bytes.</summary>
    public static byte[] Raw(uint id, params byte[] body) => Concat(Id(id), Size(body.Length), body);

    /// <summary>An unsigned integer always written in eight bytes, so its element's length never depends on the value.</summary>
    public static byte[] UInt64(uint id, ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return Element(id, bytes);
    }

    public static byte[] UInt(uint id, ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Insert(0, (byte)value);
            value >>= 8;
        }
        while (value != 0);
        return Element(id, [.. bytes]);
    }

    public static byte[] Float(uint id, double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        return Element(id, bytes);
    }

    public static byte[] Float32(uint id, float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return Element(id, bytes);
    }

    public static byte[] Text(uint id, string value) => Element(id, Encoding.UTF8.GetBytes(value));

    public static byte[] Id(uint id)
    {
        var length = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(id >> (8 * (length - 1 - i)));
        }

        return bytes;
    }

    /// <summary>A size as a variable-length integer, in the shortest form or in <paramref name="length"/> bytes.</summary>
    public static byte[] Size(long size, int length = 0)
    {
        if (length == 0)
        {
            length = 1;
            while (size >= (1L << (7 * length)) - 1)
            {
                length++;
            }
        }

        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(size >> (8 * (length - 1 - i)));
        }

        bytes[0] |= (byte)(0x80 >> (length - 1));
        return bytes;
    }

    /// <summary>A block body: track number, relative timestamp, flags and the frames, laced as asked (0 none, 1 Xiph, 2 fixed, 3 EBML).</summary>
    public static byte[] Block(int track, short relative, byte flags, int lacing, params byte[][] frames)
    {
        var header = Concat(Size(track), [(byte)(relative >> 8), (byte)relative, (byte)(flags | (lacing << 1))]);
        if (lacing == 0)
        {
            return Concat(header, Concat(frames));
        }

        var sizes = new List<byte> { (byte)(frames.Length - 1) };
        switch (lacing)
        {
            case 1:
                foreach (var frame in frames[..^1])
                {
                    var size = frame.Length;
                    while (size >= 255)
                    {
                        sizes.Add(255);
                        size -= 255;
                    }

                    sizes.Add((byte)size);
                }

                break;
            case 3:
                sizes.AddRange(Size(frames[0].Length));
                for (var i = 1; i < frames.Length - 1; i++)
                {
                    // Signed difference from the size before, biased by 2^(7n-1)-1 in a 2-byte field.
                    sizes.AddRange(Size(frames[i].Length - frames[i - 1].Length + ((1 << 13) - 1), 2));
                }

                break;
        }

        return Concat(header, [.. sizes], Concat(frames));
    }

    /// <summary>A whole file: the EBML header with the doc type, then a segment around the children.</summary>
    public static byte[] File(string docType, IEnumerable<byte[]> segment, bool unsizedSegment = false)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var header = Element(0x1A45DFA3, UInt(0x4286, 1), UInt(0x42F7, 1), Text(0x4282, docType), UInt(0x4287, 4), UInt(0x4285, 2));
        var body = Concat([.. segment]);
        return unsizedSegment ? Concat(header, Unsized(0x18538067, body)) : Concat(header, Element(0x18538067, body));
    }

    public static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
