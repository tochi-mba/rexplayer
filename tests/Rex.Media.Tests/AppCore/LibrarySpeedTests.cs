using System.Diagnostics;
using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The library stays quick as it grows (LIB-05): views are kept beside the library's snapshot, and
/// search goes over a prepared index that finds exactly what the culture's loose comparison finds.
/// </summary>
public sealed class LibrarySpeedTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private static LibraryEntry Song(int i, string title, string? artist = "Asake", string? album = "Work of Art", string? genre = "Afrobeats") => new()
    {
        Path = $@"D:\Music\{i:00000} {title}.mp3",
        Kind = LibraryKind.Music,
        Title = title,
        Artist = artist,
        Album = album,
        Genre = genre,
        Track = i % 14 + 1,
        Added = Monday.AddMinutes(-i),
        Plays = i % 7,
    };

    private static LibraryEntry Video(int i, string name) => new()
    {
        Path = $@"D:\Videos\{name}.mkv",
        Kind = LibraryKind.Video,
        Title = name,
        Added = Monday.AddMinutes(-i),
    };

    /// <summary>A synthetic library: songs over many albums and artists, episodes of several shows, movies and pictures.</summary>
    private static IReadOnlyList<LibraryEntry> Synthetic(int count)
    {
        string[] words = ["Lonely At The Top", "Terminator", "Sungba", "Joha", "Organise", "Basquiat", "Amapiano", "Peace Be Unto You", "Dull", "Remember"];
        string[] artists = ["Asake", "Burna Boy", "Wizkid", "Olamide", "Rema", "Tems", "Ayra Starr", "Fireboy DML"];
        var entries = new List<LibraryEntry>(count);
        for (var i = 0; entries.Count < count; i++)
        {
            entries.Add((i % 10) switch
            {
                7 => Video(i, $"Show {i % 23}.S{i % 5 + 1:00}E{i % 20 + 1:00}.1080p"),
                8 => Video(i, $"Some Movie {i} ({1980 + i % 40})"),
                9 => new LibraryEntry { Path = $@"D:\Pictures\IMG_{i:00000}.jpg", Kind = LibraryKind.Picture, Title = $"IMG_{i:00000}", Added = Monday.AddMinutes(-i) },
                _ => Song(i, $"{words[i % words.Length]} {i}", artists[i % artists.Length], $"Album {i % 400}"),
            });
        }

        return Array.AsReadOnly(entries.ToArray());
    }

    /// <summary>The search as the library always did it: every word in one of the fields, by the culture's loose comparison.</summary>
    private static string[] Reference(IEnumerable<LibraryEntry> entries, string text, Func<LibraryEntry, IEnumerable<string?>>? extra = null)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Has(string? field, string word) => field is not null && compare.IndexOf(field, word, Loose) >= 0;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = entries.Where(entry => words.Length > 0 && words.All(word =>
            new[] { entry.Title, entry.Artist, entry.AlbumArtist, entry.Album, entry.Genre, Path.GetFileName(entry.Path) }.Any(field => Has(field, word))
            || (extra?.Invoke(entry) ?? []).Any(field => Has(field, word)))).ToList();
        return [.. LibraryViews.Songs(found).Concat(LibraryViews.Videos(found)).Concat(LibraryViews.Pictures(found)).Select(entry => entry.Path)];
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("asake")]
    [InlineData("ASAKE work")]
    [InlineData("ore")]
    [InlineData("Ọ̀rẹ́")]
    [InlineData("cafe")]
    [InlineData("ｓｕｎｇｂａ")]
    [InlineData("カタカナ")]
    [InlineData("かたかな")]
    [InlineData("-")]
    [InlineData("top 12")]
    [InlineData(".mp3")]
    [InlineData("شمس")]
    [InlineData("nothing like this")]
    [InlineData("   ")]
    public void TheIndexFindsWhatTheCulturesLooseSearchFinds(string text)
    {
        IReadOnlyList<LibraryEntry> entries =
        [
            .. Synthetic(300),
            Song(90001, "Ọ̀rẹ́", "Olamide", "Ọ̀rẹ́"),
            Song(90002, "Café del Mar", "Energy 52", null, null),
            Song(90003, "Sungba", "Asake", "Mr. Money"),
            Song(90004, "カタカナ", "Artist-Name", "A-B"),
            Song(90005, "شمس", "فنان", "ألبوم"),
            Song(90006, "Tab\there", null, null, null),
        ];
        var index = LibrarySearchIndex.For(entries);
        string[] found = [.. index.Search(text, cancel: TestContext.Current.CancellationToken).Select(entry => entry.Path)];
        Assert.Equal(Reference(entries, text), found);
    }

    [Fact]
    [Capability("LIB-05")]
    public void DownloadedFactsAreAskedOnlyWhenTheFieldsMissAndOnlyOncePerEntry()
    {
        IReadOnlyList<LibraryEntry> entries = [Video(1, "The Matrix (1999)"), Video(2, "Heat (1995)")];
        var asked = new List<string>();
        string? Facts(LibraryEntry entry)
        {
            asked.Add(entry.Title);
            return entry.Title.StartsWith("The Matrix", StringComparison.Ordinal) ? "Science fiction\nKeanu Reeves" : null;
        }

        var found = LibrarySearchIndex.For(entries).Search("matrix keanu science", Facts, TestContext.Current.CancellationToken);
        Assert.Equal(["The Matrix (1999)"], found.Select(entry => entry.Title));
        Assert.Equal(["The Matrix (1999)", "Heat (1995)"], asked);
        string[] keanu = [.. LibrarySearchIndex.For(entries).Search("Keanu", Facts, TestContext.Current.CancellationToken).Select(entry => entry.Path)];
        Assert.Equal(Reference(entries, "Keanu", entry => [Facts(entry)]), keanu);
        Assert.Empty(LibrarySearchIndex.For(entries).Search(null, cancel: TestContext.Current.CancellationToken));
    }

    [Fact]
    [Capability("LIB-05")]
    public void OneFieldMatchesLikeTheLibrarySearchAndASupersededSearchStops()
    {
        Assert.True(LibrarySearchIndex.Matches("Café", "CAFE"));
        Assert.True(LibrarySearchIndex.Matches("ｓｕｎｇｂａ", "Sungba"));
        Assert.False(LibrarySearchIndex.Matches(null, "x"));
        Assert.False(LibrarySearchIndex.Matches("Terminator", "Sungba"));
        Assert.Throws<ArgumentNullException>(() => LibrarySearchIndex.Matches("x", null!));
        Assert.Throws<ArgumentNullException>(() => LibrarySearchIndex.For(null!));

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => LibrarySearchIndex.For(Synthetic(10)).Search("asake", cancel: cancel.Token));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ViewsAreKeptBesideTheirSnapshotAndWorkedOutOnce()
    {
        var snapshot = Synthetic(200);
        var calls = 0;
        Assert.Equal(1, LibraryMemo.Of(snapshot, "count", () => ++calls));
        Assert.Equal(1, LibraryMemo.Of(snapshot, "count", () => ++calls));
        Assert.Equal(2, LibraryMemo.Of(Synthetic(200), "count", () => ++calls));

        LibraryMemo.Warm(snapshot);
        Assert.Same(LibraryMemo.Albums(snapshot), LibraryMemo.Albums(snapshot));
        Assert.Equal(LibraryViews.Albums(snapshot).Select(album => album.Name), LibraryMemo.Albums(snapshot).Select(album => album.Name));
        Assert.Equal(LibraryArrangement.Sort(LibraryViews.Albums(snapshot), Rex.Media.Settings.LibrarySort.Added, false).Select(album => album.Name),
            LibraryMemo.AlbumsByAdded(snapshot).Select(album => album.Name));
        Assert.Equal(LibraryViews.Artists(snapshot).Select(artist => artist.Name), LibraryMemo.Artists(snapshot).Select(artist => artist.Name));
        Assert.Equal(LibraryViews.TvShows(snapshot).Select(show => show.Name), LibraryMemo.TvShows(snapshot).Select(show => show.Name));
        Assert.Equal(LibraryViews.Songs(snapshot), LibraryMemo.Songs(snapshot));
        Assert.Equal(LibraryViews.Videos(snapshot), LibraryMemo.Videos(snapshot));
        Assert.Equal(LibraryViews.Pictures(snapshot), LibraryMemo.Pictures(snapshot));
        Assert.Equal(LibraryViews.RecentlyAdded(snapshot), LibraryMemo.RecentlyAdded(snapshot));
        Assert.Equal(HomeShelves.MostPlayed(snapshot, 20), LibraryMemo.MostPlayed(snapshot));
        Assert.Equal((140, 40, 20), LibraryMemo.Counts(snapshot));

        Assert.Throws<ArgumentNullException>(() => LibraryMemo.Of<int>(null!, "x", () => 0));
        Assert.Throws<ArgumentNullException>(() => LibraryMemo.Of<int>(snapshot, null!, () => 0));
        Assert.Throws<ArgumentNullException>(() => LibraryMemo.Of<int>(snapshot, "x", null!));
        Assert.Throws<ArgumentNullException>(() => LibraryMemo.Warm(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheLibraryHandsOutTheSameSnapshotUntilItChanges()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        var music = Path.Combine("D:" + Path.DirectorySeparatorChar, "Music");
        Assert.True(library.AddFolder(music));
        library.Scan(music, [new LibraryFile(Path.Combine(music, "a.mp3"), 100, Monday)], _ => LibraryKind.Music);
        var first = library.Entries;
        Assert.Same(first, library.Entries);
        library.Scan(music, [new LibraryFile(Path.Combine(music, "a.mp3"), 100, Monday), new LibraryFile(Path.Combine(music, "b.mp3"), 100, Monday)], _ => LibraryKind.Music);
        Assert.NotSame(first, library.Entries);
        Assert.Single(first);
        Assert.Equal(2, library.Entries.Count);
    }

    /// <summary>
    /// The proof the window stays quick over a big library: on a background thread, as the window
    /// asks for them, Home's views of ten thousand entries are worked out well inside a budget, a
    /// return to Home over the unchanged library is free, and a keystroke's search is well inside
    /// 150 ms. The bounds are generous so a slow CI machine never fails them.
    /// </summary>
    [Fact]
    [Capability("LIB-05")]
    public async Task TenThousandEntriesAreWorkedOutAndSearchedWithinBudgetOffTheWindowThread()
    {
        var snapshot = Synthetic(10_000);
        var (first, again, search, searchAgain) = await Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            LibraryMemo.Warm(snapshot);
            var first = clock.Elapsed;
            clock.Restart();
            LibraryMemo.Warm(snapshot);
            var again = clock.Elapsed;
            var index = LibrarySearchIndex.For(snapshot);
            clock.Restart();
            var found = index.Search("asake", cancel: TestContext.Current.CancellationToken);
            var search = clock.Elapsed;
            Assert.NotEmpty(found);
            clock.Restart();
            _ = index.Search("lonely top", cancel: TestContext.Current.CancellationToken);
            _ = index.Search("show s02", cancel: TestContext.Current.CancellationToken);
            return (first, again, search, clock.Elapsed / 2);
        }, TestContext.Current.CancellationToken);

        Assert.True(first < TimeSpan.FromSeconds(5), $"Home's views of 10k entries took {first.TotalMilliseconds:0} ms");
        Assert.True(again < TimeSpan.FromMilliseconds(50), $"A return over the unchanged library took {again.TotalMilliseconds:0} ms");
        Assert.True(search < TimeSpan.FromMilliseconds(150), $"A search of 10k entries took {search.TotalMilliseconds:0} ms");
        Assert.True(searchAgain < TimeSpan.FromMilliseconds(150), $"A search of 10k entries took {searchAgain.TotalMilliseconds:0} ms");
    }
}
