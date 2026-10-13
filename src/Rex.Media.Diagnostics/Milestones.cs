using System.Globalization;

namespace Rex.Media.Diagnostics;

/// <summary>
/// Named moments of a launch and of playing (the window first painted, the first picture shown, the
/// first sound written), each written to the log as one "timing" line with the milliseconds since
/// the process started, so how long rexplayer takes can be read from the log alone and measured the
/// same way every time (scripts/measure-startup.ps1). An exit condition such as
/// "first-frame+first-audio" or "first-frame*2" calls the exit action once every moment it names
/// has been reached as often as it says, which lets a script launch the player, wait for those
/// moments and launch it again without driving the window.
/// </summary>
public sealed class Milestones
{
    /// <summary>The log source of every timing line.</summary>
    public const string LogSource = "timing";

    private readonly RexLog _log;
    private readonly DateTimeOffset _origin;
    private readonly TimeProvider _clock;
    private readonly Action _exit;
    private readonly Dictionary<string, int> _remaining;
    private bool _exited;

    /// <param name="log">Where the timing lines go.</param>
    /// <param name="origin">When the process started; every moment is measured from it.</param>
    /// <param name="exitAfter">The moments to exit after (see <see cref="ParseExitAfter"/>); null or empty never exits.</param>
    /// <param name="exit">What exiting does; called at most once.</param>
    /// <param name="clock">The clock moments are read from (tests pass their own).</param>
    public Milestones(RexLog log, DateTimeOffset origin, string? exitAfter = null, Action? exit = null, TimeProvider? clock = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _origin = origin;
        _clock = clock ?? TimeProvider.System;
        _exit = exit ?? (() => { });
        _remaining = new Dictionary<string, int>(ParseExitAfter(exitAfter), StringComparer.Ordinal);
        _exited = _remaining.Count == 0;
    }

    /// <summary>
    /// Reads an exit condition: moments joined by "+", each "name" (reached once) or "name*count".
    /// A part that does not read makes the whole condition empty, which never exits: a mistyped
    /// condition must not end a launch early.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ParseExitAfter(string? text)
    {
        var moments = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return moments;
        }

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('*', StringSplitOptions.TrimEntries);
            var count = 1;
            if (pieces[0].Length == 0 || pieces.Length > 2
                || (pieces.Length == 2 && !(int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 1)))
            {
                return new Dictionary<string, int>(StringComparer.Ordinal);
            }

            moments[pieces[0]] = count;
        }

        return moments;
    }

    /// <summary>How long since the process started, in milliseconds.</summary>
    public double ElapsedMilliseconds => (_clock.GetUtcNow() - _origin).TotalMilliseconds;

    /// <summary>Writes that <paramref name="name"/> has been reached, now; exits once every awaited moment has been.</summary>
    public void Reach(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _log.Info(LogSource, Format(name, ElapsedMilliseconds));
        lock (_remaining)
        {
            if (_exited || !_remaining.TryGetValue(name, out var left))
            {
                return;
            }

            if (left > 1)
            {
                _remaining[name] = left - 1;
                return;
            }

            _remaining.Remove(name);
            if (_remaining.Count > 0)
            {
                return;
            }

            _exited = true;
        }

        _exit();
    }

    /// <summary>One timing line: "name at 123.4 ms".</summary>
    public static string Format(string name, double milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} at {milliseconds:0.0} ms");
}
