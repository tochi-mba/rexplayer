using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Reading Wikidata items as films and shows: which hits can be the work, which item to take by
/// its date, and how each property becomes a card's facts, from recorded answers and small items.
/// </summary>
public sealed class WikidataFactsTests
{
    private static readonly IReadOnlyDictionary<string, WikidataLabel> NoLabels = new Dictionary<string, WikidataLabel>();

    private static WikidataEntity Matrix => Wikidata.ParseEntities(WikimediaFixtures.MatrixEntity)[0];

    private static IReadOnlyDictionary<string, WikidataLabel> MatrixLabels => Wikidata.ParseLabels(WikimediaFixtures.MatrixLabels);

    private static WikidataEntity Item(string id, params (string Property, WikidataClaim Claim)[] claims) =>
        new(id, "Label " + id, null, claims.GroupBy(claim => claim.Property).ToDictionary(group => group.Key, group => (IReadOnlyList<WikidataClaim>)[.. group.Select(claim => claim.Claim)]), null);

    private static WikidataClaim Claim(WikidataValue value, params (string Property, WikidataValue Value)[] qualifiers) =>
        new(value, qualifiers.ToDictionary(qualifier => qualifier.Property, qualifier => (IReadOnlyList<WikidataValue>)[qualifier.Value]));

    private static (string, WikidataClaim) Dated(string time) => ("P577", Claim(new WikidataValue(Time: time)));

    [Fact]
    public void OnlyTheFilmOfTheSameTitleIsACandidate()
    {
        var hits = Wikidata.ParseSearch(WikimediaFixtures.MatrixSearch);

        // The album and the franchise share the title; only the film is a film.
        Assert.Equal(["Q83495"], WikidataFacts.Candidates("The Matrix", WikidataWork.Film, hits).Select(hit => hit.Id));
        Assert.Equal(["Q83495"], WikidataFacts.Candidates("matrix", WikidataWork.Film, hits).Select(hit => hit.Id));
        Assert.Empty(WikidataFacts.Candidates("The Matrix", WikidataWork.Series, hits));
        Assert.Empty(WikidataFacts.Candidates("!!", WikidataWork.Film, hits));
        Assert.Empty(WikidataFacts.Candidates("Matrix Reloaded", WikidataWork.Film, hits));
    }

    [Fact]
    public void OnlyTheSeriesOfTheSameTitleIsACandidateAndNotItsSeasons()
    {
        var hits = Wikidata.ParseSearch(WikimediaFixtures.FawltySearch);

        Assert.Equal(["Q212731"], WikidataFacts.Candidates("Fawlty Towers", WikidataWork.Series, hits).Select(hit => hit.Id));
        Assert.Empty(WikidataFacts.Candidates("Fawlty Towers", WikidataWork.Film, hits));
    }

    [Fact]
    public void AtMostAFewCandidatesAreFetchedWhole()
    {
        var hits = Enumerable.Range(1, 9).Select(n => new WikidataHit("Q" + n, "Heat", n + "995 film")).ToList();

        Assert.Equal(WikidataFacts.CandidateLimit, WikidataFacts.Candidates("Heat", WikidataWork.Film, hits).Count);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Candidates(null!, WikidataWork.Film, hits));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Candidates("Heat", WikidataWork.Film, null!));
    }

    [Theory]
    [InlineData("1999 American science fiction action thriller film", WikidataWork.Film, true)]
    [InlineData("2003 film, second in the Matrix film series", WikidataWork.Film, true)]
    [InlineData("1995 movie by Michael Mann", WikidataWork.Film, true)]
    [InlineData("American science fiction film series", WikidataWork.Film, false)]
    [InlineData("film franchise", WikidataWork.Film, false)]
    [InlineData("film trilogy by Peter Jackson", WikidataWork.Film, false)]
    [InlineData("album by The Matrix", WikidataWork.Film, false)]
    [InlineData("filmmaker", WikidataWork.Film, false)]
    [InlineData("episode of the television series Lost", WikidataWork.Film, false)]
    [InlineData("1970s BBC television sitcom", WikidataWork.Series, true)]
    [InlineData("American TV series", WikidataWork.Series, true)]
    [InlineData("2016 British miniseries", WikidataWork.Series, true)]
    [InlineData("first season of television series ''Fawlty Towers''", WikidataWork.Series, true)]
    [InlineData("episode of the television series Lost", WikidataWork.Series, false)]
    [InlineData("Episode 4 of a television series", WikidataWork.Series, false)]
    [InlineData("hostel in Livingstone", WikidataWork.Series, false)]
    [InlineData("", WikidataWork.Series, false)]
    [InlineData(null, WikidataWork.Film, false)]
    public void ADescriptionSaysWhatKindOfWorkAnItemIs(string? description, WikidataWork work, bool describes) =>
        Assert.Equal(describes, WikidataFacts.Describes(description, work));

    [Fact]
    public void ThePickAgreesWithTheFilesYearWithinOneAndPrefersADatedItem()
    {
        WikidataHit[] hits = [new("Q1", "Heat", "1986 film"), new("Q2", "Heat", "film"), new("Q3", "Heat", "1995 film")];
        WikidataEntity[] items =
        [
            Item("Q3", Dated("+1995-12-15T00:00:00Z")),
            Item("Q1", Dated("+1986-03-14T00:00:00Z")),
            Item("Q2"),
        ];

        Assert.Equal("Q3", WikidataFacts.Pick(1995, WikidataWork.Film, hits, items)?.Id);
        Assert.Equal("Q3", WikidataFacts.Pick(1996, WikidataWork.Film, hits, items)?.Id);
        Assert.Equal("Q1", WikidataFacts.Pick(1986, WikidataWork.Film, hits, items)?.Id);
        // No dated item fits, so the undated one is the only reading left.
        Assert.Equal("Q2", WikidataFacts.Pick(2010, WikidataWork.Film, hits, items)?.Id);
        // Without a year, the search's order stands.
        Assert.Equal("Q1", WikidataFacts.Pick(null, WikidataWork.Film, hits, items)?.Id);
        Assert.Null(WikidataFacts.Pick(2010, WikidataWork.Film, hits, [items[0], items[1]]));
        Assert.Null(WikidataFacts.Pick(null, WikidataWork.Film, hits, []));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Pick(null, WikidataWork.Film, null!, items));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Pick(null, WikidataWork.Film, hits, null!));
    }

    [Fact]
    public void AWorksYearIsItsEarliestDateAndASeriesStartsWithItsFirstEpisode()
    {
        Assert.Equal(1999, WikidataFacts.Year(Matrix, WikidataWork.Film));
        var fawlty = Wikidata.ParseEntities(WikimediaFixtures.FawltyEntity)[0];
        Assert.Equal(1975, WikidataFacts.Year(fawlty, WikidataWork.Series));
        Assert.Null(WikidataFacts.Year(fawlty, WikidataWork.Film));
        Assert.Null(WikidataFacts.Year(Item("Q1", Dated("-0044-03-15T00:00:00Z")), WikidataWork.Film));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Year(null!, WikidataWork.Film));
    }

    [Theory]
    [InlineData("+1999-03-31T00:00:00Z", 1999, "1999-03-31")]
    [InlineData("+1999-00-00T00:00:00Z", 1999, null)]
    [InlineData("+1999-07-00T00:00:00Z", 1999, null)]
    [InlineData("+1999", null, null)]
    [InlineData("+0000-01-01T00:00:00Z", null, null)]
    [InlineData("-0044-03-15T00:00:00Z", null, null)]
    [InlineData("+abcd-01-01T00:00:00Z", null, null)]
    [InlineData("+1999-03", 1999, null)]
    [InlineData(null, null, null)]
    public void TimesReadAsYearsAndDays(string? time, int? year, string? day)
    {
        Assert.Equal(year, WikidataFacts.YearOf(time));
        Assert.Equal(day, WikidataFacts.DateOf(time));
    }

    [Fact]
    public void TheLabelQueryAsksForEveryNameACardShowsOnce()
    {
        var ids = WikidataFacts.Referenced(Matrix);

        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var id in new[] { "Q193048", "Q1750842", "Q9545711", "Q195719", "Q2484376", "Q622668", "Q1860", "Q30", "Q18665344", "Q1210827" })
        {
            Assert.Contains(id, ids);
        }

        Assert.True(ids.Count <= Wikidata.BatchLimit);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Referenced(null!));
    }

    [Fact]
    public void ARecordedFilmBecomesACardsFacts()
    {
        var summary = Wikipedia.ParseSummary(WikimediaFixtures.MatrixSummary);

        var movie = WikidataFacts.Movie(Matrix, MatrixLabels, summary, 1998);

        Assert.Equal(EnrichedMovie.CurrentSchema, movie.Schema);
        Assert.Equal(83495, movie.CatalogueId);
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal(1999, movie.Year);
        Assert.StartsWith("The Matrix is a 1999 science fiction", movie.Overview, StringComparison.Ordinal);
        Assert.Equal("https://en.wikipedia.org/wiki/The_Matrix", movie.ArticleUrl);
        Assert.Equal(["Thriller", "Dystopian", "Cyberpunk"], movie.Genres);
        Assert.Equal(136, movie.RuntimeMinutes);
        Assert.Equal("Village Roadshow Pictures", movie.Studio);
        Assert.Equal("English", movie.OriginalLanguage);
        Assert.Equal("United States", movie.Country);
        Assert.Equal("R", movie.ContentRating);
        Assert.Equal("The Matrix series", movie.Collection);
        Assert.Equal(["Lana Wachowski", "Lilly Wachowski"], movie.Directors);
        Assert.Equal(["the Wachowskis"], movie.Writers);
        Assert.Equal(["_XrKhqC5rQQ", "8VOG1nI-27Q"], movie.TrailerKeys);
        Assert.Equal("tt0133093", movie.ImdbId);
        // The poster is portrait, so the backdrop is the item's own Commons picture.
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/The.Matrix.glmatrix.1.png?width=1280", movie.BackdropUrl);
        // Billing order from the ordinals; Hugo Weaving has no English name to show and is left out.
        Assert.Equal(["Carrie-Anne Moss", "Laurence Fishburne", "Joe Pantoliano"], movie.Cast.Select(member => member.Name));
        Assert.Equal(["Trinity", "Morpheus", "Cypher"], movie.Cast.Select(member => member.Character));
        Assert.EndsWith("?width=185", movie.Cast[0].PhotoUrl, StringComparison.Ordinal);
        Assert.Equal("Thriller · Dystopian · Cyberpunk · R · 136 min", movie.Blurb());
    }

    [Fact]
    public void AFilmWithLittleKnownStillMakesACard()
    {
        var movie = WikidataFacts.Movie(new WikidataEntity("Q7", null, null, new Dictionary<string, IReadOnlyList<WikidataClaim>>(), null), NoLabels, null, 2001);

        Assert.Equal("Q7", movie.Title);
        Assert.Equal(2001, movie.Year);
        Assert.Null(movie.Overview);
        Assert.Null(movie.ArticleUrl);
        Assert.Null(movie.BackdropUrl);
        Assert.Empty(movie.Genres);
        Assert.Empty(movie.Cast);
        Assert.Equal("", movie.Blurb());
        var titled = WikidataFacts.Movie(new WikidataEntity("Q8", null, null, new Dictionary<string, IReadOnlyList<WikidataClaim>>(), null), NoLabels,
            new ArticleSummary("From The Article", null, "https://en.wikipedia.org/wiki/X", null, null), null);
        Assert.Equal("From The Article", titled.Title);
        // No synopsis, so no credit to give.
        Assert.Null(titled.ArticleUrl);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Movie(null!, NoLabels, null, null));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Movie(Matrix, null!, null, null));
    }

    [Fact]
    public void ARecordedSeriesBecomesAShowCandidate()
    {
        var fawlty = Wikidata.ParseEntities(WikimediaFixtures.FawltyEntity)[0];
        var labels = Wikidata.ParseLabels(WikimediaFixtures.FawltyLabels);
        var summary = Wikipedia.ParseSummary(WikimediaFixtures.FawltySummary);

        var show = WikidataFacts.Show(fawlty, labels, summary);

        Assert.Equal(212731, show.Id);
        Assert.Equal("Fawlty Towers", show.Name);
        Assert.Equal(1975, show.Year);
        Assert.StartsWith("Fawlty Towers is a British television sitcom", show.Summary, StringComparison.Ordinal);
        Assert.Equal(["Sitcom"], show.Genres);
        Assert.Null(show.Rating);
        // The article's lead picture is a wide title card: no poster.
        Assert.Null(show.ImageUrl);
        Assert.Equal("BBC Two", show.Network);
        Assert.Equal("English", show.Language);
        Assert.Equal(30, show.RuntimeMinutes);
        Assert.Equal("1975-09-19", show.Premiered);
        Assert.Equal("1979-10-25", show.Ended);
        Assert.Equal(["John Cleese", "Connie Booth"], WikidataFacts.Creators(fawlty, labels));
        Assert.Equal(["John Cleese", "Prunella Scales"], WikidataFacts.Cast(fawlty, labels).Select(member => member.Name));
        var bare = WikidataFacts.Show(new WikidataEntity("Q9", null, null, new Dictionary<string, IReadOnlyList<WikidataClaim>>(), null), NoLabels, null);
        Assert.Equal("Q9", bare.Name);
        Assert.Equal("Titled", WikidataFacts.Show(new WikidataEntity("Q9", null, null, new Dictionary<string, IReadOnlyList<WikidataClaim>>(), null), NoLabels,
            new ArticleSummary("Titled", null, null, null, null)).Name);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Show(null!, NoLabels, null));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Show(fawlty, null!, null));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Creators(null!, NoLabels));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Creators(fawlty, null!));
    }

    [Fact]
    public void CastIsBilledByOrdinalNamedOnceAndCharactersComeFromEitherQualifier()
    {
        var labels = new Dictionary<string, WikidataLabel>
        {
            ["Q1"] = new("Ann", "https://commons.wikimedia.org/wiki/Special:FilePath/Ann.jpg"),
            ["Q2"] = new("Bob", null),
            ["Q3"] = new("Cy", null),
            ["Q10"] = new("The Hero", null),
        };
        var item = Item("Q100",
            ("P161", Claim(new WikidataValue(Item: "Q2"), ("P4633", new WikidataValue(Text: "Sidekick")))),
            ("P161", Claim(new WikidataValue(Item: "Q1"), ("P453", new WikidataValue(Item: "Q10")), ("P1545", new WikidataValue(Text: "1")))),
            ("P161", Claim(new WikidataValue(Item: "Q3"), ("P453", new WikidataValue(Item: "Q99")), ("P1545", new WikidataValue(Text: "x")))),
            ("P161", Claim(new WikidataValue(Item: "Q1"))),
            ("P161", Claim(new WikidataValue(Item: "Q404"))),
            ("P161", Claim(new WikidataValue(Text: "not an item"))));

        var cast = WikidataFacts.Cast(item, labels);

        Assert.Equal(
        [
            new CastMember("Ann", "The Hero", "https://commons.wikimedia.org/wiki/Special:FilePath/Ann.jpg?width=185"),
            new CastMember("Bob", "Sidekick", null),
            new CastMember("Cy", null, null),
        ], cast);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Cast(null!, labels));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Cast(item, null!));
    }

    [Fact]
    public void AFullCastIsCutToTheCardsLimit()
    {
        var labels = Enumerable.Range(1, 30).ToDictionary(n => "Q" + n, n => new WikidataLabel("Actor " + n, null));
        var item = Item("Q100", [.. Enumerable.Range(1, 30).Select(n => ("P161", Claim(new WikidataValue(Item: "Q" + n))))]);

        Assert.Equal(WikidataFacts.CastLimit, WikidataFacts.Cast(item, labels).Count);
    }

    [Fact]
    public void GenresReadPlainlyOnceEachWithinTheLimit()
    {
        var labels = new Dictionary<string, WikidataLabel>
        {
            ["Q1"] = new("science fiction film", null),
            ["Q2"] = new("Science fiction", null),
            ["Q3"] = new("film", null),
            ["Q4"] = new("crime television series", null),
        };
        var item = Item("Q100", [.. new[] { "Q1", "Q2", "Q3", "Q4", "Q404" }.Select(id => ("P136", Claim(new WikidataValue(Item: id)))), ("P136", Claim(new WikidataValue(Text: "x")))]);

        Assert.Equal(["Science fiction", "Film", "Crime"], WikidataFacts.Genres(item, labels));
        var many = Enumerable.Range(1, 10).ToDictionary(n => "Q" + n, n => new WikidataLabel("genre " + n, null));
        Assert.Equal(WikidataFacts.GenreLimit, WikidataFacts.Genres(Item("Q100", [.. many.Keys.Select(id => ("P136", Claim(new WikidataValue(Item: id))))]), many).Count);
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Genres(null!, labels));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Genres(item, null!));
    }

    [Theory]
    [InlineData("science fiction film", "Science fiction")]
    [InlineData("crime television series", "Crime")]
    [InlineData("comedy movie", "Comedy")]
    [InlineData("  drama  ", "Drama")]
    [InlineData("film", "Film")]
    [InlineData(" ", "")]
    public void AGenreLabelReadsAsACardShowsIt(string label, string name) =>
        Assert.Equal(name, WikidataFacts.GenreName(label));

    [Fact]
    public void RuntimeIsReadInMinutesFromMinutesHoursOrSeconds()
    {
        static int? Runtime(decimal amount, string? unit) =>
            WikidataFacts.RuntimeMinutes(Item("Q1", ("P2047", Claim(new WikidataValue(Amount: amount, Unit: unit)))));

        Assert.Equal(136, Runtime(136, "Q7727"));
        Assert.Equal(90, Runtime(1.5m, "Q25235"));
        Assert.Equal(2, Runtime(120, "Q11574"));
        Assert.Null(Runtime(5, "Q11573"));
        Assert.Null(Runtime(0, "Q7727"));
        Assert.Null(Runtime(5, null));
        Assert.Null(WikidataFacts.RuntimeMinutes(Item("Q1", ("P2047", Claim(new WikidataValue(Text: "two hours"))))));
        // A useless first statement leaves the next to answer.
        Assert.Equal(100, WikidataFacts.RuntimeMinutes(Item("Q1",
            ("P2047", Claim(new WikidataValue(Amount: 0, Unit: "Q7727"))), ("P2047", Claim(new WikidataValue(Amount: 100, Unit: "Q7727"))))));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.RuntimeMinutes(null!));
    }

    [Fact]
    public void ACommonsAddressIsAskedForAtTheCardsWidth()
    {
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/A.jpg?width=185", WikidataFacts.Sized("https://commons.wikimedia.org/wiki/Special:FilePath/A.jpg", 185));
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/A.jpg?width=90", WikidataFacts.Sized("https://commons.wikimedia.org/wiki/Special:FilePath/A.jpg?width=1280", 90));
        Assert.Throws<ArgumentNullException>(() => WikidataFacts.Sized(null!, 1));
    }
}
