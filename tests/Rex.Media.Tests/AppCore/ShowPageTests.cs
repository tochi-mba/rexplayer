using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>A show's own page: the season it opens on, the episode to go on with, and how each episode line reads.</summary>
public sealed class ShowPageTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Episode(string file, int plays = 0, DateTime? lastPlayed = null, TimeSpan? duration = null, string? title = null, string? problem = null) =>
        new()
        {
            Path = $@"D:\TV\Alpha\{file}.mkv".Replace('\\', Path.DirectorySeparatorChar),
            Kind = LibraryKind.Video,
            Title = title ?? file,
            Added = Monday,
            Plays = plays,
            LastPlayed = lastPlayed,
            Duration = duration,
            Problem = problem,
        };

    private static LibraryGroup Show(params LibraryEntry[] episodes) => LibraryViews.TvShows(episodes).Single();

    private static Func<string, TimeSpan?> Nothing => _ => null;

    private static Func<string, TimeSpan?> LeftAt(string file, TimeSpan at) =>
        path => path.Contains(file, StringComparison.OrdinalIgnoreCase) ? at : null;

    private static VideoEpisode Mark(LibraryEntry entry) => LibraryViews.EpisodeOf(entry)!;

    [Fact]
    public void NextUpResumesWhatWasLeftPartWay()
    {
        var show = Show(Episode("Alpha S01E01", plays: 1), Episode("Alpha S02E01", lastPlayed: Monday), Episode("Alpha S02E02"));

        Assert.Equal("Alpha S02E01", ShowPage.NextUp(show, LeftAt("S02E01", TimeSpan.FromMinutes(4)))?.Title);
    }

    [Fact]
    public void NextUpIsTheFirstUnseenEpisodeOfTheRegularSeasonsBeforeTheSpecials()
    {
        var show = Show(Episode("Alpha S00E01"), Episode("Alpha S01E01", plays: 2), Episode("Alpha S01E02"));

        Assert.Equal("Alpha S01E02", ShowPage.NextUp(show, Nothing)?.Title);
    }

    [Fact]
    public void NextUpStartsAgainWhenEverythingWasSeen()
    {
        var show = Show(Episode("Alpha S01E01", plays: 1), Episode("Alpha S01E02", plays: 1));

        Assert.Equal("Alpha S01E01", ShowPage.NextUp(show, Nothing)?.Title);
        Assert.Null(ShowPage.NextUp(new LibraryGroup("Empty", null, []), Nothing));
    }

    [Fact]
    public void TheShowOpensOnTheSeasonHoldingItsNextEpisode()
    {
        var show = Show(Episode("Alpha S01E01", plays: 1), Episode("Alpha S02E01"));
        var seasons = LibraryViews.TvSeasons(show);

        Assert.Equal("Season 2", ShowPage.DefaultSeason(seasons, ShowPage.NextUp(show, Nothing))?.Name);
        Assert.Equal("Season 1", ShowPage.DefaultSeason(seasons, null)?.Name);
        Assert.Equal("Season 1", ShowPage.DefaultSeason(seasons, Episode("Other S05E01"))?.Name);
        Assert.Null(ShowPage.DefaultSeason([], null));
    }

    [Fact]
    public void PlaySaysWhichEpisodeItResumesOrStarts()
    {
        Assert.Equal("Resume S02 E01", ShowPage.PlayLabel(Episode("Alpha S02E01"), resumes: true));
        Assert.Equal("Play S01 E03", ShowPage.PlayLabel(Episode("Alpha S01E03"), resumes: false));
        Assert.Equal("Play", ShowPage.PlayLabel(Episode("Holiday"), resumes: false));
        Assert.Equal("Play", ShowPage.PlayLabel(null, resumes: false));
    }

    [Fact]
    public void ProgressIsWhereItWasLeftAllForWatchedAndNothingForUnseen()
    {
        var hour = TimeSpan.FromHours(1);
        Assert.Equal(0.25, ShowPage.Progress(Episode("A", duration: hour), TimeSpan.FromMinutes(15)));
        Assert.Equal(1, ShowPage.Progress(Episode("A", duration: hour), TimeSpan.FromHours(2)));
        Assert.Equal(0, ShowPage.Progress(Episode("A"), TimeSpan.FromMinutes(15)));
        Assert.Equal(1, ShowPage.Progress(Episode("A", plays: 1), null));
        Assert.Equal(0, ShowPage.Progress(Episode("A"), null));
    }

    [Fact]
    public void EpisodesAreNumberedSinglyOrAsARange()
    {
        Assert.Equal("3", ShowPage.Number(Mark(Episode("Alpha S01E03"))));
        Assert.Equal("1-2", ShowPage.Number(Mark(Episode("Alpha S01E01-E02"))));
        Assert.Equal("", ShowPage.Number(new VideoEpisode("Alpha", 1, null)));
    }

    [Theory]
    [InlineData("Alpha.S01E03.The.Long.Night.1080p.WEB-DL.x264", "The Long Night")]
    [InlineData("Alpha - 1x04 - Pilot (Part 2) [HDTV]", "Pilot (Part 2)")]
    [InlineData("Alpha S01E05 720p", "Episode 5")]
    [InlineData("Alpha S01E01E02", "Episodes 1-2")]
    [InlineData("Alpha S01E06", "Episode 6")]
    public void TitlesComeFromTheWordsAfterTheMark(string file, string expected)
    {
        var entry = Episode(file);
        Assert.Equal(expected, ShowPage.EpisodeTitle(entry, Mark(entry)));
    }

    [Fact]
    public void AnEmbeddedTitleWinsAndAnUnnumberedEpisodeKeepsItsFileName()
    {
        var tagged = Episode("Alpha S01E03", title: "Winter Is Here");
        Assert.Equal("Winter Is Here", ShowPage.EpisodeTitle(tagged, Mark(tagged)));

        var loose = Episode("Behind_the_scenes");
        Assert.Equal("Behind the scenes", ShowPage.EpisodeTitle(loose, new VideoEpisode("Alpha", 1, null)));
    }

    [Fact]
    public void TheDetailSaysTheSeasonAndWhereItWasLeftOrThatItWasWatched()
    {
        var hour = TimeSpan.FromHours(1);
        var entry = Episode("Alpha S01E03", duration: hour);
        Assert.Equal("Season 1 · Episode 3 · Stopped at 0:12:30", ShowPage.EpisodeDetail(entry, Mark(entry), TimeSpan.FromSeconds(750)));

        var seen = Episode("Alpha S01E04", plays: 1);
        Assert.Equal("Season 1 · Episode 4 · Watched", ShowPage.EpisodeDetail(seen, Mark(seen), null));
        Assert.Equal("Season 1 · Episode 4", ShowPage.EpisodeDetail(Episode("Alpha S01E04"), Mark(seen), null));
        Assert.Equal("", ShowPage.EpisodeDetail(Episode("Loose"), null, null));
        Assert.Equal("Could not be read: damaged", ShowPage.EpisodeDetail(Episode("Alpha S01E05", problem: "damaged"), Mark(seen), null));
    }

    [Fact]
    public void TheSubtitleCountsTheShowAndTimesTheSeasonShown()
    {
        var show = Show(Episode("Alpha S01E01", duration: TimeSpan.FromMinutes(50)), Episode("Alpha S01E02", duration: TimeSpan.FromMinutes(50)),
            Episode("Alpha S02E01", duration: TimeSpan.FromSeconds(20)));
        var seasons = LibraryViews.TvSeasons(show);

        Assert.Equal("2 seasons · 3 episodes · 1 h 40 min", ShowPage.Subtitle(show, seasons[0]));
        Assert.Equal("2 seasons · 3 episodes · 1 min", ShowPage.Subtitle(show, seasons[1]));
        Assert.Equal("2 seasons · 3 episodes", ShowPage.Subtitle(show, null));
        Assert.Equal("2 seasons · 3 episodes", ShowPage.Subtitle(show, new LibraryGroup("Season 9", null, [Episode("Alpha S09E01")])));
    }
}
