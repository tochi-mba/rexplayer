using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Subtitles;

public sealed class SubtitleWordsTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static SubtitleCue Cue(params SubtitleLine[] lines) => new(S(1), S(4), lines);

    [Fact]
    [Capability("SUB-18")]
    public void WordsSplitAtWhiteSpaceAndKeepTheirPunctuationLineAndLook()
    {
        var cue = Cue(
            new SubtitleLine([new SubtitleRun("  Hello,  there "), new SubtitleRun("friend!", Bold: true, Color: 0xFF8000)]),
            new SubtitleLine([new SubtitleRun(" "), new SubtitleRun("Bye", Italic: true)]));

        var words = SubtitleWords.Of(cue);

        Assert.Equal(["Hello,", "there", "friend!", "Bye"], words.Select(word => word.Text));
        Assert.Equal([0, 0, 0, 1], words.Select(word => word.Line));
        Assert.Equal(new SubtitleRun("", Bold: true, Color: 0xFF8000), words[2].Style);
        Assert.Equal(new SubtitleRun("", Italic: true), words[3].Style);
        Assert.All(words, word => Assert.Null(word.At));
        Assert.False(SubtitleWords.IsTimed(cue));
    }

    [Fact]
    [Capability("SUB-18")]
    public void AWordSpanningRunsKeepsTheLookOfTheRunItStartsInAndItsTimeOnlyWhenItStartsTheRun()
    {
        var cue = Cue(new SubtitleLine(
        [
            new SubtitleRun("syl", At: S(0)),
            new SubtitleRun("la", Italic: true, At: S(0.2)),
            new SubtitleRun("ble ", Bold: true, At: S(0.4)),
            new SubtitleRun("two th", At: S(1)),
            new SubtitleRun("ree", Bold: true, At: S(1.5)),
        ]));

        var words = SubtitleWords.Of(cue);

        Assert.Equal(["syllable", "two", "three"], words.Select(word => word.Text));
        Assert.Equal([S(0), S(1), null], words.Select(word => word.At));
        Assert.Equal([false, false, false], words.Select(word => word.Style.Bold));
        Assert.True(SubtitleWords.IsTimed(cue));
    }

    [Fact]
    public void AnEmptyLineHasNoWordsAndNullIsRefused()
    {
        Assert.Empty(SubtitleWords.Of(Cue(new SubtitleLine([new SubtitleRun("   ")]))));
        Assert.Throws<ArgumentNullException>(() => SubtitleWords.Of(null!));
        Assert.Throws<ArgumentNullException>(() => SubtitleWords.IsTimed(null!));
    }

    [Fact]
    public void OnlyTheFirstWordTimedIsNotTimedWords()
    {
        var cue = Cue(new SubtitleLine([new SubtitleRun("first ", At: S(0)), new SubtitleRun("rest")]));
        Assert.False(SubtitleWords.IsTimed(cue));
    }
}
