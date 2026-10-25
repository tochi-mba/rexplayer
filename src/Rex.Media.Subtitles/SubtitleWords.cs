// Spec: W3C WebVTT section 3 (cue text, the shared model every format is read into), split into words at white space as Unicode UAX #29 section 4 breaks them (rule WB3d: break at spaces); punctuation stays with its word.
namespace Rex.Media.Subtitles;

/// <summary>
/// One word of a cue: its text, the look of the run it starts in, when it is spoken (measured
/// from the cue's start) where the file says, and the line it is on.
/// </summary>
public sealed record SubtitleWord(string Text, SubtitleRun Style, TimeSpan? At, int Line);

/// <summary>A cue as words, for showing them one at a time as they are spoken.</summary>
public static class SubtitleWords
{
    /// <summary>
    /// The words of <paramref name="cue"/> in reading order. A word spanning two runs keeps the
    /// look of the run it starts in, and the time of that run only when it starts the run.
    /// </summary>
    public static IReadOnlyList<SubtitleWord> Of(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var words = new List<SubtitleWord>();
        for (var line = 0; line < cue.Lines.Count; line++)
        {
            var text = new System.Text.StringBuilder();
            SubtitleRun? style = null;
            TimeSpan? at = null;

            void End()
            {
                if (text.Length > 0)
                {
                    words.Add(new SubtitleWord(text.ToString(), style! with { Text = "" }, at, line));
                    text.Clear();
                }
            }

            foreach (var run in cue.Lines[line].Runs)
            {
                for (var i = 0; i < run.Text.Length; i++)
                {
                    var c = run.Text[i];
                    if (char.IsWhiteSpace(c))
                    {
                        End();
                        continue;
                    }

                    if (text.Length == 0)
                    {
                        style = run;
                        at = i == 0 ? run.At : null;
                    }

                    text.Append(c);
                }
            }

            End();
        }

        return words;
    }

    /// <summary>Whether the file times the words of <paramref name="cue"/>: at least one beyond the first says when it is spoken.</summary>
    public static bool IsTimed(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return Of(cue).Skip(1).Any(word => word.At is not null);
    }
}
