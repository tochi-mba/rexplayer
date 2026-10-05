using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.TestKit;

/// <summary>Builds AIFF and AIFF-C files for demuxer tests.</summary>
public sealed class AiffBuilder
{
    private readonly List<(string Id, byte[] Body)> _chunks = [];
    private readonly string _formType;

    public AiffBuilder(bool compressed = false)
    {
        _formType = compressed ? "AIFC" : "AIFF";
    }

    public AiffBuilder Common(int channels, uint sampleFrames, int bitsPerSample, double sampleRate, string? compression = null)
    {
        var body = new List<byte>();
        body.AddRange(U16(channels));
        body.AddRange(U32(sampleFrames));
        body.AddRange(U16(bitsPerSample));
        body.AddRange(Extended(sampleRate));
        if (compression is not null)
        {
            body.AddRange(Encoding.ASCII.GetBytes(compression));
            body.AddRange([0, 0]);
        }

        return Chunk("COMM", [.. body]);
    }

    public AiffBuilder SoundData(byte[] data, uint offset = 0)
    {
        var body = new byte[8 + offset + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(body, offset);
        data.CopyTo(body, 8 + (int)offset);
        return Chunk("SSND", body);
    }

    public AiffBuilder Text(string id, string text) => Chunk(id, Encoding.Latin1.GetBytes(text));

    public AiffBuilder Chunk(string id, byte[] body)
    {
        _chunks.Add((id, body));
        return this;
    }

    public byte[] Build()
    {
        var output = new List<byte>();
        output.AddRange(Encoding.ASCII.GetBytes("FORM"));
        output.AddRange(new byte[4]);
        output.AddRange(Encoding.ASCII.GetBytes(_formType));
        foreach (var (id, body) in _chunks)
        {
            output.AddRange(Encoding.ASCII.GetBytes(id));
            output.AddRange(U32((uint)body.Length));
            output.AddRange(body);
            if (body.Length % 2 == 1)
            {
                output.Add(0);
            }
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    /// <summary>An IEEE 754 80-bit extended value, as AIFF stores its sample rate.</summary>
    public static byte[] Extended(double value)
    {
        var bytes = new byte[10];
        if (value == 0)
        {
            return bytes;
        }

        var sign = value < 0 ? 0x8000 : 0;
        value = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log2(value));
        var mantissa = (ulong)Math.Round(value / Math.Pow(2, exponent - 63));
        var biased = exponent + 16383;
        bytes[0] = (byte)(((sign | biased) >> 8) & 0xFF);
        bytes[1] = (byte)(biased & 0xFF);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(2), mantissa);
        return bytes;
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
