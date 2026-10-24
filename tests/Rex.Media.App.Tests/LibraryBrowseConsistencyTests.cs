using System.IO;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Movies and TV browse are library views, not dead-end special cases: their filters
/// and empty-state recovery work like the same controls in List/Cards/Wall.
/// </summary>
[Collection("desktop")]
public sealed class LibraryBrowseConsistencyTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Theory]
    [Capability("LIB-05")]
    [InlineData("Movies")]
    [InlineData("TV Shows")]
    public void AFilteredEmptyBrowseOffersClearFiltersAndRestoresContent(string route)
    {
        _scene.Film(Path.Combine("Movies", "Moon Harbour.2024.mp4"));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Arrival.mp4"));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Departure.mp4"));
        using var app = _scene.Ready().Start();
        app.Visit(route);
        Wait.For(() => app.IsShown("LibraryBillboard") && app.IsShown("LibraryBillboardTitle")
            && app.IsShown("LibraryBillboardPlay") && app.IsShown("LibraryBillboardMore"),
            "the browse route to start with an accessible native feature and its actions");
        Assert.False(string.IsNullOrWhiteSpace(app.Find("LibraryBillboardTitle").Current.Name));
        Screenshots.Match(app,
            "library-" + (route == "Movies" ? "movies" : "tv") + "-browse-featured", "LibraryPane");

        // Keep the curated browse layout while filtering: all the fixture's media is
        // unwatched, so an In Progress filter must empty every shelf and the billboard.
        app.Filter("LibraryFilterProgress", "InProgress");
        Wait.For(() => app.IsShown("LibraryEmptyText") &&
            app.Text("LibraryEmptyText").StartsWith("Nothing here matches these filters", StringComparison.Ordinal),
            "the filtered browse to explain why it has no movies or shows");
        Wait.For(() => app.IsShown("LibraryEmptyClearFilters"), "the browse page's recovery action");
        Screenshots.Match(app, "library-" + (route == "Movies" ? "movies" : "tv") + "-browse-empty-filter", "LibraryPane");

        app.Press("LibraryEmptyClearFilters");
        Wait.For(() => app.Find("LibraryFilter").Current.HasKeyboardFocus,
            "focus to return to the persistent filter control, not the removed empty-state button");
        Wait.For(() => app.IsShown("LibraryBillboard"), "the real library's browse shelves to return");
        Assert.Equal(0, app.Close());
    }
    [Fact]
    [Capability("LIB-05")]
    public void BrowseHeroPlayAndMoreInfoRemainIndependentNativeActions()
    {
        _scene.Film(Path.Combine("Movies", "Moon Harbour.2024.mp4"));
        using var app = _scene.Ready().Start();
        app.Visit("Movies");
        Wait.For(() => app.IsShown("LibraryBillboardPlay") &&
            app.IsShown("LibraryBillboardMore"), "the native feature actions");

        Assert.Contains("Moon Harbour", app.Find("LibraryBillboardPlay").Current.Name,
            StringComparison.Ordinal);
        Assert.Equal("More info about Moon Harbour", app.Find("LibraryBillboardMore").Current.Name);

        // Use actual mouse messages, not InvokePattern: pointer Tapped bubbles
        // through the banner while UIA Invoke does not.
        Pointer.Click(app, app.Find("LibraryBillboardMore"));
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" &&
            app.Text("LibraryTitle") == "Moon Harbour",
            "More info to navigate only to the selected film");
        app.Press("LibraryBack");
        Wait.For(() => app.IsShown("LibraryBillboardPlay"),
            "the movie feature after returning from details");

        Pointer.Click(app, app.Find("LibraryBillboardPlay"));
        Wait.For(() => !app.IsShown("LibraryPane") &&
            app.Text("NowPlaying").Contains("Moon Harbour", StringComparison.Ordinal),
            "Play to start only the actual film and reveal the player");
        Assert.Equal(0, app.Close());
    }

}
