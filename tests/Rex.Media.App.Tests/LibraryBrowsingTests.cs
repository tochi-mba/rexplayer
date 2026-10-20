using System.IO;
using System.Windows.Automation;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The library's views, layouts, orders, groups, filters and search, driven on the real window
/// over Asake's songs and a few videos and pictures.
/// </summary>
[Collection("desktop")]
public sealed class LibraryBrowsingTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private static bool HasText(AppProcess app, string list, string text) =>
        app.Find(list).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.NameProperty, text))) is not null;

    [Fact]
    [Capability("LIB-05")]
    public void HomeShelvesSeeAllAndEveryViewShowWhatTheLibraryHolds()
    {
        var sungba = _scene.WithAsake().Song("Sungba (Remix)", "Mr. Money with the Vibe", 2022, "Amapiano");
        _scene.Video("Small.Film.2023.mp4");
        _scene.Picture("Lagos.jpg");
        using var app = _scene.Ready(played: sungba).Start();

        // Home: a shelf per kind, each with See all opening its view.
        Assert.Equal("Albums", app.Find("LibraryShelfCards-Albums").Current.Name);
        Assert.Equal("Your most played", app.Find("LibraryShelfCards-Songs").Current.Name);
        app.Press("LibraryShelf-Albums");
        Wait.For(() => app.Text("LibraryTitle") == "Albums", "See all to open the albums");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("3 albums", StringComparison.Ordinal), "three albums");

        // An album opens to its songs, and Back returns to the albums.
        app.Open("Work of Art");
        Wait.For(() => app.Text("LibraryKicker") == "ALBUM" && app.Text("LibraryTitle") == "Work of Art", "the album");
        app.Line("Terminator");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "Albums", "Back to the albums");

        // The artist page: top songs, then albums; an album from it comes back to the artist.
        app.Visit("Artists");
        app.Open("Asake");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST", "the artist page");
        Assert.StartsWith("3 albums", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Wait.For(() => HasText(app, "LibraryList", "Top songs") && HasText(app, "LibraryList", "Albums"), "the page's two sections");
        Assert.False(app.IsShown("LibraryLook"), "An artist page has its own layout.");
        app.Line("Sungba (Remix)");
        app.Open("Lungu Boy");
        Wait.For(() => app.Text("LibraryKicker") == "ALBUM" && app.Text("LibraryTitle") == "Lungu Boy", "the artist's album");
        app.Line("Lonely At The Top");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST", "Back to the artist page");

        app.Visit("Genres");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("2 genres", StringComparison.Ordinal), "two genres");
        app.Open("Amapiano");
        Wait.For(() => app.Text("LibraryKicker") == "GENRE", "the genre");
        app.Line("Sungba");

        app.Visit("Songs");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("4 items", StringComparison.Ordinal), "the four songs");
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 movie", StringComparison.Ordinal), "the one movie");
        app.Visit("All videos");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 video", StringComparison.Ordinal), "the one video");
        app.Visit("Pictures");
        app.Line("Lagos");
        app.Visit("Playlists");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText") == "Playlists you keep (Media, Playlists) show here.", "the empty playlists");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachLayoutOrderAndGroupIsShownAndKeptForItsView()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Songs");

        app.Pick("LibraryLook", "List");
        Wait.For(() => app.Find("LibraryList").Current.Name == "Compact list" && app.Text("LibraryLook") == "List", "the list");
        Assert.False(app.IsShown("LibraryCardSize"), "A list has no card size.");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Find("LibraryList").Current.Name == "Cover grid" && app.Text("LibraryLook") == "Cards", "the cards");
        app.SetValue("LibraryCardSize", 300);
        Wait.For(() => app.SavedSettings.LibraryViews.GetValueOrDefault("Songs")?.CardSize == 300, "the card size to be kept");
        app.Pick("LibraryLook", "Wall");
        Wait.For(() => app.Text("LibraryLook") == "Big cards", "the big cards");
        app.Pick("LibraryLook", "Collage");
        Wait.For(() => app.IsShown("LibraryCollage") && !app.IsShown("LibraryList"), "the collage");
        app.Line("Sungba", "LibraryCollage");
        Screenshots.Match(app, "library-songs-collage", "LibraryPane");
        app.Pick("LibraryLook", "List");
        Wait.For(() => app.IsShown("LibraryList") && !app.IsShown("LibraryCollage"), "the list again");

        // Ordered by title, then reversed; gathered under first letters.
        app.Pick("LibrarySort", "Title");
        Wait.For(() => app.Lines().SequenceEqual(["Lonely At The Top", "Sungba", "Terminator"]), "the songs by title", () => string.Join(", ", app.Lines()));
        app.Pick("LibrarySort", "Reverse");
        Wait.For(() => app.Lines().SequenceEqual(["Terminator", "Sungba", "Lonely At The Top"]), "the order reversed", () => string.Join(", ", app.Lines()));
        Assert.StartsWith("↑", app.Text("LibrarySort"), StringComparison.Ordinal);
        app.Pick("LibraryGroup", "Letter");
        Wait.For(() => HasText(app, "LibraryList", "L") && HasText(app, "LibraryList", "S") && HasText(app, "LibraryList", "T"), "a heading per first letter");

        Wait.For(() => app.SavedSettings.LibraryViews.GetValueOrDefault("Songs") is { Look: LibraryLook.List, Sort: LibrarySort.Title, Descending: true, Grouping: LibraryGrouping.Letter },
            "the songs' layout, order and groups to be kept");

        // Another view keeps its own choice, and coming back finds the songs as they were left.
        app.Visit("Albums");
        app.Pick("LibraryLook", "Wall");
        Wait.For(() => app.Text("LibraryLook") == "Big cards", "the albums' own layout");
        app.Visit("Songs");
        Wait.For(() => app.Text("LibraryLook") == "List", "the songs' layout as it was left");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void FiltersSayWhenTheyEmptyAViewAndSearchClearsOnEscape()
    {
        var terminator = Path.Combine(_scene.Media, "Terminator.wav");
        using var app = _scene.WithAsake().Ready(played: terminator).Start();
        app.Visit("Songs");

        // A filter that leaves nothing says so and offers to clear it.
        app.Filter("LibraryFilterProgress", "InProgress");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText").StartsWith("Nothing here matches these filters", StringComparison.Ordinal), "the filtered empty state");
        Wait.For(() => app.IsShown("LibraryEmptyClearFilters"), "the Clear filters button");
        Screenshots.Match(app, "library-empty-filters", "LibraryPane");
        app.Press("LibraryEmptyClearFilters");
        Wait.For(() => app.Lines().Length == 3, "Clear filters to bring the songs back");
        Wait.For(() => app.SavedSettings.LibraryViews.GetValueOrDefault("Songs") is { IsFiltered: false }, "the cleared filters to be kept");

        app.Filter("LibraryFilterProgress", "Finished");
        Wait.For(() => app.Lines().SequenceEqual(["Terminator"]), "only the played song");
        app.Filter("LibraryFilterKind", "Video");
        Wait.For(() => app.IsShown("LibraryEmptyClearFilters"), "two filters to empty the songs");
        app.Press("LibraryFilter");
        app.Press("LibraryFilterClear");
        Wait.For(() => app.Lines().Length == 3, "the filter menu's Clear filters");

        // Search: what is typed narrows everything; nothing found says so; Escape clears it.
        var search = (ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("no such song");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText") == "Nothing in the library matches that.", "no results");
        Pointer.Key(app, app.Find("LibrarySearch"), Pointer.Escape);
        Wait.For(() => search.Current.Value.Length == 0 && app.Text("LibraryTitle") == "Songs", "Escape to clear the search");

        // Down moves from the search into what it found and chooses the first; Enter plays it.
        search.SetValue("Sungba");
        Wait.For(() => app.Lines().SequenceEqual(["Sungba"]), "the search to find one song", () => string.Join(", ", app.Lines()));
        Pointer.Key(app, app.Find("LibrarySearch"), Pointer.Down);
        Wait.For(() => app.IsChosen("Sungba"), "Down to choose the first found");
        Pointer.Key(app, Pointer.Enter);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "Enter to play the chosen song");

        Assert.Equal(0, app.Close());
    }
}
