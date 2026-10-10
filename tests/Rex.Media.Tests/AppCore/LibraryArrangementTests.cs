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
    public void AViewsChoicesAreKeptInRange()
    {
        var wild = new LibraryViewChoice((LibraryLook)9, (LibrarySort)99, true, (LibraryGrouping)42, 5000).Normalize();
        Assert.Equal(new LibraryViewChoice(LibraryLook.Grid, LibrarySort.Natural, true, LibraryGrouping.None, LibraryViewChoice.LargestCard), wild);
        Assert.Equal(LibraryViewChoice.SmallestCard, new LibraryViewChoice(CardSize: 1).Normalize().CardSize);

        var settings = new PlayerSettings { LibraryViews = new Dictionary<string, LibraryViewChoice> { ["Songs"] = new(CardSize: 1), ["Bad"] = null! } }.Normalize();
        Assert.Equal(["Songs"], settings.LibraryViews.Keys);
        Assert.Equal(LibraryViewChoice.SmallestCard, settings.LibraryViews["Songs"].CardSize);
        Assert.Empty(new PlayerSettings { LibraryViews = null! }.Normalize().LibraryViews);
    }
}
