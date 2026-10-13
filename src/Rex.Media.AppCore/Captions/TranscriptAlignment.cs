using System.Globalization;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Puts a subtitle file's words at the times a transcript heard them (OSD-11, "subtitles timed by
/// the transcript"): the file's words are the right ones (names, spelling, a translation's sense)
/// and the transcript's times are the right times. Each cue's words are matched, in order, to the
/// transcript's words heard within the cue's stretch (a second either side); a matched word takes
/// its time, and the rest are left for the voice to place.
/// </summary>
public static class TranscriptAlignment
{
    /// <summary>How far outside a cue's own stretch a transcript word may be and still be its word.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    /// <summary>A copy of <paramref name="subtitles"/> whose runs carry the times <paramref name="transcript"/> heard.</summary>
    public static SubtitleTrack Time(SubtitleTrack subtitles, Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(subtitles);
        ArgumentNullException.ThrowIfNull(transcript);
        var timed = new SubtitleTrack(subtitles.Name) { Language = subtitles.Language };
        var heard = transcript.Words.Where(word => Key(word.Text).Length > 0).OrderBy(word => word.Start).ToList();
        foreach (var cue in subtitles.All())
        {
            timed.Add(Time(cue, heard));
        }

        return timed;
    }

    /// <summary>The cue with its runs timed where the transcript's words match, in order, within its stretch.</summary>
    public static SubtitleCue Time(SubtitleCue cue, IReadOnlyList<TranscriptWord> heard)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(heard);
        var from = cue.Start - Slack;
        var to = cue.End + Slack;
        var candidates = heard.Where(word => word.Start >= from && word.Start <= to).ToList();
        if (candidates.Count == 0)
        {
            return cue;
        }

        var next = 0;
        var lines = new List<SubtitleLine>();
        foreach (var line in cue.Lines)
        {
            var runs = new List<SubtitleRun>();
            foreach (var run in line.Runs)
            {
                // A run's time is its first word's; a run of several words is split so each has its own.
                var pieces = Split(run.Text);
                foreach (var piece in pieces)
                {
                    TimeSpan? at = null;
                    var key = Key(piece);
                    if (key.Length > 0)
                    {
                        // The next heard word that matches, looking a few ahead for a word the model missed or added.
                        for (var look = next; look < Math.Min(candidates.Count, next + 4); look++)
                        {
                            if (Key(candidates[look].Text) == key)
                            {
                                at = candidates[look].Start - cue.Start;
                                next = look + 1;
                                break;
                            }
                        }
                    }

                    runs.Add(run with { Text = piece, At = at ?? (pieces.Count == 1 ? run.At : null) });
                }
            }

            lines.Add(new SubtitleLine(runs));
        }

        return cue with { Lines = lines };
    }

    /// <summary>The run's text as words, each keeping the space after it, so joining them back gives the text.</summary>
    private static List<string> Split(string text)
    {
        var pieces = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) && (i + 1 == text.Length || !char.IsWhiteSpace(text[i + 1])))
            {
                pieces.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            pieces.Add(text[start..]);
        }

        return pieces.Count == 0 ? [text] : pieces;
    }

    /// <summary>A word as it is compared: letters and digits only, lower case.</summary>
    public static string Key(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return new string([.. word.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit)]);
    }
}
