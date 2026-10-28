using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The fact lines under a library view's title (LIB-05): one separator, no count of nothing, plain lengths.</summary>
public sealed class ViewFactsTests
{
    [Fact]
    [Capability("LIB-05")]
    public void FactsAreJoinedByOneSeparatorAndTheUnknownOnesLeftOut()
    {
        Assert.Equal("EP · 2024 · 4 songs", ViewFacts.Join("EP", null, "2024", "", "4 songs"));
        Assert.Equal("", ViewFacts.Join(null, ""));
        Assert.Throws<ArgumentNullException>(() => ViewFacts.Join(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ACountOfNothingIsLeftOut()
    {
        Assert.Null(ViewFacts.CountIfAny(0, "song"));
        Assert.Equal("1 song", ViewFacts.CountIfAny(1, "song"));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("1,204 songs", ViewFacts.CountIfAny(1204, "song"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    [Capability("LIB-05")]
    public void HomeNamesEachKindTheLibraryHoldsListeningIncluded()
    {
        Assert.Equal("3 songs · 1 video · 1 picture", ViewFacts.Home(3, 1, 1, 0, 0, 0));
        // A library of one podcast used to read "0 songs · 0 videos · 0 pictures".
        Assert.Equal("1 podcast", ViewFacts.Home(0, 0, 0, 1, 0, 0));
        Assert.Equal("2 audiobooks · 3 recordings", ViewFacts.Home(0, 0, 0, 0, 2, 3));
        Assert.Equal("Nothing here yet", ViewFacts.Home(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    [Capability("LIB-05")]
    public void LengthsAddUpAndAViewWithNothingSaysSo()
    {
        TimeSpan?[] book = [TimeSpan.FromMinutes(50), null, TimeSpan.FromMinutes(40)];
        Assert.Equal("1 h 30 min", ViewFacts.Length(book));
        Assert.Equal("2 h", ViewFacts.Length([TimeSpan.FromHours(2)]));
        Assert.Equal("", ViewFacts.Length([null]));
        Assert.Equal("3 books · 1 h 30 min", ViewFacts.CountAndLength(3, "book", book));
        Assert.Equal("1 book", ViewFacts.CountAndLength(1, "book", [null]));
        // A filter that hides every book no longer reads "0 books · 1 h 30 min".
        Assert.Equal("No books", ViewFacts.CountAndLength(0, "book", book));
        Assert.Throws<ArgumentNullException>(() => ViewFacts.Length(null!));
        Assert.Throws<ArgumentNullException>(() => ViewFacts.CountAndLength(1, null!, book));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheSongsTableKeepsAPlaysColumnOnlyWhenSomethingWasPlayed()
    {
        Assert.False(ViewFacts.SongsShowPlays([0, 0, 0]));
        Assert.False(ViewFacts.SongsShowPlays([]));
        Assert.True(ViewFacts.SongsShowPlays([0, 2, 0]));
        Assert.Throws<ArgumentNullException>(() => ViewFacts.SongsShowPlays(null!));
    }
}
