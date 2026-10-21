using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class TitleFactsTests
{
    private static LibraryEntry Film(string title = "Small Film", int? year = null, TimeSpan? duration = null, string? genre = null) => new()
    {
        Path = $@"D:\Movies\{title}.mkv",
        Kind = LibraryKind.Video,
        Title = title,
        Year = year,
        Duration = duration,
        Genre = genre,
    };

    private static EnrichedMovie Facts(int? year = null, int? runtime = null, string? rating = null, params string[] genres) =>
        new() { CatalogueId = 1, Title = "Small Film", Year = year, RuntimeMinutes = runtime, ContentRating = rating, Genres = genres };

    [Fact]
    public void AMovieWithoutFetchedFactsReadsItsOwnFile()
    {
        var film = Film(year: 2023, duration: TimeSpan.FromMinutes(112), genre: "Drama;Thriller");
        Assert.Equal("2023 · 1 h 52 min · Drama", TitleFacts.Movie(film, null));
        Assert.Equal(StreamingBrowse.MovieLine(film), TitleFacts.Movie(film, null));
    }

    [Fact]
    public void FetchedFactsWinAndTheFileFillsTheirGaps()
    {
        var film = Film(year: 2020, duration: TimeSpan.FromMinutes(45), genre: "Comedy");
        Assert.Equal("1999 · 2 h 16 min · R · Action · Science fiction · Thriller",
            TitleFacts.Movie(film, Facts(1999, 136, "R", "Action", "Science fiction", "Thriller", "Cyberpunk")));
        Assert.Equal("2020 · 45 min · Comedy", TitleFacts.Movie(film, Facts()));
        Assert.Equal("2020 · 45 min · Comedy", TitleFacts.Movie(film, Facts(runtime: 0, rating: " ")));
    }

    [Fact]
    public void AMovieKnownForNothingHasAnEmptyLine() =>
        Assert.Equal("", TitleFacts.Movie(Film("Untitled"), Facts()));

    [Fact]
    public void AShowsBillboardLineUsesItsFactsOrTheLibrarysOwn()
    {
        var show = new EnrichedShow { CatalogueId = 82, Name = "Game of Thrones", Premiered = "2011-04-17", RuntimeMinutes = 60, Rating = 8.9, Genres = ["Drama", "Adventure", "Fantasy", "War"] };
        Assert.Equal("2011 · 1 h 0 min · 8.9 / 10 · Drama · Adventure · Fantasy", TitleFacts.ShowLine(show, "3 seasons"));
        Assert.Equal("3 seasons", TitleFacts.ShowLine(null, "3 seasons"));
        Assert.Equal("3 seasons", TitleFacts.ShowLine(new EnrichedShow { CatalogueId = 0, Name = "Nothing" }, "3 seasons"));
        Assert.Equal("3 seasons", TitleFacts.ShowLine(new EnrichedShow { CatalogueId = 5, Name = "Bare", RuntimeMinutes = 0 }, "3 seasons"));
    }

    [Fact]
    public void AShowsFactsAreItsNetworkYearsAndStatus()
    {
        Assert.Equal("HBO · 2011-2019 · Ended", TitleFacts.Show(new EnrichedShow
        {
            CatalogueId = 82,
            Name = "Game of Thrones",
            Network = "HBO",
            Premiered = "2011-04-17",
            Ended = "2019-05-19",
            Status = "Ended",
        }));
        Assert.Equal("Apple TV+ · 2019 · Running", TitleFacts.Show(new EnrichedShow { CatalogueId = 1, Name = "See", Network = "Apple TV+", Premiered = "2019-11-01", Status = "Running" }));
        Assert.Equal("", TitleFacts.Show(new EnrichedShow { CatalogueId = 1, Name = "Bare" }));
        Assert.Equal("", TitleFacts.Show(new EnrichedShow { CatalogueId = 0, Name = "Unknown", Network = "HBO" }));
        Assert.Equal("", TitleFacts.Show(null));
    }

    [Theory]
    [InlineData("2011-04-17", "2019-05-19", "2011-2019")]
    [InlineData("2011-04-17", "2011-12-01", "2011")]
    [InlineData("2011-04-17", null, "2011")]
    [InlineData(null, "2019-05-19", null)]
    [InlineData("soon", null, null)]
    public void YearsSpanWhenTheShowBeganAndEnded(string? premiered, string? ended, string? expected) =>
        Assert.Equal(expected, TitleFacts.Years(premiered, ended));

    [Theory]
    [InlineData("2011-04-17", 2011)]
    [InlineData("1999", 1999)]
    [InlineData("99", null)]
    [InlineData("-123", null)]
    [InlineData(null, null)]
    public void AYearIsTheStartOfAnIsoDate(string? date, int? expected) => Assert.Equal(expected, TitleFacts.YearOf(date));

    [Fact]
    public void PeopleAreNamedOnceInPlainWords()
    {
        Assert.Equal("", TitleFacts.People("Directed by", []));
        Assert.Equal("", TitleFacts.People("Directed by", [" ", ""]));
        Assert.Equal("Directed by Ava DuVernay", TitleFacts.People("Directed by", ["Ava DuVernay"]));
        Assert.Equal("Directed by Lana Wachowski and Lilly Wachowski", TitleFacts.People("Directed by", ["Lana Wachowski", "Lilly Wachowski", "lana wachowski"]));
        Assert.Equal("Written by A, B and C", TitleFacts.People("Written by", [" A ", "B", "C"]));
    }

    [Fact]
    public void TheCrewLineHoldsDirectorsAndWritersWhicheverAreKnown()
    {
        Assert.Equal("", TitleFacts.Crew(null));
        Assert.Equal("", TitleFacts.Crew(Facts()));
        Assert.Equal("Directed by A · Written by B and C", TitleFacts.Crew(Facts() with { Directors = ["A"], Writers = ["B", "C"] }));
        Assert.Equal("Written by B", TitleFacts.Crew(Facts() with { Writers = ["B"] }));
    }

    [Fact]
    public void JoinLeavesOutWhatIsMissing() =>
        Assert.Equal("a · b", TitleFacts.Join(["a", null, " ", "", "b"]));

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => TitleFacts.Movie(null!, null));
        Assert.Throws<ArgumentNullException>(() => TitleFacts.ShowLine(null, null!));
        Assert.Throws<ArgumentNullException>(() => TitleFacts.People(null!, []));
        Assert.Throws<ArgumentNullException>(() => TitleFacts.People("x", null!));
        Assert.Throws<ArgumentNullException>(() => TitleFacts.Join(null!));
    }
}
