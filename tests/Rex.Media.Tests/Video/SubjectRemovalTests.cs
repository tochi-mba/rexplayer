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

    [Fact]
    public void AModelsOutlineAndFillAreUsedForTheirPicture()
    {
        var scene = Walk();
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        var opacity = new float[320 * 180];
        using var picture = scene.Render(0, opacity: opacity);
        var shape = opacity.Select(alpha => alpha >= 0.5f ? (byte)255 : (byte)0).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => removal.UseShape(shape, 0, 180, picture.Pts));
        Assert.Throws<ArgumentOutOfRangeException>(() => removal.UseShape(shape, 320, 0, picture.Pts));
        Assert.Throws<ArgumentException>(() => removal.UseShape(shape, 100, 180, picture.Pts));
        removal.UseShape(shape, 320, 180, picture.Pts);
        removal.Apply(picture, scene.Box(0), advance: true, 3, 50, preview);
        var (both, either) = RemovalScore.Overlap(removal.IsSubject, opacity, 320, 180);
        Assert.True(both / (double)either > 0.93, $"{both / (double)either}");

        // Nothing was seen behind the figure yet: every replaced pixel is in the hole a model fills.
        var hole = new byte[320 * 180];
        Assert.Equal(removal.EstimatedPixels, removal.EstimatedHole(hole, 320));
        Assert.Equal(255, hole[100 * 320 + 80]);
        Assert.Equal(0, hole[5 * 320 + 5]);
        Assert.False(removal.UsedEstimate);

        using var estimate = Preview(scene);
        estimate.Plane(0).Fill(0x40);
        removal.Apply(picture, scene.Box(0), advance: false, 3, 50, preview, estimate);
        Assert.True(removal.UsedEstimate);
        Assert.Equal(0x40, preview.Row(0, 100)[80 * 4]);
        Assert.Equal(picture.Row(0, 5)[5 * 4], preview.Row(0, 5)[5 * 4]);
        using var small = VideoFrame.Rent(PixelFormat.Bgra32, 10, 10);
        Assert.Throws<ArgumentException>(() => removal.Apply(picture, scene.Box(0), false, 3, 50, preview, small));

        // Nothing removed leaves no hole.
        var flatRemoval = new SubjectRemoval();
        using var flat = VideoFrame.Rent(PixelFormat.Bgra32, 120, 80);
        flat.Plane(0).Fill(70);
        using var flatPreview = VideoFrame.Rent(PixelFormat.Bgra32, 120, 80);
        flatRemoval.Apply(flat, (40, 20, 70, 60), true, 3, 0, flatPreview);
        Assert.Equal(0, flatRemoval.EstimatedHole(new byte[120 * 80], 120));
    }

    [Fact]
    public void ABlendCoversALogoWithTheShirtAroundItAndNeverTheSceneBehind()
    {
        // A yellow logo on a red and white striped shirt in front of a blue wall: Blend covers it
        // with the stripes around it, as a retouching brush would, and shows nothing of the wall.
        static VideoFrame Shirt(int shift, double seconds)
        {
            var frame = VideoFrame.Rent(PixelFormat.Bgra32, 320, 200);
            frame.Pts = MediaTime.FromSeconds(seconds);
            for (var y = 0; y < 200; y++)
            {
                var row = frame.Row(0, y);
                for (var x = 0; x < 320; x++)
                {
                    var (sx, sy) = (x - shift, y);
                    var shirt = sx >= 60 && sx < 260 && sy >= 30 && sy < 180;
                    var logo = sx >= 150 && sx < 172 && sy >= 92 && sy < 112;
                    (byte B, byte G, byte R) colour = logo ? ((byte)20, (byte)220, (byte)240)
                        : shirt ? ((sy / 6) % 2 == 0 ? ((byte)40, (byte)40, (byte)200) : ((byte)230, (byte)230, (byte)230))
                        : ((byte)200, (byte)80, (byte)30);
                    (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = (colour.B, colour.G, colour.R, 255);
                }
            }

            return frame;
        }

        var removal = new SubjectRemoval();
        using var preview = VideoFrame.Rent(PixelFormat.Bgra32, 320, 200);
        for (var i = 0; i < 6; i++)
        {
            using var picture = Shirt(2 * i, 1 + i / 25.0);
            var box = (146 + 2 * i, 88, 176 + 2 * i, 116);
            Assert.True(removal.Apply(picture, box, advance: true, 2, 50, preview, mode: RemovalMode.Blend));
            Assert.Equal(0, removal.ObservedPixels);
            Assert.True(removal.EstimatedPixels > 0);
            int yellow = 0, wall = 0, stripes = 0;
            for (var y = 94; y < 110; y++)
            {
                var row = preview.Row(0, y);
                for (var x = 152 + 2 * i; x < 170 + 2 * i; x++)
                {
                    var (b, g, r) = (row[x * 4], row[x * 4 + 1], row[x * 4 + 2]);
                    yellow += g > 180 && b < 90 ? 1 : 0;
                    wall += b > 150 && r < 90 ? 1 : 0;
                    stripes += r > 150 && b < 90 || (r > 190 && g > 190 && b > 190) ? 1 : 0;
                }
            }

            Assert.Equal(0, yellow);
            Assert.Equal(0, wall);
            Assert.True(stripes > 16 * 18 * 0.6, $"picture {i}: {stripes} of {16 * 18} pixels look like the shirt");
        }
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
    public void ALargeSubjectIsRemovedAtHalfSizeAndDrawnBackAtFullSize()
    {
        // The walking figure filmed four times closer: its box holds about 58,000 pixels, so the
        // removal works on a half-size copy and draws the result back at full size.
        var scene = new RemovalScene(1280, 720, _ => (0, 0), i => (70 + 2.5f * i, 92), zoom: 4);
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        using var wall = VideoFrame.Rent(PixelFormat.Bgra32, 1280, 720);
        var opacity = new float[1280 * 720];
        double error = 0;
        var count = 0;
        long both = 0, either = 0;
        for (var i = 0; i < 14; i++)
        {
            using var picture = scene.Render(i, wall, opacity);
            Assert.True(removal.Apply(picture, scene.Box(i, loose: 30), advance: true, 3, 50, preview));
            Assert.Equal(2, removal.WorkingScale);
            if (i >= 8)
            {
                var (e, n) = RemovalScore.FillError(preview, wall, opacity);
                (error, count) = (error + e, count + n);
                var (b, u) = RemovalScore.Overlap(removal.IsSubject, opacity, 1280, 720);
                (both, either) = (both + b, either + u);
            }
        }

        Assert.True(RemovalScore.Psnr(error, count) > 26,
            $"PSNR {RemovalScore.Psnr(error, count)}, overlap {both / (double)either}, " +
            $"observed {removal.ObservedPixels}, estimated {removal.EstimatedPixels}");
        Assert.True(both / (double)either > 0.85, $"{both / (double)either}");
        Assert.False(removal.IsSubject(-1, 300));

        // The hole a model would fill is marked at full size, two by two for each half-size pixel.
        var hole = new byte[1280 * 720];
        Assert.Equal(removal.EstimatedPixels, removal.EstimatedHole(hole, 1280));
        Assert.True(removal.EstimatedPixels > 0);

        // A model's fill of this paused picture shows at full size, its own pixels.
        using (var picture = scene.Render(13))
        using (var estimate = Preview(scene))
        {
            new Random(5).NextBytes(estimate.Plane(0));
            removal.Apply(picture, scene.Box(13, loose: 30), advance: false, 3, 50, preview, estimate);
            Assert.True(removal.UsedEstimate);
            var marked = Array.IndexOf(hole, (byte)255);
            var (x, y) = (marked % 1280, marked / 1280);
            Assert.Equal(estimate.Row(0, y).Slice(x * 4, 3).ToArray(), preview.Row(0, y).Slice(x * 4, 3).ToArray());

            // A model's outline of the picture is halved with it.
            var shape = new byte[1280 * 720];
            scene.Render(13, opacity: opacity).Dispose();
            for (var at = 0; at < shape.Length; at++)
            {
                shape[at] = (byte)(opacity[at] >= 0.5f ? 255 : 0);
            }

            removal.UseShape(shape, 1280, 720, picture.Pts);
            removal.Apply(picture, scene.Box(13, loose: 30), advance: false, 3, 50, preview);
            removal.Apply(picture, scene.Box(13, loose: 30), advance: false, 3, 60, preview);
            var (b, u) = RemovalScore.Overlap(removal.IsSubject, opacity, 1280, 720);
            Assert.True(b / (double)u > 0.9, $"{b / (double)u}");
        }

        // A box between the two thresholds keeps the size in use; a small one goes back to full
        // size, and what was recorded at half size is dropped.
        using (var picture = scene.Render(14))
        {
            var box = scene.Box(14, loose: 12);
            removal.Apply(picture, box, advance: true, 3, 50, preview);
            Assert.Equal(2, removal.WorkingScale);
            var (cx, cy) = ((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
            removal.Apply(picture, (cx - 40, cy - 80, cx + 40, cy + 80), advance: true, 3, 50, preview);
            Assert.Equal(1, removal.WorkingScale);
            Assert.Equal(0, removal.ObservedPixels);
        }

        removal.Release();
        Assert.Equal(1, removal.WorkingScale);
    }

    [Fact]
    public void ASteadyHalfSizePreviewAllocatesNothing()
    {
        var scene = new RemovalScene(1280, 720, i => (1.5f * i, 0.25f * i), i => (100 + 1f * i, 92), zoom: 4);
        var removal = new SubjectRemoval();
        using var preview = Preview(scene);
        var pictures = Enumerable.Range(0, 16).Select(i => scene.Render(i)).ToArray();
        try
        {
            for (var i = 0; i < 8; i++)
            {
                removal.Apply(pictures[i], scene.Box(i, loose: 30), true, 3, 50, preview);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 8; i < 16; i++)
            {
                removal.Apply(pictures[i], scene.Box(i, loose: 30), true, 3, 50, preview);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(2, removal.WorkingScale);
        }
        finally
        {
            foreach (var picture in pictures)
            {
                picture.Dispose();
            }
        }
    }

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
