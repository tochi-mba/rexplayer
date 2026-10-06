using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>PNG files from the writer, read back by a second reader written from the specification.</summary>
public sealed class PngWriterTests
{
    private static PngImage Write(VideoFrame frame)
    {
        using var file = new MemoryStream();
        PngWriter.Write(file, frame);
        return PngReader.Read(file.ToArray());
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

        var image = Write(frame);

        Assert.Equal((37, 5, 3), (image.Width, image.Height, image.Channels));
        Assert.Equal(["IHDR", "IDAT", "IEND"], image.Chunks);
        for (var y = 0; y < image.Height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < image.Width; x++)
            {
                var at = ((y * image.Width) + x) * 3;
                Assert.Equal((row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4]), (image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2]));
            }
        }

        Assert.Equal(double.PositiveInfinity, image.Psnr(image));
    }

    [Fact]
    public void AYuvPictureIsConvertedFirst()
    {
        using var frame = VideoFrame.Rent(PixelFormat.I420, 2, 2);
        frame.Plane(0).Fill(235);
        frame.Plane(1).Fill(128);
        frame.Plane(2).Fill(128);

        Assert.All(Write(frame).Pixels, value => Assert.Equal(255, value));
        Assert.Throws<ArgumentNullException>(() => PngWriter.Write(null!, frame));
        Assert.Throws<ArgumentNullException>(() => PngWriter.Write(new MemoryStream(), null!));
    }
}
