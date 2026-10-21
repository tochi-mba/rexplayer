using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Movies, and shows TVmaze does not know, fetched whole into the cache from Wikidata and
/// Wikipedia through the fetch delegate, which here answers from recorded replies and never
/// touches the network: matches, no-matches, services that cannot answer, and records of an
/// older shape replaced.
/// </summary>
public sealed class WikidataFetchTests : IDisposable
{
    private static readonly byte[] PosterBytes = [1, 2, 3];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-wikidata-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> _answers = new(StringComparer.Ordinal);
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
        return Task.FromResult(_answers.TryGetValue(uri.AbsoluteUri, out var answer) ? answer : null);
    });

    private void Answer(Uri uri, string text) => _answers[uri.AbsoluteUri] = Encoding.UTF8.GetBytes(text);

    private static WikidataEntity MatrixItem => Wikidata.ParseEntities(WikimediaFixtures.MatrixEntity)[0];

    private static Uri MatrixLabelsUri => Wikidata.LabelsUri(WikidataFacts.Referenced(MatrixItem));

    private static string MatrixPosterUrl => Wikipedia.ParseSummary(WikimediaFixtures.MatrixSummary)!.Original!.Url;

    /// <summary>Every answer one whole movie fetch asks for.</summary>
    private void AnswerTheMatrix()
    {
        Answer(Wikidata.SearchUri("The Matrix"), WikimediaFixtures.MatrixSearch);
        Answer(Wikidata.EntitiesUri(["Q83495"]), WikimediaFixtures.MatrixEntity);
        Answer(MatrixLabelsUri, WikimediaFixtures.MatrixLabels);
        Answer(Wikipedia.SummaryUri("The Matrix"), WikimediaFixtures.MatrixSummary);
        _answers[new Uri(MatrixPosterUrl).AbsoluteUri] = PosterBytes;
    }

    [Fact]
    public void ANewCacheHoldsNoMovies() =>
        Assert.Empty(Fetcher.CachedMovies());

    [Fact]
    public async Task AMatchedMovieLandsWholeInTheCacheAndIsNotAskedForAgain()
    {
        AnswerTheMatrix();

        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));

        var movie = Fetcher.CachedMovie("The Matrix", 1999);
        Assert.NotNull(movie);
        Assert.True(movie.Found);
        Assert.True(movie.Current);
        Assert.True(movie.HasPoster);
        Assert.Equal(83495, movie.CatalogueId);
        Assert.Equal("Thriller · Dystopian · Cyberpunk · R · 136 min", movie.Blurb());
        Assert.Equal("https://en.wikipedia.org/wiki/The_Matrix", movie.ArticleUrl);
        Assert.Equal(PosterBytes, File.ReadAllBytes(Fetcher.MoviePosterPath("The Matrix", 1999)));
        Assert.Equal("The Matrix", Assert.Single(Fetcher.CachedMovies()).Title);
        // Search, the item, the names, the article and the poster: five requests, and none again.
        Assert.Equal(5, _asked.Count);
        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));
        Assert.Equal(5, _asked.Count);
    }

    [Fact]
    public async Task AMovieWithNoArticleOrPosterStillLandsWithItsFacts()
    {
        AnswerTheMatrix();
        _answers.Remove(Wikipedia.SummaryUri("The Matrix").AbsoluteUri);

        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", null, CancellationToken.None));

        var movie = Fetcher.CachedMovie("The Matrix", null)!;
        Assert.Null(movie.Overview);
        Assert.Null(movie.ArticleUrl);
        Assert.False(movie.HasPoster);
        Assert.Equal(["Lana Wachowski", "Lilly Wachowski"], movie.Directors);
        Assert.False(File.Exists(Fetcher.MoviePosterPath("The Matrix", null)));
    }

    [Fact]
    public async Task AnArticleOfAnotherShapeOrAMissingPosterOnlyCostsTheCardItsPicture()
    {
        AnswerTheMatrix();
        Answer(Wikipedia.SummaryUri("The Matrix"), "{ not json");

        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", null, CancellationToken.None));
        Assert.Null(Fetcher.CachedMovie("The Matrix", null)!.Overview);

        Fetcher.ForgetMovie("The Matrix", null);
        AnswerTheMatrix();
        _answers.Remove(new Uri(MatrixPosterUrl).AbsoluteUri);
        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", null, CancellationToken.None));
        Assert.False(Fetcher.CachedMovie("The Matrix", null)!.HasPoster);
        Assert.NotNull(Fetcher.CachedMovie("The Matrix", null)!.Overview);
    }

    [Fact]
    public async Task NothingMatchingIsRecordedOnceAndNotAskedAgain()
    {
        Answer(Wikidata.SearchUri("Not A Film At All"), WikimediaFixtures.MatrixSearch);

        Assert.True(await Fetcher.FetchMovieAsync("Not A Film At All", 2001, CancellationToken.None));

        var none = Fetcher.CachedMovie("Not A Film At All", 2001);
        Assert.NotNull(none);
        Assert.False(none.Found);
        Assert.True(none.Current);
        Assert.Single(_asked);
        Assert.True(await Fetcher.FetchMovieAsync("Not A Film At All", 2001, CancellationToken.None));
        Assert.Single(_asked);
    }

    [Fact]
    public async Task CandidatesWhoseDatesDisagreeAreNoMatch()
    {
        Answer(Wikidata.SearchUri("The Matrix"), WikimediaFixtures.MatrixSearch);
        Answer(Wikidata.EntitiesUri(["Q83495"]), WikimediaFixtures.MatrixEntity);

        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", 2021, CancellationToken.None));

        Assert.False(Fetcher.CachedMovie("The Matrix", 2021)!.Found);
    }

    [Fact]
    public async Task AServiceThatCannotAnswerLeavesNothingToBeTriedAgain()
    {
        // The search fails.
        Assert.False(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));
        Assert.Null(Fetcher.CachedMovie("The Matrix", 1999));

        // The search answers, the items do not.
        Answer(Wikidata.SearchUri("The Matrix"), WikimediaFixtures.MatrixSearch);
        Assert.False(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));

        // The items answer, the label query does not.
        Answer(Wikidata.EntitiesUri(["Q83495"]), WikimediaFixtures.MatrixEntity);
        Assert.False(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));
        Assert.Null(Fetcher.CachedMovie("The Matrix", 1999));

        // A search answer of another shape is tried again later too.
        Answer(Wikidata.SearchUri("Garbled"), "<html>");
        Assert.False(await Fetcher.FetchMovieAsync("Garbled", null, CancellationToken.None));
        Assert.Null(Fetcher.CachedMovie("Garbled", null));
    }

    [Fact]
    public async Task ManyNamesAreAskedForInBatchesTheQueryServiceAccepts()
    {
        var directors = string.Join(",", Enumerable.Range(1, 60).Select(n =>
            "{\"mainsnak\":{\"snaktype\":\"value\",\"datavalue\":{\"value\":{\"id\":\"Q" + (1000 + n) + "\"}}},\"rank\":\"normal\"}"));
        var item = "{\"entities\":{\"Q5\":{\"id\":\"Q5\",\"labels\":{\"en\":{\"value\":\"Crowd\"}},\"claims\":{\"P57\":[" + directors + "]}}}}";
        Answer(Wikidata.SearchUri("Crowd"), """{"search":[{"id":"Q5","label":"Crowd","description":"2020 film"}]}""");
        Answer(Wikidata.EntitiesUri(["Q5"]), item);
        var ids = WikidataFacts.Referenced(Wikidata.ParseEntities(item)[0]);
        Assert.Equal(60, ids.Count);
        Answer(Wikidata.LabelsUri([.. ids.Take(Wikidata.BatchLimit)]),
            """{"results":{"bindings":[{"item":{"value":"http://www.wikidata.org/entity/Q1001"},"itemLabel":{"value":"First"}}]}}""");
        Answer(Wikidata.LabelsUri([.. ids.Skip(Wikidata.BatchLimit)]),
            """{"results":{"bindings":[{"item":{"value":"http://www.wikidata.org/entity/Q1060"},"itemLabel":{"value":"Last"}}]}}""");

        Assert.True(await Fetcher.FetchMovieAsync("Crowd", null, CancellationToken.None));

        var movie = Fetcher.CachedMovie("Crowd", null)!;
        Assert.Equal(["First", "Last"], movie.Directors);
        Assert.Null(movie.Overview);
        Assert.Equal(4, _asked.Count);
    }

    [Fact]
    public async Task RecordsFromTheEarlierCatalogueAreFetchedAgainAndReplaced()
    {
        var cache = new MetadataCache(_folder);
        // A match and a no-match as the catalogue earlier versions used wrote them.
        cache.WriteText(MetadataFetcher.MovieKind, MetadataMatch.MovieKey("The Matrix", 1999), """{"catalogueId":603,"title":"The Matrix","year":1999,"rating":8.2,"schema":2}""");
        cache.WriteText(MetadataFetcher.MovieKind, MetadataMatch.MovieKey("Heat", null), """{"catalogueId":0,"title":"Heat"}""");
        Assert.False(Fetcher.CachedMovie("The Matrix", 1999)!.Current);
        Assert.False(Fetcher.CachedMovie("Heat", null)!.Current);
        AnswerTheMatrix();
        Answer(Wikidata.SearchUri("Heat"), """{"search":[]}""");

        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));
        Assert.True(await Fetcher.FetchMovieAsync("Heat", null, CancellationToken.None));

        Assert.Equal(83495, Fetcher.CachedMovie("The Matrix", 1999)!.CatalogueId);
        Assert.True(Fetcher.CachedMovie("Heat", null)!.Current);
    }

    [Fact]
    public async Task ForgettingAMovieRemovesItsRecordAndPoster()
    {
        AnswerTheMatrix();
        Assert.True(await Fetcher.FetchMovieAsync("The Matrix", 1999, CancellationToken.None));

        Fetcher.ForgetMovie("The Matrix", 1999);

        Assert.Null(Fetcher.CachedMovie("The Matrix", 1999));
        Assert.False(File.Exists(Fetcher.MoviePosterPath("The Matrix", 1999)));
    }

    [Fact]
    public async Task TheMovieFetchNeedsATitle() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Fetcher.FetchMovieAsync(null!, null, CancellationToken.None));

    [Fact]
    public void TheMovieRecordSurvivesItsJsonAndRefusesWhatIsNotOne()
    {
        var movie = WikidataFacts.Movie(MatrixItem, Wikidata.ParseLabels(WikimediaFixtures.MatrixLabels), Wikipedia.ParseSummary(WikimediaFixtures.MatrixSummary), null);

        var back = EnrichedMovie.FromJson(movie.ToJson())!;

        Assert.Equal(movie.ToJson(), back.ToJson());
        Assert.Equal(movie.Cast, back.Cast);
        Assert.Null(EnrichedMovie.FromJson("null"));
        Assert.Null(EnrichedMovie.FromJson("{ broken"));
        var nullLists = EnrichedMovie.FromJson("""{"catalogueId":1,"title":"T","genres":null,"cast":null,"directors":null,"writers":null,"trailerKeys":null}""");
        Assert.Empty(nullLists!.Genres);
        Assert.Empty(nullLists.Cast);
        Assert.Empty(nullLists.Directors);
        Assert.Empty(nullLists.Writers);
        Assert.Empty(nullLists.TrailerKeys);
        Assert.Equal("", nullLists.Blurb());
        Assert.Throws<ArgumentNullException>(() => EnrichedMovie.FromJson(null!));
    }

    [Fact]
    public async Task AShowTvmazeDoesNotKnowComesFromWikidataAndWikipedia()
    {
        Answer(TvMaze.SearchUri("Fawlty Towers"), "[]");
        Answer(Wikidata.SearchUri("Fawlty Towers"), WikimediaFixtures.FawltySearch);
        Answer(Wikidata.EntitiesUri(["Q212731"]), WikimediaFixtures.FawltyEntity);
        Answer(Wikidata.LabelsUri(WikidataFacts.Referenced(Wikidata.ParseEntities(WikimediaFixtures.FawltyEntity)[0])), WikimediaFixtures.FawltyLabels);
        Answer(Wikipedia.SummaryUri("Fawlty Towers"), WikimediaFixtures.FawltySummary);

        Assert.True(await Fetcher.FetchShowAsync("Fawlty Towers", null, null, CancellationToken.None));

        var show = Fetcher.Cached("Fawlty Towers")!;
        Assert.True(show.Found);
        Assert.True(show.Current);
        Assert.Equal("Q212731", show.WikidataId);
        Assert.Equal(212731, show.CatalogueId);
        Assert.Equal("https://en.wikipedia.org/wiki/Fawlty_Towers", show.ArticleUrl);
        Assert.StartsWith("Fawlty Towers is a British television sitcom", show.Summary, StringComparison.Ordinal);
        Assert.Equal(["Sitcom"], show.Genres);
        Assert.Equal("BBC Two", show.Network);
        Assert.Equal("1975-09-19", show.Premiered);
        Assert.Equal(["John Cleese", "Connie Booth"], show.Creators);
        Assert.Equal("John Cleese", show.Cast[0].Name);
        Assert.Empty(show.Episodes);
        // The article's picture is a wide title card, so no poster was fetched.
        Assert.False(show.HasPoster);
    }

    [Fact]
    public async Task AFallbackShowGetsThePosterItsArticleOffers()
    {
        const string Summary = """
            {"type":"standard","title":"Tall Show","extract":"A show.","content_urls":{"desktop":{"page":"https://en.wikipedia.org/wiki/Tall_Show"}},
             "originalimage":{"source":"https://upload.wikimedia.org/tall.jpg","width":300,"height":450}}
            """;
        Answer(TvMaze.SearchUri("Tall Show"), "[]");
        Answer(Wikidata.SearchUri("Tall Show"), """{"search":[{"id":"Q77","label":"Tall Show","description":"American television series"}]}""");
        Answer(Wikidata.EntitiesUri(["Q77"]), """{"entities":{"Q77":{"id":"Q77","labels":{"en":{"value":"Tall Show"}},"claims":{},"sitelinks":{"enwiki":{"title":"Tall Show"}}}}}""");
        Answer(Wikipedia.SummaryUri("Tall Show"), Summary);
        _answers["https://upload.wikimedia.org/tall.jpg"] = PosterBytes;

        Assert.True(await Fetcher.FetchShowAsync("Tall Show", null, null, CancellationToken.None));

        Assert.True(Fetcher.Cached("Tall Show")!.HasPoster);
        Assert.Null(Fetcher.Cached("Tall Show")!.Genres.FirstOrDefault());
    }

    [Fact]
    public async Task AShowNeitherCatalogueKnowsIsRecordedAsNoMatch()
    {
        Answer(TvMaze.SearchUri("Nowhere Show"), "[]");
        Answer(Wikidata.SearchUri("Nowhere Show"), """{"search":[]}""");

        Assert.True(await Fetcher.FetchShowAsync("Nowhere Show", null, null, CancellationToken.None));

        Assert.False(Fetcher.Cached("Nowhere Show")!.Found);
    }

    [Fact]
    public async Task AFallbackThatCannotAnswerIsTriedAgainLater()
    {
        Answer(TvMaze.SearchUri("Fawlty Towers"), "[]");
        Assert.False(await Fetcher.FetchShowAsync("Fawlty Towers", null, null, CancellationToken.None));

        Answer(Wikidata.SearchUri("Fawlty Towers"), WikimediaFixtures.FawltySearch);
        Answer(Wikidata.EntitiesUri(["Q212731"]), WikimediaFixtures.FawltyEntity);
        Assert.False(await Fetcher.FetchShowAsync("Fawlty Towers", null, null, CancellationToken.None));
        Assert.Null(Fetcher.Cached("Fawlty Towers"));
    }
}
