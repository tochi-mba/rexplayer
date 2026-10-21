using System.Text.Json;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Reading Wikidata's answers, recorded from the real services: searches, items with their
/// statements and qualifiers, and the label query's names and pictures; and the addresses asked.
/// </summary>
public sealed class WikidataTests
{
    [Fact]
    public void TheAddressesAskForEnglishAndEscapeWhatTheyCarry()
    {
        Assert.Equal("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=10&search=Am%C3%A9lie%20%26%20Co",
            Wikidata.SearchUri("Amélie & Co").AbsoluteUri);
        Assert.Equal("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&languages=en&sitefilter=enwiki&props=labels%7Cdescriptions%7Cclaims%7Csitelinks&ids=Q83495%7CQ1",
            Wikidata.EntitiesUri(["Q83495", "Q1"]).AbsoluteUri);
        var labels = Wikidata.LabelsUri(["Q1", "Q2"]);
        Assert.Equal("query.wikidata.org", labels.Host);
        Assert.Contains("VALUES ?item { wd:Q1 wd:Q2 }", Uri.UnescapeDataString(labels.Query), StringComparison.Ordinal);
        Assert.Contains("format=json", labels.Query, StringComparison.Ordinal);
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/Red_and_blue_pill.jpg?width=1280", Wikidata.CommonsUrl("Red and blue pill.jpg", 1280));
    }

    [Fact]
    public void TheAddressesRefuseWhatIsNotAnItemOrTooMany()
    {
        Assert.Throws<ArgumentNullException>(() => Wikidata.SearchUri(null!));
        Assert.Throws<ArgumentNullException>(() => Wikidata.EntitiesUri(null!));
        Assert.Throws<ArgumentNullException>(() => Wikidata.LabelsUri(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wikidata.EntitiesUri([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wikidata.LabelsUri([]));
        var many = Enumerable.Range(1, Wikidata.BatchLimit + 1).Select(n => "Q" + n).ToList();
        Assert.Throws<ArgumentOutOfRangeException>(() => Wikidata.EntitiesUri(many));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wikidata.LabelsUri(many));
        // Ids are spliced into a query, so anything but an item id is refused rather than escaped.
        Assert.Throws<ArgumentException>(() => Wikidata.LabelsUri(["Q1 } . ?x ?y ?z {"]));
        Assert.Throws<ArgumentException>(() => Wikidata.EntitiesUri(["P31"]));
        Assert.Throws<ArgumentException>(() => Wikidata.CommonsUrl(" ", 10));
    }

    [Fact]
    public void ItemNumbersAreReadOnlyFromItemIds()
    {
        Assert.Equal(83495, Wikidata.Number("Q83495"));
        Assert.Equal(0, Wikidata.Number("P31"));
        Assert.Equal(0, Wikidata.Number("Q"));
        Assert.Equal(0, Wikidata.Number("Q-5"));
        Assert.Equal(0, Wikidata.Number(""));
    }

    [Fact]
    public void ARecordedSearchBecomesHitsInItsOrder()
    {
        var hits = Wikidata.ParseSearch(WikimediaFixtures.MatrixSearch);

        Assert.Equal(["Q2715974", "Q83495", "Q13014087", "Q1210827"], hits.Select(hit => hit.Id));
        Assert.Equal("The Matrix", hits[1].Label);
        Assert.Equal("1999 American science fiction action thriller film", hits[1].Description);
    }

    [Fact]
    public void SearchAnswersOfAnotherShapeGiveNothing()
    {
        Assert.Empty(Wikidata.ParseSearch("[]"));
        Assert.Empty(Wikidata.ParseSearch("""{"search":{}}"""));
        Assert.Empty(Wikidata.ParseSearch("""{"error":{"code":"x"}}"""));
        var hits = Wikidata.ParseSearch("""{"search":[{"id":"Q1"},{"label":"No id"},"odd",{"id":"Q2","label":"Two"}]}""");
        Assert.Equal([new WikidataHit("Q2", "Two", null)], hits);
        Assert.ThrowsAny<JsonException>(() => Wikidata.ParseSearch("{"));
        Assert.Throws<ArgumentNullException>(() => Wikidata.ParseSearch(null!));
    }

    [Fact]
    public void ARecordedItemKeepsItsStatementsQualifiersAndArticle()
    {
        var matrix = Assert.Single(Wikidata.ParseEntities(WikimediaFixtures.MatrixEntity));

        Assert.Equal("Q83495", matrix.Id);
        Assert.Equal("The Matrix", matrix.Label);
        Assert.Equal("The Matrix", matrix.EnwikiTitle);
        Assert.Null(matrix.Description);
        Assert.Equal("Q11424", matrix.Statements("P31")[0].Value.Item);
        Assert.Equal(136m, matrix.Statements("P2047")[0].Value.Amount);
        Assert.Equal("Q7727", matrix.Statements("P2047")[0].Value.Unit);
        Assert.Equal("+1999-03-31T00:00:00Z", matrix.Statements("P577")[0].Value.Time);
        Assert.Equal("tt0133093", matrix.Statements("P345")[0].Value.Text);
        var fishburne = matrix.Statements("P161")[0];
        Assert.Equal("Q193048", fishburne.Value.Item);
        Assert.Equal("Q1750842", fishburne.Qualifier("P453")?.Item);
        Assert.Equal("3", fishburne.Qualifier("P1545")?.Text);
        Assert.Null(fishburne.Qualifier("P4633"));
        Assert.Empty(matrix.Statements("P9999"));
    }

    [Fact]
    public void PreferredStatementsComeFirstAndDeprecatedOnesAndEmptyValuesAreDropped()
    {
        const string Json = """
            {"entities":{
              "Q5":{"id":"Q5","labels":{"de":{"value":"Nur Deutsch"}},"descriptions":{"en":{"value":""}},"sitelinks":{"enwiki":{"title":""}},
                "claims":{
                  "P1":[
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":"normal"}},"rank":"normal"},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":"old"}},"rank":"deprecated"},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":"best"}},"rank":"preferred",
                     "qualifiers":{"P2":[{"snaktype":"novalue"},{"snaktype":"value","datavalue":{"value":"q"}}],"P3":[{"snaktype":"somevalue"}],"P4":{}}},
                    {"mainsnak":{"snaktype":"somevalue"},"rank":"normal"},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":""}}},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":5}}},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":{"other":1}}}},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":{"amount":"lots"}}}},
                    {"mainsnak":{"snaktype":"value","datavalue":{"value":{"amount":"-2.5"}}}},
                    {"mainsnak":{"snaktype":"value","datavalue":{}}},
                    {"mainsnak":{"snaktype":"value","datavalue":"flat"}},
                    {"mainsnak":"flat"},
                    {"rank":"normal"},
                    "odd"],
                  "P6":[{"mainsnak":{"snaktype":"novalue"}}],
                  "P7":{}}},
              "Q6":{"id":"Q6","labels":{"en":"flat"},"claims":[],"sitelinks":{"enwiki":"flat"}},
              "Q404":{"missing":""},
              "Q7":"odd"}}
            """;

        var items = Wikidata.ParseEntities(Json);

        Assert.Equal(["Q5", "Q6"], items.Select(item => item.Id));
        var five = items[0];
        Assert.Null(five.Label);
        Assert.Null(five.Description);
        Assert.Null(five.EnwikiTitle);
        Assert.Equal(["best", "normal", null], five.Statements("P1").Select(claim => claim.Value.Text));
        Assert.Equal(-2.5m, five.Statements("P1")[2].Value.Amount);
        Assert.Null(five.Statements("P1")[2].Value.Unit);
        Assert.Equal("q", five.Statements("P1")[0].Qualifier("P2")?.Text);
        Assert.Null(five.Statements("P1")[0].Qualifier("P3"));
        Assert.Empty(five.Statements("P6"));
        Assert.Empty(five.Statements("P7"));
        Assert.Null(items[1].Label);
        Assert.Null(items[1].EnwikiTitle);
        Assert.Empty(items[1].Claims);
    }

    [Fact]
    public void EntitiesAnswersOfAnotherShapeGiveNothing()
    {
        Assert.Empty(Wikidata.ParseEntities("[]"));
        Assert.Empty(Wikidata.ParseEntities("""{"entities":[]}"""));
        Assert.ThrowsAny<JsonException>(() => Wikidata.ParseEntities("{"));
        Assert.Throws<ArgumentNullException>(() => Wikidata.ParseEntities(null!));
    }

    [Fact]
    public void ARecordedLabelQueryGivesNamesAndOnePictureEachOverHttps()
    {
        var labels = Wikidata.ParseLabels(WikimediaFixtures.MatrixLabels);

        Assert.Equal("Laurence Fishburne", labels["Q193048"].Label);
        Assert.StartsWith("https://commons.wikimedia.org/wiki/Special:FilePath/Laurence%20Fishburne", labels["Q193048"].Image, StringComparison.Ordinal);
        Assert.Equal("Morpheus", labels["Q1750842"].Label);
        Assert.Null(labels["Q1750842"].Image);
        Assert.Equal("R", labels["Q18665344"].Label);
        // Two pictures came back as two rows; the first stands.
        Assert.Equal("https://commons.wikimedia.org/wiki/Special:FilePath/JoePantolianoFeb2009%20%28cropped%29.jpg", labels["Q296883"].Image);
        // Hugo Weaving came back labelled with his own id, which is no name to show.
        Assert.False(labels.ContainsKey("Q42204"));
    }

    [Fact]
    public void LabelAnswersOfAnotherShapeGiveNothingAndALaterPictureFillsAGap()
    {
        Assert.Empty(Wikidata.ParseLabels("[]"));
        Assert.Empty(Wikidata.ParseLabels("""{"results":[]}"""));
        Assert.Empty(Wikidata.ParseLabels("""{"results":{"bindings":{}}}"""));
        var labels = Wikidata.ParseLabels("""
            {"results":{"bindings":[
              {"item":{"value":"http://www.wikidata.org/entity/Q1"},"itemLabel":{"value":"One"}},
              {"item":{"value":"http://www.wikidata.org/entity/Q1"},"itemLabel":{"value":"One"},"image":{"value":"https://example.org/one.jpg"}},
              {"item":{"value":"http://www.wikidata.org/entity/Q2"},"itemLabel":{"value":""}},
              {"item":"flat","itemLabel":{"value":"Flat"}},
              {"itemLabel":{"value":"No item"}},
              {"item":{"value":"http://www.wikidata.org/entity/Q3"},"itemLabel":{"value":"Three"},"image":{"value":""}},
              "odd"]}}
            """);
        Assert.Equal(new WikidataLabel("One", "https://example.org/one.jpg"), labels["Q1"]);
        Assert.Equal(new WikidataLabel("Three", null), labels["Q3"]);
        Assert.Equal(2, labels.Count);
        Assert.ThrowsAny<JsonException>(() => Wikidata.ParseLabels("{"));
        Assert.Throws<ArgumentNullException>(() => Wikidata.ParseLabels(null!));
    }
}
