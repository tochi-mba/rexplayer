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

        Assert.Equal([movie], LibraryViews.Movies(all, MovieRules.Offline));
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
        Assert.Equal(["Movie.Title.2007.1080p"], Titles(LibraryViews.Movies(again, MovieRules.Offline)));
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
        Assert.Null(MovieRules.Judge(Video(path), MovieLookup.Matched));
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
    [InlineData(@"D:\TV\The X-Files S01E01 Pilot.mkv", "The X-Files", 1, 1, null)]
    [InlineData(@"D:\TV\The.X-Files.S02E03.x264.mkv", "The X-Files", 2, 3, null)]
    public void FilenameMarksMakeEpisodesWithoutASeasonFolder(string path, string series, int season, int episode, int? through)
    {
        var found = LibraryViews.EpisodeOf(Video(path))!;
        Assert.Equal((series, season, (int?)episode, through), (found.Series, found.Season, found.Episode, found.Through));
    }

    [Fact]
    [Capability("LIB-05")]
    public void NumberedAnimeReleaseFilesFormShowsWithoutSeasonMarks()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\Downloads\Panty & Stocking With Garterbelt (1080p) V2\Panty & Stocking With Garterbelt - 01 - Excretion Without Honor And Humanity - Death Race 2010 [DarkDream].mkv"),
            Video(@"D:\Downloads\Panty & Stocking With Garterbelt (1080p) V2\Panty & Stocking With Garterbelt - 13 - Bitch Girls - Bitch Girls, 2 Bitch [DarkDream].mkv"),
            Video(@"D:\Downloads\Golden Boy\Golden Boy (sigla) [Anime ITA - Dvd Rip] [tntvillage.org].mkv"),
            Video(@"D:\Downloads\Golden Boy\Golden Boy 1 [Anime ITA - Dvd Rip] [tntvillage.org].mkv"),
            Video(@"D:\Downloads\Golden Boy\Golden Boy 2 [Anime ITA - Dvd Rip] [tntvillage.org].mkv"),
        ];

        var shows = LibraryViews.TvShows(library);
        Assert.Equal(["Golden Boy", "Panty & Stocking With Garterbelt"], shows.Select(show => show.Name));
        Assert.Equal([3, 2], shows.Select(show => show.Entries.Count));
        Assert.Empty(LibraryViews.Standalone(library));
        Assert.Equal((1, (int?)1), (LibraryViews.EpisodeOf(library[0])!.Season, LibraryViews.EpisodeOf(library[0])!.Episode));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ASeasonMarkerInsideAReleaseFolderGroupsEveryEpisode()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\Downloads\Nathan for You S00 Nathan on Your Side (240p re-tvrip)\Nathan on Your Side - Advertising.mkv"),
            Video(@"D:\Downloads\Nathan for You S00 Nathan on Your Side (240p re-tvrip)\Nathan on Your Side - Buying an MP3 Player.mkv"),
        ];

        var show = Assert.Single(LibraryViews.TvShows(library));
        Assert.Equal(("Nathan for You", "1 season · 2 episodes"), (show.Name, show.Detail));
        Assert.All(show.Entries, entry => Assert.Equal(0, LibraryViews.EpisodeOf(entry)!.Season));
        Assert.Empty(LibraryViews.Standalone(library));
    }

    [Fact]
    [Capability("LIB-05")]
    public void NamedShowPresentationsInTheShowsFolderAreSpecials()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\TV\Family Guy\Family Guy - Stewie Griffin- The Untold Story.mkv"),
            Video(@"D:\TV\Family Guy\Family Guy Presents Seth and Alex's Almost Live Comedy Show.mkv"),
        ];

        var show = Assert.Single(LibraryViews.TvShows(library));
        Assert.Equal("Family Guy", show.Name);
        var specials = Assert.Single(LibraryViews.TvSeasons(show));
        Assert.Equal("Specials", specials.Name);
        Assert.Equal(["Family Guy - Stewie Griffin- The Untold Story", "Family Guy Presents Seth and Alex's Almost Live Comedy Show"],
            Titles(specials.Entries));
        Assert.All(specials.Entries, entry => Assert.Equal(0, LibraryViews.EpisodeOf(entry)!.Season));
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
    [InlineData(Root + @"\X-Men (2000).mkv")]
    [InlineData(Root + @"\X-Men (2000)\X-Men (2000).mkv")]
    [InlineData(Root + @"\Malcolm X (1992).mkv")]
    [InlineData(Root + @"\Spider-Man 2.mkv")]
    [InlineData(Root + @"\Spider-Man 2 (2004)\Spider-Man 2 (2004) 1080p.mkv")]
    [InlineData(Root + @"\Apollo 13.mkv")]
    [InlineData(Root + @"\Movie Special Edition (2001).mkv")]
    [InlineData(Root + @"\Movie.Title.Special.Features.2001.mkv")]
    [InlineData(Root + @"\Special Delivery (1999).mkv")]
    [InlineData(Root + @"\Show.S01.x264.mkv")]
    [InlineData(Root + @"\Show.S01.576p.mkv")]
    [InlineData(@"D:\Downloads\Special.mkv")]
    [InlineData(@"D:\Extras\Making Of.mkv")]
    public void VideosThatAreNotEpisodesStayOutOfTvShows(string path)
    {
        Assert.Null(LibraryViews.EpisodeOf(Video(path)));
        Assert.NotNull(MovieRules.Judge(Video(path), MovieLookup.NotSearched));
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
        Assert.Equal(("Special", "Special"), (special.Spoken, special.Badge));
        var twoParter = LibraryViews.EpisodeOf(season[5])!;
        Assert.Equal(("Family Guy · S07E01-E02", "S07 E01", "Season 7 · Episodes 1-2"), (twoParter.DisplayName, twoParter.Badge, twoParter.Spoken));
        Assert.Equal("Season 6 · Episode 2", LibraryViews.EpisodeOf(season[3])!.Spoken);
        Assert.Equal(("Special 3", "Special 3", "Show · S00E03"), (new VideoEpisode("Show", 0, 3).Spoken, new VideoEpisode("Show", 0, 3).Badge, new VideoEpisode("Show", 0, 3).DisplayName));
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

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\TV\Futurama\Futurama S07X01 Simpsorama.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\Downloads\Futurama S07 X01 Simpsorama 1080p.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\Downloads\Futurama.S07x01.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\Downloads\Futurama.S07SP01.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\Downloads\Futurama S07 SP02.mkv", "Futurama", 7, 2)]
    [InlineData(@"D:\Downloads\Futurama S07 Special 1.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\Downloads\Futurama.S07.Special.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\Downloads\Futurama Season 7 Special 2.mkv", "Futurama", 7, 2)]
    [InlineData(@"D:\Downloads\Futurama.S07E00.Behind.The.Voices.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\Downloads\Futurama 7x00.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\Downloads\Futurama S07.5.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\TV\Futurama\S07X01.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama S07X01 Simpsorama.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\TV\Futurama\Season 7\S07X01.mkv", "Futurama", 7, 1)]
    [InlineData(@"D:\TV\Futurama\Season 7\Simpsorama Special.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama - Holiday Special 2.mkv", "Futurama", 7, 2)]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama - OVA.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\TV\Futurama\Season 7\Extras\Making Of.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\TV\Futurama Season 7\Behind the Scenes\Table Read.mkv", "Futurama", 7, null)]
    [InlineData(@"D:\TV\Futurama\Specials\Featurettes\Table Read.mkv", "Futurama", null, null)]
    [InlineData(@"D:\TV\Futurama\Specials\Futurama SP03.mkv", "Futurama", null, 3)]
    [InlineData(@"D:\TV\Futurama\Specials\Futurama - 04 - Holiday Special.mkv", "Futurama", null, 4)]
    [InlineData(@"D:\Downloads\Futurama.S00E05.mkv", "Futurama", null, 5)]
    public void SpecialsThatAreCertainFileUnderTheShowsSpecials(string path, string series, int? specialOf, int? episode)
    {
        var found = EpisodeNames.Parse(Native(path))!;
        Assert.Equal((series, 0, specialOf, episode, false), (found.Series, found.Season, found.SpecialOf, found.Episode, found.Tentative));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama - 05 - Special Delivery.mkv", 5)]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama - Special Delivery.mkv", null)]
    [InlineData(@"D:\TV\Futurama\Season 7\Futurama - 06 - Holiday Special.mkv", 6)]
    public void AnEpisodeTitledWithTheWordSpecialStaysAnEpisode(string path, int? episode)
    {
        var found = EpisodeNames.Parse(Native(path))!;
        Assert.Equal(("Futurama", 7, episode, (int?)null), (found.Series, found.Season, found.Episode, found.SpecialOf));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\TV\Futurama\Extras\Making Of.mkv", "Futurama", null)]
    [InlineData(@"D:\TV\Futurama\Featurettes\Making Of.mkv", "Futurama", null)]
    [InlineData(@"D:\TV\Futurama\Behind the Scenes\Making Of.mkv", "Futurama", null)]
    [InlineData(@"D:\TV\Futurama\Special Features\Making Of.mkv", "Futurama", null)]
    [InlineData(@"D:\TV\Futurama\Special.mkv", "Futurama", null)]
    [InlineData(@"D:\Downloads\Futurama SP01.mkv", "Futurama", 1)]
    [InlineData(@"D:\Downloads\Futurama OVA 2.mkv", "Futurama", 2)]
    [InlineData(@"D:\Downloads\Futurama.OAD.mkv", "Futurama", null)]
    [InlineData(@"D:\Downloads\Futurama Special (2005).mkv", "Futurama", null)]
    [InlineData(@"D:\Movies\X-Men (2000)\Featurettes\Making Of.mkv", "X-Men", null)]
    public void SpecialsThatAreUncertainJoinOnlyAShowThatIsThere(string path, string series, int? episode)
    {
        var found = EpisodeNames.Parse(Native(path))!;
        Assert.Equal((series, 0, episode, true), (found.Series, found.Season, found.Episode, found.Tentative));
        Assert.Single(LibraryViews.Standalone([Video(path)]));
        Assert.Empty(LibraryViews.TvShows([Video(path)]));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\Movies\Season 7\Extras\Making Of.mkv")]
    [InlineData(@"D:\Movies\Extras\Making Of.mkv")]
    public void ExtrasWithNoShowAboveThemAreMovies(string path)
    {
        Assert.Null(EpisodeNames.Parse(Native(path)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheCrossoverSpecialJoinsItsShowInsteadOfMakingOne()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\TV\Futurama\Season 1\Futurama S01E01 Space Pilot 3000.mkv"),
            Video(@"D:\TV\Futurama\Season 7\Futurama S07E01 The Bots and the Bees.mkv"),
            Video(@"D:\TV\Futurama\Season 7\Futurama S07E02 A Farewell to Arms.mkv"),
            Video(@"D:\TV\Futurama\Futurama S07X01 Simpsorama.mkv"),
            Video(@"D:\TV\Futurama\Extras\Making Of.mkv"),
            Video(@"D:\Downloads\Futurama SP01.mkv"),
            Video(@"D:\Downloads\Futurama S07 OVA.mkv"),
            Video(@"D:\Movies\Inception\Extras\Dream Levels.mkv"),
        ];

        var show = Assert.Single(LibraryViews.TvShows(library));
        Assert.Equal(("Futurama", "3 seasons · 7 episodes"), (show.Name, show.Detail));
        var seasons = LibraryViews.TvSeasons(show);
        Assert.Equal(["Season 1", "Season 7", "Specials"], seasons.Select(season => season.Name));
        // The specials naming no season first (numbered, then by name), then each season's own.
        Assert.Equal(["Futurama SP01", "Futurama S07 OVA", "Making Of", "Futurama S07X01 Simpsorama"], Titles(seasons[2].Entries));
        Assert.Equal(["Dream Levels"], Titles(LibraryViews.Standalone(library)));

        var special = LibraryViews.EpisodeOf(library[3])!;
        Assert.Equal(("S07 Special 1", "Season 7, special 1", "Futurama · S07 Special 1"), (special.Badge, special.Spoken, special.DisplayName));
        Assert.Equal("Simpsorama", ShowPage.EpisodeTitle(library[3], special));
        var located = LibraryViews.LocateCurrent(library, library[3].Path)!;
        Assert.Equal(("Futurama", "Specials"), (located.Show!.Name, located.Season!.Name));
        Assert.Null(LibraryViews.LocateCurrent(library, library[7].Path)!.Show);

        var unnumbered = new VideoEpisode("Futurama", 0, null) { SpecialOf = 7 };
        Assert.Equal(("S07 Special", "Season 7, special"), (unnumbered.Badge, unnumbered.Spoken));
        var pair = new VideoEpisode("Futurama", 0, 1) { Through = 2 };
        Assert.Equal(("Special 1-2", "Special 1-2"), (pair.Badge, pair.Spoken));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMoviesExtrasStayItsOwnUnlessTheShowsYearAgrees()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\TV\X-Men\Season 1\X-Men S01E01 Night of the Sentinels.mkv"),
            Video(@"D:\Movies\X-Men (2000)\X-Men (2000).mkv"),
            Video(@"D:\Movies\X-Men (2000)\Featurettes\Making Of.mkv"),
            Video(@"D:\TV\Signal (2005)\Season 1\Signal - Pilot.mkv"),
            Video(@"D:\TV\Signal (2005)\Extras\Table Read.mkv"),
            Video(@"D:\TV\Signal (2016)\Extras\Cast Interviews.mkv"),
        ];

        Assert.Equal(["Cast Interviews", "Making Of", "X-Men (2000)"], Titles(LibraryViews.Standalone(library)).Order());
        var shows = LibraryViews.TvShows(library);
        Assert.Equal(["Signal", "X-Men"], shows.Select(show => show.Name));
        Assert.Equal(["Signal - Pilot", "Table Read"], Titles(shows[0].Entries).Order());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ALoneEpisodeNamedAfterAShowAndAMarkJoinsThatShow()
    {
        LibraryEntry[] library =
        [
            Video(@"D:\TV\Futurama\Season 7\Futurama - 01 - The Bots and the Bees.mkv"),
            Video(@"D:\TV\Futurama\Season 7\Futurama - 02 - A Farewell to Arms.mkv"),
            // No mark the parser reads: the show's name is everything before the dash.
            Video(@"D:\TV\Season 7\Futurama S07 Bonus - Simpsorama.mkv"),
            // Spelled differently: case, accents and punctuation do not make another show.
            Video(@"D:\TV\Pokémon\Season 1\Pokémon - 01 - Pokémon, I Choose You.mkv"),
            Video(@"D:\TV\Pokémon\Season 1\Pokémon - 02 - Pokémon Emergency.mkv"),
            Video(@"D:\Downloads\pokemon.S01E03.mkv"),
            Video(@"D:\Downloads\Marvels Agents S01E01.mkv"),
            Video(@"D:\TV\Marvel's Agents\Season 1\Marvel's Agents - 02 - 0-8-4.mkv"),
            Video(@"D:\TV\Law & Order\Season 1\Law & Order - 01 - Prescription for Death.mkv"),
            Video(@"D:\Downloads\Law.and.Order.S01E02.mkv"),
            // A real show whose name begins with another's stays its own, even with one episode.
            Video(@"D:\TV\Doctor Who\Season 1\Doctor Who - 01 - Rose.mkv"),
            Video(@"D:\TV\Doctor Who\Season 1\Doctor Who - 02 - The End of the World.mkv"),
            Video(@"D:\TV\Doctor Who Confidential\Season 1\Doctor Who Confidential - 01 - Bringing Back the Doctor.mkv"),
        ];

        var shows = LibraryViews.TvShows(library);
        Assert.Equal(["Doctor Who", "Doctor Who Confidential", "Futurama", "Law & Order", "Marvel's Agents", "Pokémon"], shows.Select(show => show.Name));
        Assert.Equal([2, 1, 3, 2, 2, 3], shows.Select(show => show.Entries.Count));
        Assert.Equal("Futurama S07 Bonus - Simpsorama", shows[2].Entries[^1].Title);
        Assert.Empty(LibraryViews.Standalone(library));
    }
}
