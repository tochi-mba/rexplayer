using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class TranscriptEditHistoryTests
{
    private static readonly Transcript Source = new("en", "base.en",
        [new TranscriptWord("one", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
         new TranscriptWord("two", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))]);

    private static TranscriptRevision Original() => new(Source, TimeRanges.Empty, TimeSpan.FromSeconds(10), false);

    [Fact]
    public void CorrectionsKeepTimingsAndCanBeUndoneRedoneAndReset()
    {
        var history = new TranscriptEditHistory(Original());
        Assert.True(history.IsOriginal);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.Undo());
        Assert.False(history.Redo());
        Assert.False(history.ReplaceWord(0, "one"));
        Assert.True(history.ReplaceWord(0, "once"));
        Assert.False(history.IsOriginal);
        Assert.True(history.CanUndo);
        Assert.Equal("once", history.Current.Words[0].Text);
        Assert.Equal(Source.Words[0].Start, history.Current.Words[0].Start);
        Assert.Equal(Original().Done, history.Snapshot.Done);
        Assert.Equal(Original().Duration, history.Snapshot.Duration);
        Assert.True(history.Undo());
        Assert.True(history.IsOriginal);
        Assert.True(history.CanRedo);
        Assert.True(history.Redo());
        Assert.Equal("once", history.Current.Words[0].Text);
        history.Reset();
        Assert.True(history.IsOriginal);
        Assert.False(history.CanRedo);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void EditingAfterUndoDiscardsRedoWithoutModifyingTheOriginal()
    {
        var history = new TranscriptEditHistory(Original());
        Assert.True(history.ReplaceWord(0, "first"));
        Assert.True(history.ReplaceWord(1, "second"));
        Assert.True(history.Undo());
        Assert.True(history.ReplaceWord(1, "again"));
        Assert.False(history.Redo());
        Assert.Equal(["first", "again"], history.Current.Words.Select(word => word.Text));
        Assert.Equal(["one", "two"], Source.Words.Select(word => word.Text));
    }

    [Fact]
    public void EditedSnapshotReopensOnlyForTheSameModelAndWordLayout()
    {
        var correction = Source with { Words = [Source.Words[0] with { Text = "edited" }, Source.Words[1]] };
        var history = new TranscriptEditHistory(Original(), correction);
        Assert.True(history.CanUndo);
        Assert.Equal("edited", history.Current.Words[0].Text);
        Assert.True(history.Undo());
        Assert.True(history.IsOriginal);
        Assert.False(new TranscriptEditHistory(Original(), Source).CanUndo);
        Assert.False(new TranscriptEditHistory(Original(), correction with { Model = "small.en" }).CanUndo);
        Assert.False(new TranscriptEditHistory(Original(), correction with { Words = [] }).CanUndo);
    }

    [Fact]
    public void WordEditsRejectNullAndOutOfRangePositions()
    {
        var history = new TranscriptEditHistory(Original());
        Assert.Throws<ArgumentNullException>(() => history.ReplaceWord(0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.ReplaceWord(-1, "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.ReplaceWord(2, "bad"));
    }
    [Fact]
    public void LiveRecognitionAddsNewWordsWithoutOverwritingCorrections()
    {
        var oldCoverage = TimeRanges.Of([new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2))]);
        var saved = new TranscriptRevision(Source with
        {
            Words = [Source.Words[0] with { Text = "once" }, Source.Words[1]],
        }, oldCoverage, TimeSpan.FromSeconds(10), false);
        var later = Source with
        {
            Words = [Source.Words[0] with { Text = "another guess" }, Source.Words[1],
                new TranscriptWord("three", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4))],
        };
        var additional = TimeRanges.Of([new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5))]);
        var merged = saved.MergeGenerated(later, additional, TimeSpan.FromSeconds(12), complete: false);

        Assert.Equal(["once", "two", "three"], merged.Content.Words.Select(word => word.Text));
        Assert.Equal(3, merged.Content.Words.Count);
        Assert.Equal(TimeSpan.FromSeconds(12), merged.Duration);
        Assert.Equal(TimeRanges.Of([new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))]).Ranges, merged.Done.Ranges);
        Assert.False(merged.Complete);

        var history = new TranscriptEditHistory(new TranscriptRevision(later, merged.Done, merged.Duration, false), merged.Content);
        Assert.True(history.CanUndo);
        Assert.Equal("once", history.Current.Words[0].Text);
        Assert.True(history.Undo());
        Assert.Equal("another guess", history.Current.Words[0].Text);
        Assert.Equal("three", history.Current.Words[2].Text);
    }

    [Fact]
    public void ACompletedGenerationDoesNotDuplicateOldWordsOrDowngradeItsCoverage()
    {
        var oldCoverage = TimeRanges.Of([new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2))]);
        var saved = new TranscriptRevision(Source, oldCoverage, TimeSpan.FromSeconds(10), true);
        var generated = Source with
        {
            Words = [Source.Words[0], Source.Words[1], Source.Words[1],
                new TranscriptWord("later", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5))],
        };
        var merged = saved.MergeGenerated(generated,
            TimeRanges.Of([new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10))]),
            TimeSpan.FromSeconds(10), complete: false);

        Assert.Equal(["one", "two", "later"], merged.Content.Words.Select(word => word.Text));
        Assert.True(merged.Complete);
        Assert.Throws<ArgumentException>(() => saved.MergeGenerated(generated with { Model = "tiny.en" },
            TimeRanges.Empty, null, complete: false));
        Assert.Throws<ArgumentNullException>(() => saved.MergeGenerated(null!, TimeRanges.Empty, null, false));
        Assert.Throws<ArgumentNullException>(() => saved.MergeGenerated(generated, null!, null, false));
        Assert.Equal(["one", "two"], Source.Words.Select(word => word.Text));
    }

}
