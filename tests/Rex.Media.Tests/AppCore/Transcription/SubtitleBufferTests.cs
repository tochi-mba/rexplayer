using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class SubtitleBufferTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeRange R(double start, double end) => new(S(start), S(end));

    [Fact]
    [Capability("SUB-21")]
    public void TheSeekBarShowsTheStretchesReadyAsShares()
    {
        var view = SubtitleBuffer.View(TimeRanges.Of([R(0, 50), R(100, 150)]), S(200), S(10), hearing: true);
        Assert.Equal([(0.0, 0.25), (0.5, 0.75)], view.Spans);
        Assert.Equal(BufferState.Ahead, view.State);
        Assert.Equal("", view.Label);
        Assert.Equal("Subtitles are ready for 1:40 of 3:20, 0:40 ahead of where you are. Transcribing ahead of you.", view.ToolTip);

        // Past the end of the file a stretch is cut at the end.
        Assert.Equal([(0.5, 1.0)], SubtitleBuffer.View(TimeRanges.Of([R(100, 300)]), S(200), S(0), hearing: false).Spans);

        // Nothing for a file of unknown length or nothing being heard.
        Assert.Same(BufferView.Hidden, SubtitleBuffer.View(null, S(200), S(0), hearing: false));
        Assert.Same(BufferView.Hidden, SubtitleBuffer.View(TimeRanges.Empty, TimeSpan.Zero, S(0), hearing: true));
        Assert.Empty(BufferView.Hidden.Spans);
        Assert.Equal(BufferState.None, BufferView.Hidden.State);
    }

    [Fact]
    [Capability("SUB-21")]
    public void CatchingUpWithTheTranscriptionSaysSoRatherThanMissingSubtitles()
    {
        var done = TimeRanges.Of([R(0, 50)]);
        var catching = SubtitleBuffer.View(done, S(200), S(47), hearing: true);
        Assert.Equal((BufferState.CatchingUp, "Transcribing ahead..."), (catching.State, catching.Label));
        Assert.Equal(BufferState.CatchingUp, SubtitleBuffer.View(done, S(200), S(60), hearing: true).State);

        var stalled = SubtitleBuffer.View(done, S(200), S(60), hearing: false);
        Assert.Equal((BufferState.Stalled, "No subtitles here yet"), (stalled.State, stalled.Label));
        Assert.Equal("Subtitles are ready for 0:50 of 3:20.", stalled.ToolTip);

        // Close to the end but ready to it is ahead; all ready is complete.
        Assert.Equal(BufferState.Ahead, SubtitleBuffer.View(TimeRanges.Of([R(100, 201)]), S(201), S(199), hearing: true).State);
        var whole = SubtitleBuffer.View(TimeRanges.Of([R(0, 200)]), S(200), S(199), hearing: false);
        Assert.Equal((BufferState.Complete, "", "Subtitles are ready for the whole of it."), (whole.State, whole.Label, whole.ToolTip));

        Assert.Equal(BufferState.CatchingUp, SubtitleBuffer.StateAt(done, S(200), S(47), hearing: true));
        Assert.Equal(BufferState.Stalled, SubtitleBuffer.StateAt(done, S(200), S(60), hearing: false));
        Assert.Equal(BufferState.Ahead, SubtitleBuffer.StateAt(done, S(200), S(10), hearing: false));
        Assert.Equal(BufferState.None, SubtitleBuffer.StateAt(null, S(200), S(10), hearing: true));
        Assert.Equal(BufferState.None, SubtitleBuffer.StateAt(done, TimeSpan.Zero, S(10), hearing: true));
        Assert.Equal("Subtitles ready here", SubtitleBuffer.HintAt(done, S(10)));
        Assert.Equal("No subtitles here yet", SubtitleBuffer.HintAt(done, S(60)));
        Assert.Throws<ArgumentNullException>(() => SubtitleBuffer.HintAt(null!, S(1)));
        Assert.Equal(S(3), SubtitleBuffer.Ahead(done, S(47)));
        Assert.Equal(TimeSpan.Zero, SubtitleBuffer.Ahead(done, S(51)));
        Assert.Throws<ArgumentNullException>(() => SubtitleBuffer.Ahead(null!, S(1)));
    }

    [Fact]
    [Capability("SUB-21")]
    public void PlaybackHoldsForTheSubtitlesOnlyWhenAllowedAndCarriesOnOnceTheyAreAhead()
    {
        var done = TimeRanges.Of([R(0, 50)]);
        Assert.Equal(HoldAction.Hold, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: true, done, S(50), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: false, holding: false, hearing: true, done, S(50), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: false, done, S(50), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: true, done, S(40), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: true, done, S(200), S(200)));

        // Held: until fifteen seconds are ready, the end is ready, hearing stops or the setting goes.
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(40), S(200)));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(35), S(200)));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(45), S(50)));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: false, done, S(49), S(200)));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(allowed: false, holding: true, hearing: true, done, S(49), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(60), S(200)));
        Assert.Throws<ArgumentNullException>(() => SubtitleBuffer.Hold(true, false, true, null!, S(1), S(2)));
    }

    [Fact]
    public void ASlowTranscriberBuildsEnoughLeadToAvoidRepeatedPauses()
    {
        Assert.Equal(S(15), SubtitleBuffer.ReleaseTarget(4, 1, S(10), S(200)));
        Assert.Equal(S(57), SubtitleBuffer.ReleaseTarget(0.5, 1, S(10), S(200)));
        Assert.Equal(S(90), SubtitleBuffer.ReleaseTarget(0.1, 2, S(10), S(500)));

        var fiftySecondsAhead = TimeRanges.Of([R(0, 60)]);
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, true, true, fiftySecondsAhead, S(10), S(200), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, true, TimeRanges.Of([R(0, 70)]), S(10), S(200), 0.5, 1));
    }

    [Fact]
    public void AdaptiveLeadHandlesRateChangesEndsAndBadMeasurements()
    {
        Assert.Equal(S(31), SubtitleBuffer.ReleaseTarget(1, 1, S(0), S(200)));
        Assert.Equal(S(57), SubtitleBuffer.ReleaseTarget(1, 2, S(0), S(200)));
        Assert.Equal(S(8), SubtitleBuffer.ReleaseTarget(0.1, 2, S(42), S(50)));
        Assert.Equal(S(15), SubtitleBuffer.ReleaseTarget(double.NaN, double.NaN, S(0), S(200)));
        Assert.Equal(TimeSpan.Zero, SubtitleBuffer.ReleaseTarget(1, 1, S(50), S(50)));
    }
}
