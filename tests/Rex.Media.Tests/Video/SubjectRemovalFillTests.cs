using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>Synthesising never-seen background from the texture around it, from the edge inwards.</summary>
public sealed class SubjectRemovalFillTests
{
    /// <summary>Vertical stripes six cells apart, so the truth inside any hole is known.</summary>
    private static (byte B, byte G, byte R) Stripe(int x, int y) =>
        x % 6 < 3 ? ((byte)40, (byte)(60 + y % 2), (byte)200) : ((byte)180, (byte)170, (byte)30);

    private static SubjectRemovalFill Prepare(int width, int height, (int Left, int Top, int Right, int Bottom) hole)
    {
        var fill = new SubjectRemovalFill();
        fill.Prepare(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = y * width + x;
                var missing = x >= hole.Left && x < hole.Right && y >= hole.Top && y < hole.Bottom;
                fill.State[cell] = missing ? SubjectRemovalFill.Missing : SubjectRemovalFill.Known;
                fill.Guess[cell] = -1;
                var (b, g, r) = missing ? ((byte)0, (byte)0, (byte)0) : Stripe(x, y);
                (fill.Colour[cell * 3], fill.Colour[cell * 3 + 1], fill.Colour[cell * 3 + 2]) = (b, g, r);
            }
        }

        return fill;
    }

    private static double MeanError(SubjectRemovalFill fill, (int Left, int Top, int Right, int Bottom) hole)
    {
        double total = 0;
        var count = 0;
        for (var y = hole.Top; y < hole.Bottom; y++)
        {
            for (var x = hole.Left; x < hole.Right; x++)
            {
                var cell = y * fill.Width + x;
                var (b, g, r) = Stripe(x, y);
                total += Math.Abs(fill.Colour[cell * 3] - b) + Math.Abs(fill.Colour[cell * 3 + 1] - g) + Math.Abs(fill.Colour[cell * 3 + 2] - r);
                count += 3;
            }
        }

        return total / count;
    }

    [Fact]
    public void ASizeMustBePositiveAndNoHoleLeavesEverythingAlone()
    {
        var fill = new SubjectRemovalFill();
        Assert.Throws<ArgumentOutOfRangeException>(() => fill.Prepare(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => fill.Prepare(3, 0));
        fill = Prepare(20, 20, (0, 0, 0, 0));
        fill.Solve(1);
        Assert.False(fill.Blended);
        Assert.All(fill.Source.ToArray(), source => Assert.Equal(-1, source));
        Assert.All(fill.Correction.ToArray(), correction => Assert.Equal(0f, correction));
    }

    [Fact]
    public void ASmallHoleContinuesTheStripesAcrossIt()
    {
        var hole = (20, 14, 30, 24);
        var fill = Prepare(48, 40, hole);
        fill.Solve(7);
        Assert.False(fill.Blended);
        Assert.True(MeanError(fill, hole) < 12, $"error {MeanError(fill, hole)}");
        Assert.True(fill.Source[18 * 48 + 24] >= 0);
        Assert.Equal(-1, fill.Source[2 * 48 + 2]);
    }

    [Fact]
    public void ABigHoleIsFilledFromItsEdgeInwards()
    {
        var hole = (40, 30, 80, 70);
        var fill = Prepare(120, 100, hole);
        fill.Solve(11);
        Assert.False(fill.Blended);
        Assert.True(MeanError(fill, hole) < 25, $"error {MeanError(fill, hole)}");
    }

    [Fact]
    public void AGoodSuggestedMatchIsKept()
    {
        // Suggest, for every hole cell, the cell twelve to its left: the same phase of the stripes.
        foreach (var (width, height, hole) in new[] { (48, 40, (20, 14, 30, 24)), (120, 100, (40, 30, 80, 70)) })
        {
            var fill = Prepare(width, height, hole);
            for (var y = hole.Item2; y < hole.Item4; y++)
            {
                for (var x = hole.Item1; x < hole.Item3; x++)
                {
                    fill.Guess[y * width + x] = y * width + x - 36;
                }
            }

            fill.Solve(5);
            Assert.True(MeanError(fill, hole) < 12, $"error {MeanError(fill, hole)}");
        }
    }

    [Fact]
    public void WithNothingBigEnoughToCopyTheHoleIsBlendedFromItsEdges()
    {
        // A three-cell rim leaves no whole five-by-five patch: the edges are pulled inwards.
        var fill = new SubjectRemovalFill();
        fill.Prepare(9, 9);
        for (var cell = 0; cell < 81; cell++)
        {
            var (x, y) = (cell % 9, cell / 9);
            var rim = x < 2 || y < 2 || x > 6 || y > 6;
            fill.State[cell] = rim ? SubjectRemovalFill.Known : SubjectRemovalFill.Missing;
            fill.Guess[cell] = -1;
            var value = (byte)(x < 4 ? 100 : 200);
            (fill.Colour[cell * 3], fill.Colour[cell * 3 + 1], fill.Colour[cell * 3 + 2]) = rim ? (value, value, value) : ((byte)0, (byte)0, (byte)0);
        }

        fill.Solve(3);
        Assert.True(fill.Blended);
        Assert.InRange(fill.Colour[(4 * 9 + 4) * 3], 120, 180);
        Assert.InRange(fill.Colour[(4 * 9 + 2) * 3], 95, 150);
        Assert.InRange(fill.Colour[(4 * 9 + 6) * 3], 150, 205);
    }

    [Fact]
    public void AWideHoleWithANarrowRimStillCopiesTexture()
    {
        // Only a rim of whole patches is left around the hole to copy from.
        var hole = (7, 7, 33, 33);
        var fill = Prepare(40, 40, hole);
        fill.Solve(9);
        Assert.False(fill.Blended);
        Assert.True(MeanError(fill, hole) < 40, $"error {MeanError(fill, hole)}");
    }

    [Fact]
    public void CellsThatMayHoldTheSubjectAreNeverCopiedIntoTheHole()
    {
        // The owner's report: chunks of shoes and trouser legs reappeared inside the removed area.
        // Cells marked as possibly the subject guide the matching but are never a source.
        var hole = (20, 14, 30, 24);
        var fill = Prepare(48, 40, hole);
        for (var y = 10; y < 30; y++)
        {
            for (var x = 30; x < 40; x++)
            {
                var cell = y * 48 + x;
                fill.Avoid[cell] = true;
                (fill.Colour[cell * 3], fill.Colour[cell * 3 + 1], fill.Colour[cell * 3 + 2]) = ((byte)10, (byte)10, (byte)250);
            }
        }

        fill.Solve(21);
        for (var y = hole.Item2; y < hole.Item4; y++)
        {
            for (var x = hole.Item1; x < hole.Item3; x++)
            {
                var source = fill.Source[y * 48 + x];
                Assert.True(source >= 0);
                // No patch around a source touches an avoided cell.
                for (var dy = -SubjectRemovalFill.Radius; dy <= SubjectRemovalFill.Radius; dy++)
                {
                    for (var dx = -SubjectRemovalFill.Radius; dx <= SubjectRemovalFill.Radius; dx++)
                    {
                        Assert.False(fill.Avoid[source + dy * 48 + dx]);
                    }
                }
            }
        }

        // With everything around avoided there is nothing to copy, and the hole is blended instead.
        var walled = Prepare(20, 20, (8, 8, 12, 12));
        walled.Avoid.Fill(true);
        walled.Solve(3);
        Assert.True(walled.Blended);
    }

    [Fact]
    public void AnEdgeRunningIntoTheHoleIsCarriedAcrossIt()
    {
        // Grained grass below a line, grained gravel above it: the line continues through the hole
        // instead of either texture spilling over it.
        var fill = new SubjectRemovalFill();
        fill.Prepare(60, 40);
        var hole = (24, 8, 36, 32);
        for (var cell = 0; cell < 60 * 40; cell++)
        {
            var (x, y) = (cell % 60, cell / 60);
            var missing = x >= hole.Item1 && x < hole.Item3 && y >= hole.Item2 && y < hole.Item4;
            fill.State[cell] = missing ? SubjectRemovalFill.Missing : SubjectRemovalFill.Known;
            fill.Guess[cell] = -1;
            var grain = (byte)((x * 7 + y * 13) % 5 * 4);
            var (b, g, r) = y < 20 ? ((byte)(150 + grain), (byte)(150 + grain), (byte)(170 + grain)) : ((byte)(40 + grain), (byte)(140 + grain), (byte)(50 + grain));
            (fill.Colour[cell * 3], fill.Colour[cell * 3 + 1], fill.Colour[cell * 3 + 2]) = missing ? ((byte)0, (byte)0, (byte)0) : (b, g, r);
        }

        fill.Solve(13);
        int wrong = 0, cells = 0;
        for (var y = hole.Item2; y < hole.Item4; y++)
        {
            for (var x = hole.Item1; x < hole.Item3; x++)
            {
                var greenish = fill.Colour[(y * 60 + x) * 3 + 1] - fill.Colour[(y * 60 + x) * 3] > 50;
                wrong += greenish != y >= 20 ? 1 : 0;
                cells++;
            }
        }

        Assert.True(wrong < cells / 12, $"{wrong} of {cells} cells are on the wrong side of the line");
    }

    [Fact]
    public void ALightingGradientCarriesOnThroughTheFill()
    {
        // A wall lit from the left: texture copied from either side of the hole would leave a step
        // where the two copies meet; the fusion makes the fill follow the light.
        var fill = new SubjectRemovalFill();
        fill.Prepare(64, 24);
        var hole = (22, 4, 42, 20);
        byte Shade(int x, int y) => (byte)Math.Clamp(60 + x * 2.5 + (x * 31 + y * 17) % 7, 0, 255);
        for (var cell = 0; cell < 64 * 24; cell++)
        {
            var (x, y) = (cell % 64, cell / 64);
            var missing = x >= hole.Item1 && x < hole.Item3 && y >= hole.Item2 && y < hole.Item4;
            fill.State[cell] = missing ? SubjectRemovalFill.Missing : SubjectRemovalFill.Known;
            fill.Guess[cell] = -1;
            var value = missing ? (byte)0 : Shade(x, y);
            (fill.Colour[cell * 3], fill.Colour[cell * 3 + 1], fill.Colour[cell * 3 + 2]) = (value, value, value);
        }

        fill.Solve(17);
        double error = 0;
        var moved = false;
        for (var y = hole.Item2; y < hole.Item4; y++)
        {
            for (var x = hole.Item1; x < hole.Item3; x++)
            {
                error += Math.Abs(fill.Colour[(y * 64 + x) * 3] - (60 + x * 2.5 + 3));
                moved |= fill.Correction[(y * 64 + x) * 3] != 0;
            }
        }

        var mean = error / ((hole.Item3 - hole.Item1) * (hole.Item4 - hole.Item2));
        Assert.True(mean < 8, $"The fill is {mean:0.0} levels from the light on average.");
        Assert.True(moved);
    }
}
