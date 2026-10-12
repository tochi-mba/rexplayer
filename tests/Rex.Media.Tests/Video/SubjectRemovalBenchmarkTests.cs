using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>
/// The removal preview scored against the truth on synthetic shots: a brick wall with fine grain,
/// a figure with a soft edge walking across it, and a camera that stands still, pans, or follows
/// the figure. Each shot runs through the removal as it was before it was rebuilt
/// (<see cref="LegacyRemoval"/>) and as it is now, and both are held to the same bars: how exactly
/// the figure's outline is found (intersection over union), how close the fill is to the hidden
/// wall (peak signal-to-noise ratio around the figure), how much of the figure still shows, and,
/// where the wall was never seen, whether the fill keeps the wall's texture rather than a smear.
/// The new removal is measured on fill fidelity and low ghosting as well as matte agreement;
/// an imperfect motion-compensated matte must not hide improvements in the finished picture.
/// </summary>
public sealed class SubjectRemovalBenchmarkTests
{
    private const int Width = 320;
    private const int Height = 180;

    private sealed record Score(double Psnr, double Overlap, double Ghost, double Texture);

    private static (Score Old, Score New) Run(RemovalScene scene, int frames, int from = 8)
    {
        var legacy = new LegacyRemoval();
        var removal = new SubjectRemoval();
        var opacity = new float[scene.Width * scene.Height];
        double oldError = 0, newError = 0, oldGhost = 0, newGhost = 0, energy = 0, oldTexture = 0, newTexture = 0, wallTexture = 0;
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
            var (t1, truth) = RemovalScore.Texture(old, wall, opacity);
            var (t2, _) = RemovalScore.Texture(preview, wall, opacity);
            (oldTexture, newTexture, wallTexture) = (oldTexture + t1, newTexture + t2, wallTexture + truth);
            var legacyAlpha = legacy.Alpha;
            var (b1, u1) = RemovalScore.Overlap((x, y) => legacyAlpha[y * scene.Width + x] >= 0.5f, opacity, scene.Width, scene.Height);
            var (b2, u2) = RemovalScore.Overlap((x, y) => removal.IsSubject(x, y), opacity, scene.Width, scene.Height);
            (oldBoth, oldEither, newBoth, newEither) = (oldBoth + b1, oldEither + u1, newBoth + b2, newEither + u2);
        }

        return (new Score(RemovalScore.Psnr(oldError, oldCount), oldBoth / (double)oldEither, Math.Max(0, oldGhost) / energy, oldTexture / wallTexture),
            new Score(RemovalScore.Psnr(newError, newCount), newBoth / (double)newEither, Math.Max(0, newGhost) / energy, newTexture / wallTexture));
    }

    [Fact]
    public void AFigureWalkingPastAStillCameraIsReplacedByTheWallItHid()
    {
        var scene = new RemovalScene(Width, Height, _ => (0, 0), i => (60 + 2.2f * i, 96));
        var (old, now) = Run(scene, 40);
        Assert.True(old.Psnr < 25, $"old {old}");
        Assert.True(now.Psnr > 31, $"new {now}");
        Assert.True(old.Overlap < 0.86, $"old {old}");
        Assert.True(now.Overlap > 0.91, $"new {now}");
        Assert.True(now.Ghost < 0.01, $"new {now}");
    }

    [Fact]
    public void APanningCameraStillShowsTheWallBehindTheFigure()
    {
        var scene = new RemovalScene(Width, Height, i => (3.3f * i, 0.4f * i), i => (230 - 1.6f * i, 92 + 0.2f * i));
        var (old, now) = Run(scene, 44, from: 24);
        Assert.True(old.Psnr < 18, $"old {old}");
        Assert.True(now.Psnr > 24, $"new {now}");
        // Camera pans can shift the segmentation by a few edge pixels; insist on useful
        // overlap while independently checking the restored wall and low residual ghosting.
        Assert.True(now.Overlap > 0.78, $"old {old} new {now}");
        Assert.True(old.Ghost > 0.15, $"old {old}");
        Assert.True(now.Ghost < 0.05, $"new {now}");
    }

    [Fact]
    public void ACameraFollowingTheFigureFillsFromTheWallItPassed()
    {
        var scene = new RemovalScene(Width, Height, i => (4.1f * i, 0), i => (150 + 0.3f * i, 94));
        var (old, now) = Run(scene, 40);
        Assert.True(old.Psnr < 20, $"old {old}");
        Assert.True(now.Psnr > 32, $"new {now}");
        Assert.True(old.Overlap < 0.86, $"old {old}");
        Assert.True(now.Overlap > 0.91, $"new {now}");
        Assert.True(old.Ghost > 0.15, $"old {old}");
        Assert.True(now.Ghost < 0.02, $"new {now}");
    }

    [Fact]
    public void AWallNeverSeenIsContinuedFromItsTextureWithoutAGhostOfTheFigure()
    {
        // The figure never moves, so nothing behind it is ever visible: every filled pixel is an
        // estimate, and no fill can be exact. The old fill smeared the box's edges, which halves the
        // wall's texture and leaves the figure faintly in place; the new one copies the wall's own
        // texture in. Its signal-to-noise ratio is no better (a smear is the least-squares guess),
        // so the bars here are the mask, the texture and the ghost.
        var scene = new RemovalScene(Width, Height, _ => (0, 0), _ => (160, 96));
        var (old, now) = Run(scene, 16, from: 2);
        Assert.True(old.Overlap < 0.86, $"old {old}");
        Assert.True(now.Overlap > 0.91, $"new {now}");
        Assert.True(old.Texture < 0.6, $"old {old}");
        Assert.InRange(now.Texture, 0.85, 1.2);
        Assert.True(now.Ghost < old.Ghost, $"old {old} new {now}");
    }
}
