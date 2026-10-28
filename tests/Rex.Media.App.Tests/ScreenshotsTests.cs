using System.IO;

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

    private static void Fill((int Width, int Height, byte[] Bgra) picture, int left, int top, int width, int height, byte shade)
    {
        for (var y = top; y < top + height; y++)
        {
            Array.Fill(picture.Bgra, shade, ((y * picture.Width) + left) * 4, width * 4);
        }
    }

    [Fact]
    public void AnUnpaintedWindowLooksVariedOnlyThroughItsBordersAndTitleBar()
    {
        // As CI captured it: black resize borders, a dark title bar with its caption, and a client
        // area that is still the plain backdrop the window shows before WinUI draws into it.
        var window = Plain(1000, 700, 0);
        Fill(window, 8, 0, 984, 31, 0x20);
        Fill(window, 30, 10, 60, 12, 0xF0);
        Fill(window, 8, 31, 984, 661, 0xD8);
        Assert.True(Screenshots.HasVisibleVariation(window));

        var client = Screenshots.Crop(window, new System.Windows.Rect(8, 8, 1000, 700), new System.Windows.Rect(16, 39, 984, 661));
        Assert.False(Screenshots.HasVisibleVariation(client));
        Assert.Equal((0xD8, 0xD8, 0xD8), Screenshots.MostCommonColour(client));
    }

    [Fact]
    public void AShortLabelShowsInkWhereAnUndrawnBoxDoesNot()
    {
        var label = Plain(318, 16, 0xF3);
        Assert.False(Screenshots.HasInk(label, pixelThreshold: 40));
        Fill(label, 4, 4, 7, 1, 0x1B);
        Assert.False(Screenshots.HasInk(label, pixelThreshold: 40));
        Fill(label, 4, 5, 6, 8, 0x1B);
        Assert.True(Screenshots.HasInk(label, pixelThreshold: 40));
        Assert.False(Screenshots.HasInk((0, 16, []), pixelThreshold: 40));
    }

    [Fact]
    public void TheMostCommonColourIsTheBackgroundNotTheFirstPixel()
    {
        var picture = Plain(100, 80, 0x20);
        Fill(picture, 0, 0, 3, 3, 0xFF);
        Assert.Equal((0x20, 0x20, 0x20), Screenshots.MostCommonColour(picture));
    }

    [Fact]
    public void AFadeStillUnderWayIsAChangeBetweenFramesThoughNotABaselineDifference()
    {
        var before = Plain(4, 4, 100);
        var after = Plain(4, 4, 100);
        after.Bgra[0] = 120;
        after.Bgra[(5 * 4) + 1] = 160;
        Assert.Equal(2, Screenshots.Changed(before, after, Screenshots.SettledPixelThreshold));
        Assert.Equal(1, Screenshots.Changed(before, after, Screenshots.DefaultPixelThreshold));
        Assert.Equal(0, Screenshots.Changed(before, Plain(4, 4, 100), Screenshots.SettledPixelThreshold));
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

    [Fact]
    public void EveryCapturedSceneHasAnExplicitUiReadinessContract()
    {
        var scenes = ScreenshotScenes.All;
        Assert.True(scenes.Count >= 60, "The inventory must cover existing and edge-state captures.");
        Assert.Contains(scenes, scene => scene.Id == "preferences-search-empty");
        Assert.Contains(scenes, scene => scene.Id == "library-episode-still-letterboxed");
        Assert.Contains(scenes, scene => scene.Id == "caption-buffer-loading" && scene.State == "loading");
        Assert.Contains(scenes, scene => scene.Id == "library-incomplete-download-notice");
        Assert.Contains(scenes, scene => scene.Id == "player-corrupt-file-error"
            && scene.State == "faulted-with-error-stage");
        Assert.Contains(scenes, scene => scene.Id == "library-long-title-compact");
        Assert.Contains(scenes, scene => scene.Id == "library-missing-artwork-wide");
        Assert.Contains(scenes, scene => scene.Id == "library-songs-high-dpi"
            && scene.RequiredLane == "ci-windows-high-dpi");
        Assert.Contains(scenes, scene => scene.Id == "library-songs-high-contrast"
            && scene.RequiredLane == "ci-windows-high-contrast");

        foreach (var scene in scenes)
        {
            Assert.StartsWith("uia:", scene.ReadinessSignal, StringComparison.Ordinal);
            Assert.Contains(scene.ReadinessId, scene.ExpectedAutomationIds);
            Assert.NotEmpty(scene.Fixture);
            Assert.NotEmpty(scene.Route);
            Assert.NotEmpty(scene.Owner);
            if (scene.Baseline == "ci-windows")
            {
                Assert.True(File.Exists(Rex.Media.TestKit.RepoPaths.Combine(
                    "tests", "screenshot-baselines", "ci-windows", scene.Id + ".png")),
                    $"Scene {scene.Id} claims a baseline that is not tracked.");
            }
        }
    }
}
