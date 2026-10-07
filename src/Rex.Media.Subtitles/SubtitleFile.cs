// Spec: Format recognition by content for the subtitle formats below: W3C WebVTT section 4.1 (the "WEBVTT" signature), the ASS v4+ format description ("[Script Info]"), SAMI 1.0 ("<SAMI>"), MicroDVD ("{start}{end}"), MPL2 ("[start][end]"), SubViewer 2 ("[INFORMATION]" or "hh:mm:ss.cc,hh:mm:ss.cc"), and SubRip (numbered blocks with "-->").
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.Subtitles;

/// <summary>The subtitle formats rexplayer reads from files.</summary>
public enum SubtitleFormat
{
    SubRip,
    WebVtt,
    Ass,
    MicroDvd,
    Mpl2,
    SubViewer,
    Sami,
}

/// <summary>A subtitle file read: its format and its cues, in time order.</summary>
public sealed record SubtitleDocument(SubtitleFormat Format, IReadOnlyList<SubtitleCue> Cues);

/// <summary>
/// Reads a subtitle file. The format is recognised from the text itself, never from the file's
/// name; damaged entries are skipped and the rest kept, and text that is no subtitle format at all
/// is refused.
/// </summary>
public static partial class SubtitleFile
{
    /// <summary>
    /// The cues in <paramref name="text"/>. <paramref name="frameRate"/> is for MicroDVD files, whose
    /// times are frame numbers; one that states its own rate in its first cue overrides it.
    /// </summary>
    public static SubtitleDocument Parse(string text, double frameRate = 23.976)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var format = Recognise(start) ?? throw new FormatException("This is not a subtitle file rexplayer knows.");
        var cues = format switch
        {
            SubtitleFormat.WebVtt => WebVtt.Parse(start),
            SubtitleFormat.Ass => Ass.Parse(start),
            SubtitleFormat.Sami => Sami.Parse(start),
            SubtitleFormat.MicroDvd => MicroDvd.Parse(start, frameRate),
            SubtitleFormat.Mpl2 => Mpl2.Parse(start),
            SubtitleFormat.SubViewer => SubViewer.Parse(start),
            _ => SubRip.Parse(start),
        };
        return new SubtitleDocument(format, [.. cues.OrderBy(cue => cue.Start)]);
    }

    /// <summary>The format <paramref name="text"/> is in, or null when it is none of them.</summary>
    public static SubtitleFormat? Recognise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var head = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (head.StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            return SubtitleFormat.WebVtt;
        }

        if (head.StartsWith("[Script Info]", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.Ass;
        }

        if (head.StartsWith("<SAMI", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.Sami;
        }

        if (MicroDvdLine().IsMatch(head))
        {
            return SubtitleFormat.MicroDvd;
        }

        if (Mpl2Line().IsMatch(head))
        {
            return SubtitleFormat.Mpl2;
        }

        if (head.StartsWith("[INFORMATION]", StringComparison.OrdinalIgnoreCase) || SubViewerTimes().IsMatch(head))
        {
            return SubtitleFormat.SubViewer;
        }

        return head.Contains("-->", StringComparison.Ordinal) ? SubtitleFormat.SubRip : null;
    }

    /// <summary>
    /// A time written as h:mm:ss with ',' or '.' before the fraction ("01:02:03,450", "2:03.4",
    /// "00:01.000"); the hours may be left out. Null when it is not one.
    /// </summary>
    internal static TimeSpan? Time(string text)
    {
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3
            || !double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || parts[..^1].Any(part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        var hours = parts.Length == 3 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromSeconds((hours * 3600.0) + (minutes * 60.0) + seconds);
    }

    /// <summary>A cue from styled text, or null when the text is empty.</summary>
    internal static SubtitleCue? Cue(TimeSpan start, TimeSpan end, string text, SubtitlePlacement placement = SubtitlePlacement.Bottom)
    {
        if (end <= start || text.Trim().Length == 0)
        {
            return null;
        }

        var (lines, asked) = SubtitleMarkup.Parse(text.Trim('\r', '\n'));
        return new SubtitleCue(start, end, lines, asked ?? placement);
    }

    [GeneratedRegex(@"^\{\d+\}\{\d*\}")]
    private static partial Regex MicroDvdLine();

    [GeneratedRegex(@"^\[\d+\]\[\d*\]")]
    private static partial Regex Mpl2Line();

    [GeneratedRegex(@"^\d{1,2}:\d{2}:\d{2}\.\d{1,3},\d{1,2}:\d{2}:\d{2}\.\d{1,3}")]
    private static partial Regex SubViewerTimes();
}
