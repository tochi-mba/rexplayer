using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Places the words of a cue in time as they are spoken (OSD-11). Where the file times its words,
/// they are used as they are, with any untimed word between two timed ones placed by its letters.
/// Otherwise each word has a share of the cue's time by its letters, and comes in on the first
/// syllable heard once its share is near, one word a syllable; when no syllable is heard it comes
/// in anyway half way through its share, unless the voice has paused, and never later than the end
/// of its share, so a cue over music or a cue the voice does not match still reads.
/// </summary>
public sealed class WordAligner
{
    /// <summary>Seconds of silence after which the next word waits for a syllable.</summary>
    public const double Pause = 0.4;

    /// <summary>The least time between two words, so one syllable does not spill two.</summary>
    public const double MinimumGap = 0.06;

    private double[] _early = [];
    private double[] _due = [];
    private double[] _latest = [];
    private double _lastReveal = double.NegativeInfinity;

    /// <summary>The cue's words.</summary>
    public IReadOnlyList<SubtitleWord> Words { get; private set; } = [];

    /// <summary>Whether the file timed the words, so the voice is not needed.</summary>
    public bool Timed { get; private set; }

    /// <summary>How many of <see cref="Words"/> have come in.</summary>
    public int Revealed { get; private set; }

    /// <summary>When each word is due, in seconds from the cue's start.</summary>
    public IReadOnlyList<double> Due => _due;

    /// <summary>Starts on <paramref name="cue"/>: nothing revealed, the words' shares worked out.</summary>
    public void Begin(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        Words = SubtitleWords.Of(cue);
        Timed = SubtitleWords.IsTimed(cue);
        Revealed = 0;
        _lastReveal = double.NegativeInfinity;
        var length = Math.Max(0.001, (cue.End - cue.Start).TotalSeconds);
        (_early, _due, _latest) = Timed ? Exact(Words, length) : Shares(Words, length);
    }

    /// <summary>
    /// Takes the voice's frame at <paramref name="since"/> seconds into the cue and says how many words
    /// have come in now, revealing at most one on a syllable, and every word whose share has run out.
    /// </summary>
    public int Advance(double since, SpeechFrame frame)
    {
        var onset = frame.Onset;
        while (Revealed < Words.Count)
        {
            var i = Revealed;
            bool reveal;
            if (Timed)
            {
                reveal = since >= _due[i];
            }
            else
            {
                var spaced = since - _lastReveal >= MinimumGap;
                var onSyllable = spaced && onset && since >= _early[i];
                var onTime = spaced && since >= _due[i] && frame.SilentFor < Pause;
                reveal = since >= _latest[i] || onSyllable || onTime;
                if (onSyllable)
                {
                    onset = false;
                }
            }

            if (!reveal)
            {
                break;
            }

            Revealed++;
            _lastReveal = since;
        }

        return Revealed;
    }

    /// <summary>Every word timed exactly: the file's times, untimed words placed by letters between their timed neighbours.</summary>
    private static (double[] Early, double[] Due, double[] Latest) Exact(IReadOnlyList<SubtitleWord> words, double length)
    {
        var due = new double[words.Count];
        var i = 0;
        while (i < words.Count)
        {
            var from = words[i].At?.TotalSeconds ?? 0;
            var next = i + 1;
            while (next < words.Count && words[next].At is null)
            {
                next++;
            }

            var to = next < words.Count ? words[next].At!.Value.TotalSeconds : length;
            var letters = Enumerable.Range(i, next - i).Sum(index => words[index].Text.Length);
            var before = 0;
            for (var j = i; j < next; j++)
            {
                due[j] = Math.Max(from, from + ((to - from) * before / Math.Max(1, letters)));
                before += words[j].Text.Length;
            }

            i = next;
        }

        return (due, due, due);
    }

    /// <summary>Each word's share of the cue by its letters: it may come early on a syllable, is due half way through, and is forced at the end.</summary>
    private static (double[] Early, double[] Due, double[] Latest) Shares(IReadOnlyList<SubtitleWord> words, double length)
    {
        var total = Math.Max(1, words.Sum(word => word.Text.Length));
        var early = new double[words.Count];
        var due = new double[words.Count];
        var latest = new double[words.Count];
        var before = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var start = length * before / total;
            var span = length * words[i].Text.Length / total;
            early[i] = Math.Max(0, start - (0.35 * span));
            due[i] = start + (0.5 * span);
            latest[i] = start + span;
            before += words[i].Text.Length;
        }

        return (early, due, latest);
    }
}
