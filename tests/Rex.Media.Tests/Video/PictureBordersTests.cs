using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>The bars recorded into a picture, found so a card's thumbnail shows picture, not bars (LIB-05).</summary>
public sealed class PictureBordersTests
{
    /// <summary>
    /// A <paramref name="width"/> by <paramref name="height"/> picture whose inside
    /// <paramref name="inside"/> is a busy pattern and whose outside is <paramref name="bar"/>.
    /// </summary>
    private static VideoFrame Picture(int width, int height, PictureArea inside, (byte B, byte G, byte R) bar)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        for (var y = 0; y < height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                var within = x >= inside.X && x < inside.X + inside.Width && y >= inside.Y && y < inside.Y + inside.Height;
                var (b, g, r) = within ? ((byte)((x * 37) + (y * 11)), (byte)(200 - (x * 3)), (byte)((y * 29) ^ x)) : bar;
                row[x * 4] = b;
                row[(x * 4) + 1] = g;
                row[(x * 4) + 2] = r;
                row[(x * 4) + 3] = 255;
            }
        }

        return frame;
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(128, 72, 0, 12, 128, 48)] // A wide film letterboxed into 16:9.
    [InlineData(128, 72, 16, 0, 96, 72)] // A 4:3 programme pillarboxed into 16:9.
    [InlineData(128, 96, 8, 10, 112, 76)] // Both at once.
    [InlineData(64, 48, 0, 0, 64, 48)] // No bars.
    public void BlackBarsAreFoundOnEachEdge(int width, int height, int x, int y, int insideWidth, int insideHeight)
    {
        var inside = new PictureArea(x, y, insideWidth, insideHeight);
        using var frame = Picture(width, height, inside, (0, 0, 0));
        Assert.Equal(inside, PictureBorders.Find(frame));
    }

    [Fact]
    [Capability("LIB-05")]
    public void GreyBarsWithCompressionNoiseAreBarsToo()
    {
        var inside = new PictureArea(0, 10, 160, 70);
        using var frame = Picture(160, 90, inside, (40, 40, 40));

        // A few noisy pixels in a bar and a bar line drifting a little darker.
        frame.Row(0, 2)[5 * 4] = 200;
        frame.Row(0, 85)[7 * 4] = 250;
        frame.Row(0, 1).Fill(28);

        Assert.Equal(inside, PictureBorders.Find(frame));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABarsFringeTintedByThePictureIsCutAwayWithIt()
    {
        // A decoder shares colour between neighbouring lines, so the bar's last lines beside the
        // picture are dark but tinted: still bar, and a dark band if kept.
        var inside = new PictureArea(0, 12, 128, 48);
        using var frame = Picture(128, 72, inside, (0, 0, 0));
        for (var x = 0; x < 128; x++)
        {
            foreach (var y in new[] { 58, 59, 12, 13 })
            {
                var row = frame.Row(0, y);
                (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2]) = ((byte)(x % 3 * 15), 4, (byte)(x % 2 * 35));
            }
        }

        Assert.Equal(new PictureArea(0, 14, 128, 44), PictureBorders.Find(frame));

        // No more than three lines of it: darker picture lines beyond those stay.
        using var deep = Picture(128, 72, inside, (0, 0, 0));
        for (var x = 0; x < 128; x++)
        {
            for (var y = 55; y < 60; y++)
            {
                var row = deep.Row(0, y);
                (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2]) = ((byte)(x % 3 * 15), 4, (byte)(x % 2 * 35));
            }
        }

        Assert.Equal(new PictureArea(0, 12, 128, 45), PictureBorders.Find(deep));
    }

    [Fact]
    [Capability("LIB-05")]
    public void BrightOrColouredEdgesAndAllDarkPicturesStayWhole()
    {
        using var sky = Picture(64, 48, new PictureArea(0, 8, 64, 40), (250, 250, 250));
        Assert.Equal(new PictureArea(0, 0, 64, 48), PictureBorders.Find(sky));

        using var red = Picture(64, 48, new PictureArea(0, 8, 64, 40), (0, 0, 90));
        Assert.Equal(new PictureArea(0, 0, 64, 48), PictureBorders.Find(red));

        // A fade to black is all bar; it is not cut down to nothing.
        using var black = Picture(64, 48, new PictureArea(0, 0, 0, 0), (0, 0, 0));
        Assert.Equal(new PictureArea(0, 0, 64, 48), PictureBorders.Find(black));

        // A dark line that is not one colour (a night scene's edge) is not a bar.
        using var night = Picture(64, 48, new PictureArea(0, 0, 64, 48), (0, 0, 0));
        for (var x = 0; x < 64; x++)
        {
            night.Row(0, 0)[x * 4] = (byte)(x % 2 == 0 ? 0 : 80);
            night.Row(0, 0)[(x * 4) + 1] = (byte)(x % 2 == 0 ? 0 : 80);
            night.Row(0, 0)[(x * 4) + 2] = (byte)(x % 2 == 0 ? 0 : 80);
        }

        Assert.Equal(0, PictureBorders.Find(night).Y);
    }

    [Fact]
    [Capability("LIB-05")]
    public void APictureThatKeepsTooLittleStaysWhole()
    {
        // A thin strip of picture between wide bars is more likely a dark scene than a film.
        using var strip = Picture(90, 90, new PictureArea(0, 40, 90, 10), (0, 0, 0));
        Assert.Equal(new PictureArea(0, 0, 90, 90), PictureBorders.Find(strip));
        using var column = Picture(90, 90, new PictureArea(40, 0, 10, 90), (0, 0, 0));
        Assert.Equal(new PictureArea(0, 0, 90, 90), PictureBorders.Find(column));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ThePngHoldsOnlyThePictureInsideTheBars()
    {
        var inside = new PictureArea(16, 12, 96, 48);
        using var frame = Picture(128, 72, inside, (0, 0, 0));
        using var file = new MemoryStream();
        PictureBorders.WritePng(file, frame);
        var image = PngReader.Read(file.ToArray());

        Assert.Equal((96, 48), (image.Width, image.Height));
        for (var y = 0; y < image.Height; y++)
        {
            var row = frame.Row(0, y + inside.Y);
            for (var x = 0; x < image.Width; x++)
            {
                var at = ((y * image.Width) + x) * 3;
                var source = (x + inside.X) * 4;
                Assert.Equal((row[source + 2], row[source + 1], row[source]), (image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2]));
            }
        }
    }

    [Fact]
    [Capability("LIB-05")]
    public void APictureNotInBgraIsConvertedBeforeItsBarsAreFound()
    {
        // Limited-range black in Y'CbCr: the bars become black in BGRA and are cut away.
        using var frame = VideoFrame.Rent(PixelFormat.Nv12, 64, 48);
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                frame.Row(0, y)[x] = (byte)(y is < 8 or >= 40 ? 16 : 60 + ((x * 7) % 160));
            }
        }

        frame.Plane(1).Fill(128);
        using var file = new MemoryStream();
        PictureBorders.WritePng(file, frame);
        var image = PngReader.Read(file.ToArray());
        Assert.Equal((64, 32), (image.Width, image.Height));
    }

    [Fact]
    public void OnlyBgraPicturesAreSearchedAndOnlyAreasInsideAreWritten()
    {
        using var nv12 = VideoFrame.Rent(PixelFormat.Nv12, 16, 16);
        Assert.Throws<ArgumentException>(() => PictureBorders.Find(nv12));
        Assert.Throws<ArgumentNullException>(() => PictureBorders.Find(null!));
        Assert.Throws<ArgumentNullException>(() => PictureBorders.WritePng(Stream.Null, null!));

        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 16, 16);
        foreach (var area in new PictureArea[] { new(-1, 0, 4, 4), new(0, -1, 4, 4), new(0, 0, 0, 4), new(0, 0, 4, 0), new(13, 0, 4, 4), new(0, 13, 4, 4) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Write(Stream.Null, frame, area));
        }
    }
}
