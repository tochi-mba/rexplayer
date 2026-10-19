using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Reading the movie catalogue's answers with the user's own key: searches, genre names and careful
/// picking (exact titles, the file's year within one), and one movie fetched whole into the cache.
/// </summary>
public sealed class TmdbTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-tmdb-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly List<string> _asked = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MetadataFetcher Fetcher => new(new MetadataCache(_folder), (uri, _) =>
    {
        _asked.Add(uri.AbsoluteUri);
        return Task.FromResult(_answers.TryGetValue(uri.AbsoluteUri, out var answer) ? Encoding.UTF8.GetBytes(answer) : null);
    });

    private const string Search = """
        {"results":[
          {"id":603,"title":"The Matrix","release_date":"1999-03-30","overview":"A hacker learns the truth.","genre_ids":[28,878],"vote_average":8.2,"poster_path":"/abc.jpg"},
          {"id":604,"title":"The Matrix Reloaded","release_date":"2003-05-15","overview":"","genre_ids":[],"vote_average":0,"poster_path":null},
          {"title":"No id, skipped"},
          {"id":7},
          "odd"
        ]}
        """;

    private const string Genres = """{"genres":[{"id":28,"name":"Action"},{"id":878,"name":"Science Fiction"},{"id":1},{"name":"Bare"},9]}""";

    [Fact]
    public void ASearchAnswerBecomesCandidates()
    {
        var candidates = Tmdb.ParseSearch(Search);

        Assert.Equal(2, candidates.Count);
        var best = candidates[0];
        Assert.Equal((603, "The Matrix", 1999, 8.2, "/abc.jpg"), (best.Id, best.Title, best.Year, best.Rating, best.PosterPath));
        Assert.Equal("A hacker learns the truth.", best.Overview);
        Assert.Equal([28, 878], best.GenreIds);
        var bare = candidates[1];
        Assert.Equal((2003, null, null, null), (bare.Year, bare.Overview, bare.Rating, bare.PosterPath));
        Assert.Empty(Tmdb.ParseSearch("{}"));
        Assert.Empty(Tmdb.ParseSearch("[]"));
        Assert.Throws<ArgumentNullException>(() => Tmdb.ParseSearch(null!));
    }

    [Fact]
    public void GenreNamesAreReadAndOddOnesSkipped()
    {
        var names = Tmdb.ParseGenres(Genres);
        Assert.Equal(new Dictionary<int, string> { [28] = "Action", [878] = "Science Fiction" }, names);
        Assert.Empty(Tmdb.ParseGenres("{}"));
        Assert.Empty(Tmdb.ParseGenres("[]"));
        Assert.Throws<ArgumentNullException>(() => Tmdb.ParseGenres(null!));
    }

    [Fact]
    public void TheUrisCarryTheUsersKeyAndEscapeTheTitle()
    {
        Assert.Equal("https://api.themoviedb.org/3/search/movie?api_key=k%201&year=1999&query=The%20Matrix", Tmdb.SearchUri("k 1", "The Matrix", 1999).AbsoluteUri);
        Assert.Equal("https://api.themoviedb.org/3/search/movie?api_key=k&query=The%20Matrix", Tmdb.SearchUri("k", "The Matrix", null).AbsoluteUri);
        Assert.Equal("https://api.themoviedb.org/3/genre/movie/list?api_key=k", Tmdb.GenresUri("k").AbsoluteUri);
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", Tmdb.PosterUri("/abc.jpg").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => Tmdb.SearchUri(" ", "t", null));
        Assert.Throws<ArgumentNullException>(() => Tmdb.SearchUri("k", null!, null));
        Assert.Throws<ArgumentException>(() => Tmdb.GenresUri(""));
        Assert.Throws<ArgumentNullException>(() => Tmdb.PosterUri(null!));
    }

    [Fact]
    public void PickingTakesTheExactTitleWithinAYearAndLeavesDoubtAlone()
    {
        var candidates = Tmdb.ParseSearch(Search);

        Assert.Equal(603, Tmdb.Pick("The Matrix", 1999, candidates)?.Id);
        Assert.Equal(603, Tmdb.Pick("the matrix!", null, candidates)?.Id);
        // A premiere and a disc release can straddle a year boundary.
        Assert.Equal(603, Tmdb.Pick("The Matrix", 2000, candidates)?.Id);
        Assert.Null(Tmdb.Pick("The Matrix", 2010, candidates));
        Assert.Null(Tmdb.Pick("Another Movie", null, candidates));
        Assert.Null(Tmdb.Pick("---", null, candidates));
        // With a year, a dated candidate beats an undated one.
        var undated = new MovieCandidate(1, "The Matrix", null, null, null, null, []);
        var dated = new MovieCandidate(2, "The Matrix", 1999, null, null, null, []);
        Assert.Equal(2, Tmdb.Pick("The Matrix", 1999, [undated, dated])?.Id);
        Assert.Equal(1, Tmdb.Pick("The Matrix", null, [undated, dated])?.Id);
        Assert.Throws<ArgumentNullException>(() => Tmdb.Pick(null!, null, []));
        Assert.Throws<ArgumentNullException>(() => Tmdb.Pick("t", null, null!));
    }

    [Fact]
    public void OneMovieKeyWhereverItIsNamed()
    {
        Assert.Equal(Tmdb.MovieKey("The Matrix!", 1999), Tmdb.MovieKey("the matrix", 1999));
        Assert.NotEqual(Tmdb.MovieKey("The Matrix", 1999), Tmdb.MovieKey("The Matrix", null));
        Assert.Throws<ArgumentNullException>(() => Tmdb.MovieKey(null!, null));
    }

    [Fact]
    public async Task AMatchedMovieLandsWholeInTheCacheWithItsGenresFetchedOnce()
    {
        _answers[Tmdb.SearchUri("k", "The Matrix", 1999).AbsoluteUri] = Search;
        _answers[Tmdb.GenresUri("k").AbsoluteUri] = Genres;
        _answers["https://image.tmdb.org/t/p/w500/abc.jpg"] = "poster-bytes";
        var fetcher = Fetcher;

        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        var movie = fetcher.CachedMovie("the matrix", 1999);
        Assert.NotNull(movie);
        Assert.Equal((603, "The Matrix", 1999, 8.2, true, true), (movie!.CatalogueId, movie.Title, movie.Year, movie.Rating, movie.HasPoster, movie.Found));
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
        Assert.Equal("Action · Science Fiction · 8.2 / 10", movie.Blurb());
        Assert.Equal("poster-bytes"u8.ToArray(), File.ReadAllBytes(fetcher.MoviePosterPath("The Matrix", 1999)));
        Assert.Equal(movie.ToJson(), Assert.Single(fetcher.CachedMovies()).ToJson());

        // Already cached: nothing more is asked; the genre map was kept too.
        _asked.Clear();
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        Assert.Empty(_asked);

        // A second movie reuses the kept genre map instead of asking again.
        _answers[Tmdb.SearchUri("k", "The Matrix Reloaded", null).AbsoluteUri] = Search;
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix Reloaded", null, CancellationToken.None));
        Assert.DoesNotContain(_asked, uri => uri.Contains("genre", StringComparison.Ordinal));
        var second = fetcher.CachedMovie("The Matrix Reloaded", null);
        Assert.True(second is { CatalogueId: 604, HasPoster: false });
        Assert.Empty(second!.Genres);
        Assert.Equal("", second.Blurb());

        fetcher.ForgetMovie("The Matrix", 1999);
        Assert.Null(fetcher.CachedMovie("The Matrix", 1999));
    }

    [Fact]
    public async Task TheCatalogueFailingLeavesNothingAndNoMatchIsRecorded()
    {
        var fetcher = Fetcher;
        Assert.False(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        Assert.Null(fetcher.CachedMovie("The Matrix", 1999));

        _answers[Tmdb.SearchUri("k", "The Matrix", 1999).AbsoluteUri] = "broken";
        Assert.False(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));

        // A clean no-match is recorded and not asked again.
        _answers[Tmdb.SearchUri("k", "Obscure", null).AbsoluteUri] = """{"results":[]}""";
        Assert.True(await fetcher.FetchMovieAsync("k", "Obscure", null, CancellationToken.None));
        Assert.True(fetcher.CachedMovie("Obscure", null) is { Found: false });
        _asked.Clear();
        Assert.True(await fetcher.FetchMovieAsync("k", "Obscure", null, CancellationToken.None));
        Assert.Empty(_asked);

        // A match whose genre list never answers is still kept, just without genre names.
        _answers[Tmdb.SearchUri("k", "The Matrix", 1999).AbsoluteUri] = Search;
        _answers["https://image.tmdb.org/t/p/w500/abc.jpg"] = "poster-bytes";
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        var movie = fetcher.CachedMovie("The Matrix", 1999);
        Assert.True(movie is { CatalogueId: 603, HasPoster: true });
        Assert.Empty(movie!.Genres);

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchMovieAsync("", "t", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fetcher.FetchMovieAsync("k", null!, null, CancellationToken.None));
    }

    [Fact]
    public void TheMovieRecordSurvivesItsJsonAndRefusesWhatIsNotOne()
    {
        var movie = new EnrichedMovie { CatalogueId = 603, Title = "The Matrix", Year = 1999, Genres = ["Action"] };
        var reloaded = EnrichedMovie.FromJson(movie.ToJson());
        Assert.Equal(movie.ToJson(), reloaded!.ToJson());
        Assert.Equal(movie.Genres, reloaded.Genres);
        Assert.Null(EnrichedMovie.FromJson("broken"));
        Assert.Throws<ArgumentNullException>(() => EnrichedMovie.FromJson(null!));
    }
}
