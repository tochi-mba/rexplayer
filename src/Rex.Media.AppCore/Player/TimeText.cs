using System.Globalization;
using Rex.Media.Engine;

namespace Rex.Media.AppCore.Player;

/// <summary>Positions and durations as people read and type them: "4:05", "1:02:03".</summary>
public static class TimeText
{
    /// <summary>
    /// <paramref name="time"/> as m:ss, or h:mm:ss when it or <paramref name="scale"/> (the duration,
    /// so a position and its duration line up) reaches an hour. Negative times show a minus sign.
    /// </summary>
    public static string Format(TimeSpan time, TimeSpan? scale = null)
    {
        var sign = time < TimeSpan.Zero ? "-" : "";
        var whole = TimeSpan.FromSeconds(Math.Floor(Math.Abs(time.TotalSeconds)));
        var hours = whole.TotalHours >= 1 || (scale is { } s && s.Duration().TotalHours >= 1);
        return sign + (hours
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalMinutes}:{whole.Seconds:00}"));
    }

    /// <summary>What a clock shows for a time that is not known: nothing is loaded, or the length is not read yet.</summary>
    public const string Unknown = "--:--";

    /// <summary>
    /// Timeline coordinates that never become negative, infinite, or extend past
    /// the known duration. Unknown durations retain a disabled/placeholder rail
    /// and no invented remaining time. An item that has played to its end
    /// (<paramref name="ended"/>) stands at its end, so the rail is full however
    /// short it is. Remaining is the whole seconds between the two clocks as they
    /// read, so the time played and the time left always add up to the length shown.
    /// </summary>
    public static (TimeSpan Elapsed, TimeSpan? Remaining, double SliderValue, double SliderMaximum) Timeline(
        TimeSpan position, TimeSpan duration, bool ended = false)
    {
        var elapsed = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (duration <= TimeSpan.Zero)
        {
            return (elapsed, null, 0, 1);
        }

        if (position < TimeSpan.Zero && !ended)
        {
            // An invalid negative clock position must not invent a remaining time.
            return (TimeSpan.Zero, TimeSpan.Zero, 0, duration.TotalSeconds);
        }

        if (ended || elapsed > duration)
        {
            elapsed = duration;
        }

        return (elapsed, Whole(duration) - Whole(elapsed), elapsed.TotalSeconds, duration.TotalSeconds);
    }

    /// <summary>
    /// The transport's two clocks: the time played, and the time left (or the whole length when
    /// <paramref name="showRemaining"/> is off). With nothing loaded (<paramref name="state"/> is
    /// null), or an item that failed before its length was known, both read <see cref="Unknown"/>; a
    /// length not known yet reads <see cref="Unknown"/> on the right. No time left reads "0:00",
    /// never "-0:00".
    /// </summary>
    public static (string Elapsed, string Remaining) Clocks(TimeSpan position, TimeSpan duration, SessionState? state, bool showRemaining)
    {
        if (state is not { } known || (known == SessionState.Faulted && duration <= TimeSpan.Zero))
        {
            return (Unknown, Unknown);
        }

        var timeline = Timeline(position, duration, known == SessionState.Ended);
        var right = timeline.Remaining is not { } left ? Unknown
            : !showRemaining ? Format(duration)
            : left > TimeSpan.Zero ? "-" + Format(left, duration)
            : Format(TimeSpan.Zero, duration);
        return (Format(timeline.Elapsed, duration), right);
    }

    /// <summary><paramref name="time"/> down to its whole seconds, as <see cref="Format"/> shows it.</summary>
    private static TimeSpan Whole(TimeSpan time) => TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds));

    /// <summary>
    /// A time typed by the user: "1:02:03", "62:03", "3723" or "12.5" (seconds), with spaces around
    /// the parts allowed; null when it is not a time. Minutes and seconds after a colon stay below 60.
    /// </summary>
    public static TimeSpan? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 3 || !double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var total = seconds;
        var unit = 60.0;
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || (i > 0 && value >= 60) || (i == parts.Length - 2 && seconds >= 60))
            {
                return null;
            }

            total += value * unit;
            unit *= 60;
        }

        return total <= TimeSpan.MaxValue.TotalSeconds / 2 ? TimeSpan.FromSeconds(total) : null;
    }
}
