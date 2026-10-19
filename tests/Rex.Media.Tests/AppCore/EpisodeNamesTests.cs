using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// How the library tells a show's episodes from movies (LIB-05): a season folder is strong evidence
/// whatever its ancestors are called, conventional filename marks work with or without one, and
/// real movies, sequels included, stay movies.
/// </summary>
public sealed class EpisodeNamesTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private const string Root = @"E:\Media\Videos\Movies";

    /// <summary>A path written Windows-style, in the separators of the system the tests run on.</summary>
    private static string Native(string path) => path.Replace('\\', Path.DirectorySeparatorChar);

    private static LibraryEntry Video(string path) =>
        new() { Path = Native(path), Kind = LibraryKind.Video, Title = MediaLibrary.TitleOf(Native(path)), Added = Monday };

    private static string[] Titles(IEnumerable<LibraryEntry> entries) => [.. entries.Select(entry => entry.Title)];

    [Fact]
    [Capability("LIB-05")]
    public void AShowsSeasonFolderInsideAMoviesFolderIsTv()
    {
        var season6 = new[] { "Family Guy - Gamma Story", "Family Guy - Alpha Story", "Family Guy - Beta Story" }
            .Select(title => Video($@"{Root}\Family Guy\Season 6\{title}.mkv")).ToList();
        var season7 = Video($@"{Root}\Family Guy\Season 7\Family Guy - Some Episode Title.mkv");
        var movie = Video($@"{Root}\Movie Title (2007).mkv");
        LibraryEntry[] all = [.. season6, season7, movie];

        Assert.Equal([movie], LibraryViews.Movies(all));
        var show = Assert.Single(LibraryViews.TvShows(all));
        Assert.Equal("Family Guy", show.Name);
        Assert.Equal("2 seasons · 4 episodes", show.Detail);
        var seasons = LibraryViews.TvSeasons(show);
        Assert.Equal(["Season 6", "Season 7"], seasons.Select(season => season.Name));
        // No episode number anywhere: the file names' natural order.
        Assert.Equal(["Family Guy - Alpha Story", "Family Guy - Beta Story", "Family Guy - Gamma Story"], Titles(seasons[0].Entries));
        Assert.Equal(Titles(seasons[0].Entries), Titles(LibraryViews.AutoplayVideos(all, season6[2])));

        var episode = LibraryViews.EpisodeOf(season6[0])!;
        Assert.Equal(new VideoEpisode("Family Guy", 6, null), episode);
        Assert.Equal("Family Guy · S06", episode.DisplayName);
        Assert.Equal("S06", episode.Badge);
        Assert.Equal("Season 6", episode.Spoken);

        var located = LibraryViews.LocateCurrent(all, season7.Path)!;
        Assert.Equal(("Family Guy", "Season 7"), (located.Show!.Name, located.Season!.Name));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AStoredLibraryReclassifiesWhenItLoads()
    {
        // Nothing about the kind of video is kept: what a library saved before this rule is read again by it.
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store);
        var episode = Native($@"{Root}\Family Guy\Season 6\Family Guy - Some Episode Title.mkv");
        library.Scan(Native(Root), [new LibraryFile(episode, 100, Monday), new LibraryFile(Native($@"{Root}\Movie.Title.2007.1080p.mkv"), 100, Monday)], _ => LibraryKind.Video);
        library.SetDetails(episode, new MediaInfo
        {
            FormatName = "Matroska",
            Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }],
            Metadata = new Dictionary<string, string> { [MetadataKeys.Title] = "Some Episode Title", [MetadataKeys.Date] = "2007" },
        });

        var again = new MediaLibrary(store).Entries;
        Assert.Equal(["Movie.Title.2007.1080p"], Titles(LibraryViews.Movies(again)));
        Assert.Equal(["Some Episode Title"], Titles(Assert.Single(LibraryViews.TvShows(again)).Entries));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(Root + @"\Family Guy\Season 6\Family Guy - Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Films\Family Guy\S06\Family Guy - Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Videos\Family Guy\s6\Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Videos\Family Guy\Series 6\Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Downloads\Die Serie\Staffel 2\Eine Folge.mkv", "Die Serie", 2, null)]
    [InlineData(@"D:\Videos\La Serie\Saison 03\Un Episode.mkv", "La Serie", 3, null)]
    [InlineData(@"D:\Movies\Family Guy\Specials\Family Guy - Holiday Special.mkv", "Family Guy", 0, null)]
    [InlineData(@"D:\Movies\Family Guy\Season 0\Family Guy - Pilot Pitch.mkv", "Family Guy", 0, null)]
    [InlineData(@"D:\Movies\Family Guy Season 6\Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Family.Guy.S06\Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Season 6\Family Guy - Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Season 6\Family Guy - Some Episode Title.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy - 05 - Some Episode Title.mkv", "Family Guy", 6, 5)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy 601 Some Episode Title.mkv", "Family Guy", 6, 1)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy - 0612 - Some Episode Title.mkv", "Family Guy", 6, 12)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy - Some Episode Title 1080p x264.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy - Some Episode Title h.264 DD5.1.mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy - Some Episode Title (2007).mkv", "Family Guy", 6, null)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\E03.mkv", "Family Guy", 6, 3)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Episode 4.mkv", "Family Guy", 6, 4)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy Ep 05 Some Episode Title.mkv", "Family Guy", 6, 5)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family Guy e6.mkv", "Family Guy", 6, 6)]
    [InlineData(@"D:\Movies\Family Guy\Season 6\Family.Guy.S07E02.Some.Episode.Title.mkv", "Family Guy", 7, 2)]
    [InlineData(@"D:\Movies\30 Rock\Season 6\30 Rock - Some Episode Title.mkv", "30 Rock", 6, null)]
    [InlineData(@"D:\Movies\30 Rock\Season 6\30 Rock - 04 - Some Episode Title.mkv", "30 Rock", 6, 4)]
    public void ASeasonFolderMakesEpisodesWhateverItsAncestorsAreCalled(string path, string series, int season, int? episode)
    {
        var found = LibraryViews.EpisodeOf(Video(path))!;
        Assert.Equal((series, season, episode), (found.Series, found.Season, found.Episode));
        Assert.Empty(LibraryViews.Movies([Video(path)]));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\Downloads\Show.S06E01.Some.Episode.Title.1080p.WEB.mkv", "Show", 6, 1, null)]
    [InlineData(@"D:\Downloads\show s6e1.mkv", "show", 6, 1, null)]
    [InlineData(@"D:\Downloads\Show S06 E01.mkv", "Show", 6, 1, null)]
    [InlineData(@"D:\Downloads\Show - 6x01 - Some Episode Title.mkv", "Show", 6, 1, null)]
    [InlineData(@"D:\Downloads\Show S06E01-E02 Two Parter.mkv", "Show", 6, 1, 2)]
    [InlineData(@"D:\Downloads\Show.S06E01E02.mkv", "Show", 6, 1, 2)]
    [InlineData(@"D:\Downloads\Show.S06E01-02.720p.mkv", "Show", 6, 1, 2)]
    [InlineData(@"D:\Downloads\Show.S06E01-1080p.mkv", "Show", 6, 1, null)]
    [InlineData(@"D:\Downloads\Show 6x01-02.mkv", "Show", 6, 1, 2)]
    [InlineData(@"D:\Downloads\Show 6x01x02.mkv", "Show", 6, 1, 2)]
    [InlineData(@"D:\Downloads\Show - Episode 1.mkv", "Show", 1, 1, null)]
    [InlineData(@"D:\Downloads\Show Ep 01.mkv", "Show", 1, 1, null)]
    [InlineData(@"D:\Downloads\Show Ep.01-02.mkv", "Show", 1, 1, 2)]
    [InlineData(@"D:\Downloads\Show E01.mkv", "Show", 1, 1, null)]
    [InlineData(@"D:\Downloads\Show.Season.2.Episode.5.mkv", "Show", 2, 5, null)]
    [InlineData(@"D:\Downloads\Show S02 Episode 05.mkv", "Show", 2, 5, null)]
    [InlineData(@"D:\Downloads\Frieren\Episode 07.mkv", "Frieren", 1, 7, null)]
    [InlineData(@"D:\Media\TV\Sky City\S01E04.mkv", "Sky City", 1, 4, null)]
    public void FilenameMarksMakeEpisodesWithoutASeasonFolder(string path, string series, int season, int episode, int? through)
    {
        var found = LibraryViews.EpisodeOf(Video(path))!;
        Assert.Equal((series, season, (int?)episode, through), (found.Series, found.Season, found.Episode, found.Through));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(Root + @"\Movie Title (2007).mkv")]
    [InlineData(Root + @"\Movie.Title.2007.1080p.BluRay.x264.mkv")]
    [InlineData(Root + @"\Movie Title (2007)\Movie Title (2007).mkv")]
    [InlineData(Root + @"\Rocky II.mkv")]
    [InlineData(Root + @"\Rocky II (1979)\Rocky II (1979).mkv")]
    [InlineData(Root + @"\Ocean's 12.mkv")]
    [InlineData(Root + @"\Collection\Ocean's 12 (2004)\Ocean's 12 (2004).mkv")]
    [InlineData(Root + @"\Apollo 13 (1995) 720p.mkv")]
    [InlineData(Root + @"\The Room 101.mkv")]
    [InlineData(Root + @"\Some Film 1920x1080.mkv")]
    [InlineData(Root + @"\Movie.Title.2019.1080p.WEB-DL.DD5.1.H.264.mkv")]
    [InlineData(Root + @"\Space Saga Episode 4 - A New Dawn (1977).mkv")]
    [InlineData(Root + @"\Film.E3.2019.mkv")]
    [InlineData(Root + @"\Season 6.mkv")]
    [InlineData(@"D:\Videos\Holiday 2024\Clip 01.mkv")]
    [InlineData(@"D:\Videos\Movies\E01.mkv")]
    [InlineData(@"D:\S01E01.mkv")]
    public void RealMoviesStayMovies(string path)
    {
        Assert.Null(LibraryViews.EpisodeOf(Video(path)));
        Assert.Single(LibraryViews.Movies([Video(path)]));
    }

    [Fact]
    [Capability("LIB-05")]
    public void NumberedEpisodesComeFirstThenNaturalOrderAndSpecialsLast()
    {
        var folder = $@"{Root}\Family Guy\Season 6";
        LibraryEntry[] season =
        [
            Video($@"{folder}\Family Guy - Untitled B.mkv"),
            Video($@"{folder}\Family Guy - 10 - Tenth.mkv"),
            Video($@"{folder}\Family Guy - Untitled A.mkv"),
            Video($@"{folder}\Family Guy - 2 - Second.mkv"),
            Video($@"{Root}\Family Guy\Specials\Family Guy - Holiday Special.mkv"),
            Video($@"{Root}\Family Guy\Season 7\Family.Guy.S07E01E02.Opener.mkv"),
        ];

        var show = Assert.Single(LibraryViews.TvShows(season));
        var seasons = LibraryViews.TvSeasons(show);
        Assert.Equal(["Season 6", "Season 7", "Specials"], seasons.Select(group => group.Name));
        Assert.Equal(["Family Guy - 2 - Second", "Family Guy - 10 - Tenth", "Family Guy - Untitled A", "Family Guy - Untitled B"], Titles(seasons[0].Entries));
        Assert.Equal(Titles(seasons[0].Entries), Titles(LibraryViews.AutoplayVideos(season, season[0])));

        var special = LibraryViews.EpisodeOf(season[4])!;
        Assert.Equal(("Specials", "S00"), (special.Spoken, special.Badge));
        var twoParter = LibraryViews.EpisodeOf(season[5])!;
        Assert.Equal(("Family Guy · S07E01-E02", "S07 E01", "Season 7 · Episodes 1-2"), (twoParter.DisplayName, twoParter.Badge, twoParter.Spoken));
        Assert.Equal("Season 6 · Episode 2", LibraryViews.EpisodeOf(season[3])!.Spoken);
        Assert.Equal("Specials · Episode 3", new VideoEpisode("Show", 0, 3).Spoken);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ASeasonFolderTakesTheShowsYearQualifier()
    {
        var episode = LibraryViews.EpisodeOf(Video(@"D:\Movies\Signal (2005)\Season 1\Signal - Pilot.mkv"))!;
        Assert.Equal(new VideoEpisode("Signal", 1, null, 2005), episode);
        Assert.Throws<ArgumentNullException>(() => EpisodeNames.Parse(null!));
        Assert.Null(EpisodeNames.Parse(""));
    }
}
