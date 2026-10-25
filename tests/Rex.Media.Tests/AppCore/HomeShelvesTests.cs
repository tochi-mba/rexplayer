using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Home's most played shelf and the Songs view its "See all" opens (LIB-05).</summary>
public sealed class HomeShelvesTests
{
    private static LibraryEntry Entry(string title, int plays, string? album = null, LibraryKind kind = LibraryKind.Music) => new()
    {
        Path = Path.Combine("D:" + Path.DirectorySeparatorChar, "Music", title + ".mp3"),
        Kind = kind,
        Title = title,
        Artist = "Asake",
        Album = album,
        Plays = plays,
    };

    private static readonly LibraryEntry[] Library =
    [
        Entry("Terminator", 2, "Mr Money With The Vibe"),
        Entry("Joha", 0, "Mr Money With The Vibe"),
        Entry("Lonely At The Top", 30, "Work Of Art"),
        Entry("Sungba", 9, "Mr Money With The Vibe"),
        Entry("Basquiat", 2, "Lungu Boy"),
        Entry("Sungba (live)", 50, kind: LibraryKind.Video),
    ];

    private static string[] Titles(IEnumerable<LibraryEntry> entries) => [.. entries.Select(entry => entry.Title)];

    [Fact]
    [Capability("LIB-05")]
    public void TheMostPlayedShelfHoldsPlayedSongsMostFirstWithTiesInTheSongsOrder()
    {
        // Never-played songs and videos stay off; the tie at two plays keeps the Songs view's album order.
        Assert.Equal(["Lonely At The Top", "Sungba", "Basquiat", "Terminator"], Titles(HomeShelves.MostPlayed(Library, 20)));
        Assert.Equal(["Lonely At The Top", "Sungba"], Titles(HomeShelves.MostPlayed(Library, 2)));
        Assert.Empty(HomeShelves.MostPlayed([Entry("Joha", 0)], 20));
    }

    [Fact]
    [Capability("LIB-05")]
    public void SeeAllOpensSongsInTheShelfsOrderSoTheViewStartsWithTheShelf()
    {
        var kept = new LibraryViewChoice(LibraryLook.Grid, LibrarySort.Title, Descending: true, LibraryGrouping.Letter, CardSize: 240, Filter: LibraryFilter.Unwatched);
        var opened = HomeShelves.SeeAllMostPlayed(kept);

        // The user's own layout, size and filters stay; only the order and the headings change.
        Assert.Equal(kept with { Sort = LibrarySort.MostPlayed, Descending = false, Grouping = LibraryGrouping.None }, opened);

        // Exactly what the Songs view shows in that choice, ordered and grouped as ShowLibrary does it.
        var sections = LibraryArrangement.Group(LibraryArrangement.Sort(LibraryViews.Songs(Library), opened.Sort, opened.Descending), opened.Grouping, DateTime.UtcNow);
        var view = Assert.Single(sections).Entries;
        var shelf = HomeShelves.MostPlayed(Library, 20);
        Assert.Equal(Titles(shelf), Titles(view.Take(shelf.Count)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheDownloadingShelfHoldsPartialFilesNewestFirstAndSeeAllOpensTheirViewSoFiltered()
    {
        var older = Entry("Sungba (live)", 0, kind: LibraryKind.Video) with { Downloading = true, Added = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) };
        var newer = Entry("Lungu Boy (film)", 0, kind: LibraryKind.Video) with { Downloading = true, Added = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc) };
        var song = Entry("Joha", 0) with { Downloading = true, Added = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc) };
        var library = new[] { Entry("Terminator", 2), older, song, newer };

        Assert.Equal(["Lungu Boy (film)", "Joha", "Sungba (live)"], Titles(HomeShelves.Downloading(library, 20)));
        Assert.Equal(["Lungu Boy (film)"], Titles(HomeShelves.Downloading(library, 1)));
        Assert.Empty(HomeShelves.Downloading(Library, 20));

        // The view with most of what is downloading opens, filtered to it and in the shelf's order; the user's own layout stays.
        var kept = new Dictionary<string, LibraryViewChoice> { ["Videos"] = new(LibraryLook.Wall, LibrarySort.Title, Grouping: LibraryGrouping.Folder, Filter: LibraryFilter.Unwatched) };
        var (view, choice) = HomeShelves.SeeAllDownloading(HomeShelves.Downloading(library, 20), key => kept.GetValueOrDefault(key) ?? new LibraryViewChoice());
        Assert.Equal("Videos", view);
        Assert.Equal(kept["Videos"] with { Download = LibraryDownloadFilter.Downloading, Sort = LibrarySort.Added, Grouping = LibraryGrouping.None }, choice);
        Assert.Equal(Titles(HomeShelves.Downloading(library, 20)).Where(title => title != "Joha"),
            Titles(LibraryArrangement.Sort(LibraryArrangement.Filter(LibraryViews.Videos(library), choice, _ => null), choice.Sort, choice.Descending)));
        Assert.Equal("Songs", HomeShelves.SeeAllDownloading([song], _ => new LibraryViewChoice()).View);
        Assert.Equal("Pictures", HomeShelves.SeeAllDownloading([song with { Kind = LibraryKind.Picture }], _ => new LibraryViewChoice()).View);
        Assert.Equal("Videos", HomeShelves.SeeAllDownloading([], _ => new LibraryViewChoice()).View);
    }

    [Fact]
    public void NothingIsArrangedWithoutAnArgument()
    {
        Assert.Throws<ArgumentNullException>(() => HomeShelves.MostPlayed(null!, 20));
        Assert.Throws<ArgumentNullException>(() => HomeShelves.SeeAllMostPlayed(null!));
        Assert.Throws<ArgumentNullException>(() => HomeShelves.Downloading(null!, 20));
        Assert.Throws<ArgumentNullException>(() => HomeShelves.SeeAllDownloading(null!, _ => new LibraryViewChoice()));
        Assert.Throws<ArgumentNullException>(() => HomeShelves.SeeAllDownloading([], null!));
    }
}
