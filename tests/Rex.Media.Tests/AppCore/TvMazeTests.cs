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

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("<p>Two</p><p>paragraphs</p>", "Two paragraphs")]
    [InlineData("a &lt;b&gt; &quot;c&quot; &#39;d&#39; &apos;e&apos;&nbsp;f", "a <b> \"c\" 'd' 'e' f")]
    [InlineData("  spaced \n out  ", "spaced out")]
    public void SmallHtmlFlattensToPlainText(string? html, string plain) => Assert.Equal(plain, TvMaze.Plain(html));
}
