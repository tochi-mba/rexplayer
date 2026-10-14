using Rex.Media.Engine;

namespace Rex.Media.Tests.Engine;

public sealed class FrameCadenceTests
{
    [Fact]
    public void NothingPresentedGivesZeroes()
    {
        Assert.Equal(default, new FrameCadence().Snapshot());
    }

    [Fact]
    public void SteadyPicturesHaveNoJitter()
    {
        var cadence = new FrameCadence();
        for (var i = 0; i < 10; i++)
        {
            cadence.Presented(i * 40, 2, 40);
        }

        var stats = cadence.Snapshot();

        Assert.Equal((10, 9), (stats.Pictures, stats.Intervals));
        Assert.Equal((40.0, 0.0, 40.0), (stats.IntervalMeanMs, stats.IntervalJitterMs, stats.IntervalWorstMs));
        Assert.Equal((2.0, 0.0, 2.0), (stats.LatenessMeanMs, stats.LatenessJitterMs, stats.LatenessWorstMs));
    }

    [Fact]
    public void UnevenPicturesShowTheirSpreadAndWorst()
    {
        var cadence = new FrameCadence();
        cadence.Presented(0, 0, 40);
        cadence.Presented(30, 10, 40);
        cadence.Presented(80, -10, 40);

        var stats = cadence.Snapshot();

        Assert.Equal(40, stats.IntervalMeanMs, 9);
        Assert.Equal(10, stats.IntervalJitterMs, 9);
        Assert.Equal(50, stats.IntervalWorstMs);
        Assert.Equal(0, stats.LatenessMeanMs, 9);
        Assert.Equal(Math.Sqrt(200.0 / 3), stats.LatenessJitterMs, 9);
        Assert.Equal(10, stats.LatenessWorstMs);
    }

    [Fact]
    public void APauseOrAJumpBackIsNotAnInterval()
    {
        var cadence = new FrameCadence();
        cadence.Presented(0, 0, 40);
        cadence.Presented(1000, 0, 40);
        cadence.Presented(990, 0, 40);
        cadence.Presented(1030, 0, 40);

        var stats = cadence.Snapshot();

        Assert.Equal((4, 1), (stats.Pictures, stats.Intervals));
        Assert.Equal(40, stats.IntervalMeanMs);
    }

    [Fact]
    public void AnUntimedPictureStillAllowsAShortGap()
    {
        var cadence = new FrameCadence();
        cadence.Presented(0, 0, 0);
        cadence.Presented(3, 0, 0);
        cadence.Presented(10, 0, 0);

        Assert.Equal((1, 3.0), (cadence.Snapshot().Intervals, cadence.Snapshot().IntervalMeanMs));
    }

    [Fact]
    public void ARestartBeginsANewRunOfIntervals()
    {
        var cadence = new FrameCadence();
        cadence.Presented(0, 0, 40);
        cadence.Restart();
        cadence.Presented(40, 0, 40);
        cadence.Presented(80, 0, 40);

        Assert.Equal((3, 1), (cadence.Snapshot().Pictures, cadence.Snapshot().Intervals));
    }

    [Fact]
    public void OnlyTheNewestWindowCountsAndClearingForgetsIt()
    {
        var cadence = new FrameCadence();
        for (var i = 0; i < FrameCadence.Window + 50; i++)
        {
            cadence.Presented(i * 10, i < 50 ? 100 : 1, 10);
        }

        var stats = cadence.Snapshot();
        Assert.Equal(FrameCadence.Window, stats.Pictures);
        Assert.Equal(1, stats.LatenessWorstMs);

        cadence.Clear();
        Assert.Equal(default, cadence.Snapshot());
        cadence.Presented(5, 1, 10);
        Assert.Equal((1, 0), (cadence.Snapshot().Pictures, cadence.Snapshot().Intervals));
    }
}
