using System.Text.RegularExpressions;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>What a block of release notes is.</summary>
public enum NoteKind
{
    Heading,
    Bullet,
    Paragraph,
}

/// <summary>One block of release notes: a heading, a bullet (with its bold label, if any) or a paragraph.</summary>
public sealed record NoteBlock(NoteKind Kind, string Text, string? Label = null);

/// <summary>
/// Release notes made tidy for the update panel (TOOL-09). A release's notes may be the changelog's
/// section for the version, or text a hosting service generated from merged changes; either way
/// the result is headings, bullets with their bold labels, and paragraphs, without the generated
/// "What's Changed" and "Full Changelog" lines, contributor credits, change links or Markdown marks.
/// </summary>
public static partial class ReleaseNotes
{
    private const char Bullet = (char)0x2022;

    /// <summary>Generated headings that say nothing: the lines under them stay.</summary>
    private static readonly string[] NoiseHeadings = ["what's changed", "whats changed", "what changed", "full changelog"];

    /// <summary>Generated sections that are not about rexplayer: the lines under them go too.</summary>
    private static readonly string[] CreditHeadings = ["new contributors", "contributors"];

    /// <summary>
    /// The notes in the tidy form <see cref="PendingUpdate.Notes"/> keeps: one block per line, as
    /// "### Heading", "- **Label.** text", "- text" or a paragraph. When the notes hold the
    /// changelog's section for <paramref name="version"/>, only that section is kept. Tidy notes
    /// come back unchanged.
    /// </summary>
    public static string Clean(string markdown, string version)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(version);
        var lines = Section(markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), version);
        var blocks = new List<string>();
        var skipping = false;
        var comment = false;
        var broken = true;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (comment || line.StartsWith("<!--", StringComparison.Ordinal))
            {
                comment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.Length == 0)
            {
                broken = true;
                continue;
            }

            if (HeadingLine().Match(line) is { Success: true } heading)
            {
                var text = Inline(heading.Groups[1].Value);
                skipping = CreditHeadings.Contains(text.ToLowerInvariant());
                broken = true;
                if (!skipping && !IsNoise(text) && !NamesVersion(text, version))
                {
                    blocks.Add("### " + text);
                }

                continue;
            }

            if (skipping || IsNoise(line) || Credit().IsMatch(line) || Inline(line).Length == 0)
            {
                continue;
            }

            if (BulletLine().Match(line) is { Success: true } bullet)
            {
                blocks.Add("- " + Item(bullet.Groups[1].Value));
                broken = false;
            }
            else if (!broken && blocks.Count > 0 && !blocks[^1].StartsWith("### ", StringComparison.Ordinal))
            {
                // A wrapped line carries on the bullet or paragraph above it.
                blocks[^1] += " " + Inline(line);
            }
            else
            {
                blocks.Add(Inline(line));
                broken = false;
            }
        }

        return Fit(DropEmptyHeadings(blocks));
    }

    /// <summary>The blocks of tidy (or older plain) notes, for drawing.</summary>
    public static IReadOnlyList<NoteBlock> Blocks(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var blocks = new List<NoteBlock>();
        foreach (var line in Clean(notes, "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(new NoteBlock(NoteKind.Heading, line[4..]));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var label = Label().Match(line[2..]);
                blocks.Add(label.Success
                    ? new NoteBlock(NoteKind.Bullet, label.Groups[2].Value, label.Groups[1].Value)
                    : new NoteBlock(NoteKind.Bullet, line[2..]));
            }
            else
            {
                blocks.Add(new NoteBlock(NoteKind.Paragraph, line));
            }
        }

        return blocks;
    }

    /// <summary>The lines of the changelog's section for <paramref name="version"/>, or every line when there is none.</summary>
    private static string[] Section(string[] lines, string version)
    {
        var start = version.Length == 0 ? -1 : Array.FindIndex(lines, line => VersionHeading().Match(line) is { Success: true } match && match.Groups[1].Value == version);
        if (start < 0)
        {
            return lines;
        }

        var end = Array.FindIndex(lines, start + 1, line => VersionHeading().IsMatch(line));
        return lines[(start + 1)..(end < 0 ? lines.Length : end)];
    }

    /// <summary>A bullet's text, its leading bold label kept as the one mark the notes keep.</summary>
    private static string Item(string text)
    {
        text = PullRequest().Replace(Attribution().Replace(text, ""), "");
        var label = Label().Match(text);
        return label.Success && Inline(label.Groups[1].Value) is { Length: > 0 } name
            ? $"**{name}** {Inline(label.Groups[2].Value)}".TrimEnd()
            : Inline(text);
    }

    /// <summary>Text without links, images, tags, code marks or emphasis, its spaces collapsed.</summary>
    private static string Inline(string text)
    {
        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Url().Replace(text, "");
        text = Tag().Replace(text, "");
        text = Emphasis().Replace(text, "$2");
        text = text.Replace("`", "", StringComparison.Ordinal);
        return Spaces().Replace(text, " ").Trim();
    }

    private static bool IsNoise(string line)
    {
        var plain = Inline(line).TrimStart(Bullet, '-', '*', ' ').TrimEnd(':', ' ').ToLowerInvariant();
        return NoiseHeadings.Any(noise => plain == noise || plain.StartsWith(noise + ":", StringComparison.Ordinal))
            || plain.StartsWith("full changelog", StringComparison.Ordinal);
    }

    private static bool NamesVersion(string heading, string version) =>
        version.Length > 0 && VersionHeading().Match("## " + heading.Replace("rexplayer ", "", StringComparison.OrdinalIgnoreCase)) is { Success: true } match
        && match.Groups[1].Value == version;

    private static List<string> DropEmptyHeadings(List<string> blocks) =>
        [.. blocks.Where((block, i) => !block.StartsWith("### ", StringComparison.Ordinal)
            || (i + 1 < blocks.Count && !blocks[i + 1].StartsWith("### ", StringComparison.Ordinal)))];

    /// <summary>The blocks that fit in <see cref="PendingUpdate.MaxNotesLength"/>, whole; the rest is on the release page.</summary>
    private static string Fit(List<string> blocks)
    {
        var length = 0;
        var kept = blocks.TakeWhile(block => (length += block.Length + 1) <= PendingUpdate.MaxNotesLength + 1);
        return string.Join("\n", kept);
    }

    [GeneratedRegex(@"^#{1,6}\s+(.*?)\s*#*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^#{1,3}\s*\[?v?(\d+\.\d+\.\d+)\]?(?:\s|$)")]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"^(?:[-*+]|\u2022|\d+[.)])\s+(.*)$")]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^\*\*(.+?)\*\*\s*(.*)$")]
    private static partial Regex Label();

    /// <summary>What generated notes put after each change: "by @someone in (link)" or "in #12".</summary>
    [GeneratedRegex(@"(?:\s+by\s+@[\w.\[\]-]+)?(?:\s+in\s+(?:https?://\S+|#\d+))?\s*$")]
    private static partial Regex Attribution();

    /// <summary>The change number a merged title ends with: "(#12)".</summary>
    [GeneratedRegex(@"\s*\(#\d+\)\s*$")]
    private static partial Regex PullRequest();

    /// <summary>The generated thanks to a first-time contributor.</summary>
    [GeneratedRegex(@"@\S+ made (?:their|his|her) first contribution")]
    private static partial Regex Credit();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<?https?://[^\s>]+>?")]
    private static partial Regex Url();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\1(?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
