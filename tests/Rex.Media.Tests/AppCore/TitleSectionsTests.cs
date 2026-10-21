using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

public sealed class TitleSectionsTests
{
    private static EnrichedMovie Matrix => new()
    {
        CatalogueId = 83495,
        Title = "The Matrix",
        Year = 1999,
        Overview = "  A hacker learns the truth.  ",
        ArticleUrl = "https://en.wikipedia.org/wiki/The_Matrix",
        Directors = ["Lana Wachowski", "Lilly Wachowski"],
        Cast = [new CastMember("Keanu Reeves", "Neo", null), new CastMember("Carrie-Anne Moss", "Trinity", "https://upload.wikimedia.org/moss.jpg")],
        TrailerKeys = ["", "vKQi3bBA1y8"],
        BackdropUrl = "https://upload.wikimedia.org/matrix.jpg",
        Collection = "The Matrix",
    };

    [Fact]
    public void AMovieWithEverythingShowsEverySection()
    {
        var sections = TitleSections.Movie(Matrix, alike: 3, seriesMates: 2);
        Assert.Equal(new Description("A hacker learns the truth.", DescriptionSource.Wikipedia, new Uri("https://en.wikipedia.org/wiki/The_Matrix")), sections.Synopsis);
        Assert.Equal("Directed by Lana Wachowski and Lilly Wachowski", sections.Crew);
        Assert.Equal(["Keanu Reeves", "Carrie-Anne Moss"], sections.Cast.Select(member => member.Name));
        Assert.Equal(new Uri("https://www.youtube.com/watch?v=vKQi3bBA1y8"), sections.Trailer);
        Assert.Equal(new Uri("https://upload.wikimedia.org/matrix.jpg"), sections.Backdrop);
        Assert.True(sections.MoreLikeThis);
        Assert.Equal("Part of The Matrix series", sections.SeriesHeading);
    }

    [Fact]
    public void AMovieWithNothingFetchedHidesEverySection()
    {
        var sections = TitleSections.Movie(null, alike: 0, seriesMates: 0);
        Assert.Null(sections.Synopsis);
        Assert.Equal("", sections.Crew);
        Assert.Empty(sections.Cast);
        Assert.Null(sections.Trailer);
        Assert.Null(sections.Backdrop);
        Assert.False(sections.MoreLikeThis);
        Assert.Null(sections.SeriesHeading);
    }

    [Fact]
    public void ASeriesIsShownOnlyWhenTheLibraryHoldsMoreOfIt()
    {
        Assert.Null(TitleSections.Movie(Matrix, 0, seriesMates: 0).SeriesHeading);
        Assert.Null(TitleSections.Movie(Matrix with { Collection = " " }, 0, seriesMates: 2).SeriesHeading);
        Assert.Null(TitleSections.Movie(Matrix with { Collection = null }, 0, seriesMates: 2).SeriesHeading);
    }

    [Fact]
    public void AWikipediaSynopsisNeedsItsArticleToBeCredited()
    {
        Assert.Null(TitleSections.MovieSynopsis(Matrix with { ArticleUrl = null }));
        Assert.Null(TitleSections.MovieSynopsis(Matrix with { ArticleUrl = "not a page" }));
        Assert.Null(TitleSections.MovieSynopsis(Matrix with { ArticleUrl = "file:///C:/matrix.html" }));
        Assert.Null(TitleSections.MovieSynopsis(Matrix with { Overview = " " }));
        Assert.Null(TitleSections.MovieSynopsis(Matrix with { Overview = null }));
        Assert.Equal(new Uri("http://en.wikipedia.org/wiki/X"), TitleSections.MovieSynopsis(Matrix with { ArticleUrl = "http://en.wikipedia.org/wiki/X" })!.Link);
    }

    [Fact]
    public void AShowsSynopsisIsCreditedToWhereItCameFrom()
    {
        var tvmaze = new EnrichedShow { CatalogueId = 82, Name = "Game of Thrones", Summary = " Seven noble families. " };
        Assert.Equal(new Description("Seven noble families.", DescriptionSource.TvMaze, new Uri("https://www.tvmaze.com/shows/82")), TitleSections.ShowSynopsis(tvmaze));
        Assert.True(TitleSections.FromTvMaze(tvmaze));

        var wikipedia = tvmaze with { CatalogueId = 1079, WikidataId = "Q1079", ArticleUrl = "https://en.wikipedia.org/wiki/Breaking_Bad" };
        Assert.Equal(DescriptionSource.Wikipedia, TitleSections.ShowSynopsis(wikipedia)!.Source);
        Assert.False(TitleSections.FromTvMaze(wikipedia));

        Assert.Null(TitleSections.ShowSynopsis(wikipedia with { ArticleUrl = null }));
        Assert.Null(TitleSections.ShowSynopsis(tvmaze with { Summary = "  " }));
        Assert.Null(TitleSections.ShowSynopsis(tvmaze with { Summary = null }));
        Assert.Null(TitleSections.ShowSynopsis(tvmaze with { CatalogueId = 0 }));
        Assert.Null(TitleSections.ShowSynopsis(null));
        Assert.False(TitleSections.FromTvMaze(null));
        Assert.False(TitleSections.FromTvMaze(tvmaze with { CatalogueId = 0 }));
    }

    [Fact]
    public void ASeasonsOverviewAndAnEpisodesSynopsisComeFromTheRecord()
    {
        var show = new EnrichedShow
        {
            CatalogueId = 82,
            Name = "Game of Thrones",
            Seasons = [new SeasonDetails(1, " Winter is coming. ", null), new SeasonDetails(2, " ", null)],
            Episodes = [new EnrichedEpisode(1, 1, "Winter Is Coming", " Ned is asked. "), new EnrichedEpisode(1, 2, "The Kingsroad", "")],
        };
        Assert.Equal("Winter is coming.", TitleSections.SeasonOverview(show, 1));
        Assert.Null(TitleSections.SeasonOverview(show, 2));
        Assert.Null(TitleSections.SeasonOverview(show, 3));
        Assert.Null(TitleSections.SeasonOverview(show, null));
        Assert.Null(TitleSections.SeasonOverview(null, 1));

        Assert.Equal("Ned is asked.", TitleSections.EpisodeSynopsis(show, new VideoEpisode("Game of Thrones", 1, 1)));
        Assert.Null(TitleSections.EpisodeSynopsis(show, new VideoEpisode("Game of Thrones", 1, 2)));
        Assert.Null(TitleSections.EpisodeSynopsis(show, new VideoEpisode("Game of Thrones", 1, null)));
        Assert.Null(TitleSections.EpisodeSynopsis(show, null));
        Assert.Null(TitleSections.EpisodeSynopsis(null, new VideoEpisode("Game of Thrones", 1, 1)));
    }

    [Fact]
    public void ACastRowNamesEachPersonOnceInBillingOrder()
    {
        var many = Enumerable.Range(1, 30).Select(index => new CastMember("Actor " + index, null, null));
        var row = TitleSections.CastRow([new CastMember(" ", "Nobody", null), new CastMember("Keanu Reeves", "Neo", null), new CastMember("keanu reeves ", "Thomas Anderson", null), .. many]);
        Assert.Equal(TitleSections.CastLimit, row.Count);
        Assert.Equal("Neo", row[0].Character);
        Assert.Equal("Actor 1", row[1].Name);
    }

    [Fact]
    public void ATrailerIsTheFirstKeyGiven()
    {
        Assert.Null(TitleSections.Trailer(null));
        Assert.Null(TitleSections.Trailer(Matrix with { TrailerKeys = [" "] }));
        Assert.Equal("https://www.youtube.com/watch?v=a%20b", TitleSections.Trailer(Matrix with { TrailerKeys = [" a b "] })!.AbsoluteUri);
    }

    [Theory]
    [InlineData("The Matrix film series", "Part of The Matrix film series")]
    [InlineData("The Matrix", "Part of The Matrix series")]
    [InlineData(" Toy Story ", "Part of the Toy Story series")]
    public void ASeriesHeadingReadsNaturally(string series, string heading) => Assert.Equal(heading, TitleSections.SeriesHeading(series));

    [Fact]
    public void SeriesMatesAreTheOtherFilmsOfTheSeriesOldestFirst()
    {
        var reloaded = Matrix with { CatalogueId = 2, Title = "The Matrix Reloaded", Year = 2003 };
        var resurrections = Matrix with { CatalogueId = 3, Title = "The Matrix Resurrections", Year = 2021, Collection = "the matrix " };
        var undated = Matrix with { CatalogueId = 4, Title = "Undated", Year = null };
        var library = new List<(string, EnrichedMovie)>
        {
            ("Matrix", Matrix),
            ("Resurrections", resurrections),
            ("Unknown", new EnrichedMovie { CatalogueId = 0, Title = "x", Collection = "The Matrix" }),
            ("Undated", undated),
            ("Reloaded", reloaded),
            ("Reloaded copy", reloaded),
            ("Other", Matrix with { CatalogueId = 9, Collection = "Toy Story" }),
        };
        Assert.Equal(["Reloaded", "Resurrections", "Undated"], TitleSections.SeriesMates(Matrix, library));
        Assert.Empty(TitleSections.SeriesMates(Matrix with { Collection = null }, library));
    }

    [Fact]
    public void ACastMembersViewHoldsEveryTitleTheyAppearIn()
    {
        var titles = new List<(string, IReadOnlyList<CastMember>)>
        {
            ("The Matrix", [new CastMember("Keanu Reeves", "Neo", null)]),
            ("Speed", [new CastMember(" keanu reeves", "Jack", null), new CastMember("Sandra Bullock", "Annie", null)]),
            ("Gravity", [new CastMember("Sandra Bullock", "Ryan", null)]),
        };
        Assert.Equal(["The Matrix", "Speed"], TitleSections.AppearsIn("Keanu Reeves ", titles));
        Assert.Empty(TitleSections.AppearsIn("Nobody", titles));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => TitleSections.CastRow(null!));
        Assert.Throws<ArgumentNullException>(() => TitleSections.SeriesHeading(null!));
        Assert.Throws<ArgumentException>(() => TitleSections.SeriesHeading(" "));
        Assert.Throws<ArgumentNullException>(() => TitleSections.SeriesMates<string>(null!, []));
        Assert.Throws<ArgumentNullException>(() => TitleSections.SeriesMates<string>(Matrix, null!));
        Assert.Throws<ArgumentException>(() => TitleSections.AppearsIn<string>(" ", []));
        Assert.Throws<ArgumentNullException>(() => TitleSections.AppearsIn<string>("x", null!));
        Assert.Null(TitleSections.AbsoluteUri(null));
    }
}
