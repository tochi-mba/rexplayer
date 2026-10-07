using System.Globalization;

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
