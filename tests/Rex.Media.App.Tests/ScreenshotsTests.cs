namespace Rex.Media.App.Tests;

/// <summary>The screenshot comparison on made-up pictures: no window needed.</summary>
public sealed class ScreenshotsTests
{
    private static (int Width, int Height, byte[] Bgra) Plain(int width, int height, byte shade)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, shade);
        return (width, height, pixels);
    }

    [Fact]
    public void AUniformCaptureIsNotAnAcceptableVisualBaseline()
    {
        Assert.False(Screenshots.HasVisibleVariation(Plain(100, 80, 0)));
        Assert.False(Screenshots.HasVisibleVariation(Plain(100, 80, 255)));
    }

    [Fact]
    public void AStrayPixelDoesNotMakeABlankCaptureLookRendered()
    {
        var picture = Plain(100, 80, 0);
        picture.Bgra[0] = 255; // Even a corner pixel may be corrupt or contain a cursor.
        Assert.False(Screenshots.HasVisibleVariation(picture));
        for (var i = 1; i < 16; i++)
        {
            picture.Bgra[i * 4] = 255;
        }

        Assert.False(Screenshots.HasVisibleVariation(picture));
    }

    [Fact]
    public void InvalidCaptureDimensionsOrBufferAreRejected()
    {
        Assert.False(Screenshots.HasVisibleVariation((0, 80, [])));
        Assert.False(Screenshots.HasVisibleVariation((100, 80, new byte[10])));
        Assert.False(Screenshots.HasVisibleVariation((int.MaxValue, int.MaxValue, [])));
    }

    [Fact]
    public void ARenderedInterfaceHasEnoughVariationToCapture()
    {
        var picture = Plain(100, 80, 0);
        for (var i = 0; i < picture.Bgra.Length / 4; i += 2)
        {
            picture.Bgra[i * 4] = 220;
        }

        Assert.True(Screenshots.HasVisibleVariation(picture));
    }

    [Fact]
    public void NoiseUnderTheThresholdIsNotADifference()
    {
        var (differing, _) = Screenshots.Difference(Plain(4, 4, 100), Plain(4, 4, 130), pixelThreshold: 40);
        Assert.Equal(0, differing);
    }

    [Fact]
    public void ChangedPixelsAreCountedAndShownInRed()
    {
        var actual = Plain(4, 4, 100);
        actual.Bgra[(5 * 4) + 2] = 250;
        var (differing, diff) = Screenshots.Difference(Plain(4, 4, 100), actual, pixelThreshold: 40);
        Assert.Equal(1, differing);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, diff.Bgra[(5 * 4)..(6 * 4)]);
    }

    [Fact]
    public void ACropTakesTheControlsPixelsFromTheWindowsPicture()
    {
        var picture = Plain(10, 10, 0);
        picture.Bgra[((3 * 10) + 2) * 4] = 9;
        var cropped = Screenshots.Crop(picture, new System.Windows.Rect(100, 200, 10, 10), new System.Windows.Rect(102, 203, 4, 2));
        Assert.Equal((4, 2), (cropped.Width, cropped.Height));
        Assert.Equal(9, cropped.Bgra[0]);
    }
}
