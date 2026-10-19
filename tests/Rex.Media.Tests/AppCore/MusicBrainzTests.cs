using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Reading the music catalogue's answers and choosing carefully: exact titles (and artists, when
/// both sides name one) win by the catalogue's score, anything doubtful stays unmatched, and the
/// cover only ever fills a gap - files that carry their own art are recorded and never looked up.
/// </summary>
public sealed class MusicBrainzTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-music-" + Guid.NewGuid().ToString("N"));
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
        {"release-groups":[
          {"id":"rg-1","title":"Work of Art","score":100,"first-release-date":"2022-09-16","artist-credit":[{"name":"Asake"}]},
          {"id":"rg-2","title":"Work of Art","score":90,"artist-credit":[{"name":"Someone"},{"name":"Else"}]},
          {"id":"rg-3","title":"Work of Art Live","score":80,"artist-credit":[]},
          {"id":"","title":"No id, skipped"},
          {"title":"Nothing"},
          42
        ]}
        """;

    [Fact]
    public void ASearchAnswerBecomesCandidates()
    {
        var candidates = MusicBrainz.ParseSearch(Search);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(new AlbumCandidate("rg-1", "Work of Art", "Asake", 100, 2022), candidates[0]);
        Assert.Equal(new AlbumCandidate("rg-2", "Work of Art", "Someone Else", 90, null), candidates[1]);
        Assert.Equal(new AlbumCandidate("rg-3", "Work of Art Live", null, 80, null), candidates[2]);
        Assert.Empty(MusicBrainz.ParseSearch("{}"));
        Assert.Empty(MusicBrainz.ParseSearch("[]"));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.ParseSearch(null!));
    }

    [Fact]
    public void TheUrisNameTheServicesAndQuoteTheNames()
    {
        var search = MusicBrainz.SearchUri("Asake", "Work \"of\" Art").AbsoluteUri;
        Assert.StartsWith("https://musicbrainz.org/ws/2/release-group/?limit=8&fmt=json&query=", search);
        Assert.Contains("releasegroup", search);
        Assert.Contains(Uri.EscapeDataString("artist:\"Asake\""), search);
        var bare = MusicBrainz.SearchUri(null, "Work of Art").AbsoluteUri;
        Assert.DoesNotContain("artist", bare);
        Assert.Equal("https://coverartarchive.org/release-group/rg-1/front-500", MusicBrainz.CoverUri("rg-1").AbsoluteUri);
        Assert.Equal("https://coverartarchive.org/release-group/rg-1/front-250", MusicBrainz.CoverThumbUri("rg-1").AbsoluteUri);
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.SearchUri("a", null!));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.CoverUri(null!));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.CoverThumbUri(null!));
    }

    [Fact]
    public void PickingTakesTheExactAlbumAndLeavesDoubtAlone()
    {
        var candidates = MusicBrainz.ParseSearch(Search);

        Assert.Equal("rg-1", MusicBrainz.Pick("Asake", "Work of Art", candidates)?.Id);
        // Without an artist wanted, the best-scoring exact title wins.
        Assert.Equal("rg-1", MusicBrainz.Pick(null, "work of art!", candidates)?.Id);
        // An artist that matches nothing exact leaves only the artist-less near title, which is not exact: no pick.
        Assert.Null(MusicBrainz.Pick("Nobody", "Work of Art Live", [MusicBrainz.ParseSearch(Search)[1]]));
        Assert.Null(MusicBrainz.Pick("Asake", "Different Album", candidates));
        Assert.Null(MusicBrainz.Pick("Asake", "---", candidates));
        Assert.Null(MusicBrainz.Pick("Asake", "Work of Art", []));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.Pick("a", null!, []));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.Pick("a", "b", null!));
    }

    [Fact]
    public void OneAlbumKeyWhereverItIsNamed()
    {
        Assert.Equal(MusicBrainz.AlbumKey("Asake!", "Work of Art"), MusicBrainz.AlbumKey("asake", "WORK OF ART"));
        Assert.NotEqual(MusicBrainz.AlbumKey("Asake", "Work of Art"), MusicBrainz.AlbumKey(null, "Work of Art"));
        Assert.Equal(MusicBrainz.AlbumKey(null, "X"), MusicBrainz.AlbumKey("  ", "X"));
        Assert.Throws<ArgumentNullException>(() => MusicBrainz.AlbumKey("a", null!));
    }

    [Fact]
    public async Task ACoverIsFetchedOnlyToFillAGap()
    {
        _answers[MusicBrainz.SearchUri("Asake", "Work of Art").AbsoluteUri] = Search;
        _answers["https://coverartarchive.org/release-group/rg-1/front-500"] = "cover-bytes";
        var fetcher = Fetcher;

        // Files that carry their own art are recorded without a single request.
        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: true, CancellationToken.None));
        Assert.True(fetcher.CachedAlbum("Asake", "Work of Art") is { LocalArt: true, HasCover: false, ReleaseGroupId: null });
        Assert.Empty(_asked);

        fetcher.ForgetAlbum("Asake", "Work of Art");
        Assert.Null(fetcher.CachedAlbum("Asake", "Work of Art"));

        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: false, CancellationToken.None));
        var album = fetcher.CachedAlbum("Asake", "Work of Art");
        Assert.True(album is { ReleaseGroupId: "rg-1", HasCover: true, LocalArt: false });
        Assert.Equal("cover-bytes"u8.ToArray(), File.ReadAllBytes(fetcher.CoverPath("Asake", "Work of Art")));
        Assert.Equal(album!.ToJson(), Assert.Single(fetcher.CachedAlbums()).ToJson());

        // Already cached: nothing more is asked.
        _asked.Clear();
        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: false, CancellationToken.None));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task TheCatalogueFailingLeavesNothingAndNoMatchIsRecorded()
    {
        var fetcher = Fetcher;
        // No answer at all: try again later.
        Assert.False(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: false, CancellationToken.None));
        Assert.Null(fetcher.CachedAlbum("Asake", "Work of Art"));

        // A shapeless answer: try again later.
        _answers[MusicBrainz.SearchUri("Asake", "Work of Art").AbsoluteUri] = "broken";
        Assert.False(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: false, CancellationToken.None));

        // A clean no-match is recorded and not asked again.
        _answers[MusicBrainz.SearchUri("Asake", "Lonely At The Top").AbsoluteUri] = """{"release-groups":[]}""";
        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Lonely At The Top", filesCarryArt: false, CancellationToken.None));
        Assert.True(fetcher.CachedAlbum("Asake", "Lonely At The Top") is { ReleaseGroupId: null, HasCover: false });

        // A match whose archive has no cover is still settled.
        _answers[MusicBrainz.SearchUri("Asake", "Work of Art").AbsoluteUri] = Search;
        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Work of Art", filesCarryArt: false, CancellationToken.None));
        Assert.True(fetcher.CachedAlbum("Asake", "Work of Art") is { ReleaseGroupId: "rg-1", HasCover: false });

        await Assert.ThrowsAsync<ArgumentNullException>(() => fetcher.FetchAlbumAsync("a", null!, false, CancellationToken.None));
    }

    [Fact]
    public async Task SearchingForThePickerReturnsTheCandidatesAndRawFetchesPassThrough()
    {
        _answers[MusicBrainz.SearchUri("Asake", "Work of Art").AbsoluteUri] = Search;
        var fetcher = Fetcher;
        Assert.Equal(3, (await fetcher.SearchAlbumsAsync("Asake", "Work of Art", CancellationToken.None)).Count);
        Assert.Empty(await fetcher.SearchAlbumsAsync("Asake", "Unknown", CancellationToken.None));
        _answers[MusicBrainz.SearchUri("Asake", "Odd").AbsoluteUri] = "broken";
        Assert.Empty(await fetcher.SearchAlbumsAsync("Asake", "Odd", CancellationToken.None));

        _answers["https://coverartarchive.org/release-group/rg-1/front-250"] = "thumb";
        Assert.Equal("thumb"u8.ToArray(), await fetcher.FetchBytesAsync(MusicBrainz.CoverThumbUri("rg-1"), CancellationToken.None));
        Assert.Null(await fetcher.FetchBytesAsync(MusicBrainz.CoverThumbUri("rg-2"), CancellationToken.None));
    }

    [Fact]
    public void ReplacingCoversIsOffByDefault() => Assert.False(new Rex.Media.Settings.MetadataSettings().ReplaceCovers);

    [Fact]
    public void TheGateKeepsOneCallPerInterval()
    {
        var gate = new RateGate(TimeSpan.FromSeconds(1));
        var noon = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, gate.Delay(noon));
        Assert.Equal(TimeSpan.FromSeconds(1), gate.Delay(noon));
        Assert.Equal(TimeSpan.FromSeconds(1.5), gate.Delay(noon + TimeSpan.FromSeconds(0.5)));
        Assert.Equal(TimeSpan.Zero, gate.Delay(noon + TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateGate(TimeSpan.Zero));
    }

    [Fact]
    public void TheAlbumRecordSurvivesItsJsonAndRefusesWhatIsNotOne()
    {
        var album = new EnrichedAlbum { Album = "X", Artist = "Y", ReleaseGroupId = "rg", HasCover = true };
        Assert.Equal(album, EnrichedAlbum.FromJson(album.ToJson()));
        Assert.Null(EnrichedAlbum.FromJson("broken"));
        Assert.Throws<ArgumentNullException>(() => EnrichedAlbum.FromJson(null!));
    }
}
