using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>An 8-bit RGB or RGBA picture read from a PNG.</summary>
public sealed record PngImage(int Width, int Height, int Channels, byte[] Pixels, IReadOnlyList<string> Chunks)
{
    /// <summary>The peak signal-to-noise ratio against another picture of the same size, over red, green and blue.</summary>
    public double Psnr(PngImage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        double sum = 0;
        for (var i = 0; i < Width * Height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                var difference = Pixels[(i * Channels) + c] - other.Pixels[(i * other.Channels) + c];
                sum += difference * difference;
            }
        }

        var mse = sum / (Width * Height * 3);
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255 * 255 / mse);
    }
}

/// <summary>
/// Reads 8-bit RGB and RGBA PNGs, written here from ISO/IEC 15948 so tests have a PNG reader that is
/// not rexplayer's writer: the signature and every chunk's CRC are checked, and all five filters undone.
/// </summary>
public static class PngReader
{
    public static PngImage Read(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (!png.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            throw new InvalidDataException("The file is not a PNG.");
        }

        var chunks = new List<string>();
        using var idat = new MemoryStream();
        int width = 0, height = 0, channels = 0;
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            if (BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)) != Crc.Crc32.Compute(png.AsSpan(at + 4, length + 4)))
            {
                throw new InvalidDataException($"The {type} chunk's CRC is wrong.");
            }

            chunks.Add(type);
            if (type == "IHDR")
            {
                (width, height) = (BinaryPrimitives.ReadInt32BigEndian(body), BinaryPrimitives.ReadInt32BigEndian(body[4..]));
                if (body[8] != 8 || body[9] is not (2 or 6) || body[12] != 0)
                {
                    throw new InvalidDataException("Only 8-bit, non-interlaced RGB and RGBA are read.");
                }

                channels = body[9] == 2 ? 3 : 4;
            }
            else if (type == "IDAT")
            {
                idat.Write(body);
            }

            at += 12 + length;
        }

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var filtered = new MemoryStream();
        inflate.CopyTo(filtered);
        var data = filtered.ToArray();
        var rowBytes = width * channels;
        var pixels = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var filter = data[y * (rowBytes + 1)];
            for (var i = 0; i < rowBytes; i++)
            {
                var raw = data[(y * (rowBytes + 1)) + 1 + i];
                int left = i >= channels ? pixels[(y * rowBytes) + i - channels] : 0;
                int up = y > 0 ? pixels[((y - 1) * rowBytes) + i] : 0;
                int upLeft = y > 0 && i >= channels ? pixels[((y - 1) * rowBytes) + i - channels] : 0;
                pixels[(y * rowBytes) + i] = (byte)(raw + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Filter {filter} does not exist."),
                });
            }
        }

        return new PngImage(width, height, channels, pixels, chunks);
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var (toLeft, toUp, toUpLeft) = (Math.Abs(estimate - left), Math.Abs(estimate - up), Math.Abs(estimate - upLeft));
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }
}
