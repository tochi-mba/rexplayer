using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Who a song is by (LIB-05): combined credits taken apart, real names kept whole.</summary>
public sealed class ArtistNamesTests
{
    private static LibraryEntry Song(string title, string? artist, string? albumArtist = null, string? artists = null) =>
        new() { Path = $@"D:\Music\{title}.mp3", Kind = LibraryKind.Music, Title = title, Artist = artist, AlbumArtist = albumArtist, Artists = artists };

    /// <summary>What a library of songs credited to <paramref name="credits"/>, one song each, knows.</summary>
    private static ArtistNames Knowing(params string[] credits) => ArtistNames.Learn(credits.Select((credit, n) => Song("Song " + n, credit)));

    private static readonly ArtistNames Nobody = Knowing();

    [Fact]
    [Capability("LIB-05")]
    public void TheScreenshotsCombinedCreditsAreEachArtist()
    {
        var names = Knowing("A$AP Rocky", "A$AP Rocky/Brent Faiyaz", "beabadoobee/The Marías");

        Assert.Equal(["A$AP Rocky", "Brent Faiyaz"], names.Split("A$AP Rocky/Brent Faiyaz"));
        Assert.Equal(["beabadoobee", "The Marías"], names.Split("beabadoobee/The Marías"));
        Assert.Equal(["A$AP Rocky"], names.Split("A$AP Rocky"));
    }

    [Theory]
    [InlineData("Asake/Olamide")]
    [InlineData("Asake / Olamide")]
    [InlineData("Asake;Olamide")]
    [InlineData("Asake; Olamide")]
    [InlineData("Asake feat. Olamide")]
    [InlineData("Asake Feat. Olamide")]
    [InlineData("Asake feat Olamide")]
    [InlineData("Asake ft. Olamide")]
    [InlineData("Asake FT Olamide")]
    [InlineData("Asake featuring Olamide")]
    [InlineData("Asake x Olamide")]
    [InlineData("Asake (feat. Olamide)")]
    [InlineData("Asake [ft. Olamide]")]
    [InlineData("Asake ( featuring Olamide )")]
    [InlineData("Asake, ; Olamide")]
    public void SeparatorsThatNeverOccurInANameAlwaysSplit(string credit)
    {
        Assert.Equal(["Asake", "Olamide"], Nobody.Split(credit));
    }

    [Fact]
    public void SeparateTagValuesSplitAndRepeatsCountOnce()
    {
        Assert.Equal(["Asake", "Olamide"], Nobody.Split("Asake" + (char)0 + "Olamide"));
        Assert.Equal(["Asake", "DJ Snake"], Nobody.Split("Asake; DJ Snake; asake"));
        Assert.Empty(Nobody.Split(null));
        Assert.Empty(Nobody.Split("  "));
        Assert.Empty(Nobody.Split(" / ; "));
    }

    [Theory]
    [InlineData("Lil Nas X")]
    [InlineData("Asake X Olamide")]
    [InlineData("Malcolm X")]
    [InlineData("Lift ftw")]
    [InlineData("Florence + The Machine")]
    public void ACapitalXAndOtherLookalikesNeverSplit(string credit)
    {
        Assert.Equal([credit], Nobody.Split(credit));
    }

    [Theory]
    [InlineData("AC/DC")]
    [InlineData("Au/Ra")]
    [InlineData("Tyler, The Creator")]
    [InlineData("Earth, Wind & Fire")]
    [InlineData("Simon & Garfunkel")]
    [InlineData("Florence + The Machine")]
    [InlineData("Crosby, Stills, Nash & Young")]
    [InlineData("Crosby, Stills & Nash")]
    [InlineData("Mumford & Sons")]
    [InlineData("Of Monsters and Men")]
    [InlineData("Hall & Oates")]
    [InlineData("Hall and Oates")]
    [InlineData("Daryl Hall & John Oates")]
    [InlineData("Kool & the Gang")]
    [InlineData("Above & Beyond")]
    [InlineData("Bob Marley & The Wailers")]
    [InlineData("Bob Marley and the Wailers")]
    [InlineData("Nick Cave & the Bad Seeds")]
    [InlineData("Prince & The Revolution")]
    [InlineData("Prince and the Revolution")]
    [InlineData("Sly & the Family Stone")]
    [InlineData("Peter, Paul and Mary")]
    [InlineData("Emerson, Lake & Palmer")]
    [InlineData("Dan + Shay")]
    [InlineData("ac/dc")]
    public void NamesOnTheBuiltInListStayWholeEvenWhenTheirPartsAreArtists(string name)
    {
        // Every word of the name is an artist in this library, and still the name is one artist.
        var names = Knowing([.. name.Split(['/', ',', '&', '+', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append("Bob Marley").Append("Prince").Append("Hall").Append("Simon"), name]);

        Assert.Equal([name], names.Split(name));
        Assert.Equal([name], Nobody.Split(name));
    }

    [Fact]
    public void ABuiltInNameIsKeptWholeInsideALongerCredit()
    {
        Assert.Equal(["AC/DC", "Brent Faiyaz"], Nobody.Split("AC/DC/Brent Faiyaz"));
        Assert.Equal(["Tyler, The Creator", "Kali Uchis"], Nobody.Split("Tyler, The Creator feat. Kali Uchis"));
        Assert.Equal(["Asake", "Earth, Wind & Fire"], Nobody.Split("Asake (feat. Earth, Wind & Fire)"));
        Assert.Equal(["Silk Sonic"], Nobody.Split("Silk Sonic"));
        Assert.Equal(["Simon & Garfunkel", "Asake"], Knowing("Asake").Split("Simon & Garfunkel & Asake"));
    }

    [Fact]
    public void ABuiltInNameInsideAnotherNameIsNotMasked()
    {
        // "Simon & Garfunkel" does not stand on its own in "Paul Simon & Garfunkel", so the known
        // artists decide, as for any other credit.
        Assert.Equal(["Paul Simon", "Garfunkel"], Knowing("Paul Simon", "Garfunkel").Split("Paul Simon & Garfunkel"));
        Assert.Equal(["Simon", "Garfunkels"], Knowing("Simon", "Garfunkels").Split("Simon & Garfunkels"));
    }

    [Fact]
    public void TextAlreadyHoldingAPrivateCharacterIsTakenApartWithoutMasks()
    {
        var odd = "AC/DC" + (char)0xE000;

        Assert.Equal(["AC", "DC" + (char)0xE000], Nobody.Split(odd));
    }

    [Theory]
    [InlineData("Burna Boy & Wizkid", new[] { "Burna Boy", "Wizkid" })]
    [InlineData("Burna Boy and Wizkid", new[] { "Burna Boy", "Wizkid" })]
    [InlineData("Burna Boy AND Wizkid", new[] { "Burna Boy", "Wizkid" })]
    [InlineData("Burna Boy, Wizkid", new[] { "Burna Boy", "Wizkid" })]
    [InlineData("Burna Boy, Wizkid & Asake", new[] { "Burna Boy", "Wizkid", "Asake" })]
    [InlineData("burna boy & WIZKID", new[] { "burna boy", "WIZKID" })]
    public void AmpersandsAndCommasSplitWhenEveryPartIsAnArtistOnTheirOwn(string credit, string[] expected)
    {
        var names = Knowing("Burna Boy", "Wizkid", "Asake");

        Assert.Equal(expected, names.Split(credit));
    }

    [Theory]
    [InlineData("Burna Boy, Davido")]
    [InlineData("Wizkid and Tems")]
    [InlineData("Asake, , Wizkid")]
    [InlineData("Harry Connick, Jr.")]
    [InlineData("Sammy Davis, Jr")]
    [InlineData("Hank Williams, III")]
    public void AmpersandsAndCommasStayWhenAnyPartIsNotKnownOnItsOwn(string credit)
    {
        // Davido and Tems have no songs of their own here, and a suffix is never an artist.
        var names = Knowing("Burna Boy", "Asake", "Wizkid", "Harry Connick", "Jr.", "Sammy Davis", "Jr", "Hank Williams", "III");

        Assert.Equal([credit], names.Split(credit));
    }

    [Fact]
    public void PartsThatAppearSeparatelyAnywhereCount()
    {
        // Known from a combined credit, a featured title and the tag's list of artists alike.
        var names = ArtistNames.Learn([
            Song("One", "Asake/Olamide"),
            Song("Two (feat. Wizkid)", "Tems"),
            Song("Three", "Davido & Asake", artists: "Davido; Asake"),
            Song("Four", null, albumArtist: "Rema"),
        ]);

        Assert.Equal(["Olamide", "Wizkid", "Davido", "Rema", "Tems"], names.Split("Olamide, Wizkid & Davido and Rema, Tems"));
    }

    [Fact]
    public void ASongIsByItsAlbumArtistItsArtistsAndThoseItsTitleFeatures()
    {
        var names = Knowing("Asake", "Olamide");

        Assert.Equal(["Various Artists", "Asake", "Olamide", "Wizkid"],
            names.Of(Song("Basquiat (feat. Wizkid)", "Asake & Olamide", albumArtist: "Various Artists")));
        // The tag's list of artists takes the place of the single credit.
        Assert.Equal(["Asake", "Brent Faiyaz"], names.Of(Song("Song", "Asake & Brent Faiyaz", artists: "Asake; Brent Faiyaz")));
        // Each once, whatever the spelling.
        Assert.Equal(["Asake"], names.Of(Song("Joha (feat. ASAKE)", "asake", albumArtist: "Asake")));
        Assert.Empty(names.Of(Song("Demo", null)));
        Assert.Throws<ArgumentNullException>(() => names.Of(null!));
        Assert.Throws<ArgumentNullException>(() => ArtistNames.Learn(null!));
    }

    [Theory]
    [InlineData("Sungba (feat. Burna Boy)", new[] { "Burna Boy" })]
    [InlineData("Sungba (Remix) [ft. Burna Boy & Wizkid]", new[] { "Burna Boy & Wizkid" })]
    [InlineData("Sungba (FEATURING Burna Boy)", new[] { "Burna Boy" })]
    [InlineData("Sungba feat. Burna Boy", new[] { "Burna Boy" })]
    [InlineData("Sungba ft. Burna Boy, Wizkid", new[] { "Burna Boy, Wizkid" })]
    [InlineData("Sungba (Remix)", new string[0])]
    [InlineData("Dancing (with Myself)", new string[0])]
    [InlineData("Left feat", new string[0])]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void TitlesNameTheArtistsTheyFeature(string? title, string[] expected)
    {
        Assert.Equal(expected, ArtistNames.Featured(title));
    }

    [Fact]
    public void ArtistsFeaturedInATitleSplitByTheSameRules()
    {
        var names = Knowing("Burna Boy", "Wizkid");

        Assert.Equal(["Asake", "Burna Boy", "Wizkid"], names.Of(Song("Sungba [ft. Burna Boy & Wizkid]", "Asake")));
        Assert.Equal(["Asake", "Tems & Rema"], names.Of(Song("Sungba [ft. Tems & Rema]", "Asake")));
    }

    [Theory]
    [InlineData("The Marías", "The Marias")]
    [InlineData("the marias", "THE MARIAS")]
    [InlineData("A$AP Rocky", "ASAP Rocky")]
    [InlineData("A$AP Rocky", "a$ap rocky")]
    [InlineData("Hall & Oates", "Hall and Oates")]
    [InlineData("Florence + The Machine", "Florence and the Machine")]
    [InlineData("AC/DC", "ACDC")]
    [InlineData("Beyoncé", "Beyonce")]
    [InlineData("Ọ̀rẹ́", "Ore")]
    [InlineData("Jay-Z", "Jay Z")]
    public void SpellingsThatDifferOnlyInCaseAccentsOrPunctuationAgree(string one, string other)
    {
        Assert.Equal(ArtistNames.Key(one), ArtistNames.Key(other));
    }

    [Fact]
    public void KeysKeepDifferentArtistsApartAndCopeWithOddText()
    {
        Assert.NotEqual(ArtistNames.Key("Asake"), ArtistNames.Key("Asa"));
        Assert.Equal("!!!", ArtistNames.Key(" !!! "));
        // A broken character cannot be decomposed; the rest still matches.
        Assert.Equal("asake", ArtistNames.Key("Asake" + (char)0xD800));
        Assert.Throws<ArgumentNullException>(() => ArtistNames.Key(null!));
    }

    [Theory]
    [InlineData("beabadoobee/The Marías", "beabadoobee, The Marías")]
    [InlineData("Asake; DJ Snake; asake", "Asake, DJ Snake")]
    [InlineData("AC/DC", "AC/DC")]
    [InlineData("AC/DC/Brent Faiyaz", "AC/DC, Brent Faiyaz")]
    [InlineData("Asake feat. Olamide", "Asake feat. Olamide")]
    [InlineData("Simon & Garfunkel", "Simon & Garfunkel")]
    [InlineData(" / ", " / ")]
    [InlineData(null, null)]
    public void TheArtistLineListsTheCreditsArtists(string? credit, string? shown)
    {
        Assert.Equal(shown, ArtistNames.Display(credit));
    }
}
