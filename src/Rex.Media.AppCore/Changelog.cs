using System.Text.RegularExpressions;

namespace Rex.Media.AppCore;

/// <summary>
/// Reads CHANGELOG.md, which the app carries, for "What's new" (UI-15): one version's section as
/// plain text, with Markdown's emphasis and line wrapping undone so it reads as sentences.
/// </summary>
public static partial class Changelog
{
    /// <summary>The section for <paramref name="version"/>, or null when the changelog has none.</summary>
    public static string? Section(string markdown, string version)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith($"## {version} ", StringComparison.Ordinal) || line == $"## {version}");
        if (start < 0)
        {
            return null;
        }

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        var body = lines[(start + 1)..(end < 0 ? lines.Length : end)];

        // Wrapped list items become one line each; headings lose their marks; emphasis goes.
        var paragraphs = new List<string>();
        foreach (var raw in body)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                paragraphs.Add(line[4..]);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                paragraphs.Add("\u2022 " + line[2..]);
            }
            else if (paragraphs.Count > 0)
            {
                paragraphs[^1] += " " + line.Trim();
            }
            else
            {
                paragraphs.Add(line.Trim());
            }
        }

        return string.Join("\n", paragraphs.Select(paragraph => Emphasis().Replace(paragraph, "$1")));
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Emphasis();
}
