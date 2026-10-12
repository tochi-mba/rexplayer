using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>The camera's motion between pictures, measured on a textured wall with no figure in view.</summary>
public sealed class SubjectRemovalMotionTests
{
    private static readonly (int, int, int, int) NoSubject = (0, 0, 0, 0);

    private static RemovalScene Wall(int width, int height, Func<int, (float X, float Y)> camera, float zoom = 1) =>
        new(width, height, camera, _ => (-1000, -1000), zoom);

    [Fact]
    public void ThePanBetweenTwoPicturesIsMeasuredToAFractionOfAPixel()
    {
        // A picture large enough that the detailed stage reads a reduced copy, then a small area read whole.
        foreach (var (width, height, detail) in new[] { (640, 360, (0, 0, 640, 360)), (320, 180, (60, 40, 260, 140)) })
        {
            var scene = Wall(width, height, i => (2.6f * i, -1.3f * i));
            var motion = new SubjectRemovalMotion();
            using var first = scene.Render(0);
            using var second = scene.Render(1);
            Assert.Equal(CameraMotion.Still, motion.Estimate(first, NoSubject, detail));
            var moved = motion.Estimate(second, (100, 60, 140, 120), detail);
            Assert.Equal(1f, moved.Scale);
            Assert.InRange(moved.ShiftX, 2.45f, 2.75f);
            Assert.InRange(moved.ShiftY, -1.45f, -1.15f);
            Assert.False(motion.Cut);
            Assert.InRange(motion.Residual, 0, SubjectRemovalMotion.CutResidual);
        }
    }

    [Fact]
    public void AZoomIsMeasuredAboutTheWholePicture()
    {
        // The same wall filmed two per cent closer: what is at x now was at x / 1.02 before.
        var near = Wall(320, 180, _ => (0, 0), 1.02f);
        var far = Wall(320, 180, _ => (0, 0));
        var motion = new SubjectRemovalMotion();
        using var before = far.Render(0);
        using var after = near.Render(0);
        motion.Estimate(before, NoSubject, (0, 0, 320, 180));
        var zoom = motion.Estimate(after, NoSubject, (0, 0, 320, 180));
        Assert.InRange(zoom.Scale, 0.975f, 0.985f);
        var (x, y) = zoom.Previous(160, 90);
        Assert.InRange(x, 160 / 1.02f - 0.4f, 160 / 1.02f + 0.4f);
        Assert.InRange(y, 90 / 1.02f - 0.4f, 90 / 1.02f + 0.4f);
    }

    [Fact]
    public void AStillCameraIsExactlyStillAndADifferentShotIsACut()
    {
        var scene = Wall(320, 180, _ => (0, 0));
        var motion = new SubjectRemovalMotion();
        using var picture = scene.Render(0);
        motion.Estimate(picture, NoSubject, (0, 0, 320, 180));
        Assert.Equal(CameraMotion.Still, motion.Estimate(picture, NoSubject, (0, 0, 320, 180)));
        Assert.False(motion.Cut);

        using var grey = VideoFrame.Rent(PixelFormat.Bgra32, 320, 180);
        grey.Plane(0).Fill(20);
        motion.Estimate(grey, NoSubject, (0, 0, 320, 180));
        Assert.True(motion.Cut);
        Assert.True(motion.Residual > SubjectRemovalMotion.CutResidual);

        motion.Reset();
        Assert.False(motion.Cut);
        Assert.Equal(0, motion.Residual);
        Assert.Equal(CameraMotion.Still, motion.Estimate(picture, NoSubject, (0, 0, 320, 180)));
    }

    [Fact]
    public void TooLittleVisibleBackgroundOrANewSizeReportsAStillCamera()
    {
        var scene = Wall(320, 180, i => (3f * i, 0));
        var motion = new SubjectRemovalMotion();
        using var first = scene.Render(0);
        using var second = scene.Render(1);
        motion.Estimate(first, NoSubject, (0, 0, 320, 180));
        // The subject covers the whole picture: nothing is left to measure the camera by.
        Assert.Equal(CameraMotion.Still, motion.Estimate(second, (0, 0, 320, 180), (0, 0, 320, 180)));
        Assert.False(motion.Cut);

        // A detail area too small to fit keeps the pyramid's estimate.
        motion.Estimate(first, NoSubject, (150, 80, 154, 84));
        var coarse = motion.Estimate(second, NoSubject, (150, 80, 154, 84));
        Assert.InRange(coarse.ShiftX, 2.4f, 3.6f);

        var small = Wall(160, 90, i => (3f * i, 0));
        using var resized = small.Render(1);
        Assert.Equal(CameraMotion.Still, motion.Estimate(resized, NoSubject, (0, 0, 160, 90)));
        using var tiny = VideoFrame.Rent(PixelFormat.Bgra32, 6, 4);
        motion.Estimate(tiny, NoSubject, (0, 0, 6, 4));
        Assert.Equal(CameraMotion.Still, motion.Estimate(tiny, NoSubject, (0, 0, 6, 4)));
        Assert.Throws<ArgumentNullException>(() => motion.Estimate(null!, NoSubject, NoSubject));
    }

    [Fact]
    public void AHiddenCameraBackgroundStopsBothRefinementStages()
    {
        var scene = Wall(320, 180, _ => (0, 0));
        var motion = new SubjectRemovalMotion();
        using var first = scene.Render(0);
        motion.Estimate(first, NoSubject, (0, 0, 320, 180));
        // When the entire view is excluded there is not one valid pixel to correlate.
        // Refinement must report no candidate instead of interpreting missing data as motion.
        var allHidden = (0, 0, 320, 180);
        Assert.Null(motion.Descend(allHidden, 0, 0));
        Assert.Null(motion.Finest(allHidden, 0, 0, 1));
    }

    [Theory]
    [InlineData(4f, 1f, 2f, 0.25f)]
    [InlineData(2f, 1f, 4f, -0.25f)]
    [InlineData(1f, 1f, 1f, 0f)]
    [InlineData(float.MaxValue, 1f, 3f, 0f)]
    [InlineData(3f, 1f, float.MaxValue, 0f)]
    [InlineData(30f, 1f, 1f, 0.5f)]
    public void AParabolaThroughThreeScoresGivesTheFraction(float before, float middle, float after, float expected) =>
        Assert.Equal(expected, SubjectRemovalMotion.Vertex(before, middle, after), 4);

    [Fact]
    public void TheNormalEquationsAreSolvedOrReducedToAShift()
    {
        var (a, u, v) = SubjectRemovalMotion.Solve(2, 0, 0, 4, 0, 8, 2, 4, 8);
        Assert.Equal((1d, 1d, 1d), (a, u, v));
        (a, u, v) = SubjectRemovalMotion.Solve(0, 0, 0, 4, 0, 8, 0, 4, 8);
        Assert.Equal((0d, 1d, 1d), (a, u, v));
        (a, u, v) = SubjectRemovalMotion.Solve(0, 0, 0, 0, 0, 0, 1, 1, 1);
        Assert.Equal((0d, 0d, 0d), (a, u, v));
    }

    [Fact]
    public void TheInterpolantAndItsSlopesAreExact()
    {
        float[] values = [0, 10, 20, 50];
        var (value, x, y) = SubjectRemovalMotion.Interpolate(values, 2, 0.25f, 0.5f);
        Assert.Equal(15f, value, 4);
        Assert.Equal(20f, x, 4);
        Assert.Equal(25f, y, 4);
        Assert.Equal((5f, 5f), CameraMotion.Still.Previous(5, 5));
    }
}
