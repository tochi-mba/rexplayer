using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rex.Media.TestKit;

/// <summary>
/// Builds ID3v2 tags (2.2, 2.3 and 2.4) for tag-reader and demuxer tests, including the encodings
/// real taggers use: four text encodings, unsynchronisation of the whole tag or of one frame,
/// compressed, encrypted and grouped frames, data length indicators, extended headers and footers.
/// </summary>
public sealed class Id3Builder
{
    private readonly int _version;
    private readonly List<(string Id, byte[] Body, byte Flags)> _frames = [];
    private int _padding;
    private bool _unsynchronised;
    private bool _extendedHeader;
    private bool _footer;

    private Id3Builder(int version) => _version = version;

    public static Id3Builder V22() => new(2);

    public static Id3Builder V23() => new(3);

    public static Id3Builder V24() => new(4);

    /// <summary>A text field in encoding 0 (Latin-1), 1 (UTF-16 with BOM), 2 (UTF-16BE) or 3 (UTF-8).</summary>
    public Id3Builder Text(string id, string value, byte encoding = 3) => Frame(id, [encoding, .. Encode(value, encoding)]);

    public Id3Builder UserText(string description, string value) =>
        Frame(_version == 2 ? "TXX" : "TXXX", [3, .. Encode(description, 3), 0, .. Encode(value, 3)]);

    public Id3Builder Comment(string description, string text, byte encoding = 3) =>
        Frame(_version == 2 ? "COM" : "COMM", [encoding, .. "eng"u8, .. Encode(description, encoding), .. Terminator(encoding), .. Encode(text, encoding)]);

    public Id3Builder Lyrics(string text) => Frame(_version == 2 ? "ULT" : "USLT", [3, .. "eng"u8, 0, .. Encode(text, 3)]);

    public Id3Builder Picture(byte[] data, string mime = "image/jpeg", byte type = 3)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(mime);
        if (_version == 2)
        {
            var format = mime.EndsWith("png", StringComparison.Ordinal) ? "PNG" : mime.EndsWith("jpeg", StringComparison.Ordinal) ? "JPG" : "GIF";
            return Frame("PIC", [0, .. Encoding.ASCII.GetBytes(format), type, .. "cover"u8, 0, .. data]);
        }

        return Frame("APIC", [1, .. Encoding.ASCII.GetBytes(mime), 0, type, 0xFF, 0xFE, .. Encoding.Unicode.GetBytes("cover"), 0, 0, .. data]);
    }

    /// <summary>A CHAP frame starting at <paramref name="startMs"/>, with a TIT2 sub-frame when titled.</summary>
    public Id3Builder Chapter(string elementId, uint startMs, string? title)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        var body = new List<byte>(Encoding.ASCII.GetBytes(elementId)) { 0 };
        var times = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(times, startMs);
        BinaryPrimitives.WriteUInt32BigEndian(times.AsSpan(4), startMs + 1000);
        times.AsSpan(8).Fill(0xFF);
        body.AddRange(times);
        if (title is not null)
        {
            body.AddRange(FrameBytes("TIT2", [3, .. Encode(title, 3)], 0));
        }

        return Frame("CHAP", [.. body]);
    }

    /// <summary>A text frame stored zlib-compressed, the way 2.3 and 2.4 allow.</summary>
    public Id3Builder Compressed(string id, string value)
    {
        var plain = (byte[])[3, .. Encode(value, 3)];
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(plain);
        }

        var size = new byte[4];
        if (_version == 3)
        {
            BinaryPrimitives.WriteUInt32BigEndian(size, (uint)plain.Length);
            return Frame(id, [.. size, .. packed.ToArray()], 0x80);
        }

        SyncSafe(size, plain.Length);
        return Frame(id, [.. size, .. packed.ToArray()], 0x08 | 0x01);
    }

    /// <summary>A frame whose compression flag is set over bytes that are not zlib.</summary>
    public Id3Builder BrokenCompression(string id) => Frame(id, [0, 0, 0, 9, 1, 2, 3], _version == 3 ? (byte)0x80 : (byte)(0x08 | 0x01));

    public Id3Builder Encrypted(string id) => Frame(id, [1, 3, .. "secret"u8], _version == 3 ? (byte)0x40 : (byte)0x04);

    public Id3Builder Grouped(string id, string value) => Frame(id, [7, 3, .. Encode(value, 3)], _version == 3 ? (byte)0x20 : (byte)0x40);

    /// <summary>A 2.4 frame unsynchronised on its own, with a data length indicator.</summary>
    public Id3Builder UnsynchronisedFrame(string id, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var size = new byte[4];
        SyncSafe(size, body.Length);
        return Frame(id, [.. size, .. Unsynchronise(body)], 0x02 | 0x01);
    }

    public Id3Builder Frame(string id, byte[] body, byte flags = 0)
    {
        _frames.Add((id, body, flags));
        return this;
    }

    public Id3Builder Padding(int bytes)
    {
        _padding = bytes;
        return this;
    }

    /// <summary>Unsynchronises the whole tag (the 2.3 way).</summary>
    public Id3Builder Unsynchronised()
    {
        _unsynchronised = true;
        return this;
    }

    public Id3Builder ExtendedHeader()
    {
        _extendedHeader = true;
        return this;
    }

    public Id3Builder Footer()
    {
        _footer = true;
        return this;
    }

    public byte[] Build()
    {
        var body = new List<byte>();
        if (_extendedHeader)
        {
            body.AddRange(_version == 3 ? [0, 0, 0, 6, 0, 0, 0, 0, 0, 0] : [0, 0, 0, 6, 1, 0]);
        }

        foreach (var (id, frame, flags) in _frames)
        {
            body.AddRange(FrameBytes(id, frame, flags));
        }

        var bytes = _unsynchronised ? Unsynchronise([.. body]) : [.. body];
        bytes = [.. bytes, .. new byte[_padding]];
        var header = new byte[10];
        "ID3"u8.CopyTo(header);
        header[3] = (byte)_version;
        header[5] = (byte)((_unsynchronised ? 0x80 : 0) | (_extendedHeader ? 0x40 : 0) | (_footer ? 0x10 : 0));
        SyncSafe(header.AsSpan(6), bytes.Length);
        if (!_footer)
        {
            return [.. header, .. bytes];
        }

        var footer = (byte[])header.Clone();
        "3DI"u8.CopyTo(footer);
        return [.. header, .. bytes, .. footer];
    }

    /// <summary>Inserts a zero after every 0xFF, which the reader removes again.</summary>
    public static byte[] Unsynchronise(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new List<byte>(data.Length);
        foreach (var b in data)
        {
            result.Add(b);
            if (b == 0xFF)
            {
                result.Add(0);
            }
        }

        return [.. result];
    }

    private byte[] FrameBytes(string id, byte[] body, byte flags)
    {
        if (_version == 2)
        {
            return [.. Encoding.ASCII.GetBytes(id), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length, .. body];
        }

        var header = new byte[10];
        Encoding.ASCII.GetBytes(id).CopyTo(header, 0);
        if (_version == 3)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)body.Length);
        }
        else
        {
            SyncSafe(header.AsSpan(4), body.Length);
        }

        header[9] = flags;
        return [.. header, .. body];
    }

    private static void SyncSafe(Span<byte> target, int value)
    {
        target[0] = (byte)((value >> 21) & 0x7F);
        target[1] = (byte)((value >> 14) & 0x7F);
        target[2] = (byte)((value >> 7) & 0x7F);
        target[3] = (byte)(value & 0x7F);
    }

    private static byte[] Encode(string value, byte encoding) => encoding switch
    {
        0 => Encoding.Latin1.GetBytes(value),
        1 => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(value)],
        2 => Encoding.BigEndianUnicode.GetBytes(value),
        _ => Encoding.UTF8.GetBytes(value),
    };

    private static byte[] Terminator(byte encoding) => encoding is 1 or 2 ? [0, 0] : [0];
}
