using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

public sealed class YuvTransformTests
{
    private static readonly ColorInfo Bt709 = new(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, false);

    [Theory]
    [InlineData(235, 128, 128, 1.0, 1.0, 1.0)]
    [InlineData(16, 128, 128, 0.0, 0.0, 0.0)]
    [InlineData(63, 102, 240, 1.0, 0.0, 0.0)]
    public void LimitedRangeBt709MapsTheStandardsLevels(int y, int cb, int cr, double r, double g, double b)
    {
        var (red, green, blue) = YuvTransform.For(Bt709, 8).Apply(y, cb, cr);

        Assert.Equal(r, red, 0.01);
        Assert.Equal(g, green, 0.01);
        Assert.Equal(b, blue, 0.01);
    }

    [Theory]
    [InlineData(255.0, 8)]
    [InlineData(65535.0 / 64, 10)]
    public void TheShaderRowsGiveTheSameColourFromTextureValues(double range, int bits)
    {
        var transform = YuvTransform.For(Bt709 with { FullRange = bits == 10 }, bits);
        var rows = transform.ForShader(range);
        int y = 3 << (bits - 2), cb = 1 << (bits - 3), cr = 5 << (bits - 3);

        var expected = transform.Apply(y, cb, cr);
        double Row(int row) => (rows[row * 4] * (y / range)) + (rows[(row * 4) + 1] * (cb / range)) + (rows[(row * 4) + 2] * (cr / range)) + rows[(row * 4) + 3];

        Assert.Equal(expected.R, Row(0), 1e-5);
        Assert.Equal(expected.G, Row(1), 1e-5);
        Assert.Equal(expected.B, Row(2), 1e-5);
    }

    [Fact]
    public void GreyIgnoresChromaAndGbrMovesThePlanes()
    {
        var grey = YuvTransform.For(Bt709, 8, hasChroma: false).Apply(235, 0, 255);
        var gbr = YuvTransform.For(Bt709 with { Matrix = ColorMatrix.Rgb, FullRange = true }, 8).Apply(255, 0, 128);

        Assert.Equal((1.0, 1.0, 1.0), (Math.Round(grey.R, 6), Math.Round(grey.G, 6), Math.Round(grey.B, 6)));
        Assert.Equal((128 / 255.0, 1.0, 0.0), gbr);
        Assert.Throws<ArgumentNullException>(() => YuvTransform.For(null!, 8));
    }

    [Theory]
    [InlineData(1920, 1080, 1, 1, 1280, 720, 0, 0, 1280, 720)]
    [InlineData(1920, 1080, 1, 1, 1000, 1000, 0, 218, 1000, 563)]
    [InlineData(720, 576, 16, 15, 800, 600, 0, 0, 800, 600)]
    [InlineData(100, 100, 1, 1, 400, 200, 100, 0, 200, 200)]
    [InlineData(100, 100, 0, 1, 400, 200, 100, 0, 200, 200)]
    [InlineData(0, 100, 1, 1, 400, 200, 0, 0, 400, 200)]
    [InlineData(100, 100, 1, 1, -1, 200, 0, 0, 0, 200)]
    public void APictureFitsTheWindowAtItsDisplayAspect(int width, int height, long aspectNumerator, long aspectDenominator, int targetWidth, int targetHeight, int x, int y, int fitWidth, int fitHeight)
    {
        Assert.Equal((x, y, fitWidth, fitHeight), VideoLayout.Fit(width, height, new Rational(aspectNumerator, aspectDenominator), targetWidth, targetHeight));
    }
}
