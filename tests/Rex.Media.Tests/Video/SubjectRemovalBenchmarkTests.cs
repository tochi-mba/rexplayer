using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>
/// The removal preview scored against the truth on synthetic shots: a brick wall with fine grain,
/// a figure with a soft edge walking across it, and a camera that stands still, pans, or follows
/// the figure. Each shot runs through the removal as it was before it was rebuilt
/// (<see cref="LegacyRemoval"/>) and as it is now, and both are held to the same bars: how exactly
/// the figure's outline is found (intersection over union), how close the fill is to the hidden
/// wall (peak signal-to-noise ratio around the figure), and how much of the figure still shows.
/// The old removal fails each bar and the new one passes it.
/// </summary>
public sealed class SubjectRemovalBenchmarkTests
{
    private const int Width = 320;
    private const int Height = 180;

    private sealed record Score(double Psnr, double Overlap, double Ghost);

    private static (Score Old, Score New) Run(RemovalScene scene, int frames, int from = 8)
    {
        var legacy = new LegacyRemoval();
        var removal = new SubjectRemoval();
        var opacity = new float[scene.Width * scene.Height];
        double oldError = 0, newError = 0, oldGhost = 0, newGhost = 0, energy = 0;
        int oldCount = 0, newCount = 0;
        long oldBoth = 0, oldEither = 0, newBoth = 0, newEither = 0;
        using var preview = VideoFrame.Rent(PixelFormat.Bgra32, scene.Width, scene.Height);
        for (var i = 0; i < frames; i++)
        {
            using var wall = VideoFrame.Rent(PixelFormat.Bgra32, scene.Width, scene.Height);
            using var picture = scene.Render(i, wall, opacity);
            var box = scene.Box(i);
            using var old = legacy.Process(picture, box);
            removal.Apply(picture, box, advance: true, feather: 3, coverage: 50, preview);
            if (i < from)
            {
                continue;
            }

            var (e1, n1) = RemovalScore.FillError(old, wall, opacity);
            var (e2, n2) = RemovalScore.FillError(preview, wall, opacity);
            (oldError, oldCount, newError, newCount) = (oldError + e1, oldCount + n1, newError + e2, newCount + n2);
            var (g1, energy1) = RemovalScore.Ghost(picture, old, wall, opacity);
            var (g2, _) = RemovalScore.Ghost(picture, preview, wall, opacity);
            (oldGhost, newGhost, energy) = (oldGhost + g1, newGhost + g2, energy + energy1);
            var legacyAlpha = legacy.Alpha;
            var (b1, u1) = RemovalScore.Overlap((x, y) => legacyAlpha[y * scene.Width + x] >= 0.5f, opacity, scene.Width, scene.Height);
            var (b2, u2) = RemovalScore.Overlap((x, y) => removal.IsSubject(x, y), opacity, scene.Width, scene.Height);
            (oldBoth, oldEither, newBoth, newEither) = (oldBoth + b1, oldEither + u1, newBoth + b2, newEither + u2);
        }

        return (new Score(RemovalScore.Psnr(oldError, oldCount), oldBoth / (double)oldEither, oldGhost / energy),
            new Score(RemovalScore.Psnr(newError, newCount), newBoth / (double)newEither, newGhost / energy));
    }

    [Fact]
    public void AFigureWalkingPastAStillCameraIsReplacedByTheWallItHid()
    {
        var scene = new RemovalScene(Width, Height, _ => (0, 0), i => (60 + 2.2f * i, 96));
        var (old, now) = Run(scene, 40);
        Assert.True(old.Psnr < 25, $"old {old}");
        Assert.True(now.Psnr > 30, $"new {now}");
        Assert.True(old.Overlap < 0.86, $"old {old}");
        Assert.True(now.Overlap > 0.9, $"new {now}");
        Assert.True(now.Ghost < 0.03, $"new {now}");
    }

    [Fact]
    public void APanningCameraStillShowsTheWallBehindTheFigure()
    {
        var scene = new RemovalScene(Width, Height, i => (3.3f * i, 0.4f * i), i => (230 - 1.6f * i, 92 + 0.2f * i));
        var (old, now) = Run(scene, 44, from: 24);
        Assert.True(old.Psnr < 18, $"old {old}");
        Assert.True(now.Psnr > 24, $"new {now}");
        Assert.True(old.Ghost > 0.1, $"old {old}");
        Assert.True(now.Ghost < 0.03, $"new {now}");
    }

    [Fact]
    public void ACameraFollowingTheFigureFillsFromTheWallItPassed()
    {
        var scene = new RemovalScene(Width, Height, i => (4.1f * i, 0), i => (150 + 0.3f * i, 94));
        var (old, now) = Run(scene, 40);
        Assert.True(old.Psnr < 20, $"old {old}");
        Assert.True(now.Psnr > 30, $"new {now}");
        Assert.True(now.Ghost < 0.03, $"new {now}");
    }

    [Fact]
    public void AWallNeverSeenIsContinuedFromItsTextureWithoutAGhostOfTheFigure()
    {
        // The figure never moves, so nothing behind it is ever visible: every filled pixel is an
        // estimate. The old fill smeared the box's edges and left the figure faintly in place.
        var scene = new RemovalScene(Width, Height, _ => (0, 0), _ => (160, 96));
        var (old, now) = Run(scene, 16, from: 2);
        Assert.True(old.Ghost > 0.2, $"old {old}");
        Assert.True(now.Ghost < 0.05, $"new {now}");
        Assert.True(now.Overlap > 0.9, $"new {now}");
    }
}
