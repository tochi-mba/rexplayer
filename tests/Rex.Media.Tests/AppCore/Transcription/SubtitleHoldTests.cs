using Rex.Media.AppCore.Transcription;
using Rex.Media.Engine;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class SubtitleHoldTests
{
    [Fact]
    public void PositionAndProgressBeforePauseAcknowledgementDoNotDismissTheWait()
    {
        var hold = new SubtitleHold();
        Assert.Equal(HoldAction.Hold, hold.Update(true, SessionState.Playing, HoldAction.Hold));
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Playing, HoldAction.Nothing));
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Buffering, HoldAction.Nothing));
        Assert.True(hold.Holding);
        Assert.False(hold.Overridden);
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Paused, HoldAction.Nothing));
        Assert.Equal(HoldAction.Release, hold.Update(true, SessionState.Paused, HoldAction.Release));
        Assert.False(hold.Holding);
    }

    [Fact]
    public void ReadinessOrDisablingWhilePauseIsPendingWaitsForAcknowledgement()
    {
        foreach (var allowed in new[] { true, false })
        {
            var hold = new SubtitleHold();
            hold.Update(true, SessionState.Playing, HoldAction.Hold);
            Assert.Equal(HoldAction.Nothing, hold.Update(allowed, SessionState.Playing, HoldAction.Release));
            Assert.True(hold.Holding);
            Assert.Equal(HoldAction.Release, hold.Update(allowed, SessionState.Paused, HoldAction.Release));
        }
    }

    [Fact]
    public void PlayNowWaivesOneGapAndASeekOrReadyStretchAllowsWaitingAgain()
    {
        var hold = new SubtitleHold();
        hold.Update(true, SessionState.Playing, HoldAction.Hold);
        hold.Update(true, SessionState.Paused, HoldAction.Nothing);
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Seeking, HoldAction.Release));
        Assert.True(hold.Holding);
        hold.Update(true, SessionState.Paused, HoldAction.Nothing);
        hold.Update(true, SessionState.Playing, HoldAction.Nothing);
        Assert.True(hold.Overridden);
        Assert.False(hold.Holding);
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Playing, HoldAction.Hold));
        hold.Update(true, SessionState.Seeking, HoldAction.Hold);
        Assert.False(hold.Overridden);
        Assert.Equal(HoldAction.Hold, hold.Update(true, SessionState.Playing, HoldAction.Hold));
        hold.PlayNow();
        Assert.True(hold.Overridden);
        hold.AwaitCoverage();
        Assert.False(hold.Overridden);
        Assert.False(hold.Holding);
        hold.PlayNow();
        hold.Update(true, SessionState.Playing, HoldAction.Nothing);
        Assert.False(hold.Overridden);
    }

    [Fact]
    public void AQualityChangeKeepsTheWaitUntilTheNewTranscriptsCoverageArrives()
    {
        var hold = new SubtitleHold();
        hold.Update(true, SessionState.Playing, HoldAction.Hold);
        hold.Update(true, SessionState.Paused, HoldAction.Nothing);
        hold.AwaitCoverage();

        // While the new model's saved coverage loads, nothing asks for the wait: it must not end.
        Assert.Equal(HoldAction.Nothing, hold.Update(false, SessionState.Paused, HoldAction.Release));
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Paused, HoldAction.Release));
        Assert.True(hold.Holding);

        hold.CoverageReady();
        Assert.Equal(HoldAction.Nothing, hold.Update(true, SessionState.Paused, HoldAction.Nothing));
        Assert.True(hold.Holding);
        Assert.Equal(HoldAction.Release, hold.Update(true, SessionState.Paused, HoldAction.Release));
        Assert.False(hold.Holding);
    }

    [Fact]
    public void AwaitingCoverageNeverTrapsTheViewer()
    {
        // Play pressed during the reload is the viewer's choice and is honoured at once.
        var played = new SubtitleHold();
        played.Update(true, SessionState.Playing, HoldAction.Hold);
        played.Update(true, SessionState.Paused, HoldAction.Nothing);
        played.AwaitCoverage();
        Assert.Equal(HoldAction.Nothing, played.Update(false, SessionState.Playing, HoldAction.Nothing));
        Assert.False(played.Holding);
        Assert.True(played.Overridden);
        played.CoverageReady();
        Assert.Equal(HoldAction.Nothing, played.Update(true, SessionState.Playing, HoldAction.Hold));

        // Stopping ends the wait and forgets the reload.
        var stopped = new SubtitleHold();
        stopped.Update(true, SessionState.Playing, HoldAction.Hold);
        stopped.AwaitCoverage();
        stopped.Update(true, SessionState.Stopping, HoldAction.Nothing);
        Assert.False(stopped.Holding);
        Assert.Equal(HoldAction.Hold, stopped.Update(true, SessionState.Playing, HoldAction.Hold));
        Assert.Equal(HoldAction.Release, stopped.Update(true, SessionState.Paused, HoldAction.Release));

        // Without a wait in progress there is nothing to keep.
        var idle = new SubtitleHold();
        idle.AwaitCoverage();
        Assert.Equal(HoldAction.Nothing, idle.Update(true, SessionState.Paused, HoldAction.Release));
        Assert.Equal(HoldAction.Hold, idle.Update(true, SessionState.Playing, HoldAction.Hold));
        Assert.Equal(HoldAction.Release, idle.Update(true, SessionState.Paused, HoldAction.Release));
    }

    [Theory]
    [InlineData(SessionState.Idle)]
    [InlineData(SessionState.Stopping)]
    [InlineData(SessionState.Ended)]
    [InlineData(SessionState.Faulted)]
    public void StoppingNeverResumesTheOldItem(SessionState state)
    {
        var hold = new SubtitleHold();
        hold.Update(true, SessionState.Playing, HoldAction.Hold);
        Assert.Equal(HoldAction.Nothing, hold.Update(true, state, HoldAction.Release));
        Assert.False(hold.Holding);
        Assert.False(hold.Overridden);
    }

    [Theory]
    [InlineData(SessionState.Opening)]
    [InlineData(SessionState.Seeking)]
    [InlineData(SessionState.Ready)]
    [InlineData(SessionState.Paused)]
    public void OnlyStatesThatAcceptPauseMayStartAHold(SessionState state)
    {
        var hold = new SubtitleHold();
        Assert.Equal(HoldAction.Nothing, hold.Update(true, state, HoldAction.Hold));
        Assert.False(hold.Holding);
    }
}
