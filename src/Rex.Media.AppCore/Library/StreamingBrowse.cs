using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>A horizontal row of the streaming-style browse: its heading and the videos it holds.</summary>
public sealed record BrowseShelf(string Title, IReadOnlyList<LibraryEntry> Entries);

/// <summary>A horizontal row of whole shows: its heading and the shows it holds.</summary>
public sealed record BrowseGroupShelf(string Title, IReadOnlyList<LibraryGroup> Groups);

/// <summary>
/// The streaming-style Movies view: one movie on the billboard and the rows under it.
/// Rows a library cannot fill are left out rather than shown empty.
/// </summary>
public sealed record MovieBrowse(IReadOnlyList<LibraryEntry> Features, IReadOnlyList<BrowseShelf> Shelves)
{
    public LibraryEntry? Billboard => Features.FirstOrDefault();
}

/// <summary>
/// The streaming-style TV view: one show on the billboard, the episodes left part-way, and the
/// rows of shows under them.
/// </summary>
public sealed record TvBrowse(IReadOnlyList<LibraryGroup> Features, IReadOnlyList<LibraryEntry> ContinueWatching, IReadOnlyList<BrowseGroupShelf> Shelves)
{
    public LibraryGroup? Billboard => Features.FirstOrDefault();
}

/// <summary>
/// What the streaming-style Movies and TV shows views put where: the billboard (what was left
/// part-way, else what is watched most, else what arrived last) and the rows by category.
/// Pure decisions over the library's entries; the window only draws them.
/// </summary>
public static class StreamingBrowse
{
    /// <summary>How many items one row holds at most; the "All" row alone is unabridged.</summary>
    public const int ShelfSize = 20;

    /// <summary>How many genre rows the Movies view offers at most.</summary>
    public const int MostGenreShelves = 6;

    /// <summary>A genre row exists only when this many movies share the genre.</summary>
    public const int SmallestGenreShelf = 2;

    /// <summary>The Movies view over its films (<see cref="LibraryViews.Movies"/>): billboard first, then its rows in a fixed, familiar order.</summary>
    public static MovieBrowse Movies(IReadOnlyList<LibraryEntry> movies, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(movies);
        ArgumentNullException.ThrowIfNull(leftAt);
        var continueWatching = LibraryViews.ContinueWatching(movies, leftAt).Take(ShelfSize).ToArray();
        var recentlyAdded = movies.OrderByDescending(movie => movie.Added)
            .ThenBy(movie => movie.Title, NaturalOrder.Instance).Take(ShelfSize).ToArray();
        var mostWatched = movies.Where(movie => movie.Plays > 0)
            .OrderByDescending(movie => movie.Plays)
            .ThenByDescending(movie => movie.LastPlayed ?? DateTime.MinValue)
            .ThenBy(movie => movie.Title, NaturalOrder.Instance).Take(ShelfSize).ToArray();
        var features = DistinctEntries(continueWatching.Concat(mostWatched).Concat(recentlyAdded)).Take(5).ToArray();

        var shelves = new List<BrowseShelf>();
        Add(shelves, "Continue watching", continueWatching);
        Add(shelves, "Recently added", recentlyAdded);
        Add(shelves, "Most watched", mostWatched);
        shelves.AddRange(GenreShelves(movies));
        Add(shelves, "All movies", movies);
        return new MovieBrowse(features, shelves);
    }

    /// <summary>The TV shows view: the show to go on with, the episodes left part-way, and the rows of shows.</summary>
    public static TvBrowse TvShows(IEnumerable<LibraryEntry> entries, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(leftAt);
        var shows = LibraryViews.TvShows(entries);
        var episodes = shows.SelectMany(show => show.Entries).ToList();
        var continueWatching = LibraryViews.ContinueWatching(episodes, leftAt).Take(ShelfSize).ToArray();
        var billboard = continueWatching.Length > 0
            ? shows.FirstOrDefault(show => show.Entries.Any(entry => LibraryViews.SameLocation(entry.Path, continueWatching[0].Path)))
            : shows.OrderByDescending(show => show.Entries.Max(entry => entry.Added)).FirstOrDefault();
        var features = (billboard is null ? [] : new[] { billboard })
            .Concat(shows.OrderByDescending(show => show.Entries.Max(entry => entry.Added)))
            .DistinctBy(show => show.Name, StringComparer.OrdinalIgnoreCase).Take(5).ToArray();

        var shelves = new List<BrowseGroupShelf>();
        var recentlyAdded = shows.OrderByDescending(show => show.Entries.Max(entry => entry.Added)).Take(ShelfSize).ToArray();
        if (recentlyAdded.Length > 0)
        {
            shelves.Add(new BrowseGroupShelf("Recently added", recentlyAdded));
            shelves.Add(new BrowseGroupShelf("All shows", shows));
        }

        return new TvBrowse(features, continueWatching, shelves);
    }

    /// <summary>The episode of a show to play next: the one left part-way last, else the first unseen one, else the first.</summary>
    public static LibraryEntry? NextUp(LibraryGroup show, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(leftAt);
        return LibraryViews.ContinueWatching(show.Entries, leftAt).FirstOrDefault()
            ?? show.Entries.FirstOrDefault(entry => entry.Plays == 0)
            ?? show.Entries.FirstOrDefault();
    }

    /// <summary>The billboard's line under a movie's title: its year, length and first genre.</summary>
    public static string MovieLine(LibraryEntry movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var parts = new[]
        {
            LibraryViews.MovieYearOf(movie)?.ToString(System.Globalization.CultureInfo.CurrentCulture),
            Length(movie.Duration),
            GenreNames.Split(movie.Genre).FirstOrDefault(),
        };
        return string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>How long one video lasts, as the billboard says it: "1 h 52 min", "45 min"; empty when unknown.</summary>
    public static string Length(TimeSpan? duration) => duration switch
    {
        null or { Ticks: <= 0 } => "",
        { TotalHours: >= 1 } time => $"{(int)time.TotalHours} h {time.Minutes} min",
        { } time => $"{Math.Max(1, time.Minutes)} min",
    };

    private static void Add(List<BrowseShelf> shelves, string title, IReadOnlyList<LibraryEntry> entries)
    {
        if (entries.Count > 0)
        {
            shelves.Add(new BrowseShelf(title, entries));
        }
    }

    private static IEnumerable<LibraryEntry> DistinctEntries(IEnumerable<LibraryEntry> entries) =>
        entries.DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase);

    /// <summary>The genre rows, fullest first, each newest first; a genre too thin to be a row is left out.</summary>
    private static IEnumerable<BrowseShelf> GenreShelves(IReadOnlyList<LibraryEntry> movies) =>
        movies.SelectMany(movie => GenreNames.Split(movie.Genre).Select(genre => (Genre: genre, Movie: movie)))
            .GroupBy(pair => pair.Genre, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() >= SmallestGenreShelf)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(MostGenreShelves)
            .Select(group => new BrowseShelf(group.Key,
                [.. group.Select(pair => pair.Movie).Distinct()
                    .OrderByDescending(movie => movie.Added).ThenBy(movie => movie.Title, NaturalOrder.Instance).Take(ShelfSize)]));
}
