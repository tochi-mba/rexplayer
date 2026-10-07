// Spec: The MicroDVD (.sub) format as commonly written: "{start frame}{end frame}text" lines, '|' between lines of a cue, {y:i}-style codes for italic, bold and underline, and a first cue of "{1}{1}rate" giving the frame rate.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.Subtitles;

/// <summary>MicroDVD: times are frame numbers, turned into time at the file's (or the given) frame rate.</summary>
internal static partial class MicroDvd
{
    public static IEnumerable<SubtitleCue> Parse(string text, double frameRate)
    {
        var rate = frameRate > 0 ? frameRate : 23.976;
        var first = true;
        foreach (Match match in Line().Matches(text))
        {
            var start = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var body = match.Groups[3].Value.TrimEnd('\r');
            if (first && start <= 1 && double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var stated) && stated > 0)
            {
                rate = stated;
                first = false;
                continue;
            }

            first = false;
            var end = match.Groups[2].Value.Length > 0 ? long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : start + (long)(rate * 3);
            if (SubtitleFile.Cue(TimeSpan.FromSeconds(start / rate), TimeSpan.FromSeconds(end / rate), Style(body)) is { } cue)
            {
                yield return cue;
            }
        }
    }

    /// <summary>MicroDVD's own codes ({y:i}, {y:b}) as the markup the other formats use; '|' between lines.</summary>
    private static string Style(string body)
    {
        var prefix = "";
        foreach (Match code in Code().Matches(body))
        {
            var value = code.Groups[1].Value.ToLowerInvariant();
            prefix += value.Contains('i', StringComparison.Ordinal) ? "<i>" : "";
            prefix += value.Contains('b', StringComparison.Ordinal) ? "<b>" : "";
            prefix += value.Contains('u', StringComparison.Ordinal) ? "<u>" : "";
        }

        return prefix + Code().Replace(body, "").Replace('|', '\n');
    }

    [GeneratedRegex(@"^\{(\d+)\}\{(\d*)\}(.*)$", RegexOptions.Multiline)]
    private static partial Regex Line();

    [GeneratedRegex(@"\{[yY]:([a-zA-Z,]+)\}")]
    private static partial Regex Code();
}
