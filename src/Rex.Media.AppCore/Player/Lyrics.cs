using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Player;

/// <summary>A piece of lyric text with an explicit enhanced LRC timestamp.</summary>
public sealed record LyricWord(TimeSpan At, string Text);

/// <summary>One line of lyrics, and when it is sung when the lyrics are timed.</summary>
public sealed record LyricLine(TimeSpan? At, string Text)
{
    /// <summary>Only source-provided, validated word timings; empty for line-only or unreliable timing.</summary>
    public IReadOnlyList<LyricWord> Words { get; init; } = [];
}

/// <summary>
/// A song's lyrics (META-09): timed, from an LRC file ("[01:02.50]words", several times to a line
/// allowed, with an [offset:...] in milliseconds), or plain, from a tag or an untimed file.
/// </summary>
public sealed partial class Lyrics
{
    private Lyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    /// <summary>The lines, in the order they are sung when timed.</summary>
    public IReadOnlyList<LyricLine> Lines { get; }

    public bool IsTimed => Lines.Count > 0 && Lines[0].At is not null;

    /// <summary>Lyrics from text: LRC when any line carries a time, else one line per line of text; null when there is no text.</summary>
    public static Lyrics? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var timed = new List<LyricLine>();
        var plain = new List<LyricLine>();
        foreach (var raw in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (OffsetTag().Match(line) is { Success: true } offsetMatch)
            {
                offset = TimeSpan.FromMilliseconds(int.Parse(offsetMatch.Groups["ms"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
                continue;
            }

            var stamps = Stamp().Matches(line);
            if (stamps.Count == 0)
            {
                // [ar:Asake] and the like say who and what; they are not sung.
                if (!InfoTag().IsMatch(line))
                {
                    plain.Add(new LyricLine(null, line));
                }

                continue;
            }

            var (words, segments) = ReadWords(line[(stamps[^1].Index + stamps[^1].Length)..]);
            foreach (Match stamp in stamps)
            {
                var at = Time(stamp);
                timed.Add(new LyricLine(at, words)
                {
                    Words = segments.Count > 0 && segments[0].At >= at ? segments : [],
                });
            }
        }

        if (timed.Count > 0)
        {
            // An offset makes the lyrics come sooner when positive (the LRC convention).
            var ordered = timed.Select(line => line with
            {
                At = line.At - offset,
                Words = [.. line.Words.Select(word => word with { At = word.At - offset })],
            }).OrderBy(line => line.At).ToArray();

            // Word timing that reaches the next line, or a duplicate line start, is ambiguous.
            for (var i = 0; i < ordered.Length; i++)
            {
                var next = i + 1 < ordered.Length ? ordered[i + 1].At : null;
                if (ordered[i].Words.Any(word => word.At < ordered[i].At || (next is { } end && word.At >= end)))
                {
                    ordered[i] = ordered[i] with { Words = [] };
                }
            }

            return new Lyrics(ordered);
        }

        // Plain lyrics keep their blank lines (verses) but not the blank lines at either end.
        var first = plain.FindIndex(line => line.Text.Length > 0);
        var last = plain.FindLastIndex(line => line.Text.Length > 0);
        return first < 0 ? null : new Lyrics(plain[first..(last + 1)]);
    }

    /// <summary>The line being sung at <paramref name="position"/>: the last that has started; -1 before the first, or when untimed.</summary>
    public int IndexAt(TimeSpan position)
    {
        if (!IsTimed)
        {
            return -1;
        }

        // Upper-bound search is fast for long documents and selects the last duplicate.
        var low = 0;
        var high = Lines.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].At <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }

    /// <summary>Preserve exact enhanced-LRC segments without inventing word boundaries.</summary>
    private static (string Text, IReadOnlyList<LyricWord> Words) ReadWords(string source)
    {
        var markers = WordStamp().Matches(source);
        if (markers.Count == 0)
        {
            return (source.Trim(), []);
        }

        var prefix = source[..markers[0].Index];
        var text = new StringBuilder(prefix);
        var words = new List<LyricWord>();
        var valid = string.IsNullOrWhiteSpace(prefix);
        TimeSpan? previous = null;
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var from = marker.Index + marker.Length;
            var to = i + 1 < markers.Count ? markers[i + 1].Index : source.Length;
            var part = source[from..to];
            var at = Time(marker);
            text.Append(part);
            if (part.Length == 0 || (previous is { } earlier && at <= earlier))
            {
                valid = false;
            }

            words.Add(new LyricWord(at, part));
            previous = at;
        }

        words[0] = words[0] with { Text = words[0].Text.TrimStart() };
        words[^1] = words[^1] with { Text = words[^1].Text.TrimEnd() };
        var visible = text.ToString().Trim();
        // Every visible character must belong to one precisely timestamped segment.
        return (visible, valid && visible.Length > 0 && string.Concat(words.Select(word => word.Text)) == visible ? words : []);
    }

    private static TimeSpan Time(Match stamp)
    {
        var minutes = int.Parse(stamp.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(stamp.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = stamp.Groups["f"].Success ? stamp.Groups["f"].Value : "0";
        var milliseconds = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    [GeneratedRegex(@"<(?<m>\d{1,3}):(?<s>\d{2})(?:[.:](?<f>\d{1,3}))?>")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordStamp();

    [GeneratedRegex(@"^\[offset:\s*(?<ms>[+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"^\[[a-z]+:.*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex InfoTag();
}
