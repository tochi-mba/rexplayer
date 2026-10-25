using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class TimeRangesTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeRange R(double start, double end) => new(S(start), S(end));

    [Fact]
    [Capability("SUB-21")]
    public void StretchesAddedInAnyOrderAreKeptSortedApartAndJoinedWhereTheyTouch()
    {
        var done = TimeRanges.Empty.Add(R(50, 60)).Add(R(10, 20)).Add(R(30, 40));
        Assert.Equal([R(10, 20), R(30, 40), R(50, 60)], done.Ranges);

        // Touching joins; overlapping several swallows them all.
        Assert.Equal([R(10, 40), R(50, 60)], done.Add(R(20, 30)).Ranges);
        Assert.Equal([R(5, 70)], done.Add(R(5, 70)).Ranges);
        Assert.Equal([R(10, 20), R(25, 45), R(50, 60)], done.Add(R(25, 45)).Ranges);
        Assert.Equal([R(10, 20), R(30, 40), R(50, 60), R(70, 80)], done.Add(R(70, 80)).Ranges);
        Assert.Equal([R(0, 5), R(10, 20), R(30, 40), R(50, 60)], done.Add(R(0, 5)).Ranges);
        Assert.Equal([R(10, 20), R(30, 40), R(50, 60)], done.Add(R(32, 38)).Ranges);

        // Empty and backward stretches change nothing.
        Assert.Same(done, done.Add(R(5, 5)));
        Assert.Same(done, done.Add(R(9, 3)));
        Assert.Equal(S(30), done.Total);
        Assert.Equal(TimeSpan.Zero, TimeRanges.Empty.Total);
        Assert.Equal(done.Ranges, TimeRanges.Of([R(30, 40), R(50, 60), R(10, 20), R(1, 1)]).Ranges);
        Assert.Throws<ArgumentNullException>(() => TimeRanges.Of(null!));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ItFindsTheStretchAtAMomentWhatIsCoveredAndTheGaps()
    {
        var done = TimeRanges.Of([R(10, 20), R(30, 40)]);
        Assert.Equal(R(10, 20), done.At(S(10)));
        Assert.Equal(R(10, 20), done.At(S(19.9)));
        Assert.Null(done.At(S(20)));
        Assert.Null(done.At(S(5)));
        Assert.Null(done.At(S(45)));
        Assert.Equal(R(30, 40), done.At(S(35)));
        Assert.True(R(1, 2).Contains(S(1)));
        Assert.False(R(1, 2).Contains(S(2)));
        Assert.Equal(S(1), R(1, 2).Length);

        Assert.True(done.Covers(R(12, 18)));
        Assert.True(done.Covers(R(10, 20)));
        Assert.False(done.Covers(R(15, 25)));
        Assert.False(done.Covers(R(22, 25)));
        Assert.True(done.Covers(R(7, 7)));

        Assert.Equal([R(0, 10), R(20, 30), R(40, 50)], done.Gaps(R(0, 50)));
        Assert.Equal([R(20, 30)], done.Gaps(R(15, 35)));
        Assert.Equal([R(20, 25)], done.Gaps(R(20, 25)));
        Assert.Empty(done.Gaps(R(31, 39)));
        Assert.Equal([R(40, 45)], done.Gaps(R(20, 45)).Skip(1));
        Assert.Equal([R(0, 9)], TimeRanges.Empty.Gaps(R(0, 9)));
    }
}
