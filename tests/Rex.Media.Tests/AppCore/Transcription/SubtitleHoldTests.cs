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
        hold.Update(true, SessionState.Playing, HoldAction.Nothing);
        Assert.False(hold.Overridden);
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
