using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Music's enrichment read from recorded answers: MusicBrainz's artist and release facts, the photo
/// and work period from Wikidata, the summary from Wikipedia, and how the fetcher puts them into
/// one cached record, asking each free service only what it must and settling cleanly on no match.
/// </summary>
public sealed class MusicEnrichmentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-music-facts-" + Guid.NewGuid().ToString("N"));
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

    private void Answer(Uri uri, string json) => _answers[uri.AbsoluteUri] = json;

    private static Uri Photo => new(Wikidata.CommonsUrl("Asake performing in a Toronto concert (2024) (cropped).png", ArtistEnrichment.PhotoWidth));

    private void AnswerAsake()
    {
        Answer(MusicBrainzFacts.ArtistSearchUri("Asake"), MusicFixtures.ArtistSearch);
        Answer(MusicBrainzFacts.ArtistUri(MusicFixtures.AsakeId), MusicFixtures.Artist);
        Answer(Wikidata.EntitiesUri(["Q112861798"]), MusicFixtures.AsakeEntity);
        Answer(Wikipedia.SummaryUri("Asake"), MusicFixtures.AsakeSummary);
        _answers[Photo.AbsoluteUri] = "photo-bytes";
    }

    [Fact]
    public void TheUrisAskOnlyForWhatThePagesRead()
    {
        Assert.Equal("https://musicbrainz.org/ws/2/artist/?limit=5&fmt=json&query=" + Uri.EscapeDataString("artist:\"Asake \\\"Mr. Money\\\"\""),
            MusicBrainzFacts.ArtistSearchUri("Asake \"Mr. Money\"").AbsoluteUri);
        Assert.Equal("https://musicbrainz.org/ws/2/artist/" + MusicFixtures.AsakeId + "?inc=url-rels%2Bartist-rels%2Bgenres&fmt=json",
            MusicBrainzFacts.ArtistUri(MusicFixtures.AsakeId).AbsoluteUri);
        Assert.Equal("https://musicbrainz.org/ws/2/release/" + MusicFixtures.LunguBoyReleaseId + "?inc=labels&fmt=json",
            MusicBrainzFacts.ReleaseUri(MusicFixtures.LunguBoyReleaseId).AbsoluteUri);
        Assert.Throws<ArgumentException>(() => MusicBrainzFacts.ArtistSearchUri(" "));
        Assert.Throws<ArgumentException>(() => MusicBrainzFacts.ArtistUri("../x"));
        Assert.Throws<ArgumentException>(() => MusicBrainzFacts.ReleaseUri(""));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ArtistUri(null!));
    }

    [Fact]
    public void AnArtistSearchBecomesCandidatesAndOnlyTheSameNameIsTaken()
    {
        var candidates = MusicBrainzFacts.ParseArtistSearch(MusicFixtures.ArtistSearch);

        Assert.Equal([new ArtistCandidate(MusicFixtures.AsakeId, "Asake", 100), new ArtistCandidate("429bb543-779c-4556-886b-e6af1f156e89", "Wizkid & Asake", 76)], candidates);
        Assert.Equal(MusicFixtures.AsakeId, MusicBrainzFacts.PickArtist("ASAKE", candidates)?.Id);
        // The duo is a different artist from either of its members.
        Assert.Null(MusicBrainzFacts.PickArtist("Wizkid", candidates));
        Assert.Null(MusicBrainzFacts.PickArtist("!!", candidates));
        Assert.Empty(MusicBrainzFacts.ParseArtistSearch("""{"artists":{}}"""));
        Assert.Empty(MusicBrainzFacts.ParseArtistSearch("[]"));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ParseArtistSearch(null!));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.PickArtist(null!, candidates));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.PickArtist("Asake", null!));
    }

    [Fact]
    public void APersonsFactsNameWhereTheyAreFromAndTheirWikidataItem()
    {
        var asake = MusicBrainzFacts.ParseArtist(MusicFixtures.Artist)!;

        Assert.Equal(MusicFixtures.AsakeId, asake.Id);
        Assert.Equal("Person", asake.Type);
        Assert.Equal("Lagos, Nigeria", asake.Origin);
        Assert.Equal("1995-01-13", asake.Begin);
        Assert.Null(asake.End);
        Assert.False(asake.Ended);
        // Most voted first; nameless genres are dropped.
        Assert.Equal(["afrobeats", "afrobeat"], asake.Genres);
        // A band he is a member of is not a member of his.
        Assert.Empty(asake.Members);
        Assert.Equal("Q112861798", asake.WikidataId);
    }

    [Fact]
    public void AGroupsFactsListItsMembersCurrentFirst()
    {
        var group = MusicBrainzFacts.ParseArtist(MusicFixtures.Group)!;

        Assert.Equal("Group", group.Type);
        // The same place twice reads once.
        Assert.Equal("Nigeria", group.Origin);
        Assert.Equal(("2009-05", "2019", true), (group.Begin, group.End, group.Ended));
        Assert.Equal(["Ada Lead", "Former Member"], group.Members);
        Assert.Null(group.WikidataId);
        Assert.Equal(["highlife"], group.Genres);
    }

    [Fact]
    public void AnArtistAnswerMissingItsPartsStillReads()
    {
        Assert.Null(MusicBrainzFacts.ParseArtist("[]"));
        Assert.Null(MusicBrainzFacts.ParseArtist("""{"id":"a"}"""));
        Assert.Null(MusicBrainzFacts.ParseArtist("""{"name":"A"}"""));
        var bare = MusicBrainzFacts.ParseArtist("""{"id":"a","name":"A","type":" ","area":{"name":"Ghana"},"life-span":"odd"}""")!;
        Assert.Equal(new ArtistFacts("a", "A", null, "Ghana", null, null, false, [], [], null) with { Genres = bare.Genres, Members = bare.Members }, bare);
        Assert.Empty(bare.Genres);
        Assert.Equal("Accra", MusicBrainzFacts.ParseArtist("""{"id":"a","name":"A","begin-area":{"name":"Accra"},"area":"odd"}""")!.Origin);
        Assert.Null(MusicBrainzFacts.ParseArtist("""{"id":"a","name":"A","begin-area":{"name":""}}""")!.Origin);
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ParseArtist(null!));
    }

    [Fact]
    public void AReleaseGroupSearchCarriesItsTypeDateTagsAndOfficialRelease()
    {
        var facts = MusicBrainzFacts.ParseReleaseGroup(MusicFixtures.LunguBoySearch, MusicFixtures.LunguBoyGroup)!;

        Assert.Equal("Album", facts.PrimaryType);
        Assert.Empty(facts.SecondaryTypes);
        Assert.Equal("2024-08-08", facts.FirstReleased);
        Assert.Equal(MusicFixtures.LunguBoyReleaseId, facts.ReleaseId);
        Assert.Equal(["afro-fusion", "afrobeats"], facts.Tags);

        var compilation = MusicBrainzFacts.ParseReleaseGroup("""
            {"release-groups":[{"id":"c","primary-type":"Album","secondary-types":["Compilation","",3],"releases":[{"id":"r1","status":"Promotion"},{"title":"no id"}]}]}
            """, "c")!;
        Assert.Equal(["Compilation"], compilation.SecondaryTypes);
        // No official release: the first one listed.
        Assert.Equal("r1", compilation.ReleaseId);
        Assert.Null(compilation.FirstReleased);
        Assert.Null(MusicBrainzFacts.ParseReleaseGroup("""{"release-groups":[{"id":"e","releases":[]}]}""", "e")!.ReleaseId);
        Assert.Null(MusicBrainzFacts.ParseReleaseGroup(MusicFixtures.LunguBoySearch, "missing"));
        Assert.Null(MusicBrainzFacts.ParseReleaseGroup("{}", "x"));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ParseReleaseGroup("{}", null!));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ParseReleaseGroup(null!, "x"));
    }

    [Fact]
    public void AReleasesLabelsReadOnceEachWithoutThePlaceholder()
    {
        Assert.Equal(["EMPIRE", "YBNL Nation"], MusicBrainzFacts.ParseLabels(MusicFixtures.LunguBoyRelease));
        Assert.Empty(MusicBrainzFacts.ParseLabels("{}"));
        Assert.Throws<ArgumentNullException>(() => MusicBrainzFacts.ParseLabels(null!));
    }

    [Theory]
    [InlineData("2024-08-08", 2024)]
    [InlineData("2018", 2018)]
    [InlineData("201", null)]
    [InlineData("abcd", null)]
    [InlineData("0000-01-01", null)]
    [InlineData(null, null)]
    public void ACatalogueDateGivesItsYear(string? date, int? year) => Assert.Equal(year, MusicBrainzFacts.YearOf(date));

    [Theory]
    [InlineData("https://www.wikidata.org/wiki/Q112861798", "Q112861798")]
    [InlineData("https://www.wikidata.org/wiki/Q", null)]
    [InlineData("https://www.wikidata.org/wiki/P18", null)]
    [InlineData("https://www.wikidata.org/wiki/Q12x", null)]
    [InlineData("https://example.org/wiki/Q5", null)]
    [InlineData("not a link", null)]
    [InlineData(null, null)]
    public void OnlyAWikidataLinkNamesAnItem(string? link, string? item) => Assert.Equal(item, MusicBrainzFacts.WikidataItem(link));

    [Fact]
    public void APersonsYearsActiveComeFromWikidataAndAGroupsFromItsLifeSpan()
    {
        var item = Wikidata.ParseEntities(MusicFixtures.AsakeEntity)[0];
        var summary = Wikipedia.ParseSummary(MusicFixtures.AsakeSummary);
        var asake = ArtistEnrichment.Build("Asake", MusicBrainzFacts.ParseArtist(MusicFixtures.Artist), item, summary, hasPhoto: true);

        Assert.Equal(EnrichedArtist.CurrentSchema, asake.Schema);
        // Born in 1995, active since 2018.
        Assert.Equal((2018, (int?)null), (asake.ActiveFrom, asake.ActiveTo));
        Assert.Equal("Lagos, Nigeria", asake.Origin);
        Assert.StartsWith("Ahmed Ololade", asake.Bio);
        Assert.Equal("https://en.wikipedia.org/wiki/Asake", asake.ArticleUrl);
        Assert.Equal("Asake performing in a Toronto concert (2024) (cropped).png", asake.PhotoFile);
        Assert.Equal("Q112861798", asake.WikidataId);
        Assert.Empty(asake.Members);

        var group = ArtistEnrichment.Build("The Lagos Strings", MusicBrainzFacts.ParseArtist(MusicFixtures.Group), null, null, hasPhoto: false);
        Assert.Equal((2009, (int?)2019), (group.ActiveFrom, group.ActiveTo));
        Assert.Equal(["Ada Lead", "Former Member"], group.Members);
        Assert.Null(group.Bio);
        Assert.Null(group.PhotoFile);

        // A group still going has no last year; a person with no Wikidata work period has no years at all.
        var going = MusicBrainzFacts.ParseArtist("""{"id":"g","name":"G","type":"Orchestra","life-span":{"begin":"1990","end":"2000","ended":false}}""");
        Assert.Equal((1990, (int?)null), (ArtistEnrichment.Build("G", going, null, null, false).ActiveFrom, ArtistEnrichment.Build("G", going, null, null, false).ActiveTo));
        var person = ArtistEnrichment.Build("Asake", MusicBrainzFacts.ParseArtist(MusicFixtures.Artist), null, Wikipedia.ParseSummary("""{"title":"Asake"}"""), false);
        Assert.Equal(((int?)null, (int?)null), (person.ActiveFrom, person.ActiveTo));
        Assert.Null(person.ArticleUrl);
        Assert.Equal("Q112861798", person.WikidataId);
    }

    [Fact]
    public void NoFactsMeansARecordOfNoMatch()
    {
        var none = ArtistEnrichment.Build("Nobody", null, null, null, false);
        Assert.True(none is { MusicBrainzId: null, Current: true, Schema: EnrichedArtist.CurrentSchema });
        Assert.True(new EnrichedArtist { Name = "Old", MusicBrainzId = "x" } is { Current: false });
        Assert.Throws<ArgumentNullException>(() => ArtistEnrichment.Build(null!, null, null, null, false));
        Assert.Null(ArtistEnrichment.PhotoFile(null));
        Assert.Null(ArtistEnrichment.PhotoUri(null));
        Assert.True(ArtistEnrichment.IsGroup("choir"));
        Assert.False(ArtistEnrichment.IsGroup("Person"));
        Assert.False(ArtistEnrichment.IsGroup(null));
    }

    [Fact]
    public void TheArtistRecordSurvivesItsJsonAndRefusesWhatIsNotOne()
    {
        var artist = new EnrichedArtist { Schema = 1, Name = "Asake", MusicBrainzId = "m", Genres = ["afrobeats"], Members = ["A"], HasPhoto = true };
        Assert.Equal(artist.ToJson(), EnrichedArtist.FromJson(artist.ToJson())!.ToJson());
        Assert.DoesNotContain("Current", artist.ToJson(), StringComparison.Ordinal);
        Assert.Null(EnrichedArtist.FromJson("broken"));
        Assert.Throws<ArgumentNullException>(() => EnrichedArtist.FromJson(null!));
    }

    [Fact]
    public async Task AnArtistIsFetchedWholeOnceAndServedFromTheCache()
    {
        AnswerAsake();
        var fetcher = Fetcher;
        Assert.Empty(fetcher.CachedArtists());

        Assert.True(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        var asake = fetcher.CachedArtist("asake")!;
        Assert.True(asake is { MusicBrainzId: MusicFixtures.AsakeId, HasPhoto: true, ActiveFrom: 2018 });
        Assert.Equal("photo-bytes"u8.ToArray(), File.ReadAllBytes(fetcher.ArtistPhotoPath("Asake")));
        Assert.Equal(asake.ToJson(), Assert.Single(fetcher.CachedArtists()).ToJson());
        Assert.Equal(5, _asked.Count);

        _asked.Clear();
        Assert.True(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        Assert.Empty(_asked);

        fetcher.ForgetArtist("Asake");
        Assert.Null(fetcher.CachedArtist("Asake"));
        Assert.False(File.Exists(fetcher.ArtistPhotoPath("Asake")));
        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchArtistAsync(" ", CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => MetadataFetcher.ArtistKey(null!));
    }

    [Fact]
    public async Task WikimediaNotAnsweringCostsOnlyThePhotoAndTheBio()
    {
        AnswerAsake();
        _answers.Remove(Wikidata.EntitiesUri(["Q112861798"]).AbsoluteUri);
        var fetcher = Fetcher;

        Assert.True(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        Assert.True(fetcher.CachedArtist("Asake") is { MusicBrainzId: MusicFixtures.AsakeId, HasPhoto: false, Bio: null, Origin: "Lagos, Nigeria", WikidataId: "Q112861798" });
        Assert.DoesNotContain(_asked, asked => asked.Contains("wikipedia", StringComparison.Ordinal));

        // An artist with no Wikidata link asks nothing of Wikimedia at all.
        _asked.Clear();
        Answer(MusicBrainzFacts.ArtistSearchUri("The Lagos Strings"), """{"artists":[{"id":"b0000000-0000-0000-0000-000000000001","name":"The Lagos Strings","score":100}]}""");
        Answer(MusicBrainzFacts.ArtistUri("b0000000-0000-0000-0000-000000000001"), MusicFixtures.Group);
        Assert.True(await fetcher.FetchArtistAsync("The Lagos Strings", CancellationToken.None));
        Assert.Equal(2, _asked.Count);
        Assert.Equal(["Ada Lead", "Former Member"], fetcher.CachedArtist("The Lagos Strings")!.Members);
    }

    [Fact]
    public async Task MusicBrainzFailingLeavesNothingAndNoMatchIsRecorded()
    {
        var fetcher = Fetcher;
        Assert.False(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        Assert.Null(fetcher.CachedArtist("Asake"));

        Answer(MusicBrainzFacts.ArtistSearchUri("Asake"), "broken");
        Assert.False(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));

        // Found, but the lookup itself does not answer, or answers with no artist: try again later.
        Answer(MusicBrainzFacts.ArtistSearchUri("Asake"), MusicFixtures.ArtistSearch);
        Assert.False(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        Answer(MusicBrainzFacts.ArtistUri(MusicFixtures.AsakeId), "{}");
        Assert.False(await fetcher.FetchArtistAsync("Asake", CancellationToken.None));
        Assert.Null(fetcher.CachedArtist("Asake"));

        Answer(MusicBrainzFacts.ArtistSearchUri("Nobody Known"), """{"artists":[]}""");
        Assert.True(await fetcher.FetchArtistAsync("Nobody Known", CancellationToken.None));
        Assert.True(fetcher.CachedArtist("Nobody Known") is { MusicBrainzId: null, Current: true });
    }

    [Fact]
    public async Task AnAlbumGetsItsTypeDateLabelsAndGenres()
    {
        Answer(MusicBrainz.SearchUri("Asake", "Lungu Boy"), MusicFixtures.LunguBoySearch);
        Answer(MusicBrainzFacts.ReleaseUri(MusicFixtures.LunguBoyReleaseId), MusicFixtures.LunguBoyRelease);
        _answers[MusicBrainz.CoverUri(MusicFixtures.LunguBoyGroup).AbsoluteUri] = "cover";
        var fetcher = Fetcher;

        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Lungu Boy", filesCarryArt: false, CancellationToken.None));
        var album = fetcher.CachedAlbum("Asake", "Lungu Boy")!;
        Assert.True(album is { Type: "Album", FirstReleased: "2024-08-08", HasCover: true, Schema: EnrichedAlbum.CurrentSchema, Current: true });
        Assert.Equal(["EMPIRE", "YBNL Nation"], album.Labels);
        Assert.Equal(["afro-fusion", "afrobeats"], album.Genres);
    }

    [Fact]
    public async Task AnOlderAlbumRecordIsFetchedAgainKeepingItsCover()
    {
        Answer(MusicBrainz.SearchUri("Asake", "Lungu Boy"), MusicFixtures.LunguBoySearch);
        var fetcher = Fetcher;
        var cache = new MetadataCache(_folder);
        var key = MusicBrainz.AlbumKey("Asake", "Lungu Boy");
        cache.WriteText(MetadataFetcher.MusicKind, key, new EnrichedAlbum { Album = "Lungu Boy", Artist = "Asake", ReleaseGroupId = MusicFixtures.LunguBoyGroup, HasCover = true }.ToJson());
        cache.Write(MetadataFetcher.CoverKind, key, MetadataFetcher.ArtExtension, "old-cover"u8.ToArray());
        Assert.False(fetcher.CachedAlbum("Asake", "Lungu Boy")!.Current);

        // The labels do not answer: the album is settled all the same, without them.
        Assert.True(await fetcher.FetchAlbumAsync("Asake", "Lungu Boy", filesCarryArt: false, CancellationToken.None));
        Assert.DoesNotContain(_asked, asked => asked.Contains("coverartarchive", StringComparison.Ordinal));
        Assert.True(fetcher.CachedAlbum("Asake", "Lungu Boy") is { HasCover: true, Type: "Album", Labels.Count: 0 });
        Assert.Equal("old-cover"u8.ToArray(), File.ReadAllBytes(fetcher.CoverPath("Asake", "Lungu Boy")));

        // An old record of no match stays settled; an old local-art record gets its facts.
        Assert.True(new EnrichedAlbum { Album = "A" } is { Current: true });
        Assert.True(new EnrichedAlbum { Album = "A", LocalArt = true } is { Current: false });
    }
}
