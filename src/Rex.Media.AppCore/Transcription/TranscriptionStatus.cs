using System.Globalization;

namespace Rex.Media.AppCore.Transcription;

/// <summary>What a library file's transcription badge says.</summary>
public enum TranscriptBadge
{
    None,
    Ready,
    NoSpeech,
    Partial,
    Working,
    Queued,
    Failed,
    NotEligible,
}

/// <summary>A badge: its kind, its icon (a Segoe Fluent Icons character) and its words, which are also its tooltip and what a screen reader says.</summary>
public sealed record BadgeView(TranscriptBadge Kind, string Glyph, string Text)
{
    public static BadgeView Nothing { get; } = new(TranscriptBadge.None, "", "");
}

/// <summary>
/// How fast speech is being heard (SUB-20): seconds of sound a second of work, smoothed over the
/// pieces heard so one slow piece does not swing the estimate.
/// </summary>
public sealed class TranscriptionSpeed
{
    /// <summary>How much each new piece moves the estimate.</summary>
    private const double Weight = 0.3;

    private readonly object _gate = new();
    private double? _factor;
    private double? _workPerSecond;
    private double _deviation;
    private double _recentSlow;

    /// <summary>Seconds of new subtitle coverage per wall-clock second, smoothed for progress estimates.</summary>
    public double? Factor
    {
        get
        {
            lock (_gate)
            {
                return _factor;
            }
        }
    }

    /// <summary>
    /// A conservative rate for playback buffering. Smooth the processing cost (seconds of work
    /// per media second) and its absolute variation, while retaining recent slow chunks. Looking
    /// only at the average rate would resume playback right before a slow recognition pass.
    /// </summary>
    public double? SafeFactor
    {
        get
        {
            lock (_gate)
            {
                return _workPerSecond is { } cost ? 1 / Math.Max(cost + 2 * _deviation, _recentSlow) : null;
            }
        }
    }

    /// <summary>Notes a piece of <paramref name="sound"/> heard in <paramref name="took"/>.</summary>
    public void Heard(TimeSpan sound, TimeSpan took)
    {
        if (sound <= TimeSpan.Zero || took <= TimeSpan.Zero)
        {
            return;
        }

        var factor = sound / took;
        var cost = took / sound;
        lock (_gate)
        {
            _factor = _factor is { } before ? before + Weight * (factor - before) : factor;
            if (_workPerSecond is { } previous)
            {
                _deviation += Weight * (Math.Abs(cost - previous) - _deviation);
                _workPerSecond = previous + Weight * (cost - previous);
                _recentSlow = Math.Max(cost, _recentSlow * 0.85);
            }
            else
            {
                _workPerSecond = cost;
                _recentSlow = cost;
            }
        }
    }

    /// <summary>How long hearing <paramref name="sound"/> should take, or null before the first piece.</summary>
    public TimeSpan? TimeFor(TimeSpan sound) => Factor is { } factor ? sound / factor : null;
}

/// <summary>
/// The words of library transcription (SUB-20): the badge on each file, the line that says how
/// the queue is going, and what is said to a screen reader, which changes only when the state does.
/// </summary>
public static class TranscriptionStatus
{
    /// <summary>The badge for a file: being heard, waiting, heard, failed, or not one to hear.</summary>
    public static BadgeView Badge(TranscriptRecord? record, TranscriptionSkip skip, bool working, bool queued, double share, DateTimeOffset now)
    {
        if (working)
        {
            return new(TranscriptBadge.Working, Glyph(0xE895), string.Create(CultureInfo.InvariantCulture, $"Transcribing, {Percent(share)} done"));
        }

        return record?.State switch
        {
            TranscriptState.Done when record.Words > 0 => new(TranscriptBadge.Ready, Glyph(0xE7F0), string.Create(CultureInfo.InvariantCulture, $"Subtitles from its speech are ready ({record.Words} words)")),
            TranscriptState.Done => new(TranscriptBadge.NoSpeech, Glyph(0xE7F0), "Transcribed: no speech was heard"),
            TranscriptState.NoSound => new(TranscriptBadge.NotEligible, Glyph(0xE733), TranscriptionEligibility.Describe(TranscriptionSkip.NoSound)),
            TranscriptState.Failed => new(TranscriptBadge.Failed, Glyph(0xE7BA), "Not transcribed. " + (record.Reason is { Length: > 0 } reason ? reason.TrimEnd('.') + ". " : "")
                + (record.NextTry is { } next ? RetryIn(next - now) + " " : "") + "Choose Retry transcription to try now."),
            _ when queued => new(TranscriptBadge.Queued, Glyph(0xE823), record?.State == TranscriptState.Partial
                ? string.Create(CultureInfo.InvariantCulture, $"Waiting to be transcribed, {Percent(record.Share)} done")
                : "Waiting to be transcribed"),
            TranscriptState.Partial => new(TranscriptBadge.Partial, Glyph(0xE7F0), string.Create(CultureInfo.InvariantCulture, $"Partly transcribed, {Percent(record.Share)} done")),
            _ when skip is TranscriptionSkip.TooShort or TranscriptionSkip.NoSound => new(TranscriptBadge.NotEligible, Glyph(0xE733), TranscriptionEligibility.Describe(skip)),
            _ => BadgeView.Nothing,
        };
    }

    /// <summary>
    /// The queue's line: what is being heard, how far through the queue, and the time left; or why
    /// it waits; or that nothing is left.
    /// </summary>
    public static string Line(int position, int count, string? current, double share, TimeSpan? left, string? waiting)
    {
        if (waiting is not null)
        {
            return count > 0 ? string.Create(CultureInfo.InvariantCulture, $"Transcription: {waiting}, {count} waiting") : "Transcription: " + waiting;
        }

        if (current is null || count == 0)
        {
            return "Transcription: everything chosen is transcribed";
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"Transcribing {Math.Clamp(position, 1, count)} of {count}: {current} ({Percent(share)})");
        return left is { } time ? line + ", about " + About(time) + " left" : line;
    }

    /// <summary>
    /// What a screen reader is told when the queue's state changes (started, paused, waiting,
    /// finished, failed), and nothing when only its progress does.
    /// </summary>
    public static string? Announcement(string? before, string? now) => before == now ? null : now;

    /// <summary>The state the announcement follows: the line without its progress.</summary>
    public static string State(bool working, string? waiting, int count) =>
        waiting is not null ? "Transcription: " + waiting
        : working ? "Transcribing the library"
        : count == 0 ? "Transcription finished" : "Transcription idle";

    /// <summary>A length of time as people say it roughly: "under a minute", "about 5 minutes", "2 hours".</summary>
    /// <summary>
    /// When a failed file is tried again, rounded the way people say it: "Will try again in a
    /// moment.", "in about 4 minutes", "in about 30 minutes" (to the nearest five past ten
    /// minutes), "in about 2 hours".
    /// </summary>
    public static string RetryIn(TimeSpan wait)
    {
        if (wait < TimeSpan.FromMinutes(1))
        {
            return "Will try again in a moment.";
        }

        var minutes = wait.TotalMinutes;
        var said = minutes < 10 ? Count((int)Math.Ceiling(minutes), "minute")
            : minutes < 90 ? Count((int)(Math.Round(minutes / 5, MidpointRounding.AwayFromZero) * 5), "minute")
            : Count((int)Math.Round(wait.TotalHours, MidpointRounding.AwayFromZero), "hour");
        return "Will try again in about " + said + ".";
    }

    public static string About(TimeSpan time) =>
        time < TimeSpan.FromMinutes(1) ? "under a minute"
        : time < TimeSpan.FromMinutes(90) ? Count((int)Math.Round(time.TotalMinutes), "minute")
        : Count((int)Math.Round(time.TotalHours), "hour");

    private static string Count(int n, string unit) => string.Create(CultureInfo.InvariantCulture, $"{n} {unit}{(n == 1 ? "" : "s")}");

    /// <summary>A share as a whole percentage, "40%".</summary>
    public static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}%");

    private static string Glyph(int code) => ((char)code).ToString();
}
