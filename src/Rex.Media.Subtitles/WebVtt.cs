// Spec: W3C WebVTT: The Web Video Text Tracks Format, section 4.1 (file structure: signature, header, blocks), 4.3 (cue timings and settings, "line:"), and 6.1 (parsing: blocks are separated by blank lines; NOTE, STYLE and REGION blocks carry no cues).
using System.Globalization;

namespace Rex.Media.Subtitles;

/// <summary>WebVTT: the header, then cue blocks with an optional identifier, timings with settings, and text.</summary>
internal static class WebVtt
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        foreach (var block in SubRip.Blocks(text).Skip(1))
        {
            var timing = Array.FindIndex(block, line => line.Contains("-->", StringComparison.Ordinal));
            if (timing < 0 || block[0].StartsWith("NOTE", StringComparison.Ordinal) || block[0] is "STYLE" or "REGION")
            {
                continue;
            }

            var times = block[timing].Split("-->", 2);
            var after = times[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (after.Length > 0 && SubtitleFile.Time(times[0]) is { } start && SubtitleFile.Time(after[0]) is { } end
                && SubtitleFile.Cue(start, end, string.Join("\n", block[(timing + 1)..]), Placement(after[1..])) is { } cue)
            {
                yield return cue;
            }
        }
    }

    /// <summary>The cue's place from its "line:" setting: a low line number or percentage is near the top.</summary>
    private static SubtitlePlacement Placement(string[] settings)
    {
        foreach (var setting in settings)
        {
            if (!setting.StartsWith("line:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = setting[5..].Split(',')[0];
            var percent = value.EndsWith('%');
            if (double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var line))
            {
                return percent
                    ? line < 33 ? SubtitlePlacement.Top : line < 66 ? SubtitlePlacement.Middle : SubtitlePlacement.Bottom
                    : line is >= 0 and < 4 ? SubtitlePlacement.Top : SubtitlePlacement.Bottom;
            }
        }

        return SubtitlePlacement.Bottom;
    }
}
