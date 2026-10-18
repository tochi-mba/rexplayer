using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.AppCore.Captions;

namespace Rex.Media.AppCore.Transcription;

/// <summary>Where the playhead stands against the subtitles heard for what plays.</summary>
public enum BufferState
{
    /// <summary>Nothing is being transcribed for what plays: no buffer shows.</summary>
    None,

    /// <summary>Subtitles are ready for the whole of it.</summary>
    Complete,

    /// <summary>Subtitles are ready a comfortable way past the playhead.</summary>
    Ahead,

    /// <summary>Playback has caught up with the transcription, which is hearing ahead of it now.</summary>
    CatchingUp,

    /// <summary>The playhead has no subtitles and nothing is hearing them (paused, or it failed).</summary>
    Stalled,
}

/// <summary>What the seek bar shows of the subtitles ready: the stretches, the state and the words for them.</summary>
/// <param name="Spans">The stretches ready, as shares of the timeline (0 to 1), in order.</param>
/// <param name="State">Where the playhead stands.</param>
/// <param name="Label">The small text beside the seek bar; empty when there is nothing to say.</param>
/// <param name="ToolTip">What the buffer's tooltip says.</param>
public sealed record BufferView(IReadOnlyList<(double From, double To)> Spans, BufferState State, string Label, string ToolTip)
{
    public static BufferView Hidden { get; } = new([], BufferState.None, "", "");
}

/// <summary>What the window should do about holding playback for the subtitles.</summary>
public enum HoldAction
{
    Nothing,
    Hold,
    Release,
}

/// <summary>
/// The subtitle buffer on the seek bar (SUB-21): like a download's, a second track shows the
/// stretches whose subtitles are ready, and when playback catches the edge a small "Transcribing
/// ahead..." says so rather than subtitles silently missing. Optionally (never by default) playback
/// holds until the subtitles are a little ahead again. Everything here is a function of the
/// stretches heard, so the window redraws only when they change.
/// </summary>
public static class SubtitleBuffer
{
    /// <summary>Ready this far past the playhead counts as ahead; less, while hearing, as catching up.</summary>
    public static readonly TimeSpan Comfortable = TimeSpan.FromSeconds(5);

    /// <summary>A held playback carries on once the subtitles are this far ahead.</summary>
    public static readonly TimeSpan ReleaseAhead = TimeSpan.FromSeconds(15);

    /// <summary>Generated tracks from another quality are not a substitute for the requested transcript.</summary>
    public static bool HasUsableSubtitles(IEnumerable<SubtitleTrack> tracks) =>
        tracks.Any(track => track.Count > 0 && TranscriptVariant.FromTrackName(track.Name) is null);

    /// <summary>Restores the coverage rail for completed, partial, or newly requested transcription.</summary>
    public static TimeRanges? Coverage(bool complete, TimeRanges? partial, bool requested, TimeSpan duration) =>
        complete ? duration > TimeSpan.Zero ? TimeRanges.Of([new TimeRange(TimeSpan.Zero, duration)]) : TimeRanges.Empty
            : partial ?? (requested ? TimeRanges.Empty : null);

    /// <summary>Existing subtitles avoid a transcription wait unless kinetic captions use the transcript alone.</summary>
    public static bool ShouldWaitForTranscript(bool hasUsableSubtitles, CaptionWordSource words) =>
        !hasUsableSubtitles || words == CaptionWordSource.Transcript;

    /// <summary>
    /// How far subtitles must lead before held playback may safely resume. A faster
    /// transcriber only needs one chunk of reserve. If recognition is slower than playback,
    /// prebuffer the predicted lifetime deficit across the entire remaining movie: the
    /// playback rate minus the measured recognition rate, times remaining media length.
    /// Include the cost of a complete slow chunk so transient processing delays do not
    /// exhaust the reserve. There is deliberately no 90-second cap: on slow hardware
    /// uninterrupted playback can require much longer startup, up to the whole transcript.
    /// </summary>
    public static TimeSpan ReleaseTarget(double? transcriptionFactor, double playbackRate, TimeSpan playhead, TimeSpan duration)
    {
        var remaining = duration - playhead;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        playbackRate = double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
        // Without the first measurement, start with one whole completed chunk rather than
        // guessing the model's speed from its advertised model size.
        var wanted = ChunkPlanner.Length.TotalSeconds + Comfortable.TotalSeconds;
        if (transcriptionFactor is { } factor && factor > 0 && double.IsFinite(factor))
        {
            var relative = factor / playbackRate;
            var deficit = remaining.TotalSeconds * Math.Max(0, 1 - relative);
            var slowChunk = ChunkPlanner.Length.TotalSeconds * playbackRate / factor;
            wanted = Math.Max(ReleaseAhead.TotalSeconds, deficit + slowChunk + Comfortable.TotalSeconds);
        }

        return TimeSpan.FromSeconds(Math.Min(remaining.TotalSeconds, wanted));
    }

    /// <summary>How long ready subtitles run on past <paramref name="playhead"/> without a gap.</summary>
    public static TimeSpan Ahead(TimeRanges done, TimeSpan playhead)
    {
        ArgumentNullException.ThrowIfNull(done);
        return done.At(playhead) is { } holding ? holding.End - playhead : TimeSpan.Zero;
    }

    /// <summary>
    /// The buffer for a file of <paramref name="duration"/> with <paramref name="done"/> heard, the
    /// playhead at <paramref name="playhead"/>; <paramref name="hearing"/> says whether it is being heard now.
    /// </summary>
    public static BufferView View(TimeRanges? done, TimeSpan duration, TimeSpan playhead, bool hearing)
    {
        if (done is null || duration <= TimeSpan.Zero)
        {
            return BufferView.Hidden;
        }

        var spans = done.Ranges.Select(range => (Share(range.Start, duration), Share(range.End, duration))).Where(span => span.Item2 > span.Item1).ToList();
        var whole = new TimeRange(TimeSpan.Zero, duration);
        var ready = whole.Length - done.Gaps(whole).Aggregate(TimeSpan.Zero, (sum, gap) => sum + gap.Length);
        var ahead = Ahead(done, playhead);
        var complete = done.Covers(whole);
        var state = StateAt(done, duration, playhead, hearing);
        var label = state switch
        {
            BufferState.CatchingUp => "Transcribing ahead...",
            BufferState.Stalled => "No subtitles here yet",
            _ => "",
        };
        var tip = complete ? "Subtitles are ready for the whole of it."
            : string.Create(CultureInfo.InvariantCulture, $"Subtitles are ready for {TimeText.Format(ready, duration)} of {TimeText.Format(duration, duration)}")
                + (ahead > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $", {TimeText.Format(ahead, duration)} ahead of where you are.") : ".")
                + (hearing ? " Transcribing ahead of you." : "");
        return new BufferView(spans, state, label, tip);
    }

    /// <summary>
    /// Where the playhead stands, with nothing made but the answer: two binary searches, so the
    /// window can ask at every position report and redraw only when the state changes.
    /// </summary>
    public static BufferState StateAt(TimeRanges? done, TimeSpan duration, TimeSpan playhead, bool hearing)
    {
        if (done is null || duration <= TimeSpan.Zero)
        {
            return BufferState.None;
        }

        var ahead = Ahead(done, playhead);
        return done.Covers(new TimeRange(TimeSpan.Zero, duration)) ? BufferState.Complete
            : ahead >= Comfortable || (ahead > TimeSpan.Zero && playhead + ahead >= duration) ? BufferState.Ahead
            : hearing ? BufferState.CatchingUp
            : BufferState.Stalled;
    }

    /// <summary>What the timeline preview says under the pointer at <paramref name="at"/>: whether subtitles are ready there.</summary>
    public static string HintAt(TimeRanges done, TimeSpan at)
    {
        ArgumentNullException.ThrowIfNull(done);
        return done.At(at) is null ? "No subtitles here yet" : "Subtitles ready here";
    }

    /// <summary>
    /// Whether to hold or release playback for the subtitles: held, when the user allows it, while
    /// what plays is being heard and the playhead has run out of subtitles; released once they are
    /// <see cref="ReleaseAhead"/> ahead, reach the end, or hearing stops or the setting goes off.
    /// </summary>
    public static HoldAction Hold(bool allowed, bool holding, bool hearing, TimeRanges done, TimeSpan playhead, TimeSpan duration)
        => Hold(allowed, holding, hearing, done, playhead, duration, null, 1);

    /// <summary>
    /// Prebuffer before playback runs out: release at the predicted no-rebuffer target, but
    /// trigger another hold below 80% of it to avoid toggling play/pause on every progress
    /// update. The user can always manually play instead.
    /// </summary>
    public static HoldAction Hold(bool allowed, bool holding, bool hearing, TimeRanges done, TimeSpan playhead, TimeSpan duration,
        double? transcriptionFactor, double playbackRate)
    {
        ArgumentNullException.ThrowIfNull(done);
        var ahead = Ahead(done, playhead);
        if (holding)
        {
            var target = ReleaseTarget(transcriptionFactor, playbackRate, playhead, duration);
            return !allowed || !hearing || ahead >= target || (ahead > TimeSpan.Zero && playhead + ahead >= duration) ? HoldAction.Release : HoldAction.Nothing;
        }

        var targetAhead = ReleaseTarget(transcriptionFactor, playbackRate, playhead, duration);
        var lowWater = TimeSpan.FromSeconds(Math.Max(0.5, targetAhead.TotalSeconds * 0.8));
        return allowed && hearing && ahead < lowWater && playhead < duration ? HoldAction.Hold : HoldAction.Nothing;
    }

    private static double Share(TimeSpan time, TimeSpan duration) => Math.Clamp(time / duration, 0, 1);
}
