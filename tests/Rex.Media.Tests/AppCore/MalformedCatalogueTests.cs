using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// A catalogue that answers in an odd shape (a number that is not a whole one, a figure too large,
/// an image address that is not one) costs the one odd field, never the show or the album, and
/// never the fetcher: each used to throw past the parsers and stop every lookup until
/// rexplayer restarted.
/// </summary>
public sealed class MalformedCatalogueTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-malformed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ShowsWithFractionalOrHugeNumbersArePassedOverOrLoseOnlyThatNumber()
    {
        var shows = TvMaze.ParseSearch("""[{"show":{"id":1.5,"name":"Half"}},{"show":{"id":1e30,"name":"Huge"}},{"show":{"id":7,"name":"Asake Live","averageRuntime":2.5,"runtime":1e12}}]""");

        var show = Assert.Single(shows);
        Assert.Equal(7, show.Id);
        Assert.Null(show.RuntimeMinutes);
        Assert.Empty(TvMaze.ParseEpisodes("""[{"season":1.5,"number":1,"name":"Lonely At The Top"},{"season":1,"number":99999999999,"name":"Sungba"}]"""));
        Assert.Empty(TvMaze.ParseSeasons("""[{"number":0.5}]"""));
    }

    [Fact]
    public void AnAlbumWithAnOddScoreKeepsItsOtherFacts()
    {
        var album = Assert.Single(MusicBrainz.ParseSearch("""{"release-groups":[{"id":"x","title":"Work of Art","score":99.5}]}"""));

        Assert.Equal("Work of Art", album.Title);
        Assert.Equal(0, album.Score);

        // Extremely large numeric values are malformed too: keep the album rather than
        // allowing GetInt32 to abort the metadata loop.
        var huge = Assert.Single(MusicBrainz.ParseSearch("""{"release-groups":[{"id":"y","title":"Overflow","score":1e30}]}"""));
        Assert.Equal("Overflow", huge.Title);
        Assert.Equal(0, huge.Score);
    }

    [Fact]
    public async Task AShowWhosePosterAddressIsNotOneIsStillFetchedWithoutItsPoster()
    {
        const string Show = """{"id":318,"name":"The Show","image":{"original":"not an address"}}""";
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["https://api.tvmaze.com/search/shows?q=The%20Show"] = $$"""[{"score":0.9,"show":{{Show}}}]""",
            ["https://api.tvmaze.com/shows/318/episodes"] = """[{"season":1,"number":1,"name":"Winter"}]""",
        };
        var fetcher = new MetadataFetcher(new MetadataCache(_folder), (uri, _) =>
            Task.FromResult(answers.TryGetValue(uri.AbsoluteUri, out var answer) ? Encoding.UTF8.GetBytes(answer) : null));

        Assert.True(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.False(fetcher.Cached("The Show")!.HasPoster);
    }
}
