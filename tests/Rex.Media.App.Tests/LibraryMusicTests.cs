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
        return [terminator, terminator, sungba];
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
        Screenshots.Match(app, "music-home", "LibraryHome");

        // A card's play button plays it without opening it.
        var lunguBoy = Card(app, "RecentlyAdded", "Lungu Boy");
        var play = Wait.Until(() => lunguBoy.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "MusicCardPlay")));
        ((InvokePattern)play.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the album to play from its first track");

        // Choosing the card opens the album's page.
        Pointer.Click(app, Card(app, "RecentlyAdded", "Work of Art"));
        Wait.For(() => app.Text("LibraryKicker") == "ALBUM" && app.Text("LibraryTitle") == "Work of Art", "the album's page");
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
        Wait.For(() => app.Lines().SequenceEqual(["Lonely At The Top", "Wave", "Mood", "Active (feat. Travis Scott)"]), "the four tracks in order");
        Assert.True(Holds(app, "LibraryList", "Disc 2"), "A set of discs has headings.");
        Assert.True(Holds(app, "LibraryList", "feat. Travis Scott"), "A featured artist shows under the title.");
        Assert.True(Holds(app, "LibraryList", "More by Asake"), "More by the artist under the tracks.");
        Screenshots.Match(app, "album-page", "LibraryPane");

        // Playing a track marks it as playing.
        app.Open("Wave");
        Wait.For(() => app.Text("NowPlaying") == "Wave", "the track to play");
        Wait.For(() => app.Lines().Contains("Wave, now playing"), "the playing track marked");

        // The artist's link opens their page.
        app.Press("LibraryHeroArtist-Asake");
        Wait.For(() => app.Text("LibraryKicker") == "ARTIST" && app.Text("LibraryTitle") == "Asake", "the artist's page");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheSongsTableShowsItsColumnsAndPlaysFromTheArt()
    {
        var played = AsakeAcrossAlbums();
        using var app = _scene.Ready(KeepBrowsing, played).Start();

        app.Visit("Songs");
        Wait.For(() => app.IsShown("SongsTableHeader"), "the table's headings");
        var terminator = app.Line("Terminator");
        Assert.NotNull(terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "2 plays")));
        Assert.NotNull(terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Work of Art")));

        var play = terminator.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "MusicSongArtPlay"));
        ((InvokePattern)play.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the song to play from its art");
        Assert.Equal(0, app.Close());
    }
}
