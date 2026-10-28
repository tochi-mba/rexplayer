using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The transport's clocks and rail: what they read for a position, a length and the player's state.</summary>
public sealed class TimeTextTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    [Capability("PB-05")]
    public void AClipShorterThanASecondFillsItsRail()
    {
        var timeline = TimeText.Timeline(S(0.4), S(0.4));

        Assert.Equal(0.4, timeline.SliderMaximum, 9);
        Assert.Equal(timeline.SliderMaximum, timeline.SliderValue, 9);
        Assert.Equal(TimeSpan.Zero, timeline.Remaining);
    }

    [Theory]
    [InlineData(0.1, 0.4)]
    [InlineData(12, 20)]
    [InlineData(-3, 20)]
    [Capability("PB-05")]
    public void AnEndedItemStandsAtItsEnd(double position, double duration)
    {
        var timeline = TimeText.Timeline(S(position), S(duration), ended: true);

        Assert.Equal(S(duration), timeline.Elapsed);
        Assert.Equal(duration, timeline.SliderValue, 9);
        Assert.Equal(duration, timeline.SliderMaximum, 9);
        Assert.Equal(TimeSpan.Zero, timeline.Remaining);
    }

    [Theory]
    [InlineData(0.5, 20, 20)]
    [InlineData(0.5, 19.97, 19)]
    [InlineData(19.5, 20, 1)]
    [InlineData(59.9, 3600.4, 3541)]
    [Capability("PB-05")]
    public void TimePlayedAndTimeLeftAddUpToTheLengthShown(double position, double duration, double left)
    {
        var timeline = TimeText.Timeline(S(position), S(duration));

        Assert.Equal(S(left), timeline.Remaining);
        Assert.Equal(Math.Floor(duration), Math.Floor(timeline.Elapsed.TotalSeconds) + timeline.Remaining!.Value.TotalSeconds);
    }

    [Theory]
    [InlineData(0.5, 20, SessionState.Paused, true, "0:00", "-0:20")]
    [InlineData(19.5, 20, SessionState.Playing, true, "0:19", "-0:01")]
    [InlineData(0.4, 0.4, SessionState.Ended, true, "0:00", "0:00")]
    [InlineData(0.1, 20, SessionState.Ended, true, "0:20", "0:00")]
    [InlineData(0, 3600, SessionState.Ended, true, "1:00:00", "0:00:00")]
    [InlineData(30, 100, SessionState.Playing, false, "0:30", "1:40")]
    [InlineData(5, 0, SessionState.Playing, true, "0:05", TimeText.Unknown)]
    [InlineData(0, 0, SessionState.Opening, true, "0:00", TimeText.Unknown)]
    [InlineData(12, 60, SessionState.Faulted, true, "0:12", "-0:48")]
    [Capability("PB-05")]
    public void TheClocksReadWhatIsPlayedAndWhatIsLeft(double position, double duration, SessionState state, bool showRemaining,
        string elapsed, string remaining)
    {
        Assert.Equal((elapsed, remaining), TimeText.Clocks(S(position), S(duration), state, showRemaining));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 30)]
    [InlineData(SessionState.Faulted, 0)]
    public void NothingLoadedOrAFailureBeforeTheLengthIsKnownReadsUnknownOnBothClocks(SessionState? state, double duration)
    {
        Assert.Equal((TimeText.Unknown, TimeText.Unknown), TimeText.Clocks(TimeSpan.Zero, S(duration), state, showRemaining: true));
    }
}
