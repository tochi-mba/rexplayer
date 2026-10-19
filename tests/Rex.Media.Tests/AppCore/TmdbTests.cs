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

    private const string Details = """
        {"id":603,"tagline":"Free your mind.","runtime":136,"status":"Released","original_language":"en",
         "genres":[{"id":28,"name":"Action"},{"id":878,"name":"Science Fiction"},{"id":1},9],
         "production_companies":[{"name":""},{"name":"Village Roadshow Pictures"}],
         "belongs_to_collection":{"name":"The Matrix Collection"},
         "backdrop_path":"/wide.jpg",
         "credits":{
           "cast":[{"name":"Keanu Reeves","character":"Neo","profile_path":"/kr.jpg"},{"name":"Carrie-Anne Moss","character":"","profile_path":null},{"character":"Nobody"}],
           "crew":[{"name":"Lana Wachowski","job":"Director"},{"name":"Lana Wachowski","job":"Director"},{"name":"Lilly Wachowski","job":"Director"},{"name":"Lana Wachowski","department":"Writing","job":"Writer"},{"name":"Someone","department":"Camera","job":"Operator"},{"job":"Director"}]},
         "videos":{"results":[{"site":"YouTube","type":"Trailer","key":"m8e-FF8MsqU"},{"site":"YouTube","type":"Clip","key":"x"},{"site":"Vimeo","type":"Trailer","key":"y"},{"site":"YouTube","type":"Trailer","key":"second"}]},
         "recommendations":{"results":[{"title":"Dark City","release_date":"1998-02-27"},{"title":"No Date"},{"name":"tv thing"}]},
         "release_dates":{"results":[{"iso_3166_1":"GB","release_dates":[{"certification":"15"}]},{"iso_3166_1":"US","release_dates":[{"certification":""},{"certification":"R"}]}]}}
        """;

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
    public void ADetailsAnswerCarriesTheFullerFacts()
    {
        var details = Tmdb.ParseDetails(Details);

        Assert.NotNull(details);
        Assert.Equal(("Free your mind.", 136, "Released", "Village Roadshow Pictures", "en", "R"),
            (details!.Tagline, details.RuntimeMinutes, details.Status, details.Studio, details.OriginalLanguage, details.ContentRating));
        Assert.Equal(["Action", "Science Fiction"], details.Genres);
        Assert.Equal("https://image.tmdb.org/t/p/w1280/wide.jpg", details.BackdropUrl);
        Assert.Equal([new CastMember("Keanu Reeves", "Neo", "https://image.tmdb.org/t/p/w185/kr.jpg"), new CastMember("Carrie-Anne Moss", "", null)], details.Cast);
        Assert.Equal(["Lana Wachowski", "Lilly Wachowski"], details.Directors);
        Assert.Equal(["Lana Wachowski"], details.Writers);
        Assert.Equal(["m8e-FF8MsqU", "second"], details.TrailerKeys);
        Assert.Equal([new TitleRef("Dark City", 1998), new TitleRef("No Date", null)], details.Recommendations);
        Assert.Equal("The Matrix Collection", details.Collection);
        Assert.Null(Tmdb.ParseDetails("{}"));
        Assert.Null(Tmdb.ParseDetails("[]"));
        Assert.Throws<ArgumentNullException>(() => Tmdb.ParseDetails(null!));

        var bare = Tmdb.ParseDetails("""{"id":1,"tagline":"","runtime":0}""");
        Assert.NotNull(bare);
        Assert.Equal((null, null, null, null, null, null, null), (bare!.Tagline, bare.RuntimeMinutes, bare.Status, bare.Studio, bare.ContentRating, bare.BackdropUrl, bare.Collection));
        Assert.Empty(bare.Genres);
        Assert.Empty(bare.Cast);
        Assert.Empty(bare.TrailerKeys);
        Assert.Empty(bare.Recommendations);
    }

    [Fact]
    public void TheUrisCarryTheUsersKeyAndEscapeTheTitle()
    {
        Assert.Equal("https://api.themoviedb.org/3/search/movie?api_key=k%201&year=1999&query=The%20Matrix", Tmdb.SearchUri("k 1", "The Matrix", 1999).AbsoluteUri);
        Assert.Equal("https://api.themoviedb.org/3/search/movie?api_key=k&query=The%20Matrix", Tmdb.SearchUri("k", "The Matrix", null).AbsoluteUri);
        Assert.Equal("https://api.themoviedb.org/3/movie/603?api_key=k&append_to_response=credits%2Cvideos%2Crecommendations%2Crelease_dates", Tmdb.DetailsUri("k", 603).AbsoluteUri);
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", Tmdb.PosterUri("/abc.jpg").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => Tmdb.SearchUri(" ", "t", null));
        Assert.Throws<ArgumentNullException>(() => Tmdb.SearchUri("k", null!, null));
        Assert.Throws<ArgumentException>(() => Tmdb.DetailsUri("", 1));
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
    public async Task AMatchedMovieLandsWholeInTheCacheWithItsFullFacts()
    {
        _answers[Tmdb.SearchUri("k", "The Matrix", 1999).AbsoluteUri] = Search;
        _answers[Tmdb.DetailsUri("k", 603).AbsoluteUri] = Details;
        _answers["https://image.tmdb.org/t/p/w500/abc.jpg"] = "poster-bytes";
        var fetcher = Fetcher;

        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        var movie = fetcher.CachedMovie("the matrix", 1999);
        Assert.NotNull(movie);
        Assert.Equal((603, "The Matrix", 1999, 8.2, true, true), (movie!.CatalogueId, movie.Title, movie.Year, movie.Rating, movie.HasPoster, movie.Found));
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
        Assert.Equal(("Free your mind.", 136, "Released", "Village Roadshow Pictures", "R", "The Matrix Collection"),
            (movie.Tagline, movie.RuntimeMinutes, movie.Status, movie.Studio, movie.ContentRating, movie.Collection));
        Assert.Equal(["m8e-FF8MsqU", "second"], movie.TrailerKeys);
        Assert.Equal(2, movie.Cast.Count);
        Assert.Equal("Action · Science Fiction · R · 136 min · 8.2 / 10", movie.Blurb());
        Assert.Equal("poster-bytes"u8.ToArray(), File.ReadAllBytes(fetcher.MoviePosterPath("The Matrix", 1999)));
        Assert.Equal(movie.ToJson(), Assert.Single(fetcher.CachedMovies()).ToJson());

        // Already cached: nothing more is asked; the genre map was kept too.
        _asked.Clear();
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        Assert.Empty(_asked);

        // A second movie whose details cannot be fetched is tried again later, never half-written.
        _answers[Tmdb.SearchUri("k", "The Matrix Reloaded", null).AbsoluteUri] = Search;
        Assert.False(await fetcher.FetchMovieAsync("k", "The Matrix Reloaded", null, CancellationToken.None));
        Assert.Null(fetcher.CachedMovie("The Matrix Reloaded", null));
        _answers[Tmdb.DetailsUri("k", 604).AbsoluteUri] = """{"id":604}""";
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix Reloaded", null, CancellationToken.None));
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

        // A match whose details answer is not the usual shape is tried again later.
        _answers[Tmdb.SearchUri("k", "The Matrix", 1999).AbsoluteUri] = Search;
        _answers[Tmdb.DetailsUri("k", 603).AbsoluteUri] = "[]";
        Assert.False(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        _answers[Tmdb.DetailsUri("k", 603).AbsoluteUri] = Details;
        _answers["https://image.tmdb.org/t/p/w500/abc.jpg"] = "poster-bytes";
        Assert.True(await fetcher.FetchMovieAsync("k", "The Matrix", 1999, CancellationToken.None));
        var movie = fetcher.CachedMovie("The Matrix", 1999);
        Assert.True(movie is { CatalogueId: 603, HasPoster: true });

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
