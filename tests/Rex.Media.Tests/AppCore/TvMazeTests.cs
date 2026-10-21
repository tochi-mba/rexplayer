using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Reading the TV catalogue's answers: searches, one show, episodes, and its small HTML flattened
/// to plain text, with anything oddly shaped skipped rather than thrown at the user.
/// </summary>
public sealed class TvMazeTests
{
    private const string Search = """
        [
          {"score":0.9,"show":{"id":318,"name":"The Show","premiered":"2011-04-17","genres":["Drama","Adventure"],
           "rating":{"average":8.2},"summary":"<p>Knights &amp; dragons.</p>","image":{"medium":"https://img/m.jpg","original":"https://img/o.jpg"}}},
          {"score":0.5,"show":{"id":7,"name":"Another","premiered":null,"genres":[],"rating":{"average":null},"summary":null,"image":null}},
          {"score":0.1,"show":{"name":"No id, skipped"}},
          {"score":0.1},
          "not an object"
        ]
        """;

    [Fact]
    public void ASearchAnswerBecomesCandidates()
    {
        var candidates = TvMaze.ParseSearch(Search);

        Assert.Equal(2, candidates.Count);
        var (best, bare) = (candidates[0], candidates[1]);
        Assert.Equal((318, "The Show", 2011, 8.2, "https://img/o.jpg"), (best.Id, best.Name, best.Year, best.Rating, best.ImageUrl));
        Assert.Equal(["Drama", "Adventure"], best.Genres);
        Assert.Equal("Knights & dragons.", best.Summary);
        Assert.Equal((7, "Another", null, null, null, null), (bare.Id, bare.Name, bare.Year, bare.Rating, bare.ImageUrl, bare.Summary));
        Assert.Empty(bare.Genres);
    }

    [Fact]
    public void OddAnswersAreEmptyNotErrors()
    {
        Assert.Empty(TvMaze.ParseSearch("{}"));
        Assert.Empty(TvMaze.ParseSearch("[]"));
        Assert.Empty(TvMaze.ParseEpisodes("{\"a\":1}"));
        Assert.Null(TvMaze.ParseShow("[]"));
        Assert.Null(TvMaze.ParseShow("{\"name\":\"No id\"}"));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseSearch(null!));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseShow(null!));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseEpisodes(null!));
        Assert.Throws<ArgumentNullException>(() => TvMaze.SearchUri(null!));
    }

    [Fact]
    public void OneShowAndItsEpisodesAreRead()
    {
        var show = TvMaze.ParseShow("""{"id":318,"name":"The Show","premiered":"2011-04-17","image":{"medium":"https://img/m.jpg"}}""");
        Assert.Equal((318, "The Show", 2011, "https://img/m.jpg"), (show!.Id, show.Name, show.Year, show.ImageUrl));

        var episodes = TvMaze.ParseEpisodes("""
            [
              {"id":1,"season":1,"number":1,"name":"Winter","summary":"<p>It begins.</p>"},
              {"id":2,"season":1,"number":2,"name":"Road","summary":null},
              {"id":3,"season":1,"number":null,"name":"Special, skipped"},
              {"id":4,"season":1,"number":3,"name":""},
              {"id":5}
            ]
            """);
        Assert.Equal([new EnrichedEpisode(1, 1, "Winter", "It begins."), new EnrichedEpisode(1, 2, "Road", null)], episodes);
    }

    [Fact]
    public void TheUrisNameTheCatalogueAndEscapeTheName()
    {
        Assert.Equal("https://api.tvmaze.com/search/shows?q=The%20Show%20%26%20Co", TvMaze.SearchUri("The Show & Co").AbsoluteUri);
        Assert.Equal("https://api.tvmaze.com/shows/318", TvMaze.ShowUri(318).AbsoluteUri);
        Assert.Equal("https://api.tvmaze.com/shows/318/episodes", TvMaze.EpisodesUri(318).AbsoluteUri);
    }

    [Fact]
    public void AShowCarriesItsCardFacts()
    {
        var show = TvMaze.ParseShow("""
            {"id":318,"name":"The Show","premiered":"2011-04-17","ended":"2019-05-19","status":"Ended",
             "language":"English","averageRuntime":57,"network":{"name":"HBO"}}
            """);

        Assert.Equal(("Ended", "HBO", 57, "English", "2011-04-17", "2019-05-19"),
            (show!.Status, show.Network, show.RuntimeMinutes, show.Language, show.Premiered, show.Ended));

        var streamed = TvMaze.ParseShow("""{"id":1,"name":"X","network":null,"webChannel":{"name":"Streamflix"},"averageRuntime":0,"runtime":42,"status":""}""");
        Assert.Equal(("Streamflix", 42, null), (streamed!.Network, streamed.RuntimeMinutes, streamed.Status));
    }

    [Fact]
    public void EpisodesCarryAirDatesRuntimesAndStills()
    {
        var episodes = TvMaze.ParseEpisodes("""
            [{"id":1,"season":1,"number":1,"name":"Winter","airdate":"2011-04-17","runtime":62,"image":{"medium":"https://img/ep-m.jpg","original":"https://img/ep.jpg"}},
             {"id":2,"season":1,"number":2,"name":"Road","airdate":"","runtime":0,"image":null}]
            """);

        Assert.Equal(("2011-04-17", 62, "https://img/ep.jpg"), (episodes[0].AirDate, episodes[0].RuntimeMinutes, episodes[0].StillUrl));
        Assert.Equal((null, null, null), (episodes[1].AirDate, episodes[1].RuntimeMinutes, episodes[1].StillUrl));
    }

    [Fact]
    public void CastCreatorsAndSeasonsAreRead()
    {
        var cast = TvMaze.ParseCast("""
            [{"person":{"name":"Lead Actor","image":{"medium":"https://img/p.jpg"}},"character":{"name":"The Lead"}},
             {"person":{"name":"Bare Actor"},"character":null},
             {"person":{}},
             {"character":{"name":"No person"}},
             7]
            """);
        Assert.Equal([new CastMember("Lead Actor", "The Lead", "https://img/p.jpg"), new CastMember("Bare Actor", null, null)], cast);
        Assert.Empty(TvMaze.ParseCast("{}"));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseCast(null!));

        var creators = TvMaze.ParseCreators("""
            [{"type":"Creator","person":{"name":"The Creator"}},
             {"type":"Creator","person":{"name":"The Creator"}},
             {"type":"Producer","person":{"name":"Someone"}},
             {"type":"Creator"},
             3]
            """);
        Assert.Equal(["The Creator"], creators);
        Assert.Empty(TvMaze.ParseCreators("{}"));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseCreators(null!));

        var seasons = TvMaze.ParseSeasons("""
            [{"number":1,"summary":"<p>First.</p>","image":{"original":"https://img/s1.jpg"}},
             {"number":2,"summary":null,"image":null},
             {"summary":"No number"},
             9]
            """);
        Assert.Equal([new SeasonDetails(1, "First.", "https://img/s1.jpg"), new SeasonDetails(2, null, null)], seasons);
        Assert.Empty(TvMaze.ParseSeasons("{}"));
        Assert.Throws<ArgumentNullException>(() => TvMaze.ParseSeasons(null!));

        Assert.Equal("https://api.tvmaze.com/shows/318/cast", TvMaze.CastUri(318).AbsoluteUri);
        Assert.Equal("https://api.tvmaze.com/shows/318/crew", TvMaze.CrewUri(318).AbsoluteUri);
        Assert.Equal("https://api.tvmaze.com/shows/318/seasons", TvMaze.SeasonsUri(318).AbsoluteUri);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("<p>Two</p><p>paragraphs</p>", "Two paragraphs")]
    [InlineData("a &lt;b&gt; &quot;c&quot; &#39;d&#39; &apos;e&apos;&nbsp;f", "a <b> \"c\" 'd' 'e' f")]
    [InlineData("  spaced \n out  ", "spaced out")]
    public void SmallHtmlFlattensToPlainText(string? html, string plain) => Assert.Equal(plain, TvMaze.Plain(html));
}
