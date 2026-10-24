using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class PodcastEpisodeProgressTests
{
    private static LibraryEntry Episode(TimeSpan? duration = null) => new()
    {
        Path = "Podcasts/Show/Ep 1.mp3",
        Title = "Ep 1",
        Kind = LibraryKind.Music,
        Duration = duration,
    };

    [Fact]
    public void NewPlayedAndResumeUseOnlyKnownLocalInformation()
    {
        var length = TimeSpan.FromMinutes(20);
        var fresh = PodcastEpisodeProgress.Of(Episode(length), null);
        Assert.Equal("New", fresh.Status);
        Assert.Equal(length, fresh.Remaining);
        Assert.Equal(0, fresh.Fraction);
        var played = PodcastEpisodeProgress.Of(Episode(length) with { Plays = 1 }, null);
        Assert.Equal("Played", played.Status);
        Assert.Equal(TimeSpan.Zero, played.Remaining);
        Assert.Equal(1, played.Fraction);
        var resume = PodcastEpisodeProgress.Of(Episode(length), TimeSpan.FromMinutes(12));
        Assert.Equal("Stopped at 12:00", resume.Status);
        Assert.Equal(TimeSpan.FromMinutes(8), resume.Remaining);
        Assert.Equal(0.6, resume.Fraction, 5);
    }

    [Fact]
    public void ZeroMissingAndOutOfRangeTimeNeverCreateInvalidProgress()
    {
        Assert.Equal(0, PodcastEpisodeProgress.Of(Episode(TimeSpan.Zero), TimeSpan.FromSeconds(1)).Fraction);
        Assert.Null(PodcastEpisodeProgress.Of(Episode(null), TimeSpan.FromSeconds(1)).Remaining);
        Assert.Equal("Stopped at 0:00", PodcastEpisodeProgress.Of(Episode(null), TimeSpan.FromSeconds(-20)).Status);
        var pastEnd = PodcastEpisodeProgress.Of(Episode(TimeSpan.FromMinutes(3)), TimeSpan.FromDays(10));
        Assert.Equal(TimeSpan.Zero, pastEnd.Remaining);
        Assert.Equal(1, pastEnd.Fraction);
        Assert.Equal("Stopped at 3:00", pastEnd.Status);
    }

    [Fact]
    public void DamagedAndGrowingFilesDoNotPretendToBePlayable()
    {
        var damaged = Episode(TimeSpan.FromMinutes(3)) with
        {
            Problem = "truncated",
        };
        var growing = Episode(TimeSpan.FromMinutes(3)) with
        {
            Downloading = true,
        };
        Assert.Equal("File unavailable", PodcastEpisodeProgress.Of(damaged, TimeSpan.FromMinutes(1)).Status);
        Assert.Equal("File incomplete", PodcastEpisodeProgress.Of(growing, TimeSpan.FromMinutes(1)).Status);
        Assert.Null(PodcastEpisodeProgress.Of(growing, null).Remaining);
        Assert.Throws<ArgumentNullException>(() => PodcastEpisodeProgress.Of(null!, null));
    }
}
