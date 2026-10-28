using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>The streaming-style browse of Movies and TV shows: what the billboard features and which rows exist.</summary>
public sealed class StreamingBrowseTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Movie(string title, int addedDays = 0, int plays = 0, DateTime? lastPlayed = null, string? genre = null, TimeSpan? duration = null, int? year = null) =>
        new()
        {
            Path = $@"D:\Movies\{title}.mkv",
            Kind = LibraryKind.Video,
            Title = title,
            Added = Monday.AddDays(addedDays),
            Plays = plays,
            LastPlayed = lastPlayed,
            Genre = genre,
            Duration = duration,
            Year = year,
        };

    private static LibraryEntry Episode(string show, int season, int episode, int addedDays = 0, int plays = 0, DateTime? lastPlayed = null) =>
        new()
        {
            Path = $@"D:\TV\{show}\{show} S{season:00}E{episode:00}.mkv",
            Kind = LibraryKind.Video,
            Title = $"{show} S{season:00}E{episode:00}",
            Added = Monday.AddDays(addedDays),
            Plays = plays,
            LastPlayed = lastPlayed,
        };

    private static Func<string, TimeSpan?> Nothing => _ => null;

    private static Func<string, TimeSpan?> LeftAt(params string[] names) =>
        path => names.Any(name => path.Contains(name, StringComparison.OrdinalIgnoreCase)) ? TimeSpan.FromMinutes(10) : null;

    private static string[] Titles(BrowseShelf shelf) => [.. shelf.Entries.Select(entry => entry.Title)];

    [Fact]
    public void TheMovieLeftPartWayTakesTheBillboard()
    {
        var browse = StreamingBrowse.Movies(
            [Movie("Alpha", plays: 9), Movie("Beta", lastPlayed: Monday), Movie("Gamma", addedDays: 3)],
            LeftAt("Beta"));

        Assert.Equal("Beta", browse.Billboard?.Title);
        Assert.Equal(["Continue watching", "Most watched", "All movies"], browse.Shelves.Select(shelf => shelf.Title).Where(title => title != "Recently added"));
        Assert.Equal(["Beta"], Titles(browse.Shelves.Single(shelf => shelf.Title == "Continue watching")));
    }

    [Fact]
    public void TheMostWatchedMovieTakesTheBillboardWhenNothingIsPartWay()
    {
        // Equal play counts fall to the one played latest.
        var browse = StreamingBrowse.Movies(
            [Movie("Alpha", plays: 3, lastPlayed: Monday), Movie("Beta", plays: 3, lastPlayed: Monday.AddDays(1)), Movie("Gamma", addedDays: 5)],
            Nothing);

        Assert.Equal("Beta", browse.Billboard?.Title);
        Assert.Equal(["Beta", "Alpha"], Titles(browse.Shelves.Single(shelf => shelf.Title == "Most watched")));
        Assert.DoesNotContain(browse.Shelves, shelf => shelf.Title == "Continue watching");
    }

    [Fact]
    public void TheNewestMovieTakesTheBillboardInAFreshLibrary()
    {
        var browse = StreamingBrowse.Movies([Movie("Alpha"), Movie("Beta", addedDays: 2)], Nothing);

        Assert.Equal("Beta", browse.Billboard?.Title);
        Assert.Equal(["Beta", "Alpha"], Titles(browse.Shelves.Single(shelf => shelf.Title == "Recently added")));
        Assert.Equal(["Alpha", "Beta"], Titles(browse.Shelves.Single(shelf => shelf.Title == "All movies")));
    }

    [Fact]
    public void MovieFeaturesAreDeterministicUsefulAndNeverRepeated()
    {
        var browse = StreamingBrowse.Movies(
            [Movie("Newest", addedDays: 4), Movie("Popular", plays: 8), Movie("Resume", addedDays: 2), Movie("Another", addedDays: 1)],
            LeftAt("Resume"));

        Assert.Equal(["Resume", "Popular", "Newest", "Another"], browse.Features.Select(movie => movie.Title));
        Assert.Equal(browse.Features.Count, browse.Features.Select(movie => movie.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AnEmptyLibraryHasNoBillboardAndNoRows()
    {
        var browse = StreamingBrowse.Movies([], Nothing);

        Assert.Null(browse.Billboard);
        Assert.Empty(browse.Shelves);
    }

    [Fact]
    public void GenresBecomeRowsFullestFirstAndThinOnesDoNot()
    {
        var browse = StreamingBrowse.Movies(
        [
            Movie("Alpha", genre: "Drama", addedDays: 1),
            Movie("Beta", genre: "Drama;Thriller"),
            Movie("Gamma", genre: "Thriller"),
            Movie("Delta", genre: "Thriller"),
            Movie("Lone", genre: "Documentary"),
            Movie("Plain"),
        ], Nothing);

        var genreRows = browse.Shelves.Where(shelf => shelf.Title is not ("Recently added" or "All movies")).ToList();
        Assert.Equal(["Thriller", "Drama"], genreRows.Select(shelf => shelf.Title));
        // Newest first within a genre; a movie tagged with two genres sits on both rows.
        Assert.Equal(["Beta", "Delta", "Gamma"], Titles(genreRows[0]));
        Assert.Equal(["Alpha", "Beta"], Titles(genreRows[1]));
    }

    [Fact]
    public void RowsStopAtTheShelfSizeButAllMoviesHoldsEverything()
    {
        var movies = Enumerable.Range(0, StreamingBrowse.ShelfSize + 5).Select(i => Movie($"Movie {i:00}", addedDays: i)).ToArray();
        var browse = StreamingBrowse.Movies(movies, Nothing);

        Assert.Equal(StreamingBrowse.ShelfSize, browse.Shelves.Single(shelf => shelf.Title == "Recently added").Entries.Count);
        Assert.Equal(movies.Length, browse.Shelves.Single(shelf => shelf.Title == "All movies").Entries.Count);
    }

    [Fact]
    public void NoMoreGenreRowsThanTheViewOffers()
    {
        var movies = Enumerable.Range(0, (StreamingBrowse.MostGenreShelves + 2) * StreamingBrowse.SmallestGenreShelf)
            .Select(i => Movie($"Movie {i:00}", genre: $"Genre {i / StreamingBrowse.SmallestGenreShelf:00}")).ToArray();
        var browse = StreamingBrowse.Movies(movies, Nothing);

        Assert.Equal(StreamingBrowse.MostGenreShelves,
            browse.Shelves.Count(shelf => shelf.Title.StartsWith("Genre ", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheShowLeftPartWayTakesTheTvBillboard()
    {
        var browse = StreamingBrowse.TvShows(
        [
            Episode("Alpha", 1, 1), Episode("Alpha", 1, 2, lastPlayed: Monday),
            Episode("Beta", 1, 1, addedDays: 9, lastPlayed: Monday.AddDays(1)),
        ], LeftAt("Alpha S01E02", "Beta S01E01"));

        // Beta's episode was played latest, so Beta is featured and leads Continue watching.
        Assert.Equal("Beta", browse.Billboard?.Name);
        Assert.Equal(["Beta S01E01", "Alpha S01E02"], browse.ContinueWatching.Select(entry => entry.Title));
        Assert.Equal(["Recently added", "All shows"], browse.Shelves.Select(shelf => shelf.Title));
        Assert.Equal(["Beta", "Alpha"], browse.Shelves[0].Groups.Select(group => group.Name));
        Assert.Equal(["Alpha", "Beta"], browse.Shelves[1].Groups.Select(group => group.Name));
    }

    [Fact]
    public void TheNewestShowTakesTheTvBillboardWhenNothingIsPartWay()
    {
        var browse = StreamingBrowse.TvShows([Episode("Alpha", 1, 1, addedDays: 2), Episode("Beta", 1, 1)], Nothing);

        Assert.Equal("Alpha", browse.Billboard?.Name);
        Assert.Empty(browse.ContinueWatching);
    }

    [Fact]
    public void TvFeaturesStartWithTheResumableShowThenNewestShows()
    {
        var browse = StreamingBrowse.TvShows(
            [Episode("Newest", 1, 1, addedDays: 9), Episode("Resume", 1, 1), Episode("Middle", 1, 1, addedDays: 4)],
            LeftAt("Resume"));

        Assert.Equal(["Resume", "Newest", "Middle"], browse.Features.Select(show => show.Name));
    }

    [Fact]
    public void ALibraryWithoutShowsHasNoTvBillboardAndNoRows()
    {
        var browse = StreamingBrowse.TvShows([Movie("Alpha")], Nothing);

        Assert.Null(browse.Billboard);
        Assert.Empty(browse.ContinueWatching);
        Assert.Empty(browse.Shelves);
    }

    [Fact]
    public void NextUpResumesTheEpisodeLeftPartWay()
    {
        var show = LibraryViews.TvShows([Episode("Alpha", 1, 1, plays: 1), Episode("Alpha", 1, 2, lastPlayed: Monday)])[0];

        Assert.Equal("Alpha S01E02", StreamingBrowse.NextUp(show, LeftAt("S01E02"))?.Title);
    }

    [Fact]
    public void NextUpFallsToTheFirstUnseenEpisodeThenTheFirst()
    {
        var show = LibraryViews.TvShows([Episode("Alpha", 1, 1, plays: 1), Episode("Alpha", 1, 2)])[0];
        Assert.Equal("Alpha S01E02", StreamingBrowse.NextUp(show, Nothing)?.Title);

        var seen = LibraryViews.TvShows([Episode("Alpha", 1, 1, plays: 1), Episode("Alpha", 1, 2, plays: 2)])[0];
        Assert.Equal("Alpha S01E01", StreamingBrowse.NextUp(seen, Nothing)?.Title);

        Assert.Null(StreamingBrowse.NextUp(new LibraryGroup("Empty", null, []), Nothing));
    }

    [Fact]
    public void TheBillboardLineSaysYearLengthAndGenre()
    {
        Assert.Equal("2021 · 1 h 52 min · Drama",
            StreamingBrowse.MovieLine(Movie("Alpha", year: 2021, duration: TimeSpan.FromMinutes(112), genre: "Drama;Thriller")));
        Assert.Equal("45 min", StreamingBrowse.MovieLine(Movie("Beta", duration: TimeSpan.FromMinutes(45))));
        Assert.Equal("", StreamingBrowse.MovieLine(Movie("Gamma")));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "")]
    [InlineData(30, "1 min")]
    [InlineData(45 * 60, "45 min")]
    [InlineData((3600 * 2) + (5 * 60), "2 h 5 min")]
    [InlineData(3600 * 2, "2 h")]
    [InlineData((3600 * 5) + 20, "5 h")]
    public void LengthsReadTheWayPeopleSayThem(int? seconds, string expected)
    {
        Assert.Equal(expected, StreamingBrowse.Length(seconds is { } s ? TimeSpan.FromSeconds(s) : null));
    }
}
