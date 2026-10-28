using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// The words the window shows about a damaged file: short, plain, and never in the way. A file that
/// partly plays says so once and then what was passed over; one that cannot play at all says why in
/// a sentence. None of these is a dialog: the player carries on (with the next item, if there is one).
/// </summary>
public static partial class DamageNotices
{
    /// <summary>The longest reason shown; the log keeps the whole of it.</summary>
    public const int LongestReason = 120;

    public const string Damaged = "This file is damaged. Playing what can be read.";

    /// <summary>"Skipped 12 seconds that could not be read.", in minutes from two minutes up.</summary>
    public static string Skipped(TimeSpan skipped)
    {
        var seconds = Math.Max(1, (long)Math.Round(skipped.TotalSeconds));
        var amount = seconds switch
        {
            1 => "1 second",
            < 120 => seconds.ToString(CultureInfo.InvariantCulture) + " seconds",
            _ => Math.Round(seconds / 60.0).ToString(CultureInfo.InvariantCulture) + " minutes",
        };
        return $"Skipped {amount} that could not be read.";
    }

    /// <summary>"This file could not be played: why." Named by its title when several are queued, so it is clear which.</summary>
    public static string Failed(string? title, string reason) =>
        (string.IsNullOrWhiteSpace(title) ? "This file" : title) + " could not be played: " + Short(reason);

    /// <summary>
    /// A reason cut to its first sentence, without the name of the programming error behind it
    /// (that is for the log), and no longer than <see cref="LongestReason"/>.
    /// </summary>
    public static string Short(string? reason)
    {
        var text = ErrorName().Replace((reason ?? string.Empty).Trim(), string.Empty).Trim();
        if (text.Length == 0)
        {
            return "it could not be read.";
        }

        var end = text.IndexOf(". ", StringComparison.Ordinal);
        if (end > 0)
        {
            text = text[..(end + 1)];
        }

        if (text.Length > LongestReason)
        {
            text = text[..LongestReason].TrimEnd() + "...";
        }

        return text.EndsWith('.') ? text : text + ".";
    }

    /// <summary>
    /// What <see cref="Short"/> leaves out, for whoever opens the details: the rest of the reason after
    /// its first sentence (the bytes a file starts with, say), or all of it when that sentence was
    /// cut; null when the short reason is the whole of it.
    /// </summary>
    public static string? Detail(string? reason)
    {
        var text = ErrorName().Replace((reason ?? string.Empty).Trim(), string.Empty).Trim();
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        if ((end > 0 ? end + 1 : text.Length) > LongestReason)
        {
            return text;
        }

        return end > 0 ? text[(end + 2)..].TrimStart() : null;
    }

    /// <summary>The "IndexOutOfRangeException: " a technical summary starts its message with.</summary>
    [GeneratedRegex(@"\b[A-Za-z]*Exception:\s*")]
    private static partial Regex ErrorName();
}
