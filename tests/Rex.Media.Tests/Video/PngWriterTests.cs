using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>PNG files from the writer, read back by a second reader written here from the specification.</summary>
public sealed class PngWriterTests
{
    /// <summary>Reads an 8-bit RGB PNG: checks the signature and every chunk's CRC, inflates and unfilters.</summary>
    private static (int Width, int Height, byte[] Rgb, List<string> Chunks) Read(byte[] png)
    {
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = new List<string>();
        var idat = new MemoryStream();
        int width = 0, height = 0;
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)), Crc.Crc32.Compute(png.AsSpan(at + 4, length + 4)));
            chunks.Add(type);
            if (type == "IHDR")
            {
                (width, height) = (BinaryPrimitives.ReadInt32BigEndian(body), BinaryPrimitives.ReadInt32BigEndian(body[4..]));
                Assert.Equal([8, 2, 0, 0, 0], body[8..].ToArray());
            }
            else if (type == "IDAT")
            {
                idat.Write(body);
            }

            at += 12 + length;
        }

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        var filtered = new MemoryStream();
        inflate.CopyTo(filtered);
        var data = filtered.ToArray();
        var stride = (width * 3) + 1;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var filter = data[y * stride];
            for (var i = 0; i < width * 3; i++)
            {
                var raw = data[(y * stride) + 1 + i];
                var left = i >= 3 ? rgb[(y * width * 3) + i - 3] : 0;
                var up = y > 0 ? rgb[((y - 1) * width * 3) + i] : 0;
                rgb[(y * width * 3) + i] = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    _ => throw new InvalidDataException($"Filter {filter} is not expected."),
                };
            }
        }

        return (width, height, rgb, chunks);
    }

    [Fact]
    [Capability("PB-16")]
    public void APictureIsWrittenAsAnRgbPngThatReadsBackExactly()
    {
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 37, 5);
        var random = new Random(7);
        for (var y = 0; y < frame.Height; y++)
        {
            random.NextBytes(frame.Row(0, y));
        }

        using var file = new MemoryStream();
        PngWriter.Write(file, frame);
        var (width, height, rgb, chunks) = Read(file.ToArray());

        Assert.Equal((37, 5), (width, height));
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks);
        for (var y = 0; y < height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                Assert.Equal((row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4]), (rgb[(((y * width) + x) * 3) + 0], rgb[(((y * width) + x) * 3) + 1], rgb[(((y * width) + x) * 3) + 2]));
            }
        }
    }

    [Fact]
    public void AYuvPictureIsConvertedFirst()
    {
        using var frame = VideoFrame.Rent(PixelFormat.I420, 2, 2);
        frame.Plane(0).Fill(235);
        frame.Plane(1).Fill(128);
        frame.Plane(2).Fill(128);

        using var file = new MemoryStream();
        PngWriter.Write(file, frame);

        Assert.All(Read(file.ToArray()).Rgb, value => Assert.Equal(255, value));
        Assert.Throws<ArgumentNullException>(() => PngWriter.Write(null!, frame));
        Assert.Throws<ArgumentNullException>(() => PngWriter.Write(file, null!));
    }
}
