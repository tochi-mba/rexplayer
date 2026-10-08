namespace Rex.Media.TestKit;

/// <summary>One picture of a GIF the test kit writes.</summary>
public sealed record GifPicture(int Left, int Top, int Width, int Height, byte[] Indexes)
{
    /// <summary>What happens to the picture before the next: 0 or 1 keep it, 2 clears it, 3 puts back what was there.</summary>
    public int Disposal { get; init; }

    public int? Transparent { get; init; }

    /// <summary>The delay after the picture, in hundredths of a second.</summary>
    public int Delay { get; init; } = 10;

    /// <summary>The picture's own colors (RGB triples, a power of two of them), in place of the global ones.</summary>
    public byte[]? Colors { get; init; }

    public bool Interlaced { get; init; }

    /// <summary>The LZW code size; by default the least that holds the indexes.</summary>
    public int CodeSize { get; init; } = 2;

    /// <summary>Leaves out the graphic control extension.</summary>
    public bool NoControl { get; init; }
}

/// <summary>
/// Writes GIF89a files for tests, with a real LZW encoder (codes growing to 12 bits, a clear when
/// the table fills), so every path of a decoder can be reached on purpose.
/// </summary>
public static class GifWriter
{
    public static byte[] Write(int width, int height, byte[]? colors, IEnumerable<GifPicture> pictures, bool trailer = true)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        var bytes = new List<byte>("GIF89a"u8.ToArray());
        bytes.AddRange(U16(width));
        bytes.AddRange(U16(height));
        bytes.Add(colors is null ? (byte)0 : (byte)(0x80 | TableBits(colors)));
        bytes.Add(0);
        bytes.Add(0);
        if (colors is not null)
        {
            bytes.AddRange(colors);
        }

        // A looping animation's application extension, which readers pass over.
        bytes.AddRange([0x21, 0xFF, 11, .. "NETSCAPE2.0"u8.ToArray(), 3, 1, 0, 0, 0]);
        foreach (var picture in pictures)
        {
            if (!picture.NoControl)
            {
                var flags = (picture.Disposal << 2) | (picture.Transparent is null ? 0 : 1);
                bytes.AddRange([0x21, 0xF9, 4, (byte)flags, (byte)picture.Delay, (byte)(picture.Delay >> 8), (byte)(picture.Transparent ?? 0), 0]);
            }

            bytes.Add(0x2C);
            bytes.AddRange(U16(picture.Left));
            bytes.AddRange(U16(picture.Top));
            bytes.AddRange(U16(picture.Width));
            bytes.AddRange(U16(picture.Height));
            var local = picture.Colors is null ? 0 : 0x80 | TableBits(picture.Colors);
            bytes.Add((byte)(local | (picture.Interlaced ? 0x40 : 0)));
            if (picture.Colors is not null)
            {
                bytes.AddRange(picture.Colors);
            }

            bytes.Add((byte)picture.CodeSize);
            bytes.AddRange(SubBlocks(Lzw(picture.Interlaced ? Interlace(picture.Indexes, picture.Width, picture.Height) : picture.Indexes, picture.CodeSize)));
        }

        if (trailer)
        {
            bytes.Add(0x3B);
        }

        return [.. bytes];
    }

    /// <summary>A color table of <paramref name="count"/> grays, from black up.</summary>
    public static byte[] Grays(int count) => [.. Enumerable.Range(0, count).SelectMany(i => Enumerable.Repeat((byte)(i * 255 / Math.Max(1, count - 1)), 3))];

    /// <summary>GIF's variable-length LZW: a clear code first, codes growing as the table does, the end code last.</summary>
    public static byte[] Lzw(byte[] indexes, int minimum)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        var clear = 1 << minimum;
        var output = new List<byte>();
        int buffer = 0, bits = 0, size = minimum + 1, next = clear + 2;
        var table = new Dictionary<(int, byte), int>();

        void Emit(int code)
        {
            buffer |= code << bits;
            bits += size;
            while (bits >= 8)
            {
                output.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        Emit(clear);
        if (indexes.Length > 0)
        {
            int prefix = indexes[0];
            foreach (var index in indexes.Skip(1))
            {
                if (table.TryGetValue((prefix, index), out var code))
                {
                    prefix = code;
                    continue;
                }

                Emit(prefix);
                if (next < 4096)
                {
                    table[(prefix, index)] = next++;
                    if (next > 1 << size && size < 12)
                    {
                        size++;
                    }
                }
                else
                {
                    Emit(clear);
                    table.Clear();
                    (size, next) = (minimum + 1, clear + 2);
                }

                prefix = index;
            }

            Emit(prefix);
        }

        Emit(clear + 1);
        if (bits > 0)
        {
            output.Add((byte)buffer);
        }

        return [.. output];
    }

    /// <summary>Data cut into sub-blocks of up to 255 bytes, then the empty block that ends them.</summary>
    public static byte[] SubBlocks(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var output = new List<byte>();
        foreach (var chunk in data.Chunk(255))
        {
            output.Add((byte)chunk.Length);
            output.AddRange(chunk);
        }

        output.Add(0);
        return [.. output];
    }

    /// <summary>Rows in the order an interlaced GIF stores them: every 8th from 0, every 8th from 4, every 4th from 2, every 2nd from 1.</summary>
    public static byte[] Interlace(byte[] indexes, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        var rows = new List<int>();
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var row = start; row < height; row += step)
            {
                rows.Add(row);
            }
        }

        return [.. rows.SelectMany(row => indexes.Skip(row * width).Take(width))];
    }

    private static int TableBits(byte[] colors) => (int)Math.Log2(colors.Length / 3) - 1;

    private static byte[] U16(int value) => [(byte)value, (byte)(value >> 8)];
}
