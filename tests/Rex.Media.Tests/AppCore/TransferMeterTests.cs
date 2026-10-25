using Rex.Media.AppCore.Updates;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class TransferMeterTests
{
    private const long Megabyte = 1048576;

    [Fact]
    [Capability("TOOL-09")]
    public void ProgressSaysThePercentTheSizeAndRoughlyHowLongIsLeft()
    {
        Assert.Equal("15%, 42 of 280 MB, about 1 minute left", new TransferProgress(42 * Megabyte, 280 * Megabyte, TimeSpan.FromSeconds(70)).Text());
        Assert.Equal("15%, 42 of 280 MB", new TransferProgress(42 * Megabyte, 280 * Megabyte).Text());
        Assert.Equal("50%, 2.1 of 4.2 MB", new TransferProgress(2202010, 4404019).Text());
        Assert.Equal("42 MB so far", new TransferProgress(42 * Megabyte, 0).Text());
        Assert.Equal("0.5 MB so far", new TransferProgress(Megabyte / 2, 0).Text());

        // The percent never rounds up to a hundred before the last byte.
        Assert.Equal("99%, 279 of 280 MB", new TransferProgress((280 * Megabyte) - 1, 280 * Megabyte).Text());
        Assert.Equal("280 MB", TransferProgress.Size(280 * Megabyte));
    }

    [Fact]
    public void TheShareDoneAndWhetherEveryByteIsIn()
    {
        Assert.Equal(0.25, new TransferProgress(1, 4).Fraction);
        Assert.Equal(1, new TransferProgress(5, 4).Fraction);
        Assert.Null(new TransferProgress(5, 0).Fraction);
        Assert.True(new TransferProgress(4, 4).Complete);
        Assert.False(new TransferProgress(3, 4).Complete);
        Assert.False(new TransferProgress(3, 0).Complete);
    }

    [Theory]
    [InlineData(10, "less than a minute left")]
    [InlineData(44, "less than a minute left")]
    [InlineData(45, "about 1 minute left")]
    [InlineData(89, "about 1 minute left")]
    [InlineData(90, "about 2 minutes left")]
    [InlineData(59 * 60, "about 59 minutes left")]
    [InlineData(3570, "about 1 hour left")]
    [InlineData(89 * 60, "about 1 hour left")]
    [InlineData(90 * 60, "about 2 hours left")]
    [InlineData(5 * 3600, "about 5 hours left")]
    public void TimeLeftIsSaidTheWayPeopleSayIt(int seconds, string words)
    {
        Assert.Equal(words, TransferProgress.TimeLeft(TimeSpan.FromSeconds(seconds)));
        Assert.Null(TransferProgress.TimeLeft(null));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void TheTimeLeftComesFromASmoothedSpeed()
    {
        var meter = new TransferMeter();

        // The first count gives no speed yet, nor does a second count within the second.
        Assert.Null(meter.Measure(0, 100 * Megabyte, TimeSpan.Zero).Left);
        Assert.Null(meter.Measure(Megabyte, 100 * Megabyte, TimeSpan.FromMilliseconds(500)).Left);

        // A megabyte a second, then: 98 seconds for the 98 left.
        Assert.Equal(TimeSpan.FromSeconds(98), meter.Measure(2 * Megabyte, 100 * Megabyte, TimeSpan.FromSeconds(2)).Left);

        // A slower second (playback started) moves the estimate gently: 0.3 of the new speed.
        var slower = meter.Measure(2 * Megabyte + (Megabyte / 4), 100 * Megabyte, TimeSpan.FromSeconds(3)).Left!.Value;
        var expected = ((100 * Megabyte) - (2 * Megabyte) - (Megabyte / 4)) / ((0.3 * Megabyte / 4) + (0.7 * Megabyte));
        Assert.Equal(expected, slower.TotalSeconds, 6);

        // Done: nothing left to say.
        Assert.Null(meter.Measure(100 * Megabyte, 100 * Megabyte, TimeSpan.FromSeconds(60)).Left);
    }

    [Fact]
    public void ADownloadThatStartsAgainOrAStoppedClockIsMeasuredAfresh()
    {
        var meter = new TransferMeter();
        meter.Measure(0, 10 * Megabyte, TimeSpan.Zero);
        Assert.NotNull(meter.Measure(5 * Megabyte, 10 * Megabyte, TimeSpan.FromSeconds(1)).Left);

        // The host ignored the range and sent the file from the start.
        Assert.Null(meter.Measure(0, 10 * Megabyte, TimeSpan.FromSeconds(2)).Left);
        Assert.NotNull(meter.Measure(Megabyte, 10 * Megabyte, TimeSpan.FromSeconds(3)).Left);

        // A clock that went back.
        Assert.Null(meter.Measure(2 * Megabyte, 10 * Megabyte, TimeSpan.FromSeconds(1)).Left);
    }

    [Fact]
    public void ACrawlOrAnUnknownTotalGivesNoEstimate()
    {
        var meter = new TransferMeter();
        meter.Measure(0, 1000 * Megabyte, TimeSpan.Zero);
        Assert.Null(meter.Measure(1, 1000 * Megabyte, TimeSpan.FromSeconds(1)).Left);

        var unknown = new TransferMeter();
        unknown.Measure(0, 0, TimeSpan.Zero);
        Assert.Null(unknown.Measure(Megabyte, 0, TimeSpan.FromSeconds(1)).Left);
    }
}
