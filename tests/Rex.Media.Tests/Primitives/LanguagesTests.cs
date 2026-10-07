using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Primitives;

/// <summary>Languages written every way media and people write them, and choosing tracks by them.</summary>
public sealed class LanguagesTests
{
    private sealed record Track(string? Language, string? Title = null, bool IsDefault = false);

    private static Track? Choose(IReadOnlyList<Track> tracks, string preferences, string? original = null) =>
        Languages.Choose(tracks, Languages.ParseList(preferences), track => track.Language, track => track.Title, track => track.IsDefault, original);

    [Theory]
    [InlineData("en", "eng")]
    [InlineData("ENG", "eng")]
    [InlineData("en-US", "eng")]
    [InlineData("pt_BR", "por")]
    [InlineData("fre", "fra")]
    [InlineData("fra", "fra")]
    [InlineData("French", "fra")]
    [InlineData(" german ", "deu")]
    [InlineData("yo-NG", "yor")]
    [InlineData("tlh", "tlh")]
    [InlineData("Klingon-x", "klingon")]
    public void EveryWayOfWritingALanguageComesToOneCode(string written, string code) => Assert.Equal(code, Languages.Canonical(written));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("und")]
    [InlineData("zxx")]
    [InlineData("mis")]
    [InlineData("MUL")]
    public void NoLanguageAndTheCodesForNoneInParticularAreNothing(string? written) => Assert.Null(Languages.Canonical(written));

    [Fact]
    public void LanguagesMatchHoweverTheyAreWrittenButNeverWhenUnknown()
    {
        Assert.True(Languages.Same("ger", "de-AT"));
        Assert.False(Languages.Same("en", "fr"));
        Assert.False(Languages.Same(null, null));
        Assert.False(Languages.Same("und", "und"));
    }

    [Fact]
    public void AListIsSplitByCommasAndSemicolons()
    {
        Assert.Equal(["en", "Simplified Chinese", "original"], Languages.ParseList(" en ,Simplified Chinese;; original "));
        Assert.Empty(Languages.ParseList(null));
        Assert.Empty(Languages.ParseList("   "));
    }

    [Fact]
    public void ACommentaryIsKnownByItsTitle()
    {
        Assert.True(Languages.IsCommentary("Director's Commentary"));
        Assert.False(Languages.IsCommentary("Main"));
        Assert.False(Languages.IsCommentary(null));
    }

    [Fact]
    [Capability("AU-21")]
    public void TheFirstPreferredLanguageTheMediaHasWins()
    {
        Track english = new("eng"), french = new("fr"), japanese = new("jpn");
        Track[] tracks = [english, french, japanese];

        Assert.Same(french, Choose(tracks, "de, fr, en"));
        Assert.Same(english, Choose(tracks, "English"));
        Assert.Null(Choose(tracks, "de"));
        Assert.Null(Choose(tracks, ""));
        Assert.Null(Choose([], "en"));
    }

    [Fact]
    [Capability("AU-21")]
    public void ACommentaryLosesToTheProgrammeAndADefaultTrackWinsAmongEquals()
    {
        Track commentary = new("en", "Commentary", IsDefault: true), plain = new("en"), marked = new("en", IsDefault: true);

        Assert.Same(plain, Choose([commentary, plain], "en"));
        Assert.Same(marked, Choose([commentary, plain, marked], "en"));
        Assert.Same(commentary, Choose([commentary], "en"));
    }

    [Fact]
    [Capability("AU-21")]
    public void OriginalMeansTheLanguageTheMediaWasMadeIn()
    {
        Track english = new("en"), japanese = new("ja");

        Assert.Same(japanese, Choose([english, japanese], "original, en", original: "jpn"));
        Assert.Same(english, Choose([english, japanese], "ORIGINAL, en", original: null));
        Assert.Throws<ArgumentNullException>(() => Languages.Choose<Track>(null!, [], t => t.Language, t => t.Title, t => t.IsDefault));
        Assert.Throws<ArgumentNullException>(() => Languages.Choose([english], null!, t => t.Language, t => t.Title, t => t.IsDefault));
    }
}
