using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The details screens (LIB-05) over metadata records seeded in the cache before the window starts,
/// so nothing is fetched: the billboard's synopsis, a movie's page with its cast, crew, series and
/// credit, a cast member's own view, Back to where the user was, the show page's header, and the
/// clean pages of a movie with no metadata and of a library with metadata turned off.
/// </summary>
[Collection("desktop")]
public sealed class LibraryDetailsTests : IDisposable
{
    private const string MatrixOverview = "A hacker learns that the world he lives in is a simulation and joins a rebellion against its makers.";
    private const string ShowSummary = "A harbour town's lifeboat crew keep watch over a stretch of dangerous coast.";

    private static readonly CastMember Keanu = new("Keanu Reeves", "Neo", null);

    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private static Func<PlayerSettings, PlayerSettings> MetadataOn =>
        settings => settings with { Metadata = new MetadataSettings { Movies = true, TvShows = true } };

    /// <summary>A movie file whose details say its title and year, as a tagged file would.</summary>
    private string Movie(string title, int year)
    {
        var name = title.Replace(' ', '.') + "." + year.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".mp4";
        return _scene.Video(Path.Combine("Movies", name), info: Details(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.Title] = title,
            [MetadataKeys.Date] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, TimeSpan.FromHours(2)));
    }

    private static MediaInfo Details(Dictionary<string, string> tags, TimeSpan? running = null) => new()
    {
        FormatName = "MP4",
        Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }],
        Duration = MediaTime.FromSeconds((running ?? TimeSpan.FromMinutes(1)).TotalSeconds),
        Metadata = tags,
    };

    private MetadataCache Cache => new(Path.Combine(_scene.Root, "metadata"));

    private void Seed(EnrichedMovie movie) =>
        Cache.WriteText(MetadataFetcher.MovieKind, MetadataMatch.MovieKey(movie.Title, movie.Year), (movie with { Schema = EnrichedMovie.CurrentSchema }).ToJson());

    private static EnrichedMovie Facts(int id, string title, int year, params CastMember[] cast) => new()
    {
        CatalogueId = id,
        Title = title,
        Year = year,
        Cast = cast,
        Genres = ["Action"],
    };

    /// <summary>Four films: The Matrix (watched most, so featured), its sequel, another of its star's and one without him.</summary>
    private string SeedMovies()
    {
        var matrix = Movie("The Matrix", 1999);
        Movie("The Matrix Reloaded", 2003);
        Movie("Speed", 1994);
        Movie("Gravity", 2013);
        Seed(Facts(83495, "The Matrix", 1999, Keanu, new CastMember("Carrie-Anne Moss", "Trinity", null)) with
        {
            Overview = MatrixOverview,
            ArticleUrl = "https://en.wikipedia.org/wiki/The_Matrix",
            RuntimeMinutes = 136,
            ContentRating = "R",
            Genres = ["Action", "Science fiction"],
            Directors = ["Lana Wachowski", "Lilly Wachowski"],
            Writers = ["Lana Wachowski", "Lilly Wachowski"],
            TrailerKeys = ["vKQi3bBA1y8"],
            Collection = "The Matrix",
        });
        Seed(Facts(207772, "The Matrix Reloaded", 2003, Keanu) with { Collection = "The Matrix" });
        Seed(Facts(11424, "Speed", 1994, Keanu with { Character = "Jack Traven" }));
        Seed(Facts(902, "Gravity", 2013, new CastMember("Sandra Bullock", "Ryan Stone", null)) with { Genres = ["Drama"] });
        return matrix;
    }

    /// <summary>The cast card of <paramref name="name"/> once it has a place on the screen, found afresh each time.</summary>
    private static System.Windows.Automation.AutomationElement OnScreen(AppProcess app, string name) =>
        Wait.Until(() => app.Line(name, "LibraryCastRow") is var card && !card.Current.BoundingRectangle.IsEmpty ? card : null);

    /// <summary>Presses the info button on the card named <paramref name="title"/>.</summary>
    private static void PressInfo(AppProcess app, string title)
    {
        var info = Wait.Until(() => app.Line(title).FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "LibraryCardInfo")));
        ((System.Windows.Automation.InvokePattern)info.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
    }

    [Fact]
    [Capability("LIB-05")]
    public void MoreInfoOpensTheMoviesPageAndACastMemberShowsEverythingTheyAreIn()
    {
        var matrix = SeedMovies();
        using var app = _scene.Ready(MetadataOn, played: matrix).Start();
        app.Visit("Movies");

        // The billboard: the synopsis under the title, the facts line, its credit, Play and More info.
        Wait.For(() => app.IsShown("LibraryBillboardSynopsis") && app.Text("LibraryBillboardSynopsis") == MatrixOverview, "the billboard's synopsis");
        Assert.Equal("The Matrix", app.Text("LibraryBillboardTitle"));
        Assert.Equal("1999 · 2 h 16 min · R · Action · Science fiction", app.Text("LibraryBillboardLine"));
        Assert.Equal("Description from Wikipedia", app.Text("LibraryBillboardCredit"));

        // More info: the movie's page, with its makers, synopsis and credit, cast, series and More like this.
        app.Press("LibraryBillboardMore");
        Wait.For(() => app.IsShown("LibraryKicker") && app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "The Matrix", "the movie's page");
        Assert.Equal("1999 · 2 h 16 min · R · Action · Science fiction", app.Text("LibrarySubtitle"));
        Assert.Equal("Directed by Lana Wachowski and Lilly Wachowski · Written by Lana Wachowski and Lilly Wachowski", app.Text("LibraryFacts"));
        Assert.Equal(MatrixOverview, app.Text("LibraryOverview"));
        Assert.Equal("Description from Wikipedia", app.Text("LibraryCredit"));
        Wait.For(() => app.IsShown("LibraryTrailer"), "Watch trailer on a movie with a trailer");
        Assert.True(app.IsShown("LibraryBack"), "The page is left with Back.");
        app.Line("Keanu Reeves", "LibraryCastRow");
        app.Line("Carrie-Anne Moss", "LibraryCastRow");
        Assert.Equal("Part of The Matrix series", app.Find("LibraryShelfCards-Details-Series").Current.Name);
        app.Line("The Matrix Reloaded", "LibraryShelfCards-Details-Series");
        app.Line("Speed", "LibraryShelfCards-Details-MoreLikeThis");

        // Rich metadata must grow down from the command bar instead of pushing Back and the
        // title underneath it. Scrolling then keeps a smaller, useful header pinned above the
        // shelves, and returning to the top restores the full context.
        var expandedHeader = app.Find("LibraryHero").Current.BoundingRectangle;
        var back = app.Find("LibraryBack").Current.BoundingRectangle;
        Assert.True(back.Top >= app.Find("Menu").Current.BoundingRectangle.Bottom, "Back is wholly below the command bar.");
        Assert.True(back.Bottom <= expandedHeader.Bottom, "Back is wholly inside the expanded header.");
        var detailsScroll = (ScrollPattern)app.Find("LibraryHome").GetCurrentPattern(ScrollPattern.Pattern);
        Wait.For(() => detailsScroll.Current.VerticallyScrollable, "the details shelves to be scrollable");
        detailsScroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        Wait.For(() => app.Find("LibraryHero").Current.Name == "Compact library header", "the header to compact after scrolling");
        var compactHeader = app.Find("LibraryHero").Current.BoundingRectangle;
        Assert.True(compactHeader.Height < expandedHeader.Height, "The sticky header uses less vertical space.");
        Assert.True(app.IsShown("LibraryBack"), "Back remains available in the compact header.");
        detailsScroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
        Wait.For(() => app.Find("LibraryHero").Current.Name == "Expanded library header", "the header to expand at the top");
        Screenshots.Match(app, "library-movie-details", "LibraryPane");

        // A cast member: everything in the library they appear in, as a view titled with their name.
        Pointer.Click(app, OnScreen(app, "Keanu Reeves"));
        Wait.For(() => app.Text("LibraryKicker") == "CAST" && app.Text("LibraryTitle") == "Keanu Reeves", "Keanu Reeves's view");
        Wait.For(() => app.Lines("LibraryShelfCards-Person-Movies").Order().SequenceEqual(["Speed", "The Matrix", "The Matrix Reloaded"]),
            "his three films and not Gravity", () => string.Join(", ", app.Lines("LibraryShelfCards-Person-Movies")));
        Assert.Equal("3 titles", app.Text("LibrarySubtitle"));
        Assert.False(app.IsShown("LibraryOverview"), "A person's view has no synopsis.");

        // Back leaves the pages one at a time, then finds the billboard where it was.
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "The Matrix", "Back to the movie's page");
        app.Press("LibraryBack");
        Wait.For(() => app.IsShown("LibraryBillboard") && !app.IsShown("LibraryHero"), "Back to Browse");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheInfoButtonOpensAMoviesPageBackFindsTheCardChosenAndAMovieWithNoMetadataShowsACleanPage()
    {
        SeedMovies();
        Movie("Home Video", 2020);
        Seed(new EnrichedMovie { CatalogueId = 0, Title = "Home Video", Year = 2020 });
        using var app = _scene.Ready(MetadataOn).Start();
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Lines().Contains("Gravity"), "the movie cards");

        // A card's info button opens its page; Back returns to the grid with the card chosen.
        PressInfo(app, "Gravity");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "Gravity", "Gravity's page");
        app.Line("Sandra Bullock", "LibraryCastRow");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "LIBRARY" && app.Text("LibraryTitle") == "Movies", "Back to the movies");
        Wait.For(() => app.IsChosen("Gravity"), "the card that was opened to be chosen");

        // A movie the catalogue did not know: its own facts and nothing empty.
        PressInfo(app, "Home Video");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "Home Video", "the page of a movie with no metadata");
        Assert.StartsWith("2020", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        foreach (var hidden in new[] { "LibraryOverview", "LibraryCredit", "LibraryFacts", "LibraryTrailer", "LibraryCastRow", "LibraryShelfCards-Details-MoreLikeThis" })
        {
            Assert.False(app.IsShown(hidden), hidden + " shows nothing, so it is hidden.");
        }

        // A collapsed control is not in the tree, so finding Play proves the page still plays the movie.
        Assert.NotNull(app.Find("LibraryPlay"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void OpeningAMovieCardGoesToItsPageAndBackWithoutStartingPlayback()
    {
        SeedMovies();
        using var app = _scene.Ready(MetadataOn).Start();
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Lines().Contains("Gravity"), "the movie grid");

        app.Open("Gravity");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "Gravity",
            "the dedicated movie page after opening its card");
        Assert.True(app.IsShown("LibraryPlay"), "the movie has an explicit Play control");
        Assert.True(app.IsShown("LibraryBack"), "the movie has a way back");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "Movies", "back to the grid");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void WithMetadataTurnedOffTheBillboardAndAMoviesPageUseTheFileAlone()
    {
        var matrix = SeedMovies();
        using var app = _scene.Ready(played: matrix).Start();
        app.Visit("Movies");
        Wait.For(() => app.IsShown("LibraryBillboard") && app.Text("LibraryBillboardTitle") == "The Matrix", "the billboard");
        Assert.False(app.IsShown("LibraryBillboardSynopsis"), "With metadata off the billboard has no synopsis.");
        Assert.Equal("1999 · 2 h 0 min", app.Text("LibraryBillboardLine"));

        app.Press("LibraryBillboardMore");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE" && app.Text("LibraryTitle") == "The Matrix", "the movie's page");
        Assert.Equal("1999 · 2 h 0 min", app.Text("LibrarySubtitle"));
        foreach (var hidden in new[] { "LibraryOverview", "LibraryCredit", "LibraryFacts", "LibraryTrailer", "LibraryCastRow" })
        {
            Assert.False(app.IsShown(hidden), hidden + " is hidden with metadata off.");
        }

        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheShowPageShowsTheShowsSynopsisFactsCastSeasonAndEpisodeSynopses()
    {
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Harbour.Lights.S01E01.mp4"), info: Details(new()));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Harbour.Lights.S01E02.mp4"), info: Details(new()));
        Cache.WriteText(MetadataFetcher.ShowKind, MetadataMatch.Normalize("Harbour Lights"), new EnrichedShow
        {
            CatalogueId = 4242,
            Name = "Harbour Lights",
            Summary = ShowSummary,
            Network = "BBC One",
            Premiered = "2019-03-01",
            Ended = "2021-05-01",
            Status = "Ended",
            Genres = ["Drama"],
            Cast = [new CastMember("Nick Berry", "Tom Fletcher", null), new CastMember("Tina Hobley", "Melanie Rush", null)],
            Seasons = [new SeasonDetails(1, "The crew's first winter on the coast.", null)],
            Episodes = [new EnrichedEpisode(1, 1, "Arrival", "A new harbour master arrives in town and the crew meet him."), new EnrichedEpisode(1, 2, "Storm", null)],
            Schema = EnrichedShow.CurrentSchema,
        }.ToJson());
        using var app = _scene.Ready(MetadataOn).Start();
        app.Visit("TV Shows");
        Wait.For(() => app.IsShown("LibraryBillboardSynopsis") && app.Text("LibraryBillboardSynopsis") == ShowSummary, "the show's synopsis on the billboard");
        Assert.False(app.IsShown("LibraryBillboardCredit"), "TVmaze's synopsis carries no Wikipedia credit.");

        app.Press("LibraryBillboardMore");
        Wait.For(() => app.Text("LibraryKicker") == "TV SHOW" && app.Text("LibraryTitle") == "Harbour Lights", "the show page");
        Wait.For(() => app.IsShown("LibraryFacts") && app.Text("LibraryFacts") == "BBC One · 2019-2021 · Ended", "the show's facts");
        Assert.Equal(ShowSummary, app.Text("LibraryOverview"));
        Assert.Equal("Data from TVmaze", app.Text("LibraryCredit"));
        Assert.Equal("The crew's first winter on the coast.", app.Text("LibrarySeasonOverview"));
        app.Line("Nick Berry", "LibraryCastRow");
        // The episodes sit under the season's overview and the cast, so the first may be below the fold.
        Assert.Equal("A new harbour master arrives in town and the crew meet him.", app.Text("LibraryEpisodeSummary"));
        Screenshots.Match(app, "library-show-header", "LibraryPane");

        // A cast member of the show finds it in their own view, and Back returns to the show.
        Pointer.Click(app, OnScreen(app, "Nick Berry"));
        Wait.For(() => app.Text("LibraryKicker") == "CAST" && app.Text("LibraryTitle") == "Nick Berry", "Nick Berry's view");
        app.Line("Harbour Lights", "LibraryShelfCards-Person-TvShows");
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "TV SHOW" && app.Text("LibraryTitle") == "Harbour Lights", "Back to the show page");
        Assert.Equal(0, app.Close());
    }
}
