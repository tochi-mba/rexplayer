// Spec: The SubViewer 2 (.sub) format as commonly written: an optional [INFORMATION] header, then blocks of "hh:mm:ss.cc,hh:mm:ss.cc" and a text line with "[br]" between lines.
namespace Rex.Media.Subtitles;

/// <summary>SubViewer 2: a timing line, then the text with [br] for line breaks.</summary>
internal static class SubViewer
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        foreach (var block in SubRip.Blocks(text))
        {
            var timing = Array.FindIndex(block, line => line.Length > 0 && char.IsAsciiDigit(line[0]) && line.Contains(',', StringComparison.Ordinal));
            if (timing < 0 || timing + 1 >= block.Length)
            {
                continue;
            }

            var times = block[timing].Split(',');
            if (SubtitleFile.Time(times[0]) is { } start && SubtitleFile.Time(times[1]) is { } end
                && SubtitleFile.Cue(start, end, string.Join("\n", block[(timing + 1)..]).Replace("[br]", "\n", StringComparison.OrdinalIgnoreCase)) is { } cue)
            {
                yield return cue;
            }
        }
    }
}
