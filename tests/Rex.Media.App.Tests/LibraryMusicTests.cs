using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The streaming-style music pages on the real window, with Asake's songs across three albums: the
/// music Home's hero and rows, playing from a card without opening it, an album's header and
/// numbered track list, and the Songs table.
/// </summary>
[Collection("desktop")]
public sealed class LibraryMusicTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    /// <summary>Asake on three albums, with Lungu Boy whole on two discs and two songs played.</summary>
    private string[] AsakeAcrossAlbums()
    {
        _scene.Track("Lonely At The Top", "Lungu Boy", 1);
        _scene.Track("Wave", "Lungu Boy", 2);
        _scene.Track("Mood", "Lungu Boy", 3);
        _scene.Track("Active (feat. Travis Scott)", "Lungu Boy", 1, disc: 2);
        var terminator = _scene.Song("Terminator", "Work of Art", 2023, "Afrobeats");
        var sungba = _scene.Song("Sungba", "Mr. Money with the Vibe", 2022, "Amapiano");
        return [terminator, terminator, terminator, sungba];
    }

    /// <summary>Playing keeps the library open where it is, so a flow goes on from the same page.</summary>
    private static Rex.Media.Settings.PlayerSettings KeepBrowsing(Rex.Media.Settings.PlayerSettings settings) =>
        settings with { ShowPlayerWhenLibraryMediaStarts = false, OpenLibraryAtPlayingItem = false };

    private static AutomationElement Card(AppProcess app, string shelf, string name) =>
        Wait.Until(() => app.Find("MusicShelf-" + shelf).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name))));

    private static bool Holds(AppProcess app, string id, string text) =>
        app.Find(id).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, text)) is not null;

    [Fact]
    [Capability("LIB-05")]
    public void TheMusicHomeFeaturesWhatIsPlayedLatelyOverItsRows()
    {
        var played = AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing, played).Start();

        app.Visit("Music");
        Wait.For(() => app.IsShown("MusicHero"), "the music Home's hero");
        Assert.Equal("Work of Art", app.Text("MusicHeroTitle"));
        Card(app, "OnRepeat", "Terminator");
        Card(app, "RecentlyAdded", "Lungu Boy");
        Card(app, "JumpBackIn", "Mr. Money with the Vibe");
        Card(app, "NewArtists", "Asake");
        Card(app, "Mixes", "Afrobeats Mix");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "music-home", "LibraryHome");

        // A card's play button plays it without opening it.
        var lunguBoy = Card(app, "RecentlyAdded", "Lungu Boy");
        var play = Wait.Until(() => lunguBoy.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "MusicCardPlay")));
        ((InvokePattern)play.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the album to play from its first track");

        // Choosing the card opens the album's page.
        ((InvokePattern)Card(app, "RecentlyAdded", "Work of Art").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => app.Text("LibraryKicker") == "ALBUM" && app.Text("LibraryTitle") == "Work of Art", "the album's page");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void MusicHomePinsOnlyRealAlbumsAndAllowsRemovingThem()
    {
        var played = AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing, played).Start();

        app.Visit("Music");
        Wait.For(() => app.IsShown("MusicHeroPin"), "a keyboard-accessible pin control on the featured real album");
        Assert.False(app.IsShown("MusicShelf-Pinned"));

        app.Press("MusicHeroPin");
        Wait.For(() => app.IsShown("MusicShelf-Pinned"), "the pinned shelf to appear after saving");
        Card(app, "Pinned", "Work of Art");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "music-home-with-pinned-album", "LibraryHome");
        app.Press("MusicHeroPin");
        Wait.For(() => !app.IsShown("MusicShelf-Pinned"), "the empty pinned shelf to disappear");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void UnifiedHomeShowsOnlyAlbumsTheUserActuallyPinned()
    {
        var played = AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing, played).Start();
        app.Visit("Home");
        Assert.False(app.IsShown("LibraryShelfCards-PinnedAlbums"));

        app.Visit("Music");
        Wait.For(() => app.IsShown("MusicHeroPin") && app.Text("MusicHeroTitle") == "Work of Art",
            "the real album to be featured before pinning");
        app.Press("MusicHeroPin");
        app.Visit("Home");
        Wait.For(() => app.IsShown("LibraryShelfCards-PinnedAlbums"),
            "the unified Home page to include the pinned album shelf");
        var pinned = app.Line("Work of Art", "LibraryShelfCards-PinnedAlbums");
        Assert.NotNull(pinned);
        Assert.False(app.IsShown("LibraryShelf-PinnedAlbums"), "Pinned shelf does not falsely offer See all albums as pinned.");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-home-with-pinned-album", "LibraryPane");

        app.Visit("Music");
        app.Press("MusicHeroPin");
        app.Visit("Home");
        Wait.For(() => !app.IsShown("LibraryShelfCards-PinnedAlbums"), "the empty pinned shelf to disappear");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnAlbumPageShowsItsHeaderTracksDiscsAndMoreByTheArtist()
    {
        AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing).Start();

        app.Visit("Albums");
        app.Open("Lungu Boy");
        Wait.For(() => app.Text("LibraryKicker") == "ALBUM" && app.Text("LibraryTitle") == "Lungu Boy", "the album's page");
        // Four short songs and no catalogue to ask: the songs say it is an EP.
        Assert.StartsWith("EP · 2024 · 4 songs", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Assert.True(app.IsShown("LibraryHeroArtist-Asake"), "The artist is a link under the title.");
        Assert.True(app.IsShown("LibraryMore"), "The page has a menu beside Play, Shuffle and Add.");
        Assert.False(app.IsShown("LibraryLook"), "An album page has its own layout.");

        // The tracks in disc order, under disc headings, with featured artists under the title.
        Wait.For(() => app.Lines().Take(4).SequenceEqual(["Lonely At The Top", "Wave", "Mood", "Active (feat. Travis Scott)"]), "the four tracks in order",
            () => string.Join(", ", app.Lines()));
        Assert.True(Holds(app, "LibraryList", "Disc 2"), "A set of discs has headings.");
        Assert.True(Holds(app, "LibraryList", "feat. Travis Scott"), "A featured artist shows under the title.");
        Assert.True(Holds(app, "LibraryList", "More by Asake"), "More by the artist under the tracks.");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "album-page", "LibraryPane");

        // Playing a track marks it as playing.
        app.Open("Wave");
        Wait.For(() => app.Text("NowPlaying") == "Wave", "the track to play");
        Wait.For(() => app.Lines().Contains("Wave, now playing"), "the playing track marked");

        // The artist's link opens their page.
        // Playing a track may compact the scroll-linked album header.
        // Return to its top to reveal the existing, real artist link.
        var albumList = app.Find("LibraryList");
        if (albumList.TryGetCurrentPattern(ScrollPattern.Pattern, out var scrolling))
        {
            ((ScrollPattern)scrolling).SetScrollPercent(ScrollPattern.NoScroll, 0);
        }

        Wait.For(() => app.IsShown("LibraryHeroArtist-Asake"), "the album artist link to return after scrolling to the header");
        app.Press("LibraryHeroArtist-Asake");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST" && app.Text("LibraryTitle") == "Asake", "the artist's page");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ArtistPagesPreferTheirPortraitsAndShowTheirCreditedBiography()
    {
        AsakeAcrossAlbums();
        var cache = new Rex.Media.AppCore.Metadata.MetadataCache(Path.Combine(_scene.Root, "metadata"));
        var artist = new Rex.Media.AppCore.Metadata.EnrichedArtist
        {
            Schema = Rex.Media.AppCore.Metadata.EnrichedArtist.CurrentSchema,
            Name = "Asake",
            MusicBrainzId = "known-artist",
            Origin = "Lagos, Nigeria",
            ActiveFrom = 2018,
            Genres = ["Afrobeats"],
            Bio = "  A Nigerian singer and songwriter.  ",
            ArticleUrl = "https://en.wikipedia.org/wiki/Asake",
            HasPhoto = true,
        };
        cache.WriteText(Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistKind,
            Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistKey("Asake"), artist.ToJson());
        cache.Write(Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistPhotoKind,
            Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistKey("Asake"),
            Rex.Media.AppCore.Metadata.MetadataFetcher.ArtExtension,
            File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg")));

        using var app = _scene.Ready(s => s with
        {
            Metadata = new Rex.Media.Settings.MetadataSettings { Music = true },
            ShowPlayerWhenLibraryMediaStarts = false,
        }).Start();
        app.Visit("Artists");
        app.Open("Asake");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST"
            && app.Text("LibraryFacts").Contains("Lagos, Nigeria", StringComparison.Ordinal)
            && app.Text("LibraryOverview") == "A Nigerian singer and songwriter.",
            "the artist's cached facts and biography");
        Assert.Equal("Description from Wikipedia", app.Text("LibraryCredit"));
        Wait.For(() => app.IsShown("LibraryHeroArt") && app.Text("LibraryHeroArt") == "Portrait of Asake", "the decoded portrait to be accessible");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "Artists" && !app.IsShown("LibraryOverview"),
            "the biography clears when leaving the artist page");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void SongsKeepTheirSortingSelectionQueueActionsAndHonestRemovedFileState()
    {
        AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing).Start();
        app.Visit("Songs");
        app.Pick("LibraryLook", "List");
        app.Pick("LibrarySort", "Title");
        Wait.For(() => app.Lines().FirstOrDefault() == "Active (feat. Travis Scott)",
            "the songs to use their chosen title order");
        var chosen = app.Line("Terminator");
        ((SelectionItemPattern)chosen.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Assert.True(app.IsShown("LibraryEnqueue"), "The selected song has a direct queue action.");
        app.Press("LibraryEnqueue");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Find("PlaylistView").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Terminator")) is not null,
            "the chosen song to appear in Up Next");

        var missing = Path.Combine(_scene.Media, "Wave.wav");
        File.Delete(missing);
        app.Visit("Folders");
        app.Press("LibraryRescan");
        app.Visit("Songs");
        Wait.For(() => !app.Lines().Contains("Wave"), "removed media to disappear without a ghost row");
        Assert.Contains(app.Lines(), line => line is "Terminator" or "Terminator, now playing");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheSongsTableShowsItsColumnsAndPlaysFromTheArt()
    {
        var played = AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing, played).Start();

        app.Visit("Songs");
        Wait.For(() => app.IsShown("SongsTableHeading-Plays"), "the table's headings", () => string.Join(", ",
            app.Find("LibraryList").FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Take(25)
                .Select(element => element.Current.AutomationId + "=" + element.Current.Name)));
        var terminator = app.Line("Terminator");
        Assert.NotNull(terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "3 plays")));
        Assert.NotNull(terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Asake")));
        Assert.NotNull(terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Work of Art")));

        var play = terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "MusicSongArtPlay"));
        ((InvokePattern)play.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the song to play from its art");

        // The playing line tells a screen reader it is playing, and its plays read as a number.
        Wait.For(() => app.Lines().Contains("Terminator, now playing"), "the playing line named as playing");
        Assert.NotNull(app.Line("Terminator, now playing").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Work of Art")));
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "songs-table", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
