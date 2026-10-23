using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Library views ordered and grouped as the user asks (LIB-05).</summary>
public sealed class LibraryArrangementTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Song(string title, string? artist = null, string? album = null, int? year = null, int plays = 0, double daysAgo = 400, double? playedDaysAgo = null, double? minutes = null, string folder = "Music", string? genre = null) => new()
    {
        Path = Path.Combine("D:" + Path.DirectorySeparatorChar, folder, title + ".mp3"),
        Kind = LibraryKind.Music,
        Title = title,
        Artist = artist,
        Album = album,
        Genre = genre,
        Year = year,
        Plays = plays,
        Added = Now.AddDays(-daysAgo),
        LastPlayed = playedDaysAgo is { } played ? Now.AddDays(-played) : null,
        Duration = minutes is { } length ? TimeSpan.FromMinutes(length) : null,
    };

    private static readonly LibraryEntry[] Asake =
    [
        Song("Sungba", "Asake", "Mr Money With The Vibe", 2022, plays: 9, daysAgo: 0.5, playedDaysAgo: 1, minutes: 3.3, genre: "Afrobeats"),
        Song("Terminator", "Asake", "Mr Money With The Vibe", 2022, plays: 2, daysAgo: 3, playedDaysAgo: 0.1, minutes: 2.9, genre: "Afrobeats"),
        Song("Lonely At The Top", "Asake", "Work Of Art", 2023, plays: 30, daysAgo: 20, minutes: 2.5, folder: "Albums"),
        Song("2:30", null, null, null, daysAgo: 100, minutes: 75),
        Song("Joha", "Asake", "Mr Money With The Vibe", 2022, daysAgo: 800),
    ];

    private static string[] Titles(IEnumerable<LibraryEntry> entries) => [.. entries.Select(entry => entry.Title)];

    [Fact]
    public void RefreshingAnOpenedGroupKeepsItsIdentityButNeverStaleMembers()
    {
        var opened = new LibraryGroup("Series", "1 season · 2 episodes", [Song("old")]);
        var current = new LibraryGroup("Series", "1 season · 1 episode", [Song("remaining")]);
        Assert.Same(current, LibraryViews.RefreshGroup([current], opened));
        var empty = LibraryViews.RefreshGroup([], opened);
        Assert.Equal(opened.Name, empty.Name);
        Assert.Empty(empty.Entries);
        Assert.Same(current, LibraryViews.RefreshGroup([current], empty));

        var otherArtist = new LibraryGroup("Series", "Other artist", [Song("other")]);
        Assert.Empty(LibraryViews.RefreshGroup([otherArtist], opened, detailIdentifiesGroup: true).Entries);
        Assert.Same(opened, LibraryViews.RefreshGroup([otherArtist, opened], opened, detailIdentifiesGroup: true));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.RefreshGroup(null!, opened));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.RefreshGroup([], null!));
    }

    [Fact]
    public void ReversingAnOrderPreservesTheOriginalOrderOfTies()
    {
        var first = Song("First", plays: 2);
        var second = Song("Second", plays: 2);
        var third = Song("Third", plays: 5);
        Assert.Equal([first, second, third], LibraryArrangement.Sort([first, second, third], LibrarySort.MostPlayed, true));
        var groups = new[]
        {
            new LibraryGroup("First", null, [first]),
            new LibraryGroup("Second", null, [second]),
            new LibraryGroup("Third", null, [third]),
        };
        Assert.Equal(groups, LibraryArrangement.Sort(groups, LibrarySort.MostPlayed, true));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachOrderPutsWhatMattersFirst()
    {
        Assert.Equal(Titles(Asake), Titles(LibraryArrangement.Sort(Asake, LibrarySort.Natural, false)));
        Assert.Equal(["2:30", "Joha", "Lonely At The Top", "Sungba", "Terminator"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Title, false)));
        Assert.Equal(["Terminator", "Sungba", "Lonely At The Top", "Joha", "2:30"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Title, true)));
        Assert.Equal(["Sungba", "Terminator", "Joha", "Lonely At The Top", "2:30"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Artist, false)));
        Assert.Equal(["Sungba", "Terminator", "Joha", "Lonely At The Top", "2:30"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Album, false)));
        Assert.Equal(["Sungba", "Terminator", "Joha", "Lonely At The Top", "2:30"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Year, false)));
        Assert.Equal(["Sungba", "Terminator", "Lonely At The Top", "2:30", "Joha"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Added, false)));
        Assert.Equal(["Terminator", "Sungba", "Lonely At The Top", "2:30", "Joha"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Played, false)));
        Assert.Equal(["Lonely At The Top", "Sungba", "Terminator", "2:30", "Joha"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.MostPlayed, false)));
        Assert.Equal(["Lonely At The Top", "Terminator", "Sungba", "2:30", "Joha"], Titles(LibraryArrangement.Sort(Asake, LibrarySort.Length, false)));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Sort((IReadOnlyList<LibraryEntry>)null!, LibrarySort.Title, false));
    }

    [Fact]
    [Capability("LIB-05")]
    public void GroupsHaveHeadingsInTheOrderThatReads()
    {
        string[] Headers(LibraryGrouping grouping) => [.. LibraryArrangement.Group(LibraryArrangement.Sort(Asake, LibrarySort.Title, false), grouping, Now).Select(section => section.Header)];

        Assert.Equal([""], Headers(LibraryGrouping.None));
        Assert.Empty(LibraryArrangement.Group([], LibraryGrouping.None, Now));
        Assert.Equal(["J", "L", "S", "T", "#"], Headers(LibraryGrouping.Letter));
        Assert.Equal(["Asake", LibraryViews.UnknownArtist], Headers(LibraryGrouping.Artist));
        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Headers(LibraryGrouping.Album));
        Assert.Equal(["Afrobeats", LibraryViews.UnknownGenre], Headers(LibraryGrouping.Genre));
        Assert.Equal(["2023", "2022", "Unknown year"], Headers(LibraryGrouping.Year));
        Assert.Equal(["2020s", "Unknown year"], Headers(LibraryGrouping.Decade));
        Assert.Equal(["Albums", "Music"], Headers(LibraryGrouping.Folder));
        Assert.Equal(["Today", "This week", "This month", "Earlier this year", "2024"], Headers(LibraryGrouping.Added));
        Assert.Equal(["Under 3 minutes", "3 to 6 minutes", "Over an hour", "Unknown length"], Headers(LibraryGrouping.Length));

        // Within a heading, the order asked for holds.
        var album = LibraryArrangement.Group(LibraryArrangement.Sort(Asake, LibrarySort.MostPlayed, false), LibraryGrouping.Album, Now)[0];
        Assert.Equal(["Sungba", "Terminator", "Joha"], Titles(album.Entries));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Group(null!, LibraryGrouping.Letter, Now));
    }

    [Fact]
    public void TheMiddleOfTheYearAndTheRestOfTheLengthsHaveTheirHeadings()
    {
        var entries = new[]
        {
            Song("Earlier", daysAgo: 60),
            Song("Long ago", daysAgo: 400),
            Song("Mid", minutes: 10),
            Song("Long", minutes: 40),
            Song("  ", daysAgo: 1),
        };

        Assert.Contains(LibraryArrangement.Group(entries, LibraryGrouping.Added, Now), section => section.Header == "Earlier this year");
        Assert.Equal(["6 to 20 minutes", "20 minutes to an hour", "Unknown length"], LibraryArrangement.Group(entries, LibraryGrouping.Length, Now).Select(section => section.Header));
        Assert.Equal("#", LibraryArrangement.Group([entries[4]], LibraryGrouping.Letter, Now)[0].Header);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnAccentedFirstLetterSitsUnderItsPlainLetter()
    {
        // "Édith" used to get an "É" heading of its own, apart from the other E's.
        var sections = LibraryArrangement.Group([Song("Ezra"), Song("Édith"), Song("éclair"), Song("Ñandú"), Song("1999")], LibraryGrouping.Letter, Now);
        Assert.Equal(["E", "N", "#"], sections.Select(section => section.Header));
        Assert.Equal(["Ezra", "Édith", "éclair"], sections[0].Entries.Select(entry => entry.Title));
        Assert.Equal("O", LibraryArrangement.LetterOf("  özil"));
        Assert.Equal("#", LibraryArrangement.LetterOf(null));
        Assert.Equal("#", LibraryArrangement.LetterOf(""));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AstralLettersAndUnicodeMarksUseStableAlphabeticHeadings()
    {
        // Deseret letters live beyond the BMP and use a UTF-16 surrogate pair.
        // Grouping by the first char previously placed them under "#".
        Assert.Equal("𐐀", LibraryArrangement.LetterOf("𐐨lpha"));
        Assert.Equal("E", LibraryArrangement.LetterOf("E\u0301lan"));
        Assert.Equal("#", LibraryArrangement.LetterOf("🎵 music"));
        Assert.Equal("#", LibraryArrangement.LetterOf("\uD800 malformed"));

        var sections = LibraryArrangement.Group(
            [Song("𐐨lpha"), Song("𐐀lpha"), Song("🎵 music")],
            LibraryGrouping.Letter, Now);
        Assert.Equal(["𐐀", "#"], sections.Select(x => x.Header));
        Assert.Equal(2, sections[0].Entries.Count);
    }

    [Fact]
    public void GroupsOfSongsAreOrderedByWhatIsInThem()
    {
        var albums = LibraryViews.Albums(Asake);
        string[] Names(LibrarySort sort, bool descending = false) => [.. LibraryArrangement.Sort(albums, sort, descending).Select(group => group.Name)];

        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Names(LibrarySort.Natural));
        Assert.Equal([LibraryViews.UnknownAlbum, "Work Of Art", "Mr Money With The Vibe"], Names(LibrarySort.Natural, descending: true));
        Assert.Equal(["Mr Money With The Vibe", LibraryViews.UnknownAlbum, "Work Of Art"], Names(LibrarySort.Title));
        Assert.Equal(["Mr Money With The Vibe", LibraryViews.UnknownAlbum, "Work Of Art"], Names(LibrarySort.Album));
        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Names(LibrarySort.Artist));
        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Names(LibrarySort.Year));
        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Names(LibrarySort.Added));
        Assert.Equal(["Mr Money With The Vibe", "Work Of Art", LibraryViews.UnknownAlbum], Names(LibrarySort.Played));
        Assert.Equal(["Work Of Art", "Mr Money With The Vibe", LibraryViews.UnknownAlbum], Names(LibrarySort.MostPlayed));
        Assert.Equal(["Work Of Art", "Mr Money With The Vibe", LibraryViews.UnknownAlbum], Names(LibrarySort.Length));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Sort((IReadOnlyList<LibraryGroup>)null!, LibrarySort.Title, false));
    }

    [Fact]
    [Capability("LIB-05")]
    public void SameTitlesKeepIndependentArtistAlbumAndPathIdentityWithUnknownFallback()
    {
        var first = Song("Same", artist: "Alice", album: "Release", genre: "Jazz");
        var second = Song("Same", artist: "Bob", album: "Release", genre: null)
            with { Path = Path.Combine("D:" + Path.DirectorySeparatorChar, "Other", "Same.mp3") };
        var missing = Song("Mystery", artist: null, album: null, genre: null);
        var originals = new[] { first, second, missing };

        var albums = LibraryViews.Albums(originals);
        Assert.Equal(3, albums.Count);
        Assert.Equal(["Alice", "Bob"], albums.Where(group => group.Name == "Release").Select(group => group.Detail));
        Assert.Equal(missing.Path, Assert.Single(albums.Single(group => group.Name == LibraryViews.UnknownAlbum).Entries).Path);

        var artists = LibraryViews.Artists(originals);
        Assert.Contains(artists, group => group.Name == "Alice" && group.Entries.Single().Path == first.Path);
        Assert.Contains(artists, group => group.Name == "Bob" && group.Entries.Single().Path == second.Path);
        Assert.Equal(missing.Path, Assert.Single(artists.Single(group => group.Name == LibraryViews.UnknownArtist).Entries).Path);

        var groups = LibraryArrangement.Group(originals, LibraryGrouping.Genre, Now);
        Assert.Equal(["Jazz", LibraryViews.UnknownGenre], groups.Select(section => section.Header));
        Assert.Equal([second.Path, missing.Path], groups[1].Entries.Select(entry => entry.Path));
        Assert.Equal([first, second, missing], LibraryArrangement.Sort(originals, LibrarySort.Natural, false));
    }

    [Fact]
    [Capability("LIB-05")]
    public void CollageCardShapesAreBoundedVariedAndRepeatable()
    {
        foreach (var (kind, poster) in new[]
        {
            (LibraryKind.Music, false), (LibraryKind.Picture, false),
            (LibraryKind.Video, false), (LibraryKind.Video, true),
        })
        {
            var ratios = Enumerable.Range(0, 16).Select(i => CollageLayout.AspectRatio(kind, poster, i)).ToArray();
            Assert.All(ratios, ratio => Assert.InRange(ratio, 0.70, 2.1));
            Assert.True(ratios.Distinct().Count() > 3);
            Assert.Equal(ratios[..8], ratios[8..]);
            Assert.Equal(ratios[3], CollageLayout.AspectRatio(kind, poster, -3));
            Assert.Equal(ratios[0], CollageLayout.AspectRatio(kind, poster, int.MinValue));
        }

        Assert.NotEqual(CollageLayout.AspectRatio(LibraryKind.Video, false, 0), CollageLayout.AspectRatio(LibraryKind.Video, true, 0));
        Assert.Equal(LibraryLook.Collage, new LibraryViewChoice(LibraryLook.Collage).Normalize().Look);
        Assert.Equal(LibraryLook.Collage,
            new PlayerSettings { LibraryViews = new Dictionary<string, LibraryViewChoice> { ["Pictures"] = new(LibraryLook.Collage) } }
                .Normalize().LibraryViews["Pictures"].Look);
    }

    [Fact]
    [Capability("LIB-05")]
    public void FilesAreOrderedByWhenTheyChangedTheirSizeAndWhereTheyAre()
    {
        var entries = new[]
        {
            Song("B", folder: "Music") with { Modified = Now.AddDays(-2), Size = 10 },
            Song("A", folder: "Albums") with { Modified = Now.AddDays(-1), Size = 30 },
            Song("Track 10", folder: "Music") with { Modified = Now.AddDays(-3), Size = 20 },
            Song("Track 2", folder: "Music") with { Modified = Now.AddDays(-3), Size = 20 },
        };

        Assert.Equal(["A", "B", "Track 10", "Track 2"], Titles(LibraryArrangement.Sort(entries, LibrarySort.Modified, false)));
        Assert.Equal(["A", "Track 10", "Track 2", "B"], Titles(LibraryArrangement.Sort(entries, LibrarySort.Size, false)));
        Assert.Equal(["A", "B", "Track 2", "Track 10"], Titles(LibraryArrangement.Sort(entries, LibrarySort.Path, false)));
        Assert.Equal(["Track 10", "Track 2", "B", "A"], Titles(LibraryArrangement.Sort(entries, LibrarySort.Path, true)));

        var albums = new[] { new LibraryGroup("Later", null, [entries[0]]), new LibraryGroup("Big", null, [entries[1], entries[2]]), new LibraryGroup("Empty", null, []) };
        Assert.Equal(["Big", "Later", "Empty"], LibraryArrangement.Sort(albums, LibrarySort.Modified, false).Select(group => group.Name));
        Assert.Equal(["Big", "Later", "Empty"], LibraryArrangement.Sort(albums, LibrarySort.Size, false).Select(group => group.Name));
        Assert.Equal(["Empty", "Big", "Later"], LibraryArrangement.Sort(albums, LibrarySort.Path, false).Select(group => group.Name));
        Assert.Equal("Empty", LibraryArrangement.Sort(albums, LibrarySort.Added, false)[^1].Name);
    }

    [Fact]
    [Capability("LIB-05")]
    public void QuickFiltersKeepWhatIsUnwatchedPartWayOrFinishedOfOneKindInOneFolder()
    {
        var video = Song("Episode", folder: "Shows") with { Kind = LibraryKind.Video };
        var halfway = Song("Film", folder: "Films") with { Kind = LibraryKind.Video, Plays = 1 };
        var photo = Song("Beach", folder: "Photos") with { Kind = LibraryKind.Picture };
        var entries = new[] { Asake[0], video, halfway, photo, Asake[3] };
        TimeSpan? LeftAt(string path) => path == halfway.Path ? TimeSpan.FromMinutes(5) : null;

        Assert.Equal(WatchState.Finished, LibraryArrangement.StateOf(Asake[0], LeftAt));
        Assert.Equal(WatchState.InProgress, LibraryArrangement.StateOf(halfway, LeftAt));
        Assert.Equal(WatchState.Unwatched, LibraryArrangement.StateOf(video, LeftAt));

        var all = new LibraryViewChoice();
        Assert.Same(entries, LibraryArrangement.Filter(entries, all, LeftAt));
        Assert.Equal(["Episode", "Beach", "2:30"], Titles(LibraryArrangement.Filter(entries, all with { Filter = LibraryFilter.Unwatched }, LeftAt)));
        Assert.Equal(["Film"], Titles(LibraryArrangement.Filter(entries, all with { Filter = LibraryFilter.InProgress }, LeftAt)));
        Assert.Equal(["Sungba"], Titles(LibraryArrangement.Filter(entries, all with { Filter = LibraryFilter.Finished }, LeftAt)));
        Assert.Equal(["Episode", "Film"], Titles(LibraryArrangement.Filter(entries, all with { Kinds = LibraryKindFilter.Video }, LeftAt)));
        Assert.Equal(["Sungba", "2:30"], Titles(LibraryArrangement.Filter(entries, all with { Kinds = LibraryKindFilter.Music }, LeftAt)));
        Assert.Equal(["Beach"], Titles(LibraryArrangement.Filter(entries, all with { Kinds = LibraryKindFilter.Picture }, LeftAt)));
        Assert.Equal(["Episode"], Titles(LibraryArrangement.Filter(entries, all with { Folder = Path.GetDirectoryName(video.Path) }, LeftAt)));
        Assert.Equal(["Episode"], Titles(LibraryArrangement.Filter(entries, all with { Kinds = LibraryKindFilter.Video, Filter = LibraryFilter.Unwatched }, LeftAt)));

        Assert.Equal("Filter", LibraryArrangement.FilterLabel(all));
        Assert.Equal("Unwatched \u00B7 Videos \u00B7 Shows", LibraryArrangement.FilterLabel(all with { Filter = LibraryFilter.Unwatched, Kinds = LibraryKindFilter.Video, Folder = Path.GetDirectoryName(video.Path) }));
        Assert.Equal("In progress \u00B7 Music", LibraryArrangement.FilterLabel(all with { Filter = LibraryFilter.InProgress, Kinds = LibraryKindFilter.Music }));
        Assert.Equal("Finished \u00B7 Pictures \u00B7 D:" + Path.DirectorySeparatorChar, LibraryArrangement.FilterLabel(all with { Filter = LibraryFilter.Finished, Kinds = LibraryKindFilter.Picture, Folder = "D:" + Path.DirectorySeparatorChar }));
        Assert.True((all with { Folder = "x" }).IsFiltered);
        Assert.False((all with { Folder = "x" }).Unfiltered().IsFiltered);

        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.StateOf(null!, LeftAt));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.StateOf(video, null!));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Filter(null!, all, LeftAt));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Filter(entries, null!, LeftAt));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.Filter(entries, all, null!));
        Assert.Throws<ArgumentNullException>(() => LibraryArrangement.FilterLabel(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheDownloadFilterKeepsWholeOrStillDownloadingFilesAndJoinsTheOthers()
    {
        var arriving = Song("Film", folder: "Films") with { Kind = LibraryKind.Video, Downloading = true, FinalSize = 1000 };
        var entries = new[] { Asake[0], arriving, Asake[3] with { Downloading = true } };
        TimeSpan? LeftAt(string path) => null;
        var all = new LibraryViewChoice();

        Assert.Equal(["Film", "2:30"], Titles(LibraryArrangement.Filter(entries, all with { Download = LibraryDownloadFilter.Downloading }, LeftAt)));
        Assert.Equal(["Sungba"], Titles(LibraryArrangement.Filter(entries, all with { Download = LibraryDownloadFilter.Complete }, LeftAt)));
        Assert.Equal(["Film"], Titles(LibraryArrangement.Filter(entries, all with { Download = LibraryDownloadFilter.Downloading, Kinds = LibraryKindFilter.Video }, LeftAt)));
        Assert.Empty(LibraryArrangement.Filter(entries, all with { Download = LibraryDownloadFilter.Downloading, Filter = LibraryFilter.Finished }, LeftAt));

        Assert.Equal("Still downloading", LibraryArrangement.FilterLabel(all with { Download = LibraryDownloadFilter.Downloading }));
        Assert.Equal("Music \u00B7 Fully downloaded", LibraryArrangement.FilterLabel(all with { Kinds = LibraryKindFilter.Music, Download = LibraryDownloadFilter.Complete }));
        Assert.Equal("All files", LibraryArrangement.DownloadName(LibraryDownloadFilter.Any));
        Assert.True((all with { Download = LibraryDownloadFilter.Complete }).IsFiltered);
        Assert.Equal(all, (all with { Download = LibraryDownloadFilter.Complete }).Unfiltered());
        Assert.Equal(LibraryDownloadFilter.Any, (all with { Download = (LibraryDownloadFilter)9 }).Normalize().Download);
    }

    [Fact]
    public void AViewsChoicesAreKeptInRange()
    {
        var wild = new LibraryViewChoice((LibraryLook)9, (LibrarySort)99, true, (LibraryGrouping)42, 5000, (LibraryFilter)7, (LibraryKindFilter)8, "  ").Normalize();
        Assert.Equal(new LibraryViewChoice(LibraryLook.Grid, LibrarySort.Natural, true, LibraryGrouping.None, LibraryViewChoice.LargestCard), wild);
        Assert.Equal(" D:\\x ".Trim(), new LibraryViewChoice(Folder: " D:\\x ").Normalize().Folder);
        Assert.Equal(LibraryViewChoice.SmallestCard, new LibraryViewChoice(CardSize: 1).Normalize().CardSize);

        var settings = new PlayerSettings { LibraryViews = new Dictionary<string, LibraryViewChoice> { ["Songs"] = new(CardSize: 1), ["Bad"] = null! } }.Normalize();
        Assert.Equal(["Songs"], settings.LibraryViews.Keys);
        Assert.Equal(LibraryViewChoice.SmallestCard, settings.LibraryViews["Songs"].CardSize);
        Assert.Empty(new PlayerSettings { LibraryViews = null! }.Normalize().LibraryViews);
    }
}
