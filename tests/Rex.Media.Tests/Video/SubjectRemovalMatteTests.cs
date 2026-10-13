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
        // The top middle of a box leans to the subject, so on a box this loose a little wall beside the
        // head goes with it: some more fill, never a head left standing.
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.85, $"{Overlap(matte, opacity, 320, 180)}");
        Assert.True(At(matte, 160, 100) < 0);
        // The loose box's corner is wall, a few pixels clear of the outline.
        Assert.True(At(matte, box.Left + 1, box.Top + 1) > 3 * SubjectRemovalMatte.DistanceUnit);
        Assert.Equal(2, matte.Scale);
        Assert.InRange(matte.Grow, 3, 4.5f);
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
        for (var i = 0; i < SubjectRemovalPlate.Confirmations; i++)
        {
            plate.Learn(wall, (0, 0, 320, 180), (0, 0, 0, 0), default, 0);
        }
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
    public void AHeadUnlikeTheJacketJoinsTheSubjectBecauseTheWallNeverShowsItsColour()
    {
        // The owner's report: a man in a black jacket in front of a grey wall lost only his jacket;
        // the subject's colours were mostly black, so his face looked like nothing of his. A colour
        // the background around the box never shows is the subject's, and it joins the body.
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 320, 200);
        for (var y = 0; y < 200; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < 320; x++)
            {
                var grey = (byte)(118 + (x * 7 + y * 13) % 9);
                var head = (x - 160) * (x - 160) + (y - 52) * (y - 52) <= 13 * 13;
                var hand = x >= 182 && x < 192 && y >= 120 && y < 132;
                var jacket = x >= 138 && x < 182 && y >= 64 && y < 170;
                (byte B, byte G, byte R) colour = head || hand ? ((byte)88, (byte)118, (byte)176)
                    : jacket ? ((byte)(26 + (y & 3)), (byte)26, (byte)30)
                    : (grey, grey, grey);
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = (colour.B, colour.G, colour.R, 255);
            }
        }

        var matte = new SubjectRemovalMatte();
        matte.Compute(picture, (132, 34, 196, 172), null, 50, remember: true);
        Assert.True(At(matte, 160, 100) < 0, "the jacket");
        Assert.True(At(matte, 160, 52) < 0, "the head");
        Assert.True(At(matte, 160, 42) < 0, "the top of the head");
        Assert.True(At(matte, 187, 126) < 0, "the hand");
        Assert.True(At(matte, 120, 100) > 0, "the wall beside him");
    }

    [Fact]
    public void AModelsOutlineIsTakenAsTheMatteWidenedByCoverageAndLearntFrom()
    {
        var scene = Still();
        var opacity = new float[320 * 180];
        using var picture = scene.Render(0, opacity: opacity);
        var shape = opacity.Select(alpha => alpha >= 0.5f ? (byte)255 : (byte)0).ToArray();
        var box = scene.Box(0, loose: 8);
        var matte = new SubjectRemovalMatte();
        matte.Compute(picture, box, null, 50, remember: true, shape);
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.93, $"{Overlap(matte, opacity, 320, 180)}");

        // The body's right edge on one row: coverage moves the outline up to five pixels either way.
        const int Row = 104;
        var edge = Enumerable.Range(160, 40).First(x => shape[Row * 320 + x] == 0);
        Assert.True(At(matte, edge + 3, Row) > 0);
        matte.Compute(picture, box, null, 100, remember: false, shape);
        Assert.True(At(matte, edge + 3, Row) <= 0);
        matte.Compute(picture, box, null, 0, remember: false, shape);
        Assert.True(At(matte, edge - 4, Row) > 0);
        Assert.Throws<ArgumentException>(() => matte.Compute(picture, box, null, 50, false, new byte[10]));

        // The colour models and the remembered matte came from the outline, so the next picture
        // without one starts from it.
        matte.Compute(picture, box, null, 50, remember: true);
        Assert.True(Overlap(matte, opacity, 320, 180) > 0.9, $"{Overlap(matte, opacity, 320, 180)}");
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
