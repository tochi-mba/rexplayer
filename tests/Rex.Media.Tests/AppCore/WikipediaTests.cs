using System.Text.Json;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>Reading Wikipedia's page summaries, recorded from the real service, and choosing which picture is a poster and which a backdrop.</summary>
public sealed class WikipediaTests
{
    [Fact]
    public void TheSummaryAddressSpellsTheTitleAsAnArticlePath()
    {
        Assert.Equal("https://en.wikipedia.org/api/rest_v1/page/summary/Am%C3%A9lie_Poulain", Wikipedia.SummaryUri("Amélie Poulain").AbsoluteUri);
        Assert.Equal("https://en.wikipedia.org/api/rest_v1/page/summary/AC%2FDC", Wikipedia.SummaryUri("AC/DC").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => Wikipedia.SummaryUri(" "));
        Assert.Throws<ArgumentNullException>(() => Wikipedia.SummaryUri(null!));
    }

    [Fact]
    public void ARecordedSummaryGivesTheSynopsisTheArticleAndItsPictures()
    {
        var summary = Wikipedia.ParseSummary(WikimediaFixtures.MatrixSummary);

        Assert.NotNull(summary);
        Assert.Equal("The Matrix", summary.Title);
        Assert.StartsWith("The Matrix is a 1999 science fiction", summary.Extract, StringComparison.Ordinal);
        Assert.Equal("https://en.wikipedia.org/wiki/The_Matrix", summary.PageUrl);
        Assert.Equal(260, summary.Original?.Width);
        Assert.Equal(376, summary.Original?.Height);
        Assert.False(summary.Original?.Landscape);
        Assert.NotNull(summary.Thumbnail);
    }

    [Fact]
    public void SummariesOfAnotherKindOrShapeGiveNothingOrLess()
    {
        Assert.Null(Wikipedia.ParseSummary("""{"type":"disambiguation","title":"Heat"}"""));
        Assert.Null(Wikipedia.ParseSummary("""{"type":"standard"}"""));
        Assert.Null(Wikipedia.ParseSummary("[]"));
        var bare = Wikipedia.ParseSummary("""
            {"type":"standard","title":"Bare","extract":"  ","content_urls":{"desktop":{"page":""}},
             "thumbnail":{"source":"https://example.org/t.jpg","width":"wide","height":10},
             "originalimage":{"source":"","width":10,"height":10}}
            """);
        Assert.Equal(new ArticleSummary("Bare", null, null, null, null), bare);
        Assert.Null(Wikipedia.ParseSummary("""{"title":"Flat","content_urls":"flat","thumbnail":"flat"}""")?.PageUrl);
        Assert.Null(Wikipedia.ParseSummary("""{"title":"Flat","content_urls":{"desktop":"flat"},"originalimage":{"source":"x","width":1}}""")?.Original);
        Assert.ThrowsAny<JsonException>(() => Wikipedia.ParseSummary("{"));
        Assert.Throws<ArgumentNullException>(() => Wikipedia.ParseSummary(null!));
    }

    [Fact]
    public void APosterShapedPictureIsThePosterAndTheItemsOwnPictureTheBackdrop()
    {
        var summary = Wikipedia.ParseSummary(WikimediaFixtures.MatrixSummary);

        var art = Wikipedia.ChooseArt(summary, "https://commons.example/still.jpg");

        Assert.Equal(summary!.Original!.Url, art.PosterUrl);
        Assert.Equal("https://commons.example/still.jpg", art.BackdropUrl);
    }

    [Fact]
    public void AWidePictureIsTheBackdropAndNoPoster()
    {
        var summary = Wikipedia.ParseSummary(WikimediaFixtures.FawltySummary);

        var art = Wikipedia.ChooseArt(summary, "https://commons.example/other.jpg");

        Assert.Null(art.PosterUrl);
        Assert.Equal(summary!.Original!.Url, art.BackdropUrl);
    }

    [Fact]
    public void AHugeOriginalGivesWayToTheThumbnailAndMissingPicturesGiveWhatIsLeft()
    {
        var thumb = new ArticleImage("https://example.org/thumb.jpg", 320, 480);
        var huge = new ArticleImage("https://example.org/huge.jpg", 4000, 6000);

        Assert.Equal(thumb.Url, Wikipedia.ChooseArt(new ArticleSummary("A", null, null, thumb, huge), null).PosterUrl);
        Assert.Equal(huge.Url, Wikipedia.ChooseArt(new ArticleSummary("A", null, null, null, huge), null).PosterUrl);
        Assert.Equal(thumb.Url, Wikipedia.ChooseArt(new ArticleSummary("A", null, null, thumb, null), null).PosterUrl);
        Assert.Equal(new ArticleArt(null, "https://commons.example/x.jpg"), Wikipedia.ChooseArt(new ArticleSummary("A", null, null, null, null), "https://commons.example/x.jpg"));
        Assert.Equal(new ArticleArt(null, null), Wikipedia.ChooseArt(null, null));
        Assert.True(new ArticleImage("w", 130, 100).Landscape);
        Assert.False(new ArticleImage("s", 110, 100).Landscape);
    }
}
