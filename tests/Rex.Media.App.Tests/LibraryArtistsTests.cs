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
}
