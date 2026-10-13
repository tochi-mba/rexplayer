using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Updates;

/// <summary>What a line of release notes is.</summary>
public enum NoteKind
{
    /// <summary>A group heading, such as "Added".</summary>
    Heading,

    /// <summary>One change, with an optional bold label ("Faster start.") before its text.</summary>
    Bullet,

    Paragraph,
}

/// <summary>A line of tidied release notes, ready to draw.</summary>
public sealed record NoteLine(NoteKind Kind, string Label, string Text);

/// <summary>
/// Release notes as a reader wants them (TOOL-09): a version's CHANGELOG section when there is one,
/// else the release's generated notes, without the generated noise ("What's Changed", "New
/// Contributors", "Full Changelog", "by @someone in (link)", bare links, Markdown marks). Notes are
/// kept tidied as a small Markdown subset: "### Heading", "- **Label** text" and plain lines.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>The tidied notes for <paramref name="markdown"/>.</summary>
    public static string Tidy(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = new List<string>();
        var skipping = false;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("<!--", StringComparison.Ordinal) || line.StartsWith("**Full Changelog**", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                var heading = Clean(line.TrimStart('#'));
                skipping = heading.Equals("New Contributors", StringComparison.OrdinalIgnoreCase);
                if (!skipping && !heading.Equals("What's Changed", StringComparison.OrdinalIgnoreCase) && heading.Length > 0)
                {
                    lines.Add("### " + heading);
                }

                continue;
            }

            if (skipping)
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            {
                lines.Add("- " + line[2..].Trim());
            }
            else if (raw.StartsWith(' ') && lines.Count > 0 && lines[^1].StartsWith("- ", StringComparison.Ordinal))
            {
                // A wrapped list item carries on its line.
                lines[^1] += " " + line;
            }
            else
            {
                lines.Add(line);
            }
        }

        return string.Join("\n", lines.Select(Finish).Where(line => line is not ("- " or "")));
    }

    /// <summary>
    /// The tidied section for <paramref name="version"/> of a CHANGELOG ("## X.Y.Z - date" headings),
    /// or null when it has none.
    /// </summary>
    public static string? FromChangelog(string changelog, string version)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        var lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line == $"## {version}" || line.StartsWith($"## {version} ", StringComparison.Ordinal));
        if (start < 0)
        {
            return null;
        }

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        var section = Tidy(string.Join("\n", lines[(start + 1)..(end < 0 ? lines.Length : end)]));
        return section.Length > 0 ? section : null;
    }

    /// <summary>Tidied notes (or older plain notes with "•" bullets) as lines to draw.</summary>
    public static IReadOnlyList<NoteLine> Lines(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var result = new List<NoteLine>();
        foreach (var raw in notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(new NoteLine(NoteKind.Heading, "", line[4..]));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            {
                var item = line[2..];
                var label = Label().Match(item);
                result.Add(label.Success
                    ? new NoteLine(NoteKind.Bullet, label.Groups[1].Value.Trim(), item[label.Length..].Trim())
                    : new NoteLine(NoteKind.Bullet, "", item));
            }
            else if (line.Length > 0)
            {
                result.Add(new NoteLine(NoteKind.Paragraph, "", line));
            }
        }

        return result;
    }

    /// <summary>A line without credits, links and marks, keeping a list item's leading bold label.</summary>
    private static string Finish(string line)
    {
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            return line;
        }

        var bullet = line.StartsWith("- ", StringComparison.Ordinal);
        var text = bullet ? line[2..] : line;
        var label = Label().Match(text);
        return bullet && label.Success
            ? $"- **{Clean(label.Groups[1].Value)}** {Clean(text[label.Length..])}".TrimEnd()
            : (bullet ? "- " : "") + Clean(text);
    }

    private static string Clean(string text)
    {
        text = Credit().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = BareLink().Replace(text, "");
        return text.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal).Trim();
    }

    [GeneratedRegex(@"^\*\*(.+?)\*\*")]
    private static partial Regex Label();

    /// <summary>The "by @someone in (link)" generated notes put after each change.</summary>
    [GeneratedRegex(@"\s+by @\S+ in https?://\S+")]
    private static partial Regex Credit();

    [GeneratedRegex(@"\[([^\]]+)\]\(https?://[^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\s*<?https?://\S+>?")]
    private static partial Regex BareLink();
}
