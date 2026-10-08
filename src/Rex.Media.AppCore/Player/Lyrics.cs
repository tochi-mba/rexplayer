using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Player;

/// <summary>One line of lyrics, and when it is sung when the lyrics are timed.</summary>
public sealed record LyricLine(TimeSpan? At, string Text);

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

            var words = WordStamp().Replace(line[(stamps[^1].Index + stamps[^1].Length)..], "").Trim();
            foreach (Match stamp in stamps)
            {
                timed.Add(new LyricLine(Time(stamp), words));
            }
        }

        if (timed.Count > 0)
        {
            // An offset makes the lyrics come sooner when positive (the LRC convention).
            return new Lyrics([.. timed.Select(line => line with { At = line.At - offset }).OrderBy(line => line.At)]);
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

        var index = -1;
        for (var i = 0; i < Lines.Count && Lines[i].At <= position; i++)
        {
            index = i;
        }

        return index;
    }

    private static TimeSpan Time(Match stamp)
    {
        var minutes = int.Parse(stamp.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(stamp.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = stamp.Groups["f"].Success ? stamp.Groups["f"].Value : "0";
        var milliseconds = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{2})(?:[.:](?<f>\d{1,3}))?\]")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordStamp();

    [GeneratedRegex(@"^\[offset:\s*(?<ms>[+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"^\[[a-z]+:.*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex InfoTag();
}
