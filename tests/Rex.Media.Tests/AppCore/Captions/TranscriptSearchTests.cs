using Rex.Media.AppCore.Captions;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class TranscriptSearchTests
{
    private static readonly TranscriptWord[] Speech =
    [
        new("Hello,", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        new("world!", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        new("Bonjour", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)),
        new("monde.", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4)),
    ];

    [Fact]
    public void FindsWordsAndPhrasesWithoutFlatteningTheTranscript()
    {
        Assert.Equal(0, TranscriptSearch.FindNext(Speech, "heLLo"));
        Assert.Equal(0, TranscriptSearch.FindNext(Speech, "HELLO world"));
        Assert.Equal(1, TranscriptSearch.FindNext(Speech, "WORLD"));
        Assert.Equal(2, TranscriptSearch.FindNext(Speech, "bonjour monde"));
        Assert.Equal(0, TranscriptSearch.FindNext(Speech, "hello", after: 0));
        Assert.Equal(1, TranscriptSearch.FindNext(Speech, "world", after: 3));
        Assert.Equal(1, TranscriptSearch.FindNext(Speech, "world", after: int.MaxValue));
        Assert.Equal(1, TranscriptSearch.FindNext(Speech, "world", after: int.MinValue));
        Assert.Equal(-1, TranscriptSearch.FindNext(Speech, "monde hello"));
        Assert.Equal(-1, TranscriptSearch.FindNext(Speech, "not spoken"));
    }

    [Fact]
    public void EmptyAndMalformedQueriesAreSafe()
    {
        Assert.Equal(-1, TranscriptSearch.FindNext(Speech, ""));
        Assert.Equal(-1, TranscriptSearch.FindNext(Speech, "   "));
        Assert.Equal(-1, TranscriptSearch.FindNext([], "hello"));
        Assert.Equal(-1, TranscriptSearch.FindNext(Speech, "one two three four five"));
        Assert.Throws<ArgumentNullException>(() => TranscriptSearch.FindNext(null!, "text"));
        Assert.Throws<ArgumentNullException>(() => TranscriptSearch.FindNext(Speech, null!));
    }

    [Fact]
    public void VeryLongAndUnicodeTranscriptsRemainSearchable()
    {
        var words = Enumerable.Range(0, 100_000)
            .Select(number => new TranscriptWord(number == 99_999 ? "مرحبا" : "silence",
                TimeSpan.FromSeconds(number), TimeSpan.FromSeconds(number + 1)))
            .ToArray();

        Assert.Equal(99_999, TranscriptSearch.FindNext(words, "مرحبا"));
        Assert.Equal(0, TranscriptSearch.FindNext(words, "silence", after: 99_999));
    }
}
