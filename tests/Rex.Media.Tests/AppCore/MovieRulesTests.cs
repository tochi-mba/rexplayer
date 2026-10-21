using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Which videos are films and which are other videos (LIB-05): the person's choice first, then what
/// plainly is not a film, then a lookup's match, then strong local evidence; TV episodes untouched.
/// </summary>
public sealed class MovieRulesTests
{
    private static readonly TimeSpan Feature = TimeSpan.FromMinutes(118);

    /// <summary>A path written Windows-style, in the separators of the system the tests run on.</summary>
    private static string Native(string path) => path.Replace('\\', Path.DirectorySeparatorChar);

    private static LibraryEntry Video(string path, TimeSpan? length = null, string? title = null, int? year = null, bool? movie = null) => new()
    {
        Path = Native(path),
        Kind = LibraryKind.Video,
        Title = title ?? MediaLibrary.TitleOf(Native(path)),
        Year = year,
        Duration = length,
        Override = movie is null ? null : new DetailOverride { Movie = movie },
    };

    private static VideoVerdict? Offline(LibraryEntry entry) => MovieRules.Judge(entry, MovieLookup.NotSearched);

    [Theory]
    [Capability("LIB-05")]
    [InlineData(@"D:\Movies\Apollo 13 (1995).mkv")]
    [InlineData(@"D:\Movies\Movie.Title.2007.1080p.BluRay.x264.mkv")]
    [InlineData(@"D:\Movies\Movie Title 2019 WEB-DL.mp4")]
    [InlineData(@"D:\Movies\The.Matrix.1999.mkv")]
    [InlineData(@"D:\Movies\Heat [1995] Directors Cut.mkv")]
    [InlineData(@"D:\Movies\Apollo 13 (1995)\apollo13-disc1.mkv")]
    public void AReleaseStyleNameOrFolderAtFeatureLengthIsAFilm(string path)
    {
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(path, Feature)));
        Assert.True(MovieRules.IsMovie(Video(path, Feature), MovieLookup.NotSearched));
        Assert.False(MovieRules.IsOther(Video(path, Feature), MovieLookup.NotSearched));
    }

    [Fact]
    public void ApolloKeepsItsNumberInItsTitleAndItsYearFromTheName()
    {
        var apollo = Video(@"D:\Movies\Apollo 13 (1995).mkv", Feature);

        Assert.Equal("Apollo 13", LibraryViews.MovieTitleOf(apollo));
        Assert.Equal(1995, LibraryViews.MovieYearOf(apollo));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ReleasePrefixesAndEncodingsResolveToTheSameFilm()
    {
        var rip = Video(@"D:\Movies\[SPVT] Iron.Lung.2026.1080p.WEBRip.x265.6CH-SupaCvnt.mkv", Feature);
        var hdr = Video(@"D:\Movies\Iron.Lung.2026.2160p.WEB-DL.HDR.DDP5.1.H265.mkv", Feature);

        Assert.Equal("Iron Lung", LibraryViews.MovieTitleOf(rip));
        Assert.Equal("Iron Lung", LibraryViews.MovieTitleOf(hdr));
        Assert.Equal(2026, LibraryViews.MovieYearOf(rip));
        Assert.Equal(2026, LibraryViews.MovieYearOf(hdr));
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(rip));
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(hdr));
        Assert.Equal("1080p", LibraryViews.MovieResolution(rip));
        Assert.Equal("2160p", LibraryViews.MovieResolution(hdr));
        Assert.Equal([hdr, rip], LibraryViews.MovieVersions([rip, hdr], rip));
    }

    [Fact]
    public void AFilmTitleWithAYearInItsOwnNameOrBracketsIsPreserved()
    {
        var space = Video(@"D:\Movies\2001.A.Space.Odyssey.1968.1080p.BluRay.mkv", Feature);
        var rec = Video(@"D:\Movies\[REC] (2007).mkv", Feature);
        Assert.Equal("2001 A Space Odyssey", LibraryViews.MovieTitleOf(space));
        Assert.Equal(1968, LibraryViews.MovieYearOf(space));
        Assert.Equal("[REC]", LibraryViews.MovieTitleOf(rec));
        Assert.Equal(2007, LibraryViews.MovieYearOf(rec));
    }

    [Fact]
    public void AlternateResolutionsNeverCombineOtherFilmsCutsOrIncompleteFiles()
    {
        var first = Video(@"D:\Movies\Iron.Lung.2026.1080p.WEBRip.mkv", Feature);
        var second = Video(@"D:\Movies\Iron.Lung.2026.2160p.WEB-DL.mkv", Feature);
        var later = Video(@"D:\Movies\Iron.Lung.2027.2160p.mkv", Feature);
        var unrelated = Video(@"D:\Movies\Iron.Will.2026.2160p.mkv", Feature);
        var extended = Video(@"D:\Movies\Iron.Lung.2026.720p.Extended.mkv", Feature + TimeSpan.FromMinutes(16));
        var nearCut = Video(@"D:\Movies\Iron.Lung.2026.720p.Directors.Cut.mkv", Feature);
        var unfinished = Video(@"D:\Movies\Iron.Lung.2026.480p.mkv", Feature) with { Downloading = true };
        var excluded = Video(@"D:\Movies\Iron.Lung.2026.1440p.mkv", Feature, movie: false);
        var clip = Video(@"D:\Movies\Iron.Lung.2026.360p.mkv", TimeSpan.FromMinutes(3));
        var unknown = Video(@"D:\Movies\Iron Lung.mkv", Feature);
        Assert.Equal([second, first], LibraryViews.MovieVersions([first, second, later, unrelated, extended, nearCut, unfinished, excluded, clip, unknown], first));
        Assert.Empty(LibraryViews.MovieVersions([unknown, first], unknown));
        Assert.Empty(LibraryViews.MovieVersions([first, second], unfinished));
        Assert.Empty(LibraryViews.MovieVersions([first], first with { Title = "", Year = 2026 }));
        Assert.Equal("Unknown", LibraryViews.MovieResolution(unknown));
        Assert.Equal("2160p", LibraryViews.MovieResolution(Video(@"D:\Movies\Iron.Lung.2026.UHD.WEB.mkv")));
    }

    [Fact]
    public void ATitleTagWithAYearIsEvidenceButAYearAloneIsNot()
    {
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(@"D:\Videos\disc_title_01.mkv", Feature, title: "Heat", year: 1995)));
        Assert.Equal(VideoVerdict.NoEvidence, Offline(Video(@"D:\Videos\disc_title_01.mkv", Feature, year: 1995)));
        Assert.Equal(VideoVerdict.NoEvidence, Offline(Video(@"D:\Videos\disc_title_01.mkv", Feature, title: "Heat")));
    }

    [Theory]
    [InlineData(@"D:\Movies\Ocean's 12.mkv")]
    [InlineData(@"D:\Movies\Inception.mkv")]
    public void AFilmWithNoYearNeedsAMatch(string path)
    {
        var film = Video(path, Feature);

        Assert.Equal(VideoVerdict.NoEvidence, Offline(film));
        Assert.Equal(VideoVerdict.NoEvidence, MovieRules.Judge(film, MovieLookup.Unmatched));
        Assert.Equal(VideoVerdict.MovieByMatch, MovieRules.Judge(film, MovieLookup.Matched));
        Assert.True(MovieRules.WorthLookingUp(film));
        Assert.Equal("Ocean's 12", LibraryViews.MovieTitleOf(Video(@"D:\Movies\Ocean's 12.mkv")));
    }

    [Fact]
    public void AFilmOfUnknownLengthIsJudgedByItsName()
    {
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(@"D:\Movies\Apollo 13 (1995).mkv")));
        Assert.Equal(VideoVerdict.NoEvidence, Offline(Video(@"D:\Movies\Inception.mkv")));
    }

    [Theory]
    [InlineData(@"D:\Phone\IMG_1234.MOV")]
    [InlineData(@"D:\Phone\VID_20240101_101010.mp4")]
    [InlineData(@"D:\Phone\VID-20240101-WA0001.mp4")]
    [InlineData(@"D:\Phone\PXL_20240101_101010123.mp4")]
    [InlineData(@"D:\Camera\MVI_0042.MOV")]
    [InlineData(@"D:\Camera\DSC_0001.MOV")]
    [InlineData(@"D:\Camera\DSCF0001.AVI")]
    [InlineData(@"D:\GoPro\GOPR0001.MP4")]
    [InlineData(@"D:\GoPro\GH010123.MP4")]
    [InlineData(@"D:\Drone\DJI_0001.MP4")]
    [InlineData(@"D:\Phone\20240101_101010.mp4")]
    public void CameraAndPhoneNamesAreOtherVideosWhateverTheirLength(string path)
    {
        var clip = Video(path, Feature);

        Assert.Equal(VideoVerdict.CameraName, Offline(clip));
        Assert.Equal(VideoVerdict.CameraName, MovieRules.Judge(clip, MovieLookup.Matched));
        Assert.True(MovieRules.IsOther(clip, MovieLookup.Matched));
        Assert.False(MovieRules.WorthLookingUp(clip));
    }

    [Fact]
    public void ATwoHourHomeVideoWithACameraNameInAYearFolderIsStillAnOtherVideo() =>
        Assert.Equal(VideoVerdict.CameraName, Offline(Video(@"D:\Videos\Wedding (2019)\MVI_0042.MOV", TimeSpan.FromHours(2))));

    [Theory]
    [InlineData(@"D:\Videos\Screen Recording 2024-01-05 at 10.00.00.mov")]
    [InlineData(@"D:\Videos\ScreenRecording_01.mp4")]
    [InlineData(@"D:\Videos\Screencast from 2024-01-05.webm")]
    [InlineData(@"D:\Videos\my-screen-capture.mp4")]
    [InlineData(@"D:\Videos\Capture 3.mp4")]
    [InlineData(@"D:\Videos\Recording_12.mp4")]
    [InlineData(@"D:\Videos\2024-01-05 10-00-00.mkv")]
    [InlineData(@"D:\Videos\Captures\Some Game 2024.mp4")]
    [InlineData(@"D:\Videos\Screen Recordings\Demo (2024).mp4")]
    public void ScreenRecordingsAreOtherVideos(string path) =>
        Assert.Equal(VideoVerdict.ScreenRecording, MovieRules.Judge(Video(path, Feature), MovieLookup.Matched));

    [Theory]
    [InlineData(@"D:\Movies\Capture (2019).mkv")]
    [InlineData(@"D:\Movies\Captain Phillips (2013).mkv")]
    [InlineData(@"D:\Movies\Recording the Truth (2020).mkv")]
    [InlineData(@"D:\Movies\Movie 43 (2013).mkv")]
    public void FilmsWhoseNamesStartLikeACameraOrARecorderStayFilms(string path) =>
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(path, Feature)));

    [Fact]
    public void AThreeMinuteMusicVideoIsAnOtherVideoEvenWhenATitleMatches()
    {
        var song = Video(@"D:\Videos\Asake - Lonely At The Top.mp4", TimeSpan.FromMinutes(3));

        Assert.Equal(VideoVerdict.MusicVideo, MovieRules.Judge(song, MovieLookup.Matched));
        Assert.False(MovieRules.WorthLookingUp(song));
    }

    [Theory]
    [InlineData(@"D:\Videos\Asake - Terminator (Official Video).mp4")]
    [InlineData(@"D:\Videos\Asake - Sungba [Official Music Video].mp4")]
    [InlineData(@"D:\Videos\Asake - Joha (Lyric Video).mp4")]
    [InlineData(@"D:\Videos\Asake - Amapiano (Visualizer).mp4")]
    public void AMarkedMusicVideoIsOneWhateverItsLength(string path)
    {
        Assert.Equal(VideoVerdict.MusicVideo, Offline(Video(path)));
        Assert.Equal(VideoVerdict.MusicVideo, Offline(Video(path, Feature)));
    }

    [Fact]
    public void AnArtistAndTitleNameAtFeatureLengthIsNotAMusicVideo()
    {
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(@"D:\Movies\Star Wars - A New Hope (1977).mkv", Feature)));
        Assert.Equal(VideoVerdict.ShortClip, Offline(Video(@"D:\Videos\Concert - Encore.mp4", TimeSpan.FromMinutes(12))));
    }

    [Fact]
    public void AClipShorterThanAFeatureIsAnOtherVideoEvenWithAReleaseName()
    {
        var shortFilm = Video(@"D:\Movies\Short.Film.2019.1080p.mkv", MovieRules.FeatureLength - TimeSpan.FromSeconds(1));

        Assert.Equal(VideoVerdict.ShortClip, MovieRules.Judge(shortFilm, MovieLookup.Matched));
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(shortFilm with { Duration = MovieRules.FeatureLength }));
        Assert.Equal(TimeSpan.FromMinutes(40), MovieRules.FeatureLength);
        Assert.Equal(TimeSpan.FromMinutes(10), MovieRules.MusicVideoLength);
    }

    [Fact]
    public void AVideoNothingSpeaksForIsAnOtherVideoAndWorthALookup()
    {
        var holiday = Video(@"D:\Videos\Holiday at the lake.mp4", Feature);

        Assert.Equal(VideoVerdict.NoEvidence, Offline(holiday));
        Assert.True(MovieRules.IsOther(holiday, MovieLookup.Unmatched));
        Assert.True(MovieRules.WorthLookingUp(holiday));
    }

    [Theory]
    [InlineData(@"D:\TV\Family Guy\Season 6\Family Guy - Some Episode Title.mkv")]
    [InlineData(@"D:\Movies\North.Shore.S02E10.Finale.1080p.mkv")]
    [InlineData(@"D:\Movies\Sky.City.1x03.720p.mkv")]
    public void TvEpisodesAreNeverJudged(string path)
    {
        var episode = Video(path, TimeSpan.FromMinutes(22), movie: true);

        Assert.Null(MovieRules.Judge(episode, MovieLookup.Matched));
        Assert.False(MovieRules.IsMovie(episode, MovieLookup.Matched));
        Assert.False(MovieRules.IsOther(episode, MovieLookup.Matched));
        Assert.False(MovieRules.WorthLookingUp(episode));
    }

    [Fact]
    public void AnUncertainSpecialOrExtraIsJudgedLikeAnyVideo()
    {
        Assert.Equal(VideoVerdict.MovieByEvidence, Offline(Video(@"D:\Downloads\Futurama Special (2005).mkv", Feature)));
        Assert.Equal(VideoVerdict.ShortClip, Offline(Video(@"D:\Movies\X-Men (2000)\Featurettes\Making Of.mkv", TimeSpan.FromMinutes(12))));
    }

    [Fact]
    public void MusicAndPicturesAreNeverJudged()
    {
        Assert.Null(Offline(Video(@"D:\Music\Song (2020).mp3") with { Kind = LibraryKind.Music }));
        Assert.Null(Offline(Video(@"D:\Pictures\IMG_1234.jpg") with { Kind = LibraryKind.Picture }));
    }

    [Theory]
    [InlineData(@"D:\Phone\IMG_1234.MOV", 2)]
    [InlineData(@"D:\Videos\Asake - Lonely At The Top.mp4", 3)]
    [InlineData(@"D:\Videos\Screen Recording 1.mov", 60)]
    [InlineData(@"D:\Videos\Holiday at the lake.mp4", 100)]
    public void ThisIsAMovieBeatsEveryRule(string path, int minutes)
    {
        var chosen = Video(path, TimeSpan.FromMinutes(minutes), movie: true);

        Assert.Equal(VideoVerdict.MovieByChoice, MovieRules.Judge(chosen, MovieLookup.Unmatched));
        Assert.True(MovieRules.WorthLookingUp(chosen));
        Assert.True(MovieRules.NotMatched(chosen, MovieLookup.Unmatched));
        Assert.False(MovieRules.NotMatched(chosen, MovieLookup.NotSearched));
    }

    [Theory]
    [InlineData(@"D:\Movies\Apollo 13 (1995).mkv")]
    [InlineData(@"D:\Movies\The.Matrix.1999.mkv")]
    public void NotAMovieBeatsEveryRule(string path)
    {
        var chosen = Video(path, Feature, movie: false);

        Assert.Equal(VideoVerdict.OtherByChoice, MovieRules.Judge(chosen, MovieLookup.Matched));
        Assert.True(MovieRules.IsOther(chosen, MovieLookup.Matched));
        Assert.False(MovieRules.WorthLookingUp(chosen));
    }

    [Fact]
    public void OnlyAFilmTheLookupsCouldNotMatchSaysNotMatched()
    {
        var apollo = Video(@"D:\Movies\Apollo 13 (1995).mkv", Feature);
        var clip = Video(@"D:\Phone\IMG_1234.MOV", Feature);

        Assert.True(MovieRules.NotMatched(apollo, MovieLookup.Unmatched));
        Assert.False(MovieRules.NotMatched(apollo, MovieLookup.Matched));
        Assert.False(MovieRules.NotMatched(apollo, MovieLookup.NotSearched));
        Assert.False(MovieRules.NotMatched(clip, MovieLookup.Unmatched));
    }

    [Fact]
    public void TheViewsSplitTheVideosAndOfflineMeansNothingWasSearched()
    {
        var apollo = Video(@"D:\Movies\Apollo 13 (1995).mkv", Feature);
        var inception = Video(@"D:\Movies\Inception.mkv", Feature);
        var clip = Video(@"D:\Phone\VID_0001.MP4", TimeSpan.FromMinutes(1));
        var episode = Video(@"D:\TV\Sky City\Sky.City.1x03.720p.mkv", TimeSpan.FromMinutes(22));
        LibraryEntry[] all = [apollo, inception, clip, episode];
        MovieLookup Lookups(LibraryEntry entry) => entry == inception ? MovieLookup.Matched : MovieLookup.Unmatched;

        Assert.Equal(MovieLookup.NotSearched, MovieRules.Offline(apollo));
        Assert.Equal([apollo], LibraryViews.Movies(all, MovieRules.Offline));
        Assert.Equal([inception, clip], LibraryViews.OtherVideos(all, MovieRules.Offline));
        Assert.Equal([apollo, inception], LibraryViews.Movies(all, Lookups));
        Assert.Equal([clip], LibraryViews.OtherVideos(all, Lookups));
        Assert.Throws<ArgumentNullException>(() => MovieRules.Judge(null!, MovieLookup.NotSearched));
        Assert.Throws<ArgumentNullException>(() => MovieRules.Judge(apollo, (Func<LibraryEntry, MovieLookup>)null!));
    }
}
