using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Fetching one show into the cache with a fake network: search, match, episodes and poster land on
/// disk, the user's pick overrides the search, a clean no-match is recorded once, and a catalogue
/// that cannot answer leaves nothing half-written.
/// </summary>
public sealed class MetadataFetcherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-fetcher-" + Guid.NewGuid().ToString("N"));
    private readonly MetadataCache _cache;
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly List<string> _asked = [];

    public MetadataFetcherTests() => _cache = new MetadataCache(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MetadataFetcher Fetcher => new(_cache, (uri, _) =>
    {
        _asked.Add(uri.AbsoluteUri);
        return Task.FromResult(_answers.TryGetValue(uri.AbsoluteUri, out var answer) ? Encoding.UTF8.GetBytes(answer) : null);
    });

    private const string TheShow = """{"id":318,"name":"The Show","premiered":"2011-04-17","genres":["Drama"],"rating":{"average":8.2},"summary":"<p>Dragons.</p>","image":{"original":"https://img/o.jpg"}}""";

    private void AnswerEverything()
    {
        _answers["https://api.tvmaze.com/search/shows?q=The%20Show"] = $$"""[{"score":0.9,"show":{{TheShow}}}]""";
        _answers["https://api.tvmaze.com/shows/318"] = TheShow;
        _answers["https://api.tvmaze.com/shows/318/episodes"] = """[{"id":1,"season":1,"number":1,"name":"Winter","summary":"<p>It begins.</p>"}]""";
        _answers["https://img/o.jpg"] = "jpeg-bytes";
    }

    [Fact]
    public async Task AMatchedShowLandsWholeInTheCache()
    {
        AnswerEverything();
        Assert.True(await Fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));

        var fetcher = Fetcher;
        var show = fetcher.Cached("the show!");
        Assert.NotNull(show);
        Assert.Equal((318, "The Show", "Dragons.", 8.2, true), (show!.CatalogueId, show.Name, show.Summary, show.Rating, show.HasPoster));
        Assert.True(show.Found);
        Assert.Equal(["Drama"], show.Genres);
        Assert.Equal("Winter", show.EpisodeName(1, 1));
        Assert.Equal("It begins.", show.EpisodeSummary(1, 1));
        Assert.Null(show.EpisodeName(2, 1));
        Assert.Null(show.EpisodeName(1, null));
        Assert.Equal("Drama · 8.2 / 10", show.Blurb());
        Assert.Equal("jpeg-bytes"u8.ToArray(), _cache.ReadBytes(MetadataFetcher.ArtKind, "show", MetadataFetcher.ArtExtension));
        Assert.Equal(show.ToJson(), Assert.Single(fetcher.CachedShows()).ToJson());

        // Already cached: nothing more is asked.
        _asked.Clear();
        Assert.True(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.Empty(_asked);

        // Forgotten: both the record and the poster go.
        fetcher.Forget("The Show");
        Assert.Null(fetcher.Cached("The Show"));
        Assert.Null(_cache.ReadBytes(MetadataFetcher.ArtKind, "show", MetadataFetcher.ArtExtension));
    }

    [Fact]
    public async Task TheUsersPickOverridesTheSearch()
    {
        AnswerEverything();
        Assert.True(await Fetcher.FetchShowAsync("Totally Different Name", null, 318, CancellationToken.None));

        var show = Fetcher.Cached("Totally Different Name");
        Assert.Equal(318, show!.CatalogueId);
        Assert.DoesNotContain(_asked, uri => uri.Contains("search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoMatchIsRecordedOnceAndNotAskedAgain()
    {
        _answers["https://api.tvmaze.com/search/shows?q=Obscure"] = "[]";
        // TVmaze has no match, and Wikidata also confirms no matching series.
        _answers[Wikidata.SearchUri("Obscure").AbsoluteUri] = """{"search":[]}""";
        var fetcher = Fetcher;
        Assert.True(await fetcher.FetchShowAsync("Obscure", null, null, CancellationToken.None));

        var show = fetcher.Cached("Obscure");
        Assert.False(show!.Found);
        Assert.Equal("", show.Blurb());
        _asked.Clear();
        Assert.True(await fetcher.FetchShowAsync("Obscure", null, null, CancellationToken.None));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task AnUnansweredOrMalformedCatalogueLeavesNothingBehind()
    {
        var fetcher = Fetcher;
        // No search answer at all.
        Assert.False(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        // A pick whose show cannot be read.
        Assert.False(await fetcher.FetchShowAsync("The Show", null, 999, CancellationToken.None));
        // A match whose episodes never answer.
        _answers["https://api.tvmaze.com/search/shows?q=The%20Show"] = $$"""[{"score":0.9,"show":{{TheShow}}}]""";
        Assert.False(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        // A catalogue answering something that is not its usual shape.
        _answers["https://api.tvmaze.com/search/shows?q=The%20Show"] = "not json";
        Assert.False(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.Null(fetcher.Cached("The Show"));
        Assert.Empty(fetcher.CachedShows());
        Assert.Empty(await fetcher.SearchShowsAsync("The Show", CancellationToken.None));
    }

    [Fact]
    public async Task AShowWithoutAPosterIsStillKept()
    {
        AnswerEverything();
        _answers.Remove("https://img/o.jpg");
        var fetcher = Fetcher;
        Assert.True(await fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));

        var show = fetcher.Cached("The Show");
        Assert.False(show!.HasPoster);
        Assert.Null(_cache.ReadBytes(MetadataFetcher.ArtKind, "show", MetadataFetcher.ArtExtension));
    }

    [Fact]
    public async Task MalformedOptionalDetailsDoNotDiscardAUsableShow()
    {
        AnswerEverything();
        _answers["https://api.tvmaze.com/shows/318/cast"] = "broken";
        _answers["https://api.tvmaze.com/shows/318/crew"] = "broken";
        _answers["https://api.tvmaze.com/shows/318/seasons"] = "broken";

        Assert.True(await Fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));

        var show = Fetcher.Cached("The Show");
        Assert.NotNull(show);
        Assert.Equal("Winter", show!.EpisodeName(1, 1));
        Assert.Empty(show.Cast);
        Assert.Empty(show.Creators);
        Assert.Empty(show.Seasons);
    }

    [Fact]
    public async Task ACorruptCachedShowIsFetchedAgain()
    {
        // Written under the very key the fetcher reads, so the damaged record is what it finds first.
        _cache.WriteText(MetadataFetcher.ShowKind, MetadataMatch.Normalize("The Show"), "broken");
        Assert.Null(Fetcher.Cached("The Show"));
        AnswerEverything();

        Assert.True(await Fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.Equal(318, Fetcher.Cached("The Show")!.CatalogueId);
        Assert.NotEmpty(_asked);
    }

    [Fact]
    public async Task AnOlderFoundRecordIsFetchedAgainAndUpgraded()
    {
        // A record from before the schema field: found, but without cast or seasons.
        _cache.WriteText(MetadataFetcher.ShowKind, MetadataMatch.Normalize("The Show"), """{"CatalogueId":318,"Name":"The Show","Summary":"Old."}""");
        var old = Fetcher.Cached("The Show");
        Assert.True(old is { Schema: 0, Found: true, Current: false });
        AnswerEverything();

        Assert.True(await Fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.NotEmpty(_asked);
        var upgraded = Fetcher.Cached("The Show");
        Assert.True(upgraded is { Schema: EnrichedShow.CurrentSchema, Current: true, Summary: "Dragons." });

        // Current now: nothing more is asked.
        _asked.Clear();
        Assert.True(await Fetcher.FetchShowAsync("The Show", null, null, CancellationToken.None));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task AnOlderNoMatchIsNotAskedAgain()
    {
        _cache.WriteText(MetadataFetcher.ShowKind, MetadataMatch.Normalize("Obscure"), """{"CatalogueId":0,"Name":"Obscure"}""");
        Assert.True(Fetcher.Cached("Obscure") is { Schema: 0, Found: false, Current: true });

        Assert.True(await Fetcher.FetchShowAsync("Obscure", null, null, CancellationToken.None));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task SearchingForThePickerReturnsTheCandidates()
    {
        AnswerEverything();
        var found = await Fetcher.SearchShowsAsync("The Show", CancellationToken.None);
        Assert.Equal(318, Assert.Single(found).Id);

        Assert.Empty(await Fetcher.SearchShowsAsync("Unknown", CancellationToken.None));
        _answers["https://api.tvmaze.com/search/shows?q=Odd"] = "broken";
        Assert.Empty(await Fetcher.SearchShowsAsync("Odd", CancellationToken.None));
    }

    [Fact]
    public async Task NullsAreRefusedPlainly()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataFetcher(null!, (_, _) => Task.FromResult<byte[]?>(null)));
        Assert.Throws<ArgumentNullException>(() => new MetadataFetcher(_cache, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Fetcher.FetchShowAsync(null!, null, null, CancellationToken.None));
    }

    [Fact]
    public void TheRecordSurvivesItsJsonAndRefusesWhatIsNotOne()
    {
        var show = new EnrichedShow { CatalogueId = 1, Name = "X", Episodes = [new EnrichedEpisode(1, 1, "A", null)] };
        var reloaded = EnrichedShow.FromJson(show.ToJson());
        Assert.Equal(show.ToJson(), reloaded!.ToJson());
        Assert.Equal(show.Episodes, reloaded.Episodes);
        Assert.Null(EnrichedShow.FromJson("broken"));
        Assert.Throws<ArgumentNullException>(() => EnrichedShow.FromJson(null!));

        var nullLists = EnrichedShow.FromJson("""{"CatalogueId":1,"Name":"X","Genres":null,"Cast":null,"Creators":null,"Seasons":null,"Episodes":null}""");
        Assert.Equal("", nullLists!.Blurb());
        Assert.Null(nullLists.EpisodeName(1, 1));
        Assert.Empty(nullLists.Cast);
    }

    [Fact]
    public void ChoicesSurviveTheirFileAndDamageLosesOnlyThem()
    {
        var choices = new MetadataChoices();
        Assert.Equal(0, choices.Count);
        Assert.Null(choices.ChosenShow("The Show"));
        choices.ChooseShow("The Show", 318);
        choices.ChooseShow("Other", 7);
        Assert.Equal(318, choices.ChosenShow("the show!"));

        var reloaded = MetadataChoices.Load(choices.Save());
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(318, reloaded.ChosenShow("The Show"));
        reloaded.ForgetShow("The Show");
        Assert.Null(reloaded.ChosenShow("The Show"));
        Assert.Equal(7, reloaded.ChosenShow("Other"));

        Assert.Equal(0, MetadataChoices.Load(null).Count);
        Assert.Equal(0, MetadataChoices.Load("  ").Count);
        Assert.Equal(0, MetadataChoices.Load("damaged{").Count);
    }
}
