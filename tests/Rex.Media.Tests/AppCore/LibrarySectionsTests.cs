using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.AudioRulesTests;

namespace Rex.Media.Tests.AppCore;

/// <summary>Which views the library's sidebar lists: each only while it has something in it, in a fixed order, without blinking (LIB-05).</summary>
public sealed class LibrarySectionsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static readonly ListeningShelves NoListening = new([], [], [], []);

    private static LibraryFacts Facts(IReadOnlyCollection<LibraryEntry> entries, ListeningShelves? listening = null, Func<LibraryEntry, bool>? isOther = null,
        Func<string, TimeSpan?>? leftAt = null, bool recent = false, bool playlists = false, bool folders = false) =>
        LibraryFacts.Of(entries, [.. entries.Where(entry => entry.Kind != LibraryKind.Music || AudioRules.IsMusic(entry, MusicLookup.NotSearched))], listening ?? NoListening,
            video => !video.Path.Contains("Season", StringComparison.Ordinal), video => video.Path.Contains("Season", StringComparison.Ordinal), isOther,
            leftAt ?? (_ => null), recent, playlists, folders);

    private static LibraryEntry Video(string relative) => AudioFile(relative, TimeSpan.FromHours(2)) with { Kind = LibraryKind.Video };

    [Fact]
    [Capability("LIB-05")]
    public void AnEmptyLibraryListsHomeAlone()
    {
        Assert.Equal([SidebarView.Home], LibrarySections.Present(Facts([])));
        var sections = new LibrarySections();
        Assert.Equal([SidebarView.Home], sections.Shown);
        Assert.False(sections.Looked);
        Assert.False(sections.Observe(LibrarySections.Present(Facts([])), scanning: false, Now));
        Assert.True(sections.Looked);
        Assert.Equal([SidebarView.Home], sections.Shown);
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachViewHasItsOwnCondition()
    {
        var song = AudioFile("Music/Sungba.mp3", TimeSpan.FromMinutes(3), "Asake", "Mr. Money with the Vibe", genre: "Amapiano");
        var untitled = AudioFile("Music/Joha.mp3", TimeSpan.FromMinutes(3), "Asake", title: "Joha (Remix)");
        Assert.Equal([SidebarView.Home, SidebarView.Songs, SidebarView.Albums, SidebarView.Artists, SidebarView.Genres, SidebarView.RecentlyAdded],
            LibrarySections.Present(Facts([song])).Order());

        // No album tag, no Albums; no genre tag, no Genres.
        Assert.Equal([SidebarView.Home, SidebarView.Songs, SidebarView.Artists, SidebarView.RecentlyAdded], LibrarySections.Present(Facts([untitled])).Order());

        var movie = Video("Movies/Film (2007).mp4");
        var episode = Video("Show/Season 1/Arrival.mp4");
        var picture = AudioFile("Pictures/Lagos.jpg", null) with { Kind = LibraryKind.Picture };
        Assert.Equal([SidebarView.Home, SidebarView.Movies, SidebarView.Videos, SidebarView.RecentlyAdded], LibrarySections.Present(Facts([movie])).Order());
        Assert.Equal([SidebarView.Home, SidebarView.TvShows, SidebarView.Videos, SidebarView.RecentlyAdded], LibrarySections.Present(Facts([episode])).Order());
        Assert.Equal([SidebarView.Home, SidebarView.Movies, SidebarView.OtherVideos, SidebarView.Videos, SidebarView.ContinueWatching, SidebarView.RecentlyAdded],
            LibrarySections.Present(Facts([movie], isOther: _ => true, leftAt: _ => TimeSpan.FromMinutes(5))).Order());
        Assert.Equal([SidebarView.Home, SidebarView.Pictures, SidebarView.RecentlyAdded], LibrarySections.Present(Facts([picture])).Order());

        // Audiobooks, podcasts and recordings (sounds among them) are not music.
        var book = AudioFile("Audiobooks/Book.m4b", TimeSpan.FromHours(6));
        var ding = AudioFile("Downloads/ding.mp3", TimeSpan.FromSeconds(2));
        var listening = ListeningViews.Arrange([book, ding], AudioRules.Offline);
        Assert.Equal([SidebarView.Home, SidebarView.Audiobooks, SidebarView.Authors, SidebarView.Recordings, SidebarView.RecentlyAdded],
            LibrarySections.Present(Facts([book, ding], listening)).Order());
        var show = ListeningViews.Arrange([AudioFile("Podcasts/Show/Ep 1.mp3", TimeSpan.FromMinutes(30))], AudioRules.Offline);
        Assert.Contains(SidebarView.Podcasts, LibrarySections.Present(Facts([], show)));

        // Playlists, recently played and folders, by what the player and library keep.
        Assert.Equal([SidebarView.Home, SidebarView.RecentlyPlayed, SidebarView.Playlists, SidebarView.Folders],
            LibrarySections.Present(Facts([], recent: true, playlists: true, folders: true)).Order());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheViewsAreListedInAFixedOrderWithHomeAlways()
    {
        var sections = new LibrarySections();
        Assert.True(sections.Observe(new HashSet<SidebarView> { SidebarView.Folders, SidebarView.Songs, SidebarView.Podcasts }, scanning: true, Now));
        Assert.Equal([SidebarView.Home, SidebarView.Songs, SidebarView.Podcasts, SidebarView.Folders], sections.Shown);
        Assert.True(sections.IsShown(SidebarView.Home));
        Assert.False(sections.IsShown(SidebarView.Movies));
        Assert.Null(sections.NextLook);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AViewAppearsAndGoesOnlyOnceTheChangeHasSettledAndNoScanRuns()
    {
        var sections = new LibrarySections();
        sections.Observe(new HashSet<SidebarView>(), scanning: false, Now);

        // A scan finds a podcast: nothing changes while it runs, nor until the change has held.
        var podcasts = new HashSet<SidebarView> { SidebarView.Podcasts };
        Assert.False(sections.Observe(podcasts, scanning: true, Now));
        Assert.Equal(Now + LibrarySections.Settle, sections.NextLook);
        Assert.False(sections.Observe(podcasts, scanning: false, Now + TimeSpan.FromSeconds(1)));
        Assert.True(sections.Observe(podcasts, scanning: false, Now + TimeSpan.FromSeconds(1) + LibrarySections.Settle));
        Assert.Equal([SidebarView.Home, SidebarView.Podcasts], sections.Shown);
        Assert.Null(sections.NextLook);

        // A blink (gone, then back before settling) never shows.
        var later = Now + TimeSpan.FromMinutes(1);
        Assert.False(sections.Observe(new HashSet<SidebarView>(), scanning: false, later));
        Assert.False(sections.Observe(podcasts, scanning: false, later + TimeSpan.FromSeconds(1)));
        Assert.Null(sections.NextLook);

        // A different change restarts the clock.
        var recordings = new HashSet<SidebarView> { SidebarView.Recordings };
        Assert.False(sections.Observe(new HashSet<SidebarView>(), scanning: false, later + TimeSpan.FromSeconds(2)));
        Assert.False(sections.Observe(recordings, scanning: false, later + TimeSpan.FromSeconds(3)));
        Assert.False(sections.Observe(recordings, scanning: false, later + TimeSpan.FromSeconds(4)));
        Assert.True(sections.Observe(recordings, scanning: false, later + TimeSpan.FromSeconds(5)));
        Assert.Equal([SidebarView.Home, SidebarView.Recordings], sections.Shown);
    }

    [Fact]
    [Capability("LIB-05")]
    public void APersonsOwnChangeShowsAtOnce()
    {
        var sections = new LibrarySections();
        sections.Observe(new HashSet<SidebarView> { SidebarView.Recordings }, scanning: false, Now);
        Assert.True(sections.Observe(new HashSet<SidebarView> { SidebarView.Playlists }, scanning: true, Now, immediate: true));
        Assert.Equal([SidebarView.Home, SidebarView.Playlists], sections.Shown);
        Assert.False(sections.Observe(new HashSet<SidebarView> { SidebarView.Playlists, SidebarView.Home }, scanning: true, Now, immediate: true));
    }

    [Fact]
    public void NullsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LibrarySections.Present(null!));
        Assert.Throws<ArgumentNullException>(() => new LibrarySections().Observe(null!, false, Now));
        Func<LibraryEntry, bool> any = _ => true;
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of(null!, [], NoListening, any, any, null, _ => null, false, false, false));
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of([], null!, NoListening, any, any, null, _ => null, false, false, false));
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of([], [], null!, any, any, null, _ => null, false, false, false));
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of([], [], NoListening, null!, any, null, _ => null, false, false, false));
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of([], [], NoListening, any, null!, null, _ => null, false, false, false));
        Assert.Throws<ArgumentNullException>(() => LibraryFacts.Of([], [], NoListening, any, any, null, null!, false, false, false));
    }
}
