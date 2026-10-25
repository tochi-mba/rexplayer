// Spec: The inline styling subtitle files carry: HTML-like tags as SubRip uses them (b, i, u, font color), WebVTT cue text tags (W3C WebVTT section 4.2.2: b, i, u, c, v, lang, ruby, timestamps), and ASS override blocks (the ASS v4+ format description, "Style override codes": \b, \i, \u, \c, \1c, \an, \N, \n, \h).
using System.Globalization;
using System.Text;

namespace Rex.Media.Subtitles;

/// <summary>
/// Reads a cue's text with its inline styling into lines of runs. Bold, italic, underline and colour
/// are kept; tags that only position or animate text are dropped (an ASS \an tag sets the placement);
/// anything that looks like a tag but is not one is shown as written.
/// </summary>
public static class SubtitleMarkup
{
    /// <summary>
    /// The lines of <paramref name="text"/>, and the placement an ASS \an tag asked for, if any.
    /// Runs the file times (a WebVTT cue timestamp, measured against <paramref name="start"/>, the
    /// cue's start; or an ASS karaoke tag, whose durations add up from the cue's start) carry when
    /// they are spoken.
    /// </summary>
    public static (IReadOnlyList<SubtitleLine> Lines, SubtitlePlacement? Placement) Parse(string text, TimeSpan start = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<SubtitleLine>();
        var runs = new List<SubtitleRun>();
        var current = new StringBuilder();
        var style = new SubtitleRun("");
        var colors = new Stack<int?>();
        var karaoke = TimeSpan.Zero;
        SubtitlePlacement? placement = null;

        void Flush()
        {
            if (current.Length > 0)
            {
                runs.Add(style with { Text = current.ToString() });
                current.Clear();
            }
        }

        void Restyle(SubtitleRun next)
        {
            if (next != style)
            {
                Flush();
                style = next;
            }
        }

        void EndLine()
        {
            Flush();
            lines.Add(new SubtitleLine([.. runs]));
            runs.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\r')
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                EndLine();
                i++;
                continue;
            }

            if (c == '\\' && i + 1 < text.Length && text[i + 1] is 'N' or 'n' or 'h')
            {
                if (text[i + 1] == 'h')
                {
                    current.Append('\u00A0');
                }
                else
                {
                    EndLine();
                }

                i += 2;
                continue;
            }

            if (c == '{' && text.IndexOf('}', i) is > 0 and var close && text[i + 1] == '\\')
            {
                foreach (var code in text[(i + 1)..close].Split('\\', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Karaoke(code) is { } held)
                    {
                        // The syllable after the tag starts when the ones before it have been held.
                        Restyle(style with { At = karaoke });
                        karaoke += held;
                        continue;
                    }

                    (style, placement) = Override(code, style, placement, Restyle);
                }

                i = close + 1;
                continue;
            }

            if (c == '<' && text.IndexOf('>', i) is > 0 and var end && Tag(text[(i + 1)..end]) is { } tag)
            {
                var (closing, name, value) = tag;
                switch (name)
                {
                    case "b":
                        Restyle(style with { Bold = !closing });
                        break;
                    case "i":
                        Restyle(style with { Italic = !closing });
                        break;
                    case "u":
                        Restyle(style with { Underline = !closing });
                        break;
                    case "font" when !closing:
                        colors.Push(style.Color);
                        Restyle(style with { Color = Color(value) ?? style.Color });
                        break;
                    case "font":
                        Restyle(style with { Color = colors.Count > 0 ? colors.Pop() : null });
                        break;
                    case "time" when SubtitleFile.Time(value!) is { } time:
                        // A timestamp before the cue's start is the start.
                        Restyle(style with { At = time > start ? time - start : TimeSpan.Zero });
                        break;
                }

                i = end + 1;
                continue;
            }

            current.Append(c);
            i++;
        }

        EndLine();

        // Lines left empty by markup alone are dropped; at least one line is kept.
        var kept = lines.Where(line => line.Text.Trim().Length > 0).ToList();
        return (kept.Count > 0 ? kept : [lines[0]], placement);
    }

    /// <summary>An ASS override code: style changes and the placement; the rest (position, fades, fonts) is dropped.</summary>
    private static (SubtitleRun, SubtitlePlacement?) Override(string code, SubtitleRun style, SubtitlePlacement? placement, Action<SubtitleRun> restyle)
    {
        SubtitleRun? next = code switch
        {
            "b1" or "b" => style with { Bold = true },
            "b0" => style with { Bold = false },
            "i1" or "i" => style with { Italic = true },
            "i0" => style with { Italic = false },
            "u1" or "u" => style with { Underline = true },
            "u0" => style with { Underline = false },
            "r" => new SubtitleRun(""),
            _ when code.StartsWith("fn", StringComparison.OrdinalIgnoreCase) &&
                code.Length > 2 && code.Length <= 122 =>
                style with { Font = code[2..].Trim() },
            _ when code.StartsWith("fs", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(code.AsSpan(2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var size) && size is > 0 and <= 300 =>
                style with { FontSize = size },
            _ when code.StartsWith("1c&H", StringComparison.OrdinalIgnoreCase) || code.StartsWith("c&H", StringComparison.OrdinalIgnoreCase) =>
                style with { Color = AssColor(code[(code.IndexOf('H', StringComparison.OrdinalIgnoreCase) + 1)..]) ?? style.Color },
            _ => null,
        };
        if (next is not null)
        {
            restyle(next);
            return (next, placement);
        }

        if (code.StartsWith("an", StringComparison.Ordinal) && int.TryParse(code.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var alignment))
        {
            placement = alignment switch
            {
                >= 7 and <= 9 => SubtitlePlacement.Top,
                >= 4 and <= 6 => SubtitlePlacement.Middle,
                _ => SubtitlePlacement.Bottom,
            };
        }

        return (style, placement);
    }

    /// <summary>A tag's parts: whether it closes, its lower-case name (WebVTT classes dropped), and a colour attribute; null for text that is no tag.</summary>
    private static (bool Closing, string Name, string? Value)? Tag(string inner)
    {
        var closing = inner.StartsWith('/');
        var body = closing ? inner[1..] : inner;
        var name = new string([.. body.TakeWhile(char.IsAsciiLetter)]).ToLowerInvariant();
        if (name.Length == 0)
        {
            // A WebVTT timestamp tag (<00:01.000>) is a tag too, carrying its time; anything else is text.
            return body.Length > 0 && char.IsAsciiDigit(body[0]) ? (closing, "time", body) : null;
        }

        if (name is not ("b" or "i" or "u" or "font" or "c" or "v" or "lang" or "ruby" or "rt" or "span"))
        {
            return null;
        }

        var color = body.IndexOf("color", StringComparison.OrdinalIgnoreCase) is >= 0 and var at
            ? body[(at + 5)..].TrimStart(' ', '=', '"', '\'').Split('"', '\'', ' ', '>')[0]
            : null;
        return (closing, name, color);
    }

    /// <summary>
    /// How long an ASS karaoke tag holds its syllable: \k, \K, \kf and \ko followed by a count of
    /// centiseconds; null for any other code.
    /// </summary>
    private static TimeSpan? Karaoke(string code)
    {
        var digits = code switch
        {
            ['k' or 'K', .. var rest] when rest.Length > 0 && !char.IsAsciiLetter(rest[0]) => rest,
            ['k', 'f' or 'o', .. var rest] => rest,
            _ => "",
        };
        return digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var centiseconds)
            ? TimeSpan.FromMilliseconds(centiseconds * 10)
            : null;
    }

    /// <summary>"#RRGGBB" or a few colour names, as 0xRRGGBB.</summary>
    private static int? Color(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "white" => 0xFFFFFF,
        "yellow" => 0xFFFF00,
        "red" => 0xFF0000,
        "green" => 0x00FF00,
        "blue" => 0x0000FF,
        "cyan" => 0x00FFFF,
        "magenta" => 0xFF00FF,
        "black" => 0x000000,
        ['#', .. var hex] when hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) => rgb,
        _ => null,
    };

    /// <summary>An ASS colour, written blue-green-red ("&amp;H00FF8040&amp;"), as 0xRRGGBB.</summary>
    internal static int? AssColor(string value)
    {
        var hex = value.Trim('&', 'H', 'h');
        if (!int.TryParse(hex.Length > 6 ? hex[^6..] : hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bgr))
        {
            return null;
        }

        return ((bgr & 0xFF) << 16) | (bgr & 0xFF00) | ((bgr >> 16) & 0xFF);
    }
}
