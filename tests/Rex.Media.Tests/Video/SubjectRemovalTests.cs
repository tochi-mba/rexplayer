using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>The removal preview as a whole: provenance, sequences, cuts, pauses and its cost.</summary>
public sealed class SubjectRemovalTests
{
    private static RemovalScene Walk() => new(320, 180, _ => (0, 0), i => (80 + 3f * i, 96));

    private static VideoFrame Preview(RemovalScene scene) => VideoFrame.Rent(PixelFormat.Bgra32, scene.Width, scene.Height);

    [Fact]
    public void ThePictureMustBeGivenAndThePreviewMustMatchIt()
    {
        var removal = new SubjectRemoval();
        using var picture = Walk().Render(0);
        using var wrong = VideoFrame.Rent(PixelFormat.Bgra32, 10, 10);
        Assert.Throws<ArgumentNullException>(() => removal.Apply(null!, (0, 0, 1, 1), true, 3, 50, null));
        Assert.Throws<ArgumentException>(() => removal.Apply(picture, (0, 0, 1, 1), true, 3, 50, wrong));
        using var tall = VideoFrame.Rent(PixelFormat.Bgra32, 320, 10);
        Assert.Throws<ArgumentException>(() => removal.Apply(picture, (0, 0, 1, 1), true, 3, 50, tall));
    }

    [Fact]
    public void BackgroundSeenBeforeThePreviewIsUsedAndTheRestIsEstimatedThenKept()
    {
        var scene = Walk();
        var removal = new SubjectRemoval();
        // Recording only: the preview is off, nothing is drawn.
        for (var i = 0; i < 6; i++)
        {
            using var picture = scene.Render(i);
            Assert.False(removal.Apply(picture, scene.Box(i), advance: true, 3, 50, output: null));
        }

        using var preview = Preview(scene);
        using (var picture = scene.Render(6))
        {
            Assert.True(removal.Apply(picture, scene.Box(6), advance: true, 3, 50, preview));
        }

        // The figure walked right, so the wall behind its back was seen; its front was not.
        Assert.True(removal.ObservedPixels > 0);
        Assert.True(removal.EstimatedPixels > 0);
        Assert.InRange(removal.Coverage, 0.3f, 0.9f);
        Assert.True(removal.IsSubject(80 + 18, 100));
        Assert.False(removal.IsSubject(5, 5));
        Assert.False(removal.IsSubject(-1, 500));
        var estimated = removal.EstimatedPixels;
        for (var i = 7; i < 14; i++)
        {
            using var picture = scene.Render(i);
            removal.Apply(picture, scene.Box(i), advance: true, 3, 50, preview);
        }

        Assert.True(removal.EstimatedPixels < estimated + 200);
        Assert.Equal(CameraMotion.Still, removal.CameraMotion);
        Assert.False(removal.CutDetected);
    }

    [Fact]
    public void APausedRedrawRepaintsWithoutRecordingAndWithNewSettings()
    {
        var scene = Walk();
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        using var picture = scene.Render(0);
        removal.Apply(picture, scene.Box(0), advance: true, 3, 50, preview);
        var first = preview.Plane(0).ToArray();
        removal.Apply(picture, scene.Box(0), advance: false, 3, 50, preview);
        Assert.Equal(first, preview.Plane(0).ToArray());
        removal.Apply(picture, scene.Box(0), advance: false, 12, 100, preview);
        Assert.NotEqual(first, preview.Plane(0).ToArray());
        Assert.False(removal.Apply(picture, scene.Box(0), advance: false, 3, 50, null));
        // Hard edge: the fill stops where the widened outline stops.
        removal.Apply(picture, scene.Box(0), advance: false, 0, 50, preview);
        Assert.Equal(picture.Row(0, 5).ToArray(), preview.Row(0, 5).ToArray());
    }

    [Fact]
    public void ACutAGapOrANewSizeForgetsTheBackground()
    {
        var scene = Walk();
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        for (var i = 0; i < 8; i++)
        {
            using var picture = scene.Render(i);
            removal.Apply(picture, scene.Box(i), true, 3, 50, preview);
        }

        Assert.True(removal.ObservedPixels > 0);

        // A different shot: the same figure in front of a flat grey wall.
        using (var other = scene.Render(8))
        {
            other.Plane(0).Fill(30);
            removal.Apply(other, scene.Box(8), true, 3, 100, preview);
            Assert.True(removal.CutDetected);
            Assert.Equal(0, removal.ObservedPixels);
        }

        foreach (var seconds in new[] { 3.0, 1.0 })
        {
            using var later = scene.Render(9);
            removal.Apply(later, scene.Box(9), true, 3, 50, preview);
            using var jumped = scene.Render(10);
            jumped.Pts = MediaTime.FromSeconds(seconds);
            removal.Apply(jumped, scene.Box(10), true, 3, 50, preview);
            Assert.Equal(0, removal.ObservedPixels);
        }

        var small = new RemovalScene(160, 90, _ => (0, 0), _ => (60, 46));
        using var resized = small.Render(11);
        using var smallPreview = Preview(small);
        removal.Apply(resized, small.Box(11), true, 3, 50, smallPreview);
        Assert.Equal(0, removal.ObservedPixels);
        removal.Release();
        Assert.False(removal.IsSubject(60, 46));
    }

    [Fact]
    public void NothingDistinctLeavesThePictureAsItIs()
    {
        var removal = new SubjectRemoval();
        using var flat = VideoFrame.Rent(PixelFormat.Bgra32, 120, 80);
        flat.Plane(0).Fill(70);
        using var preview = VideoFrame.Rent(PixelFormat.Bgra32, 120, 80);
        Assert.False(removal.Apply(flat, (40, 20, 70, 60), true, 3, 0, preview));
        Assert.Equal(flat.Plane(0).ToArray(), preview.Plane(0).ToArray());
        Assert.Equal(0, removal.EstimatedPixels + removal.ObservedPixels);
    }

    [Fact]
    public void AFigureAtThePicturesEdgeIsFilledFromWhatLiesInside()
    {
        var scene = new RemovalScene(320, 180, _ => (0, 0), i => (6 + 0.5f * i, 40));
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        for (var i = 0; i < 4; i++)
        {
            using var picture = scene.Render(i);
            Assert.True(removal.Apply(picture, scene.Box(i), true, 3, 50, preview));
        }

        Assert.True(removal.EstimatedPixels > 0);
    }

    [Theory]
    [InlineData(-3f, 2.5f, 3, 255)]
    [InlineData(2.5f, 2.5f, 3, 255)]
    [InlineData(3f, 2.5f, 0, 0)]
    [InlineData(4f, 2.5f, 3, 128)]
    [InlineData(5.5f, 2.5f, 3, 0)]
    [InlineData(9f, 2.5f, 3, 0)]
    public void TheEdgeIsOpaqueToTheWidenedOutlineThenFades(float distance, float grow, int feather, int expected) =>
        Assert.Equal((byte)expected, SubjectRemoval.Opacity(distance, grow, feather));

    [Fact]
    public void ASteadyPreviewAllocatesNothing()
    {
        var scene = new RemovalScene(320, 180, i => (1.5f * i, 0.25f * i), i => (100 + 1f * i, 96));
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        var pictures = Enumerable.Range(0, 24).Select(i => scene.Render(i)).ToArray();
        try
        {
            for (var i = 0; i < 12; i++)
            {
                removal.Apply(pictures[i], scene.Box(i), true, 3, 50, preview);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 12; i < 24; i++)
            {
                removal.Apply(pictures[i], scene.Box(i), true, 3, 50, preview);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            foreach (var picture in pictures)
            {
                picture.Dispose();
            }
        }
    }
}
