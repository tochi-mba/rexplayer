// Spec: The MPL2 format as commonly written: "[start][end]text" lines with times in tenths of a second, '|' between lines and a leading '/' for italic.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.Subtitles;

/// <summary>MPL2: times in tenths of a second.</summary>
internal static partial class Mpl2
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        foreach (Match match in Line().Matches(text))
        {
            var start = TimeSpan.FromSeconds(long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 10.0);
            var end = match.Groups[2].Value.Length > 0
                ? TimeSpan.FromSeconds(long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) / 10.0)
                : start + TimeSpan.FromSeconds(3);
            var lines = match.Groups[3].Value.TrimEnd('\r').Split('|').Select(line => line.StartsWith('/') ? "<i>" + line[1..] + "</i>" : line);
            if (SubtitleFile.Cue(start, end, string.Join("\n", lines)) is { } cue)
            {
                yield return cue;
            }
        }
    }

    [GeneratedRegex(@"^\[(\d+)\]\[(\d*)\](.*)$", RegexOptions.Multiline)]
    private static partial Regex Line();
}
