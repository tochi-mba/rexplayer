using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Which picture a show, an episode and a movie show, best first (LIB-05).</summary>
public sealed class ArtworkChoiceTests
{
    private static readonly string Show = Path.Combine("media", "Harbour Lights");
    private static readonly string Season = Path.Combine(Show, "Season 1");
    private static readonly string Pilot = Path.Combine(Season, "Harbour.Lights.S01E01.mkv");
    private static readonly string Online = Path.Combine("cache", "art", "harbour-lights.img");

    private static Func<string, bool> Files(params string[] files)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    [Capability("LIB-05")]
    public void AShowsPosterIsItsFolderArtThenTheCataloguesThenSeasonArtThenAFrame()
    {
        var folderArt = Path.Combine(Show, "poster.jpg");
        var seasonFolderArt = Path.Combine(Season, "folder.png");
        var seasonArt = Path.Combine(Show, "season01-poster.jpg");
        var all = Files(folderArt, seasonFolderArt, seasonArt);

        Assert.Equal(new ArtworkSource(ArtworkSourceKind.FolderArt, folderArt), ArtworkChoice.ShowPoster(Pilot, 1, Online, all));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.FolderArt, seasonFolderArt), ArtworkChoice.ShowPoster(Pilot, 1, Online, Files(seasonFolderArt, seasonArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.OnlinePoster, Online), ArtworkChoice.ShowPoster(Pilot, 1, Online, Files(seasonArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.SeasonArt, seasonArt), ArtworkChoice.ShowPoster(Pilot, 1, null, Files(seasonArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null), ArtworkChoice.ShowPoster(Pilot, 1, null, Files()));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("poster.jpg")]
    [InlineData("poster.webp")]
    [InlineData("folder.jpeg")]
    [InlineData("show.png")]
    [InlineData("cover.jpg")]
    [InlineData("default.jpg")]
    public void EveryUsualNameForAShowsArtIsFound(string name)
    {
        var art = Path.Combine(Show, name);
        Assert.Equal(art, ArtworkChoice.ShowPoster(Pilot, 1, null, Files(art)).Path);
        Assert.True(ArtworkChoice.IsFolderArtName(art));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("POSTER.JPG", true)]
    [InlineData("Season02-Poster.webp", true)]
    [InlineData("season12.png", true)]
    [InlineData("season00-poster.jpg", true)]
    [InlineData("season-specials.jpg", true)]
    [InlineData("poster.gif", false)]
    [InlineData("poster.mkv", false)]
    [InlineData("front.jpg", false)]
    [InlineData("posters.jpg", false)]
    [InlineData("season two.jpg", false)]
    [InlineData("season 1234.jpg", false)]
    [InlineData("Harbour.Lights.S01E01.jpg", false)]
    [InlineData("Lagos.jpg", false)]
    public void FolderArtIsKnownByItsNameAlone(string name, bool art) =>
        Assert.Equal(art, ArtworkChoice.IsFolderArtName(Path.Combine(Show, name)));

    [Theory]
    [Capability("LIB-05")]
    [InlineData(2, "season02-poster.jpg")]
    [InlineData(2, "season02.png")]
    [InlineData(2, "season 2.jpg")]
    [InlineData(0, "season-specials-poster.jpg")]
    [InlineData(0, "specials.jpg")]
    public void ASeasonsOwnArtStandsInForTheShowsPoster(int season, string name)
    {
        var art = Path.Combine(Show, name);
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.SeasonArt, art), ArtworkChoice.ShowPoster(Pilot, season, null, Files(art)));
        Assert.True(ArtworkChoice.IsFolderArtName(art));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("Season 3")]
    [InlineData("S03")]
    [InlineData("Harbour Lights S03 (360p re-rip)")]
    [InlineData("Series 3")]
    [InlineData("Specials")]
    public void ASeasonFolderSitsInTheShowsFolder(string season)
    {
        var folder = Path.Combine(Show, season);
        Assert.Equal((Show, folder), ArtworkChoice.Folders(Path.Combine(folder, "Episode.mkv")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFolderThatHoldsALibraryIsNoShowsFolder()
    {
        // Episodes straight in a show's folder: that folder is the show's.
        Assert.Equal((Show, (string?)null), ArtworkChoice.Folders(Path.Combine(Show, "Harbour.Lights.S01E01.mkv")));

        // Episodes loose in Movies, or a season folder straight inside TV Shows: the library's own art is not the show's.
        Assert.Equal(((string?)null, (string?)null), ArtworkChoice.Folders(Path.Combine("media", "Movies", "Harbour.Lights.S01E01.mkv")));
        var season = Path.Combine("media", "TV Shows", "Season 1");
        Assert.Equal(((string?)null, season), ArtworkChoice.Folders(Path.Combine(season, "Harbour.Lights.S01E01.mkv")));
        Assert.Equal(((string?)null, (string?)null), ArtworkChoice.Folders("Harbour.Lights.S01E01.mkv"));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null),
            ArtworkChoice.ShowPoster(Path.Combine("media", "Movies", "Harbour.Lights.S01E01.mkv"), 1, null, Files(Path.Combine("media", "Movies", "poster.jpg"))));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnEpisodeShowsItsOwnArtThenTheCataloguesStillThenAFrameNeverTheShowsPoster()
    {
        var thumb = Path.Combine(Season, "Harbour.Lights.S01E01-thumb.jpg");
        var named = Path.Combine(Season, "Harbour.Lights.S01E01.png");
        var still = Path.Combine("cache", "still", "harbour-lights-s01e01.img");
        var poster = Path.Combine(Show, "poster.jpg");

        Assert.Equal(new ArtworkSource(ArtworkSourceKind.EpisodeArt, named), ArtworkChoice.EpisodePicture(Pilot, still, Files(named, thumb, poster)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.EpisodeArt, thumb), ArtworkChoice.EpisodePicture(Pilot, still, Files(thumb, poster)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.EpisodeStill, still), ArtworkChoice.EpisodePicture(Pilot, still, Files(poster)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null), ArtworkChoice.EpisodePicture(Pilot, null, Files(poster)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMovieShowsArtBesideItThenItsFoldersThenTheCataloguesPosterThenAFrame()
    {
        var folder = Path.Combine("media", "Movies", "Small Film (2023)");
        var movie = Path.Combine(folder, "Small.Film.2023.mkv");
        var poster = Path.Combine(folder, "Small.Film.2023.poster.jpg");
        var named = Path.Combine(folder, "Small.Film.2023.jpg");
        var folderArt = Path.Combine(folder, "folder.jpg");
        var online = Path.Combine("cache", "movieart", "small-film.img");

        Assert.Equal(new ArtworkSource(ArtworkSourceKind.FolderArt, poster), ArtworkChoice.MoviePoster(movie, online, Files(poster, named, folderArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.FolderArt, named), ArtworkChoice.MoviePoster(movie, online, Files(named, folderArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.FolderArt, folderArt), ArtworkChoice.MoviePoster(movie, online, Files(folderArt)));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.OnlinePoster, online), ArtworkChoice.MoviePoster(movie, online, Files()));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null), ArtworkChoice.MoviePoster(movie, null, Files()));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMovieLooseInTheLibraryNeverTakesTheLibrarysArt()
    {
        var movies = Path.Combine("media", "Movies");
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null),
            ArtworkChoice.MoviePoster(Path.Combine(movies, "Small.Film.2023.mkv"), null, Files(Path.Combine(movies, "poster.jpg"))));
        Assert.Equal(new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null), ArtworkChoice.MoviePoster("Small.Film.2023.mkv", null, Files("poster.jpg")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnEpisodesStillComesFromTheCataloguesRecordAndIsCachedUnderItsPlace()
    {
        var show = new Rex.Media.AppCore.Metadata.EnrichedShow
        {
            CatalogueId = 318,
            Name = "Harbour Lights",
            Episodes = [new(1, 1, "Arrival", null) { StillUrl = "https://img/s1e1.jpg" }, new(1, 2, "Tides", null)],
        };
        Assert.Equal("https://img/s1e1.jpg", show.EpisodeStill(1, 1));
        Assert.Null(show.EpisodeStill(1, 2));
        Assert.Null(show.EpisodeStill(1, null));
        Assert.Equal("harbour lights-s01e02", ArtworkChoice.StillKey("harbour lights", 1, 2));
        Assert.Equal("show-s12e104", ArtworkChoice.StillKey("show", 12, 104));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.StillKey(null!, 1, 1));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachShowHasADarkTintOfItsOwn()
    {
        var tints = Enumerable.Range(0, 400).Select(n => ArtworkChoice.Tint("Show " + n)).ToList();
        Assert.All(tints, tint => Assert.InRange(Math.Max(tint.Red, Math.Max(tint.Green, tint.Blue)), 70, 80));
        Assert.True(tints.Distinct().Count() > 100);
        Assert.Equal(ArtworkChoice.Tint("Harbour Lights"), ArtworkChoice.Tint("HARBOUR LIGHTS"));

        // Every sixth of the hue circle is reached.
        var dominant = tints.Select(tint => (tint.Red >= tint.Green, tint.Green >= tint.Blue, tint.Blue >= tint.Red)).Distinct().Count();
        Assert.True(dominant >= 6);
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.Tint(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void NothingIsAskedOfNothing()
    {
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.ShowPoster(null!, 1, null, Files()));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.ShowPoster(Pilot, 1, null, null!));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.EpisodePicture(null!, null, Files()));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.EpisodePicture(Pilot, null, null!));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.MoviePoster(null!, null, Files()));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.MoviePoster(Pilot, null, null!));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.Folders(null!));
        Assert.Throws<ArgumentNullException>(() => ArtworkChoice.IsFolderArtName(null!));
    }
}
