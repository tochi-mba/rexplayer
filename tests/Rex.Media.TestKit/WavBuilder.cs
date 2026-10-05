using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.TestKit;

/// <summary>
/// Builds WAV files byte by byte for demuxer tests, including the variants real files come in:
/// big-endian RIFX, 64-bit RF64, WAVEFORMATEXTENSIBLE, LIST/INFO tags, unfinished recordings whose
/// data size was never written, odd-sized chunks with their pad byte, and chunks in unusual orders.
/// </summary>
public sealed class WavBuilder
{
    private readonly List<(string Id, byte[] Body)> _chunks = [];
    private string _riff = "RIFF";
    private uint? _dataSizeOverride;

    public bool BigEndian => _riff == "RIFX";

    public static WavBuilder Pcm(int sampleRate, int channels, int bitsPerSample, byte[] data, ushort formatTag = 1)
    {
        var blockAlign = channels * ((bitsPerSample + 7) / 8);
        return new WavBuilder()
            .Format(formatTag, channels, sampleRate, blockAlign, bitsPerSample)
            .Data(data);
    }

    public WavBuilder Riff(string riffType)
    {
        _riff = riffType;
        return this;
    }

    public WavBuilder Format(ushort tag, int channels, int sampleRate, int blockAlign, int bitsPerSample, byte[]? extra = null)
    {
        var body = new byte[16 + (extra is null ? 0 : 2 + extra.Length)];
        WriteU16(body, 0, tag);
        WriteU16(body, 2, (ushort)channels);
        WriteU32(body, 4, (uint)sampleRate);
        WriteU32(body, 8, (uint)(sampleRate * blockAlign));
        WriteU16(body, 12, (ushort)blockAlign);
        WriteU16(body, 14, (ushort)bitsPerSample);
        if (extra is not null)
        {
            WriteU16(body, 16, (ushort)extra.Length);
            extra.CopyTo(body, 18);
        }

        return Chunk("fmt ", body);
    }

    /// <summary>A WAVE_FORMAT_EXTENSIBLE block whose subformat GUID starts with <paramref name="subFormat"/>.</summary>
    public WavBuilder ExtensibleFormat(ushort subFormat, int channels, int sampleRate, int containerBits, int validBits, uint channelMask)
    {
        var extra = new byte[22];
        WriteU16(extra, 0, (ushort)validBits);
        WriteU32(extra, 2, channelMask);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(6), subFormat);
        new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 }.CopyTo(extra, 8);
        return Format(0xFFFE, channels, sampleRate, channels * containerBits / 8, containerBits, extra);
    }

    public WavBuilder Data(byte[] data) => Chunk("data", data);

    /// <summary>Writes the data chunk's size field as <paramref name="size"/> (0 or 0xFFFFFFFF for an unfinished recording).</summary>
    public WavBuilder DataSizeField(uint size)
    {
        _dataSizeOverride = size;
        return this;
    }

    public WavBuilder Info(params (string Id, string Text)[] entries)
    {
        var body = new List<byte>(Encoding.ASCII.GetBytes("INFO"));
        foreach (var (id, text) in entries)
        {
            var value = Encoding.UTF8.GetBytes(text + "\0");
            body.AddRange(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            WriteU32(size, 0, (uint)value.Length);
            body.AddRange(size);
            body.AddRange(value);
            if (value.Length % 2 == 1)
            {
                body.Add(0);
            }
        }

        return Chunk("LIST", [.. body]);
    }

    public WavBuilder Chunk(string id, byte[] body)
    {
        _chunks.Add((id, body));
        return this;
    }

    public byte[] Build()
    {
        var output = new List<byte>();
        output.AddRange(Encoding.ASCII.GetBytes(_riff));
        output.AddRange(new byte[4]);
        output.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        if (_riff is "RF64" or "BW64")
        {
            var dataLength = _chunks.FirstOrDefault(c => c.Id == "data").Body?.Length ?? 0;
            var ds64 = new byte[28];
            BinaryPrimitives.WriteUInt64LittleEndian(ds64.AsSpan(8), (ulong)dataLength);
            output.AddRange(Encoding.ASCII.GetBytes("ds64"));
            output.AddRange(U32Bytes(28));
            output.AddRange(ds64);
        }

        foreach (var (id, body) in _chunks)
        {
            output.AddRange(Encoding.ASCII.GetBytes(id));
            var size = id == "data" && _dataSizeOverride is { } forced ? forced : (uint)body.Length;
            if (id == "data" && _riff is "RF64" or "BW64")
            {
                size = 0xFFFFFFFF;
            }

            output.AddRange(U32Bytes(size));
            output.AddRange(body);
            if (body.Length % 2 == 1)
            {
                output.Add(0);
            }
        }

        var bytes = output.ToArray();
        WriteU32(bytes, 4, (uint)(bytes.Length - 8));
        return bytes;
    }

    private byte[] U32Bytes(uint value)
    {
        var bytes = new byte[4];
        WriteU32(bytes, 0, value);
        return bytes;
    }

    private void WriteU16(byte[] target, int offset, ushort value)
    {
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset), value);
        }
    }

    private void WriteU32(byte[] target, int offset, uint value)
    {
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), value);
        }
    }
}
