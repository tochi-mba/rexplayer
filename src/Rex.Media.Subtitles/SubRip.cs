// Spec: The SubRip (.srt) format as commonly written: blocks of a counter line, a "start --> end" line with hh:mm:ss,mmm times, then text lines, separated by blank lines.
namespace Rex.Media.Subtitles;

/// <summary>SubRip: numbered blocks. The counter is optional and ignored, since files in the wild number them badly.</summary>
internal static class SubRip
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        foreach (var block in Blocks(text))
        {
            var timing = Array.FindIndex(block, line => line.Contains("-->", StringComparison.Ordinal));
            if (timing < 0)
            {
                continue;
            }

            var times = block[timing].Split("-->");
            if (SubtitleFile.Time(times[0]) is { } start && SubtitleFile.Time(times[1].Trim().Split(' ')[0]) is { } end
                && SubtitleFile.Cue(start, end, string.Join("\n", block[(timing + 1)..])) is { } cue)
            {
                yield return cue;
            }
        }
    }

    /// <summary>The text's blocks, split at blank lines.</summary>
    internal static IEnumerable<string[]> Blocks(string text)
    {
        var block = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                if (block.Count > 0)
                {
                    yield return [.. block];
                    block.Clear();
                }

                continue;
            }

            block.Add(line);
        }

        if (block.Count > 0)
        {
            yield return [.. block];
        }
    }
}
