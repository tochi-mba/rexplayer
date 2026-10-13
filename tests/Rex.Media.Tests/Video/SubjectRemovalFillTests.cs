using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>Synthesising never-seen background from the texture around it.</summary>
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
        Assert.Equal(0, fill.LevelsUsed);
        Assert.All(fill.Source.ToArray(), source => Assert.Equal(-1, source));
    }

    [Fact]
    public void ASmallHoleContinuesTheStripesAcrossIt()
    {
        var hole = (20, 14, 30, 24);
        var fill = Prepare(48, 40, hole);
        fill.Solve(7);
        Assert.Equal(1, fill.LevelsUsed);
        Assert.True(MeanError(fill, hole) < 12, $"error {MeanError(fill, hole)}");
        Assert.True(fill.Source[18 * 48 + 24] >= 0);
        Assert.Equal(-1, fill.Source[2 * 48 + 2]);
    }

    [Fact]
    public void ABigHoleIsSolvedCoarseToFine()
    {
        var hole = (40, 30, 80, 70);
        var fill = Prepare(120, 100, hole);
        fill.Solve(11);
        Assert.True(fill.LevelsUsed >= 2);
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
        Assert.Equal(0, fill.LevelsUsed);
        Assert.InRange(fill.Colour[(4 * 9 + 4) * 3], 120, 180);
        Assert.InRange(fill.Colour[(4 * 9 + 2) * 3], 95, 150);
        Assert.InRange(fill.Colour[(4 * 9 + 6) * 3], 150, 205);
    }

    [Fact]
    public void AHoleTooBigForTheCoarseLevelsStaysOnTheFineOnes()
    {
        // A wide hole leaves a rim of copyable patches at full size but none once halved.
        var hole = (7, 7, 33, 33);
        var fill = Prepare(40, 40, hole);
        fill.Solve(9);
        Assert.Equal(1, fill.LevelsUsed);
        Assert.True(MeanError(fill, hole) < 40, $"error {MeanError(fill, hole)}");
    }
}
