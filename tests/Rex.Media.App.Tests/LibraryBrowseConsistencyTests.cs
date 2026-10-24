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
        using var app = _scene.Ready().Start();
        app.Visit(route);
        Wait.For(() => app.IsShown("LibraryBillboard"), "the browse route to start with a real item");

        // Keep the curated browse layout while filtering: all the fixture's media is
        // unwatched, so an In Progress filter must empty every shelf and the billboard.
        app.Filter("LibraryFilterProgress", "InProgress");
        Wait.For(() => app.IsShown("LibraryEmptyText") &&
            app.Text("LibraryEmptyText").StartsWith("Nothing here matches these filters", StringComparison.Ordinal),
            "the filtered browse to explain why it has no movies or shows");
        Wait.For(() => app.IsShown("LibraryEmptyClearFilters"), "the browse page's recovery action");
        Screenshots.Match(app, "library-" + (route == "Movies" ? "movies" : "tv") + "-browse-empty-filter", "LibraryPane");

        app.Press("LibraryEmptyClearFilters");
        Wait.For(() => app.IsShown("LibraryBillboard"), "the real library's browse shelves to return");
        Assert.Equal(0, app.Close());
    }
}
