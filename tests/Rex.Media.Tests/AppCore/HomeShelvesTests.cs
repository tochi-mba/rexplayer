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
    public void NothingIsArrangedWithoutAnArgument()
    {
        Assert.Throws<ArgumentNullException>(() => HomeShelves.MostPlayed(null!, 20));
        Assert.Throws<ArgumentNullException>(() => HomeShelves.SeeAllMostPlayed(null!));
    }
}
