using Rex.Media.AppCore.Transcription;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class SubtitleBufferTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeRange R(double start, double end) => new(S(start), S(end));

    [Fact]
    public void CachedAndRequestedTranscriptsKeepTheirCoverageVisible()
    {
        var ready = SubtitleBuffer.Coverage(true, null, false, S(200));
        Assert.Equal(BufferState.Complete, SubtitleBuffer.View(ready, S(200), S(0), false).State);
        Assert.Equal([(0d, 1d)], SubtitleBuffer.View(ready, S(200), S(150), false).Spans);
        Assert.Same(TimeRanges.Empty, SubtitleBuffer.Coverage(true, null, false, S(0)));
        Assert.Same(TimeRanges.Empty, SubtitleBuffer.Coverage(false, null, true, S(200)));
        Assert.Null(SubtitleBuffer.Coverage(false, null, false, S(200)));
        var partial = TimeRanges.Of([R(40, 80)]);
        Assert.Same(partial, SubtitleBuffer.Coverage(false, partial, true, S(200)));
    }

    [Theory]
    [InlineData("Transcript (tiny.en)", false)]
    [InlineData("Transcript (base.en)", false)]
    [InlineData("Transcript (small.en)", false)]
    [InlineData("Transcript (tiny.en, original)", false)]
    [InlineData("Transcript (base.en, edited)", false)]
    [InlineData("Transcript (small.en, edited)", false)]
    [InlineData("English", true)]
    [InlineData("film.en.srt", true)]
    public void PreviousQualityTracksDoNotDisableWaitingForTheNewTranscript(string name, bool usable)
    {
        var track = new SubtitleTrack(name);
        Assert.False(SubtitleBuffer.HasUsableSubtitles([track]));
        track.Add(new SubtitleCue(S(0), S(2), [new SubtitleLine([new SubtitleRun("Hello")])]));
        Assert.Equal(usable, SubtitleBuffer.HasUsableSubtitles([track]));
        Assert.Equal(!usable, SubtitleBuffer.ShouldWaitForTranscript(SubtitleBuffer.HasUsableSubtitles([track]), CaptionWordSource.Automatic));
        Assert.True(SubtitleBuffer.ShouldWaitForTranscript(SubtitleBuffer.HasUsableSubtitles([track]), CaptionWordSource.Transcript));
    }

    [Theory]
    [InlineData(false, CaptionWordSource.Automatic, true)]
    [InlineData(false, CaptionWordSource.Subtitles, true)]
    [InlineData(false, CaptionWordSource.Transcript, true)]
    [InlineData(false, CaptionWordSource.SubtitlesTimedByTranscript, true)]
    [InlineData(true, CaptionWordSource.Automatic, false)]
    [InlineData(true, CaptionWordSource.Subtitles, false)]
    [InlineData(true, CaptionWordSource.Transcript, true)]
    [InlineData(true, CaptionWordSource.SubtitlesTimedByTranscript, false)]
    public void ExistingSubtitlesAvoidWaitingUnlessTranscriptWordsWereChosen(bool hasSubtitles, CaptionWordSource words, bool expected) =>
        Assert.Equal(expected, SubtitleBuffer.ShouldWaitForTranscript(hasSubtitles, words));

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
        Assert.Equal(HoldAction.Hold, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: true, done, S(40), S(200)));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: false, hearing: true, done, S(200), S(200)));

        // Held: wait for a complete chunk without a measured rate; finish early at the end.
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(40), S(200)));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(allowed: true, holding: true, hearing: true, done, S(10), S(200)));
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
        Assert.Equal(S(152), SubtitleBuffer.ReleaseTarget(0.5, 1, S(10), S(200)));
        Assert.Equal(S(490), SubtitleBuffer.ReleaseTarget(0.1, 2, S(10), S(500)));

        var fiftySecondsAhead = TimeRanges.Of([R(0, 60)]);
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, true, true, fiftySecondsAhead, S(10), S(200), 0.5, 1));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, true, true, TimeRanges.Of([R(0, 70)]), S(10), S(200), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, true, TimeRanges.Of([R(0, 165)]), S(10), S(200), 0.5, 1));
    }

    [Fact]
    public void SlowerThanPlaybackUsesTheAvailableReservoirInsteadOfPausingEachChunk()
    {
        // 26 seconds of speech takes 52 seconds to transcribe. Without waiting for the
        // entire expected remaining deficit, a 90-second cap will stall repeatedly.
        var slow = SubtitleBuffer.ReleaseTarget(0.5, 1, S(0), S(600));
        Assert.Equal(S(357), slow);
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, true, true,
            TimeRanges.Of([R(0, 80)]), S(0), S(600), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, true,
            TimeRanges.Of([R(0, 360)]), S(0), S(600), 0.5, 1));

        // Faster recognition needs less waiting; playback speed must count.
        Assert.Equal(S(15), SubtitleBuffer.ReleaseTarget(4, 1, S(0), S(600)));
        Assert.Equal(S(559), SubtitleBuffer.ReleaseTarget(0.5, 2, S(0), S(600)));
        Assert.Equal(S(6), SubtitleBuffer.ReleaseTarget(0.5, 2, S(94), S(100)));
    }

    [Fact]
    public void AdaptiveLeadHandlesRateChangesEndsAndBadMeasurements()
    {
        Assert.Equal(S(31), SubtitleBuffer.ReleaseTarget(1, 1, S(0), S(200)));
        Assert.Equal(S(157), SubtitleBuffer.ReleaseTarget(1, 2, S(0), S(200)));
        Assert.Equal(S(8), SubtitleBuffer.ReleaseTarget(0.1, 2, S(42), S(50)));
        Assert.Equal(S(31), SubtitleBuffer.ReleaseTarget(double.NaN, double.NaN, S(0), S(200)));
        Assert.Equal(TimeSpan.Zero, SubtitleBuffer.ReleaseTarget(1, 1, S(50), S(50)));
    }

    [Theory]
    [InlineData(600, 0.5, 1)]
    [InlineData(1200, 0.25, 1)]
    [InlineData(3600, 1, 1.5)]
    [InlineData(300, 4, 1)]
    public void AConstantRecognitionRateNeverRunsOutAfterTheInitialWait(int length, double factor, double playbackSpeed)
    {
        var end = S(length);
        var lead = SubtitleBuffer.ReleaseTarget(factor, playbackSpeed, TimeSpan.Zero, end);
        Assert.True(lead <= end);
        // The initial user-selected hold builds that much continuous subtitle coverage.
        Assert.Equal(HoldAction.Hold, SubtitleBuffer.Hold(true, false, true,
            TimeRanges.Empty, TimeSpan.Zero, end, factor, playbackSpeed));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, true,
            TimeRanges.Of([R(0, lead.TotalSeconds)]), TimeSpan.Zero, end, factor, playbackSpeed));

        // Time passes in wall-clock seconds. The recognizer continues ahead while the
        // playhead moves at its chosen rate. At no point should playback need another hold.
        for (var second = 0; second * playbackSpeed < length; second++)
        {
            var position = S(second * playbackSpeed);
            var available = S(Math.Min(length, lead.TotalSeconds + factor * second));
            var heard = TimeRanges.Of([new TimeRange(TimeSpan.Zero, available)]);
            Assert.NotEqual(HoldAction.Hold, SubtitleBuffer.Hold(true, false, true,
                heard, position, end, factor, playbackSpeed));
        }
    }

    [Fact]
    public void SlowHardwareWaitsPastNinetySecondsWhenThatIsNeededToAvoidAStall()
    {
        // One media second of transcription takes two seconds. For a one-hour movie,
        // half the movie must be ready before starting, plus an uncertain next chunk.
        var target = SubtitleBuffer.ReleaseTarget(0.5, 1, TimeSpan.Zero, S(3600));
        Assert.Equal(S(1857), target);
        Assert.Equal(HoldAction.Hold, SubtitleBuffer.Hold(true, false, true,
            TimeRanges.Of([R(0, 90)]), TimeSpan.Zero, S(3600), 0.5, 1));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, true, true,
            TimeRanges.Of([R(0, 90)]), TimeSpan.Zero, S(3600), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, true,
            TimeRanges.Of([R(0, 1857)]), TimeSpan.Zero, S(3600), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(false, true, true,
            TimeRanges.Empty, TimeSpan.Zero, S(3600), 0.5, 1));
        Assert.Equal(HoldAction.Release, SubtitleBuffer.Hold(true, true, false,
            TimeRanges.Empty, TimeSpan.Zero, S(3600), 0.5, 1));
    }

    [Fact]
    public void NoRateYetWaitsForAFullFirstChunkAndShowsTheRemainingTail()
    {
        Assert.Equal(S(31), SubtitleBuffer.ReleaseTarget(null, 1, TimeSpan.Zero, S(900)));
        Assert.Equal(S(7), SubtitleBuffer.ReleaseTarget(0.01, 1, S(893), S(900)));
        Assert.Equal(HoldAction.Hold, SubtitleBuffer.Hold(true, false, true,
            TimeRanges.Empty, TimeSpan.Zero, S(900), null, 1));
        Assert.Equal(HoldAction.Nothing, SubtitleBuffer.Hold(true, false, false,
            TimeRanges.Empty, TimeSpan.Zero, S(900), null, 1));
    }

    [Theory]
    [InlineData(false, BufferState.Complete, false, true)]
    [InlineData(false, BufferState.Ahead, false, true)]
    [InlineData(false, BufferState.CatchingUp, false, true)]
    [InlineData(false, BufferState.Stalled, false, true)]
    [InlineData(false, BufferState.None, false, false)]
    [InlineData(true, BufferState.None, false, false)]
    [InlineData(true, BufferState.Complete, false, false)]
    [InlineData(false, BufferState.Complete, true, false)]
    public void TheRailShowsOnlyCoverageThereIsForSoundAndVideoWithRoomForIt(bool picture, BufferState state, bool compact, bool shown)
    {
        Assert.Equal(shown, SubtitleBuffer.RailShown(picture, state, compact));
    }
}
