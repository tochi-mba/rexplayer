using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>The recorded background: scene coordinates, guarded recording, provenance and reading back.</summary>
public sealed class SubjectRemovalPlateTests
{
    private static readonly (int, int, int, int) Nothing = (0, 0, 0, 0);

    /// <summary>A smooth gradient: blue follows x, green follows y, red is fixed.</summary>
    private static VideoFrame Gradient(int width = 64, int height = 48, int red = 90)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        for (var y = 0; y < height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = ((byte)(x * 3), (byte)(y * 4), (byte)red, 0);
            }
        }

        return frame;
    }

    /// <summary>Records the same picture as many times as a cell needs agreeing sightings to count as observed.</summary>
    private static void See(SubjectRemovalPlate plate, VideoFrame frame, (int, int, int, int) area, (int, int, int, int) guard, ReadOnlySpan<short> distance, int clearance)
    {
        for (var i = 0; i < SubjectRemovalPlate.Confirmations; i++)
        {
            plate.Learn(frame, area, guard, distance, clearance);
        }
    }

    private static (int B, int G, int R) Split(uint colour) => ((int)(colour & 0xFF), (int)((colour >> 8) & 0xFF), (int)((colour >> 16) & 0xFF));

    [Fact]
    public void ASizeMustBePositiveAndANewSizeStartsEmpty()
    {
        var plate = new SubjectRemovalPlate();
        Assert.Throws<ArgumentOutOfRangeException>(() => plate.Prepare(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => plate.Prepare(4, 0));
        plate.Prepare(64, 48);
        using var frame = Gradient();
        See(plate, frame, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(10, 10, out _));
        plate.Prepare(64, 48);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(10, 10, out _));
        plate.Release();
        Assert.Equal((0, 0), (plate.Width, plate.Height));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(0, 0, out _));
        Assert.Throws<ArgumentNullException>(() => plate.Learn(null!, (0, 0, 1, 1), Nothing, default, 0));
    }

    [Fact]
    public void AStillCameraRecordsExactPixelsAndNeverInsideTheGuard()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(64, 48);
        using var frame = Gradient();
        // Without distances the guard is left alone entirely.
        See(plate, frame, (-5, -5, 70, 60), (20, 10, 30, 20), default, 0);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(40, 30, out var seen));
        Assert.Equal((120, 120, 90), Split(seen));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(25, 15, out _));
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(19, 15, out _));

        // With distances only pixels clear of the subject are recorded.
        var distance = new short[10 * 10];
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                distance[y * 10 + x] = (short)(x < 5 ? 0 : 100);
            }
        }

        See(plate, frame, (0, 0, 64, 48), (20, 10, 30, 20), distance, 50);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(22, 15, out _));
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(27, 15, out var cleared));
        Assert.Equal((81, 60, 90), Split(cleared));
    }

    [Fact]
    public void APanOfAFractionOfAPixelIsFollowedWithoutDrift()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(64, 48);
        using var first = Gradient();
        See(plate, first, (0, 0, 64, 48), Nothing, default, 0);
        // The camera moved: what is at (x, y) now was at (x + 2.5, y + 1.25) before.
        plate.Advance(new CameraMotion(1, 2.5f, 1.25f));
        Assert.Equal((1f, 2.5f, 1.25f), plate.Mapping);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(10, 10, out var carried));
        // Blue 3 per pixel and green 4 per pixel, read 2.5 and 1.25 pixels on.
        var (b, g, _) = Split(carried);
        Assert.InRange(b, 36, 38);
        Assert.InRange(g, 44, 46);

        // Recording the new picture puts each pixel at its own scene point, blended between cells.
        using var second = Gradient(red: 200);
        See(plate, second, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(10, 10, out var fresh));
        var (fb, fg, fr) = Split(fresh);
        Assert.InRange(fb, 29, 31);
        Assert.InRange(fg, 39, 41);
        Assert.InRange(fr, 198, 200);
    }

    [Fact]
    public void AZoomIsFollowedAndTooMuchZoomForgetsEverything()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(64, 48);
        using var frame = Gradient();
        See(plate, frame, (0, 0, 64, 48), Nothing, default, 0);
        plate.Advance(new CameraMotion(1.05f, -1, -1));
        Assert.Equal(1.05f, plate.Mapping.Scale, 3);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(20, 20, out var zoomed));
        var (b, _, _) = Split(zoomed);
        Assert.InRange(b, 59, 61);

        // Recording through a zoom reads each cell's scene point from the picture.
        See(plate, frame, (-4, -4, 70, 52), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(20, 20, out var again));
        Assert.InRange(Split(again).B, 59, 61);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(-30, 20, out _));

        plate.Advance(new CameraMotion(1.6f, 0, 0));
        Assert.Equal((1f, 0f, 0f), plate.Mapping);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(20, 20, out _));
    }

    [Fact]
    public void ALongPanMovesTheRecordInWholeCellsAndForgetsWhatFallsOff()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(64, 48);
        using var frame = Gradient();
        See(plate, frame, (0, 0, 64, 48), Nothing, default, 0);
        plate.Keep((10, 10, 30, 30));
        Assert.Equal((1f, 0f, 0f), plate.Mapping);

        // Pan right and down until the area of interest would leave the plate: the record moves.
        plate.Advance(new CameraMotion(1, 30, 20));
        plate.Keep((10, 10, 30, 30));
        var (_, offsetX, offsetY) = plate.Mapping;
        Assert.True(offsetX < 30 && offsetY < 20);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(20, 20, out var kept));
        Assert.Equal((150, 160, 90), Split(kept));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(40, 26, out _));

        // Back the other way: what survived both moves is still where the scene put it. The camera
        // has moved (-15, -10) in all, so (45, 35) shows what (30, 25) showed at first.
        plate.Advance(new CameraMotion(1, -45, -30));
        plate.Keep((10, 10, 30, 30));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(0, 0, out _));
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(45, 35, out var back));
        Assert.Equal((90, 100, 90), Split(back));

        // A pan wider than the plate leaves nothing.
        plate.Advance(new CameraMotion(1, 500, 0));
        plate.Keep((10, 10, 30, 30));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(20, 20, out _));
    }

    [Fact]
    public void ReadingSaysHowEachValueIsKnown()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(8, 8);
        using var frame = Gradient(8, 8);
        See(plate, frame, (0, 0, 8, 4), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(9, 0, out _));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(0, 9, out _));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(-1, 0, out _));

        // An estimate never replaces an observation, and an estimated neighbour makes a blend estimated.
        plate.Estimate(2, 2, 0x112233);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(2, 2, out var observed));
        Assert.Equal((6, 8, 90), Split(observed));
        plate.Estimate(2, 5, 0x404040);
        plate.Estimate(3, 5, 0x404040);
        plate.Estimate(2, 6, 0x404040);
        plate.Estimate(3, 6, 0x404040);
        plate.Estimate(40, 5, 0x404040);
        Assert.Equal(SubjectRemovalPlate.Estimated, plate.Sample(2, 5, out var estimate));
        Assert.Equal(0x404040u, estimate);

        // Half a cell along x and y in turn.
        plate.Advance(new CameraMotion(1, 0.5f, 0));
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(2, 1, out var across));
        Assert.Equal(8, Split(across).B);
        Assert.Equal(SubjectRemovalPlate.Estimated, plate.Sample(2, 5, out _));
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(7, 1, out _));
        plate.Advance(new CameraMotion(1, -0.5f, 0.5f));
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(2, 1, out var down));
        Assert.Equal(6, Split(down).G);
        Assert.Equal(SubjectRemovalPlate.Estimated, plate.Sample(2, 5, out _));

        // Half a cell from the last row seen, half from one never seen: the edge of what is known is
        // estimated from the known row alone; with nothing known around it, it is unknown.
        Assert.Equal(SubjectRemovalPlate.Estimated, plate.Sample(2, 3, out var edge));
        plate.Advance(new CameraMotion(1, 0, -0.5f));
        plate.Sample(2, 3, out var lastRow);
        plate.Advance(new CameraMotion(1, 0, 0.5f));
        Assert.Equal(lastRow, edge);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(5, 6, out var nothing));
        Assert.Equal(0u, nothing);
    }

    [Fact]
    public void OnlyBackgroundSeenAlikeSeveralTimesInARowCountsAsObserved()
    {
        var plate = new SubjectRemovalPlate();
        plate.Prepare(64, 48);
        using var wall = Gradient();
        using var passing = Gradient(red: 250);
        // Seen once, then twice: not yet trusted.
        plate.Learn(wall, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(10, 10, out _));
        plate.Learn(wall, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(10, 10, out _));
        // Something passing breaks the run, and alternating views never agree long enough.
        for (var i = 0; i < 6; i++)
        {
            plate.Learn(i % 2 == 0 ? passing : wall, (0, 0, 64, 48), Nothing, default, 0);
        }

        Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(10, 10, out _));
        See(plate, wall, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(10, 10, out var seen));
        Assert.Equal(90, Split(seen).R);

        // A lasting change, such as a light switched on, replaces the record once it has held.
        plate.Learn(passing, (0, 0, 64, 48), Nothing, default, 0);
        Assert.Equal(90, plate.Sample(10, 10, out var held) == SubjectRemovalPlate.Observed ? Split(held).R : -1);
        See(plate, passing, (0, 0, 64, 48), Nothing, default, 0);
        plate.Sample(10, 10, out var changed);
        Assert.Equal(250, Split(changed).R);
    }

    [Fact]
    public void ALargeAreaIsRecordedInAlternateRows()
    {
        foreach (var zoom in new[] { 1f, 1.05f })
        {
            var plate = new SubjectRemovalPlate();
            plate.Prepare(64, 48);
            plate.Advance(new CameraMotion(zoom, 0, 0));
            using var frame = Gradient();
            for (var i = 0; i < SubjectRemovalPlate.Confirmations; i++)
            {
                plate.Learn(frame, (0, 0, 64, 48), Nothing, default, 0, rowStep: 2, rowPhase: 0);
            }

            // Even plate rows were seen three times; odd ones not at all yet.
            var (u, v) = plate.Locate(20, 20);
            var even = (int)MathF.Round(v) % 2 == 0;
            Assert.Equal(SubjectRemovalPlate.Unknown, plate.Sample(20, even ? 21 : 20, out _));
            for (var i = 0; i < SubjectRemovalPlate.Confirmations; i++)
            {
                plate.Learn(frame, (0, 0, 64, 48), Nothing, default, 0, rowStep: 2, rowPhase: 1);
            }

            Assert.Equal(SubjectRemovalPlate.Observed, plate.Sample(30, 30, out _));
            Assert.True(u >= 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 0x102030u)]
    [InlineData(256, 0, 0x405060u)]
    [InlineData(0, 256, 0x708090u)]
    [InlineData(128, 128, 0x5868_78u)]
    public void FourPixelsBlendLaneByLane(int wx, int wy, uint expected)
    {
        var mixed = SubjectRemovalPlate.Mix(0xFF102030, 0x00405060, 0x12708090, 0x34A0B0C0, wx, wy);
        Assert.Equal(expected, mixed);
    }
}
