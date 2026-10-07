// Spec: The Advanced SubStation Alpha (ASS v4+) and SubStation Alpha (SSA v4) script format description: the [V4+ Styles] / [V4 Styles] "Format:" and "Style:" lines (Alignment, Bold, Italic, PrimaryColour), and the [Events] "Format:" and "Dialogue:" lines (Start, End, Style, Text, the Text field taking the rest of the line).
using System.Globalization;

namespace Rex.Media.Subtitles;

/// <summary>
/// ASS and SSA scripts: dialogue lines with their style's alignment, weight, slant and colour.
/// Fields are found by the section's own Format line, as the format requires; comments and
/// drawings are left out.
/// </summary>
internal static class Ass
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        var styles = new Dictionary<string, AssStyle>(StringComparer.OrdinalIgnoreCase);
        string[] styleFormat = [];
        string[] eventFormat = [];
        var section = "";
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line.ToLowerInvariant();
                continue;
            }

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line[7..].Split(',').Select(field => field.Trim().ToLowerInvariant()).ToArray();
                if (section.Contains("styles", StringComparison.Ordinal))
                {
                    styleFormat = fields;
                }
                else
                {
                    eventFormat = fields;
                }
            }
            else if (line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) && styleFormat.Length > 0)
            {
                var style = AssStyle.From(styleFormat, line[6..].Split(',', styleFormat.Length));
                styles[style.Name] = style;
            }
            else if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) && Dialogue(eventFormat, line[9..], styles) is { } cue)
            {
                yield return cue;
            }
        }
    }

    /// <summary>The styles in a script's header, for subtitles carried in a container whose blocks hold only the events.</summary>
    internal static Dictionary<string, AssStyle> Styles(string header)
    {
        var styles = new Dictionary<string, AssStyle>(StringComparer.OrdinalIgnoreCase);
        string[] format = [];
        foreach (var raw in header.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                format = [];
            }
            else if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase) && format.Length == 0)
            {
                format = [.. line[7..].Split(',').Select(field => field.Trim().ToLowerInvariant())];
            }
            else if (line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) && format.Length > 0)
            {
                var style = AssStyle.From(format, line[6..].Split(',', format.Length));
                styles[style.Name] = style;
            }
        }

        return styles;
    }

    /// <summary>A Dialogue line's cue, by the event Format (or the standard one when the file gives none).</summary>
    internal static SubtitleCue? Dialogue(string[] format, string fields, IReadOnlyDictionary<string, AssStyle> styles)
    {
        format = format.Length > 0 ? format : ["layer", "start", "end", "style", "name", "marginl", "marginr", "marginv", "effect", "text"];
        var values = fields.Split(',', format.Length);
        string Field(string name) => Array.IndexOf(format, name) is >= 0 and var at && at < values.Length ? values[at].Trim() : "";
        if (SubtitleFile.Time(Field("start")) is not { } start || SubtitleFile.Time(Field("end")) is not { } end)
        {
            return null;
        }

        var text = Array.IndexOf(format, "text") is >= 0 and var t && t < values.Length ? values[t] : "";
        return Styled(start, end, text, styles.GetValueOrDefault(Field("style")));
    }

    /// <summary>Dialogue text with its style; drawings (\p1) are not text and give no cue.</summary>
    internal static SubtitleCue? Styled(TimeSpan start, TimeSpan end, string text, AssStyle? style)
    {
        if (text.Contains("\\p1", StringComparison.Ordinal))
        {
            return null;
        }

        style ??= AssStyle.Default;
        return SubtitleFile.Cue(start, end, style.Prefix + text, style.Placement);
    }
}

/// <summary>The parts of an ASS style rexplayer uses, written as the override tags that would give them.</summary>
internal sealed record AssStyle(string Name, string Prefix, SubtitlePlacement Placement)
{
    public static AssStyle Default { get; } = new("Default", "", SubtitlePlacement.Bottom);

    public static AssStyle From(string[] format, string[] values)
    {
        string Field(string name) => Array.IndexOf(format, name) is >= 0 and var at && at < values.Length ? values[at].Trim() : "";
        var prefix = (Field("bold") is "-1" or "1" ? "{\\b1}" : "") + (Field("italic") is "-1" or "1" ? "{\\i1}" : "");
        if (Field("primarycolour") is { Length: > 0 } colour && SubtitleMarkup.AssColor(colour) is { } rgb and not 0xFFFFFF)
        {
            // Written back as ASS writes colours: blue, green, red.
            var bgr = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
            prefix += "{\\1c&H" + bgr.ToString("X6", CultureInfo.InvariantCulture) + "&}";
        }

        var alignment = int.TryParse(Field("alignment"), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : 2;
        var placement = alignment switch
        {
            >= 7 and <= 9 => SubtitlePlacement.Top,
            >= 4 and <= 6 => SubtitlePlacement.Middle,
            _ => SubtitlePlacement.Bottom,
        };
        return new AssStyle(Field("name"), prefix, placement);
    }
}
