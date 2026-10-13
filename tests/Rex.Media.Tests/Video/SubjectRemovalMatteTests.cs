using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>Finding the subject's outline inside and around its box.</summary>
public sealed class SubjectRemovalMatteTests
{
    private static RemovalScene Still(float x = 160, float y = 96) => new(320, 180, _ => (0, 0), _ => (x, y));

    private static short At(SubjectRemovalMatte matte, int x, int y)
    {
        var area = matte.Area;
        return matte.Distance[(y - area.Top) * (area.Right - area.Left) + x - area.Left];
    }

    private static double Overlap(SubjectRemovalMatte matte, float[] opacity, int width, int height)
    {
        var area = matte.Area;
        var (both, either) = RemovalScore.Overlap((x, y) => x >= area.Left && y >= area.Top && x < area.Right && y < area.Bottom
            && At(matte, x, y) <= 0, opacity, width, height);
        return both / (double)either;
    }

    [Fact]
    public void TheFiguresOutlineIsFoundInsideALooseBox()
    {
        var scene = Still();
        var opacity = new float[320 * 180];
        using var picture = scene.Render(0, opacity: opacity);
        var matte = new SubjectRemovalMatte();
        var box = scene.Box(0, loose: 8);
        matte.Compute(picture, box, plate: null, coverage: 50, remember: true);
        Assert.False(matte.IsEmpty);
        // The figure fills about a third of a box loose by eight pixels.
        Assert.InRange(matte.Coverage, 0.25f, 0.45f);
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.9);
        Assert.True(At(matte, 160, 100) < 0);
        // The loose box's corner is wall, a few pixels clear of the outline.
        Assert.True(At(matte, box.Left + 1, box.Top + 1) > 3 * SubjectRemovalMatte.DistanceUnit);
        Assert.Equal(2, matte.Scale);
        Assert.InRange(matte.Grow, 2, 3);
        Assert.Throws<ArgumentNullException>(() => matte.Compute(null!, box, null, 50, true));
    }

    [Fact]
    public void FullCoverageTakesTheWholeBoxAndNoneTakesNothingFromAFlatPicture()
    {
        var scene = Still();
        using var picture = scene.Render(0);
        var matte = new SubjectRemovalMatte();
        var box = scene.Box(0, loose: 8);
        matte.Compute(picture, box, null, coverage: 100, remember: false);
        Assert.True(At(matte, box.Left + 1, box.Top + 1) <= 0);
        Assert.Equal(1f, matte.Coverage);

        using var flat = VideoFrame.Rent(PixelFormat.Bgra32, 320, 180);
        flat.Plane(0).Fill(90);
        matte.Reset();
        matte.Compute(flat, (100, 50, 140, 120), null, coverage: 0, remember: true);
        Assert.True(matte.IsEmpty);
        Assert.Equal(0f, matte.Coverage);
        Assert.True(At(matte, 120, 85) > 0);
    }

    [Fact]
    public void TheBackgroundSeenBeforeIsAStrongCue()
    {
        var scene = Still();
        var opacity = new float[320 * 180];
        using var wall = VideoFrame.Rent(PixelFormat.Bgra32, 320, 180);
        using var picture = scene.Render(0, wall, opacity);
        var plate = new SubjectRemovalPlate();
        plate.Prepare(320, 180);
        plate.Learn(wall, (0, 0, 320, 180), (0, 0, 0, 0), default, 0);
        var matte = new SubjectRemovalMatte();
        matte.Compute(picture, scene.Box(0, loose: 14), plate: null, coverage: 50, remember: false);
        var colourOnly = Overlap(matte, opacity, 320, 180);
        matte.Compute(picture, scene.Box(0, loose: 14), plate, coverage: 50, remember: true);
        var withPlate = Overlap(matte, opacity, 320, 180);
        Assert.True(withPlate > 0.92, $"{withPlate}");
        Assert.True(withPlate > colourOnly + 0.02, $"{withPlate} against {colourOnly}");
    }

    [Fact]
    public void TheMatteFollowsTheBoxFromPictureToPictureAndAPausedRedrawChangesNothing()
    {
        var scene = new RemovalScene(320, 180, _ => (0, 0), i => (100 + 4f * i, 96));
        var opacity = new float[320 * 180];
        var matte = new SubjectRemovalMatte();
        for (var i = 0; i < 6; i++)
        {
            using var picture = scene.Render(i, opacity: opacity);
            matte.Compute(picture, scene.Box(i, loose: 6), null, 50, remember: true);
            Assert.True(Overlap(matte, opacity, 320, 180) > 0.88);
        }

        using var last = scene.Render(5, opacity: opacity);
        var before = matte.Distance.ToArray();
        matte.Compute(last, scene.Box(5, loose: 6), null, 50, remember: false);
        matte.Compute(last, scene.Box(5, loose: 6), null, 50, remember: false);
        Assert.Equal(before.Length, matte.Distance.Length);
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.88);
        // A box that has changed size reads the remembered matte stretched to it.
        matte.Compute(last, scene.Box(5, loose: 20), null, 50, remember: true);
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.8);
    }

    [Fact]
    public void ABigAreaIsAnalysedOnAGridAndABoxAtTheEdgeIsClipped()
    {
        var scene = new RemovalScene(1280, 720, _ => (0, 0), _ => (64, 40), zoom: 8);
        var opacity = new float[1280 * 720];
        using var picture = scene.Render(0, opacity: opacity);
        var matte = new SubjectRemovalMatte();
        matte.Compute(picture, scene.Box(0, loose: 20), null, 50, remember: true);
        Assert.True(matte.Scale > 1);
        Assert.True(matte.Area.Right - matte.Area.Left > 300);
        Assert.True(Overlap(matte, opacity, 1280, 720) > 0.88);
        Assert.InRange(matte.Grow, 3, 10);

        // A box hanging off the picture, even an empty one, is clipped to it.
        matte.Compute(picture, (1270, 700, 1400, 900), null, 50, remember: true);
        Assert.True(matte.Area.Right <= 1280 && matte.Area.Bottom <= 720);
        matte.Compute(picture, (-50, -50, -20, -20), null, 50, remember: false);
        Assert.Equal(0, matte.Area.Left);
    }

    [Fact]
    public void AFigureAwayFromTheBoxsMiddleIsStillFoundAndHolesInsideItAreClosed()
    {
        // A red ring on a textured grey wall, at the left of a wide box: the box's middle holds
        // only wall, and the ring's middle is wall surrounded by the subject.
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 200, 120);
        var random = new Random(3);
        for (var y = 0; y < 120; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < 200; x++)
            {
                var grey = (byte)(110 + random.Next(-12, 12));
                var d = Math.Sqrt((x - 60) * (x - 60) + (y - 60) * (y - 60));
                var ring = d is > 9 and < 22;
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = ring ? ((byte)40, (byte)30, (byte)200, (byte)255) : (grey, grey, grey, (byte)255);
            }
        }

        var matte = new SubjectRemovalMatte();
        matte.Compute(picture, (36, 36, 150, 84), null, 50, remember: true);
        Assert.False(matte.IsEmpty);
        Assert.True(At(matte, 45, 60) < 0);
        Assert.True(At(matte, 60, 60) <= 0);
        Assert.True(At(matte, 120, 60) > 0);
    }
}
