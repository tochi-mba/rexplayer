using System.Globalization;

namespace Rex.Media.Diagnostics;

/// <summary>
/// Marks how long something took, as lines in the log, so the time from a launch or an open to the
/// moment the user sees or hears something can be read from the log file alone. Every mark of a
/// scope is measured from the same origin and written once, at Info, under the source
/// <see cref="Source"/>: <c>timing: open.first-picture at 143 ms</c>. Safe to mark from any thread.
/// </summary>
public sealed class TimingMarks
{
    /// <summary>The log source every mark is written under.</summary>
    public const string Source = "timing";

    private readonly RexLog _log;
    private readonly TimeProvider _time;
    private readonly long _origin;
    private readonly TimeSpan _before;
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

    /// <param name="log">Where the marks are written.</param>
    /// <param name="scope">What is being timed, which prefixes every mark: launch, open, seek.</param>
    /// <param name="time">The clock; the system's by default.</param>
    /// <param name="before">Time that had already passed when the timing began, such as the runtime's start before the first line of code.</param>
    public TimingMarks(RexLog log, string scope, TimeProvider? time = null, TimeSpan before = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        _log = log;
        Scope = scope;
        _time = time ?? TimeProvider.System;
        _origin = _time.GetTimestamp();
        _before = before < TimeSpan.Zero ? TimeSpan.Zero : before;
    }

    public string Scope { get; }

    /// <summary>The time since the origin.</summary>
    public TimeSpan Elapsed => _before + _time.GetElapsedTime(_origin);

    /// <summary>
    /// Writes the mark <paramref name="name"/> with the time since the origin, the first time it is
    /// reached; false (and nothing written) when it was already marked.
    /// </summary>
    public bool Mark(string name, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var elapsed = Elapsed;
        lock (_marked)
        {
            if (!_marked.Add(name))
            {
                return false;
            }
        }

        _log.Info(Source, Line(Scope + "." + name, elapsed, detail));
        return true;
    }

    /// <summary>Whether <paramref name="name"/> has been marked.</summary>
    public bool Has(string name)
    {
        lock (_marked)
        {
            return _marked.Contains(name);
        }
    }

    /// <summary>The text of a mark: its full name, the milliseconds since the origin and what it concerns.</summary>
    public static string Line(string fullName, TimeSpan elapsed, string? detail = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{fullName} at {elapsed.TotalMilliseconds:0} ms") + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")");

    /// <summary>The full name of the mark a log entry records (such as open.first-picture), or null when it records none.</summary>
    public static string? NameOf(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Source != Source)
        {
            return null;
        }

        var end = entry.Message.IndexOf(" at ", StringComparison.Ordinal);
        return end > 0 ? entry.Message[..end] : null;
    }
}
