using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
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
        _scene.Film("Small.Film.2023.mp4");
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
        Wait.For(() => app.Find("LibraryList").FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().Any(item => item.Current.Name.Contains("1920 × 1080", StringComparison.Ordinal)),
            "honest decoded video resolution to appear with its file");
        app.Visit("Pictures");
        Assert.Equal("Start slideshow", app.Text("LibraryPlayText"));
        app.Line("Lagos");
        Wait.For(() => app.Find("LibraryList").FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Any(item => item.Current.Name.Contains("Lagos.jpg", StringComparison.Ordinal)),
            "the picture card to show its original filename outside the image");
        // With no playlist kept, the sidebar lists no Playlists view.
        Assert.False(app.IsListed("Playlists"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("FMT-C19")]
    public void OpeningAPhotoPlaysItsFolderOnlyInNaturalGalleryOrder()
    {
        _scene.Picture(Path.Combine("Trip", "Trip 10.jpg"));
        _scene.Picture(Path.Combine("Trip", "Trip 2.jpg"));
        _scene.Picture(Path.Combine("Other", "Another.jpg"));
        using var app = _scene.Ready(settings: s => s with { PictureSeconds = 60 }).Start();
        app.Visit("Pictures");
        app.Open("Trip 2");
        Wait.For(() => app.Text("NowPlaying") == "Trip 2",
            "the chosen picture to be presented");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "2 items", "the selected picture folder slideshow");
        Assert.Equal("Now playing: Trip 2", app.Text("PlaylistQueueNow"));
        Assert.Equal("Up next: Trip 10", app.Text("PlaylistQueueNext"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachLayoutOrderAndGroupIsShownAndKeptForItsView()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Songs");

        app.Pick("LibraryLook", "List");
        Wait.For(() => app.Find("LibraryList").Current.Name == "Songs, table" && app.Text("LibraryLook") == "List", "the list");
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
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-songs-collage", "LibraryPane");
        app.Pick("LibraryLook", "List");
        Wait.For(() => app.IsShown("LibraryList") && !app.IsShown("LibraryCollage"), "the list again");

        // Ordered by title, then reversed; gathered under first letters.
        app.Pick("LibrarySort", "Title");
        Wait.For(() => app.Lines().SequenceEqual(["Lonely At The Top", "Sungba", "Terminator"]), "the songs by title", () => string.Join(", ", app.Lines()));
        app.Pick("LibrarySort", "Reverse");
        Wait.For(() => app.Lines().SequenceEqual(["Terminator", "Sungba", "Lonely At The Top"]), "the order reversed", () => string.Join(", ", app.Lines()));
        Assert.Equal("Title, reversed", app.Text("LibrarySort"));
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
    public void GroupHeadersFoldIndependentlyAndRememberTheirStatePerView()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Songs");
        app.Pick("LibraryLook", "List");
        app.Pick("LibraryGroup", "Letter");
        Wait.For(() => app.Lines().Length == 3 && app.IsShown("LibraryGroupToggle-S"), "the grouped songs and headers");

        // A whole header is one accessible button. Count stays visible when its rows are hidden.
        app.Press("LibraryGroupToggle-S");
        Wait.For(() => app.Lines().SequenceEqual(["Lonely At The Top", "Terminator"])
            && app.Find("LibraryGroupToggle-S").Current.Name.Contains("Expand S (1 items)", StringComparison.Ordinal),
            "S folded with its original item count");
        Assert.True(app.IsShown("LibraryGroupToggle-S"));
        app.Visit("Albums");
        app.Visit("Songs");
        Wait.For(() => app.Lines().Length == 2 && app.IsShown("LibraryGroupToggle-S"), "S stays folded on returning");

        app.Pick("LibraryGroup", "None");
        Wait.For(() => app.Lines().Length == 3, "the ungrouped songs are all visible");
        app.Pick("LibraryGroup", "Letter");
        Wait.For(() => app.Lines().Length == 2 && app.IsShown("LibraryGroupToggle-S"), "group choice recalls folded S");

        app.Pick("LibraryGroup", "CollapseAll");
        Wait.For(() => app.Lines().Length == 0
            && app.IsShown("LibraryGroupToggle-L") && app.IsShown("LibraryGroupToggle-S") && app.IsShown("LibraryGroupToggle-T"),
            "all groups collapse but their headers remain usable");
        app.Pick("LibraryGroup", "ExpandAll");
        Wait.For(() => app.Lines().Length == 3, "all groups expand again");
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
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-empty-filters", "LibraryPane");
        app.Press("LibraryEmptyClearFilters");
        Wait.For(() => app.Find("LibraryFilter").Current.HasKeyboardFocus,
            "focus to return to the surviving filter control");
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
        app.Press("LibraryEmptyClearSearch");
        Wait.For(() => app.Find("LibrarySearch").Current.HasKeyboardFocus
            && ((ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Length == 0,
            "clear-search focus to return to the still-existing search box");
        search = (ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("no such song");
        Wait.For(() => app.IsShown("LibraryEmptyClearSearch"), "the searchable empty view to return");
        Pointer.Key(app, "LibrarySearch", Pointer.Escape);
        Wait.For(() =>
        {
            try
            {
                var current = (ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern);
                return current.Current.Value.Length == 0 && app.Text("LibraryTitle") == "Songs";
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }, "Escape to clear the search");

        // Down moves from the search into what it found and chooses the first; Enter plays it.
        ((ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern)).SetValue("Sungba");
        Wait.For(() => app.Lines().SequenceEqual(["Sungba"]), "the search to find one song", () => string.Join(", ", app.Lines()));
        Pointer.Key(app, "LibrarySearch", Pointer.Down);
        Wait.For(() => app.IsChosen("Sungba"), "Down to choose the first found");
        Pointer.Key(Pointer.Enter);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "Enter to play the chosen song");

        Assert.Equal(0, app.Close());
    }
}
