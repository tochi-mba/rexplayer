using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Songs credited to several artists, driven on the real window: each artist gets their own card
/// and the combined credit gets none.
/// </summary>
[Collection("desktop")]
public sealed class LibraryArtistsTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    /// <summary>The song count an artist's line shows, such as "2 songs".</summary>
    private static bool Shows(AppProcess app, string artist, string count) =>
        app.Line(artist).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.NameProperty, count))) is not null;

    [Fact]
    [Capability("LIB-05")]
    public void ACombinedCreditIsCountedUnderEachArtistAndHasNoCardOfItsOwn()
    {
        _scene.WithAsake();
        _scene.Song("Praise", "Testing", 2018, "Hip-Hop", "A$AP Rocky");
        _scene.Song("Slip", "Testing", 2018, "Hip-Hop", "A$AP Rocky/Brent Faiyaz");
        _scene.Song("Ballerina", "Singles", 2024, "Indie Pop", "beabadoobee/The Marías");
        using var app = _scene.Ready().Start();

        app.Visit("Artists");
        Wait.For(() => app.Lines().Length == 5, "the five artists");
        Assert.Equal(["A$AP Rocky", "Asake", "beabadoobee", "Brent Faiyaz", "The Marías"], app.Lines().Order(StringComparer.CurrentCultureIgnoreCase));
        Assert.True(Shows(app, "A$AP Rocky", "2 songs"), "A$AP Rocky counts both songs.");
        Assert.True(Shows(app, "Brent Faiyaz", "1 song"), "Brent Faiyaz counts the song he is on.");
        Assert.True(Shows(app, "Asake", "3 songs"), "Asake keeps his three songs.");
        Assert.DoesNotContain(app.Lines(), line => line.Contains('/', StringComparison.Ordinal));

        // The second artist's page holds the shared song too.
        app.Open("The Marías");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST", "The Marías's page");
        app.Line("Singles");
        Assert.Equal(0, app.Close());
    }
    [Fact]
    [Capability("LIB-05")]
    public void LongRtlTitlesAndDuplicateFilenamesOfferAStableAccessibleContext()
    {
        var file = new Rex.Media.Library.LibraryEntry
        {
            Path = Path.Combine("Library", "Artist A", "Demo.wav"),
            Kind = Rex.Media.Library.LibraryKind.Music,
            Title = "07 🎵 שיר שחוזר",
        };
        var row = new Rex.Media.App.LibraryRow(file.Title, "Artist A · Demo album", "", file);
        Assert.Equal(Microsoft.UI.Xaml.FlowDirection.RightToLeft, row.NameDirection);
        Assert.Contains("Demo.wav", row.AccessibilityContext, StringComparison.Ordinal);
        Assert.Contains("Artist A", row.AccessibilityContext, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine("Library", "Artist A", "Demo.wav"),
            row.AccessibilityContext, StringComparison.Ordinal);
        Assert.Equal(file.Title, row.AccessibilityName);

        var english = new Rex.Media.App.LibraryRow("Song 2", "Other artist", "", file with
        {
            Path = Path.Combine("Library", "Artist B", "Demo.wav"),
        });
        Assert.Equal(Microsoft.UI.Xaml.FlowDirection.LeftToRight, english.NameDirection);
        Assert.NotEqual(row.AccessibilityContext, english.AccessibilityContext);
    }

    [Fact]
    [Capability("LIB-05")]
    public void SimilarArtistNamesHaveSeparateOfflinePagesAndNoInventedBiography()
    {
        _scene.Song("Short Name Track", "Demos", 2024, "Alternative", "Asa");
        _scene.Song("Long Name Track", "Demos", 2024, "Afrobeats", "Asake");
        using var app = _scene.Ready(settings => settings with
        {
            Metadata = new Rex.Media.Settings.MetadataSettings { Music = false },
        }).Start();

        app.Visit("Artists");
        Assert.Equal(["Asa", "Asake"], app.Lines().Order(StringComparer.CurrentCultureIgnoreCase));
        app.Open("Asa");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST" && app.Text("LibraryTitle") == "Asa",
            "the first artist's own offline page");
        app.Line("Short Name Track");
        Assert.DoesNotContain("Long Name Track", app.Lines());
        Assert.False(app.IsShown("LibraryOverview"), "offline pages cannot invent biographies");
        Assert.False(app.IsShown("LibraryCredit"), "there is no source to credit without metadata");

        app.Press("LibraryBack");
        app.Open("Asake");
        Wait.For(() => app.Text("LibraryTitle") == "Asake", "the separately credited artist");
        app.Line("Long Name Track");
        Assert.DoesNotContain("Short Name Track", app.Lines());
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void NoSpaceAfterFeaturedCreditKeepsEveryGuestOnTheirOwnArtistCard()
    {
        _scene.Song("If I Never See Your Face Again", "It Won't Be Soon Before Long", 2008, "Pop", "Maroon 5 feat.Rihanna");
        _scene.Song("Hate That I Love You", "Good Girl Gone Bad", 2007, "Pop", "Rihanna feat.Ne-Yo");
        _scene.Song("Umbrella", "Good Girl Gone Bad", 2007, "Pop", "Rihanna feat.Jay-Z");
        using var app = _scene.Ready().Start();

        app.Visit("Artists");
        Wait.For(() => app.Lines().Length == 4, "four separate credited artists");
        Assert.Equal(["Jay-Z", "Maroon 5", "Ne-Yo", "Rihanna"],
            app.Lines().Order(StringComparer.CurrentCultureIgnoreCase));
        Assert.True(Shows(app, "Rihanna", "3 songs"), "Rihanna counts her own and featured songs.");
        Assert.True(Shows(app, "Jay-Z", "1 song"), "Jay-Z has his own card.");
        Assert.True(Shows(app, "Ne-Yo", "1 song"), "Ne-Yo has his own card.");
        Assert.True(Shows(app, "Maroon 5", "1 song"), "Maroon 5 has its own card.");
        Assert.DoesNotContain(app.Lines(), line => line.Contains("feat.", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, app.Close());
    }

}
