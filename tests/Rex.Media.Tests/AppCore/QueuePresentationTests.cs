using Rex.Media.AppCore.Player;
using Rex.Media.Engine;

namespace Rex.Media.Tests.AppCore;

public sealed class QueuePresentationTests
{
    private static readonly PlaylistItem Sungba = new(@"C:\Music\Asake\Sungba.mp3");
    private static readonly PlaylistItem Terminator = new(@"C:\Music\Asake\Terminator.mp3");

    [Fact]
    public void AnEmptyQueueSaysSoOnceAndOffersNothingItCannotDo()
    {
        var queue = QueuePresentation.Of(0, null, null, null, SessionState.Idle);

        Assert.False(queue.ShowsStatus);
        Assert.False(queue.ShowsDragHint);
        Assert.False(queue.CanJump);
        Assert.Null(queue.CurrentState);
        Assert.Equal(("0 items", "Nothing playing", "No upcoming items"), (queue.Count, queue.Now, queue.Next));
    }

    [Fact]
    public void ASingleEntryHasNothingToReorderButCanBeJumpedTo()
    {
        var queue = QueuePresentation.Of(1, Sungba, null, Sungba, SessionState.Playing);

        Assert.Equal(new QueuePresentation("1 item", "Now playing: Sungba", "No upcoming items", true, false, true, "Playing"), queue);
    }

    [Theory]
    [InlineData(SessionState.Opening, "Playing")]
    [InlineData(SessionState.Playing, "Playing")]
    [InlineData(SessionState.Buffering, "Playing")]
    [InlineData(SessionState.Seeking, "Playing")]
    [InlineData(SessionState.Stopping, "Playing")]
    [InlineData(SessionState.Paused, "Paused")]
    [InlineData(SessionState.Ready, "Paused")]
    public void ThePlayingLineSaysWhetherItPlaysOrIsPaused(SessionState state, string said)
    {
        var queue = QueuePresentation.Of(2, Sungba, Terminator, Sungba, state);

        Assert.Equal(said, queue.CurrentState);
        Assert.Equal("Now playing: Sungba", queue.Now);
        Assert.Equal("Up next: Terminator", queue.Next);
        Assert.True(queue.ShowsDragHint);
    }

    [Theory]
    [InlineData(SessionState.Idle)]
    [InlineData(SessionState.Ended)]
    [InlineData(SessionState.Faulted)]
    public void AStoppedQueueKeepsItsCurrentEntryButNothingPlays(SessionState state)
    {
        var queue = QueuePresentation.Of(2, Sungba, Terminator, Sungba, state);

        Assert.Equal("Nothing playing", queue.Now);
        Assert.Null(queue.CurrentState);
        Assert.True(queue.CanJump);
        Assert.Equal("2 items", queue.Count);
    }

    [Fact]
    public void MediaFromOutsideTheQueueIsNotAnEntryToJumpTo()
    {
        var queue = QueuePresentation.Of(2, null, Sungba, Terminator, SessionState.Playing);

        Assert.Equal("Current media outside the queue", queue.Now);
        Assert.False(queue.CanJump);
        Assert.Null(queue.CurrentState);
    }

    [Fact]
    public void NothingHeldIsNothingPlayingWhateverTheState()
    {
        var queue = QueuePresentation.Of(2, Sungba, Terminator, null, SessionState.Playing);

        Assert.Equal("Nothing playing", queue.Now);
        Assert.Null(queue.CurrentState);
    }
}
