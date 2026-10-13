namespace Rex.Media.Diagnostics;

/// <summary>
/// Watches the log for a set of <see cref="TimingMarks"/>, so a measuring run can stop as soon as
/// what it measures has happened: REXPLAYER_EXIT_AFTER=open.first-picture,open.first-sound closes the
/// player once both are marked. A mark whose name ends in ".failed" also ends the watch, so a run
/// that cannot succeed does not wait for its timeout.
/// </summary>
public sealed class MarkWatch
{
    private readonly HashSet<string> _waiting;
    private bool _done;

    public MarkWatch(IEnumerable<string> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        _waiting = new HashSet<string>(wanted.Where(name => name.Length > 0), StringComparer.Ordinal);
        Wanted = [.. _waiting.Order(StringComparer.Ordinal)];
    }

    /// <summary>The marks waited for, in name order.</summary>
    public IReadOnlyList<string> Wanted { get; }

    /// <summary>A watch for the comma-separated marks in <paramref name="text"/>, or null when it names none.</summary>
    public static MarkWatch? Parse(string? text)
    {
        var names = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 0 ? null : new MarkWatch(names);
    }

    /// <summary>
    /// Takes in one log entry; true exactly once, for the entry that completes the watch (the last
    /// wanted mark, or a failure).
    /// </summary>
    public bool Observe(LogEntry entry)
    {
        if (TimingMarks.NameOf(entry) is not { } name)
        {
            return false;
        }

        // Entries are written from every thread.
        lock (_waiting)
        {
            if (_done)
            {
                return false;
            }

            _waiting.Remove(name);
            _done = _waiting.Count == 0 || name.EndsWith(".failed", StringComparison.Ordinal);
            return _done;
        }
    }
}
