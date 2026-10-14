using System.Globalization;
using Rex.Media.AppCore.Player;

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

    /// <summary>The largest buffer retained before playback resumes; transcription stays bounded and near the playhead.</summary>
    public static readonly TimeSpan MostReleaseAhead = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How far subtitles should lead before a held player resumes. The measured transcription
    /// factor is seconds of media heard per wall-clock second. When hearing is slower than playback,
    /// one chunk's processing time is covered so playback does not repeatedly stop between chunks.
    /// Unknown or invalid measurements use the conservative fixed buffer.
    /// </summary>
    public static TimeSpan ReleaseTarget(double? transcriptionFactor, double playbackRate, TimeSpan playhead, TimeSpan duration)
    {
        playbackRate = double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
        var wanted = ReleaseAhead;
        if (transcriptionFactor is { } factor && factor > 0 && double.IsFinite(factor))
        {
            var seconds = ChunkPlanner.Length.TotalSeconds * playbackRate / factor + Comfortable.TotalSeconds;
            wanted = TimeSpan.FromSeconds(Math.Clamp(seconds, ReleaseAhead.TotalSeconds, MostReleaseAhead.TotalSeconds));
        }

        var remaining = duration - playhead;
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : wanted < remaining ? wanted : remaining;
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

    /// <summary>An adaptive hold/release policy, capped so work and memory stay near the playhead.</summary>
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

        return allowed && hearing && ahead < TimeSpan.FromSeconds(0.5) && playhead < duration ? HoldAction.Hold : HoldAction.Nothing;
    }

    private static double Share(TimeSpan time, TimeSpan duration) => Math.Clamp(time / duration, 0, 1);
}
