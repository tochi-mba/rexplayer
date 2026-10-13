using Rex.Media.AppCore.Captions;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class WordAlignerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static readonly SpeechFrame Quiet = new(0.3f, 0.5f, false, 2, false, 0);
    private static readonly SpeechFrame Syllable = Quiet with { Onset = true };
    private static readonly SpeechFrame Paused = new(0, 0.5f, false, 0, true, 1);

    private static SubtitleCue Cue(string text, double start = 10, double end = 12) =>
        new(S(start), S(end), [new SubtitleLine([new SubtitleRun(text)])]);

    [Fact]
    [Capability("OSD-11")]
    public void UntimedWordsShareTheCueByTheirLettersAndComeInOnSyllables()
    {
        var aligner = new WordAligner();
        // 2 + 7 + 5 = 14 letters over 2 s: "so" has 0.29 s, "amazing" 1 s, "words" 0.71 s.
        aligner.Begin(Cue("so amazing words"));

        Assert.False(aligner.Timed);
        Assert.Equal(["so", "amazing", "words"], aligner.Words.Select(word => word.Text));
        Assert.Equal([0.143, 0.786, 1.643], aligner.Due.Select(due => Math.Round(due, 3)));

        Assert.Equal(0, aligner.Advance(0, Quiet));
        // The first syllable brings the first word, and only the first, however early the next could be.
        Assert.Equal(1, aligner.Advance(0.05, Syllable));
        // No syllable: the second word waits for half its share.
        Assert.Equal(1, aligner.Advance(0.5, Quiet));
        Assert.Equal(2, aligner.Advance(0.8, Quiet));
        // Too soon after the last for a syllable to be another word.
        Assert.Equal(2, aligner.Advance(0.84, Syllable));
        // A syllable before the last word's share is near is not it.
        Assert.Equal(2, aligner.Advance(1.0, Syllable));
        Assert.Equal(3, aligner.Advance(1.1, Syllable));
        Assert.Equal(3, aligner.Advance(5, Syllable));
    }

    [Fact]
    [Capability("OSD-11")]
    public void APauseHoldsTheNextWordUntilItsShareRunsOut()
    {
        var aligner = new WordAligner();
        aligner.Begin(Cue("one two"));
        Assert.Equal(1, aligner.Advance(0.1, Syllable));
        // Due at 1.5 s, but the voice has paused: it waits.
        Assert.Equal(1, aligner.Advance(1.3, Paused));
        Assert.Equal(1, aligner.Advance(1.9, Paused));
        // Its share ends at 2 s: it comes in regardless.
        Assert.Equal(2, aligner.Advance(2.0, Paused));
    }

    [Fact]
    [Capability("OSD-11")]
    public void TimedWordsUseTheFilesTimesAndFillInTheUntimedOnesByLetters()
    {
        var cue = new SubtitleCue(S(0), S(4), [new SubtitleLine(
        [
            new SubtitleRun("Lonely ", At: S(0.5)),
            new SubtitleRun("at the "),
            new SubtitleRun("top", At: S(2)),
            new SubtitleRun(" again"),
        ])]);
        var aligner = new WordAligner();
        aligner.Begin(cue);

        Assert.True(aligner.Timed);
        // "at" and "the" share 0.5 to 2 by letters with "Lonely": 6, 2, 3 of 11; "again" shares 2 to 4 with "top".
        Assert.Equal([0.5, 1.318, 1.591, 2, 2.75], aligner.Due.Select(due => Math.Round(due, 3)));
        Assert.Equal(0, aligner.Advance(0.4, Syllable));
        Assert.Equal(1, aligner.Advance(0.5, Quiet));
        Assert.Equal(3, aligner.Advance(1.7, Paused));
        Assert.Equal(5, aligner.Advance(3.5, Quiet));
    }

    [Fact]
    public void ACueWithNoWordsRevealsNothingAndNullIsRefused()
    {
        var aligner = new WordAligner();
        aligner.Begin(Cue(" "));
        Assert.Empty(aligner.Words);
        Assert.Equal(0, aligner.Advance(1, Syllable));
        Assert.Throws<ArgumentNullException>(() => aligner.Begin(null!));
    }

    [Fact]
    public void BeginningAgainForgetsTheLastCue()
    {
        var aligner = new WordAligner();
        aligner.Begin(Cue("one two three"));
        aligner.Advance(5, Quiet);
        Assert.Equal(3, aligner.Revealed);
        aligner.Begin(Cue("four", 0, 0));
        Assert.Equal(0, aligner.Revealed);
        Assert.Equal(1, aligner.Advance(0.001, Quiet));
    }
}
