using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class ChunkPlannerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeRange R(double start, double end) => new(S(start), S(end));

    [Fact]
    [Capability("SUB-21")]
    public void APieceKeepsTwentySixSecondsFromThePlayheadAndHearsTwoMoreEitherSide()
    {
        var chunk = ChunkPlanner.Next(TimeRanges.Empty, S(600), S(100.7))!.Value;
        Assert.Equal(R(100, 126), chunk.Kept);
        Assert.Equal(R(98, 128), chunk.Heard);

        // At the start nothing is heard before zero; at the end nothing past the end.
        var first = ChunkPlanner.Next(TimeRanges.Empty, S(600), TimeSpan.Zero)!.Value;
        Assert.Equal((R(0, 26), R(0, 28)), (first.Kept, first.Heard));
        var last = ChunkPlanner.Next(TimeRanges.Empty, S(600), S(580))!.Value;
        Assert.Equal((R(580, 600), R(578, 600)), (last.Kept, last.Heard));

        // A playhead before zero or past the end starts at the top.
        Assert.Equal(R(0, 26), ChunkPlanner.Next(TimeRanges.Empty, S(600), S(-3))!.Value.Kept);
        Assert.Equal(R(0, 26), ChunkPlanner.Next(TimeRanges.Empty, S(600), S(700))!.Value.Kept);
    }

    [Fact]
    [Capability("SUB-21")]
    public void ItRunsAheadOfThePlayheadToTheEndThenFillsInWhatCameBefore()
    {
        var done = TimeRanges.Of([R(100, 126)]);
        Assert.Equal(R(126, 152), ChunkPlanner.Next(done, S(200), S(110))!.Value.Kept);

        // Ahead is all heard: the part before the playhead comes next, from the top.
        done = TimeRanges.Of([R(100, 200)]);
        Assert.Equal(R(0, 26), ChunkPlanner.Next(done, S(200), S(150))!.Value.Kept);

        // A gap shorter than a piece and a sliver is heard whole, never as a sliver after it.
        done = TimeRanges.Of([R(0, 80), R(110, 200)]);
        Assert.Equal(R(80, 110), ChunkPlanner.Next(done, S(200), S(50))!.Value.Kept);
        done = TimeRanges.Of([R(0, 80), R(150, 200)]);
        Assert.Equal(R(80, 106), ChunkPlanner.Next(done, S(200), S(50))!.Value.Kept);

        // Everything heard: nothing left.
        Assert.Null(ChunkPlanner.Next(TimeRanges.Of([R(0, 200)]), S(200), S(20)));
        Assert.Throws<ArgumentNullException>(() => ChunkPlanner.Next(null!, null, TimeSpan.Zero));
    }

    [Fact]
    [Capability("SUB-21")]
    public void WithTheLengthUnknownPiecesGoOnPastTheLastOne()
    {
        var chunk = ChunkPlanner.Next(TimeRanges.Of([R(0, 52)]), null, S(10))!.Value;
        Assert.Equal((R(52, 78), R(50, 80)), (chunk.Kept, chunk.Heard));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ASeekDropsThePieceOnlyWhenThePlayheadLeftItBehind()
    {
        var chunk = new SpeechChunk(R(100, 126), R(98, 128));
        var done = TimeRanges.Of([R(0, 100)]);

        // In it, or just before it: kept.
        Assert.False(ChunkPlanner.ShouldAbandon(chunk, S(110), done));
        Assert.False(ChunkPlanner.ShouldAbandon(chunk, S(99), TimeRanges.Empty));

        // Before it, with everything between heard and not too far: kept.
        Assert.False(ChunkPlanner.ShouldAbandon(chunk, S(40), done));

        // Past it, too far before it, or before it with a hole between: dropped.
        Assert.True(ChunkPlanner.ShouldAbandon(chunk, S(126), done));
        Assert.True(ChunkPlanner.ShouldAbandon(chunk, S(5), done));
        Assert.True(ChunkPlanner.ShouldAbandon(chunk, S(40), TimeRanges.Of([R(0, 60)])));
        Assert.Throws<ArgumentNullException>(() => ChunkPlanner.ShouldAbandon(chunk, S(1), null!));
    }
}
