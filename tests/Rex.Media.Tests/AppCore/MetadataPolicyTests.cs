using Rex.Media.AppCore.Metadata;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Online lookups are strictly off by default, each kind has its own switch, movies also need a
/// TMDB key, and a file under an excluded folder is never looked up at all.
/// </summary>
public sealed class MetadataPolicyTests
{
    private const string Show = @"D:\Media\TV\Show\Season 1\Show S01E01.mkv";

    [Fact]
    public void EverythingIsOffByDefault()
    {
        var fresh = new MetadataSettings();

        Assert.False(fresh.TvShows);
        Assert.False(fresh.Movies);
        Assert.False(fresh.Music);
        Assert.Equal("", fresh.TmdbApiKey);
        Assert.Empty(fresh.Excluded);
        foreach (var kind in Enum.GetValues<MetadataWork>())
        {
            Assert.False(MetadataPolicy.Allows(fresh, kind, Show));
        }
    }

    [Theory]
    [InlineData(MetadataWork.TvShow)]
    [InlineData(MetadataWork.Music)]
    public void EachSwitchAllowsOnlyItsOwnKind(MetadataWork kind)
    {
        var settings = new MetadataSettings
        {
            TvShows = kind == MetadataWork.TvShow,
            Music = kind == MetadataWork.Music,
        };

        foreach (var asked in Enum.GetValues<MetadataWork>())
        {
            Assert.Equal(asked == kind, MetadataPolicy.Allows(settings, asked, Show));
        }
    }

    [Fact]
    public void MoviesNeedTheSwitchAndAKey()
    {
        Assert.False(MetadataPolicy.MoviesReady(new MetadataSettings { Movies = true }));
        Assert.False(MetadataPolicy.MoviesReady(new MetadataSettings { TmdbApiKey = "abc123" }));
        Assert.True(MetadataPolicy.MoviesReady(new MetadataSettings { Movies = true, TmdbApiKey = "abc123" }));
        Assert.False(MetadataPolicy.Allows(new MetadataSettings { Movies = true }, MetadataWork.Movie, Show));
        Assert.True(MetadataPolicy.Allows(new MetadataSettings { Movies = true, TmdbApiKey = "abc123" }, MetadataWork.Movie, Show));
    }

    [Fact]
    public void AnExcludedFolderCoversEverythingUnderIt()
    {
        string[] excluded = [@"D:\Media\TV", @"E:\Private"];

        Assert.True(MetadataPolicy.Excludes(excluded, Show));
        Assert.True(MetadataPolicy.Excludes(excluded, @"d:\media\tv\other.mkv"));
        Assert.True(MetadataPolicy.Excludes(excluded, @"D:\Media\TV"));
        Assert.False(MetadataPolicy.Excludes(excluded, @"D:\Media\TVShows\file.mkv"));
        Assert.False(MetadataPolicy.Excludes(excluded, @"D:\Media\Movies\file.mkv"));
        Assert.False(MetadataPolicy.Excludes([], Show));
    }

    [Fact]
    public void AnAllowedKindStillSkipsAnExcludedFile()
    {
        var settings = new MetadataSettings { TvShows = true, Excluded = [@"D:\Media\TV"] };

        Assert.False(MetadataPolicy.Allows(settings, MetadataWork.TvShow, Show));
        Assert.True(MetadataPolicy.Allows(settings, MetadataWork.TvShow, @"D:\Elsewhere\Show S01E01.mkv"));
    }

    [Fact]
    public void AnUnknownKindIsNeverAllowed() =>
        Assert.False(MetadataPolicy.Allows(new MetadataSettings { TvShows = true, Movies = true, Music = true, TmdbApiKey = "k" }, (MetadataWork)99, Show));

    [Fact]
    public void NullsAreRefusedPlainly()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Excludes(null!, Show));
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Excludes([], null!));
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.MoviesReady(null!));
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Allows(null!, MetadataWork.TvShow, Show));
    }

    [Fact]
    public void NormalizeTrimsTheKeyAndCleansTheFolders()
    {
        var settings = new MetadataSettings
        {
            TmdbApiKey = "  abc123  ",
            Excluded = [@" D:\Media ", "", "   ", @"D:\Media", @"d:\media", @"E:\Other"],
        }.Normalize();

        Assert.Equal("abc123", settings.TmdbApiKey);
        Assert.Equal([@"D:\Media", @"E:\Other"], settings.Excluded);
    }

    [Fact]
    public void NormalizeSurvivesNullsReadFromDisk()
    {
        var settings = (new MetadataSettings { TmdbApiKey = null!, Excluded = null! }).Normalize();

        Assert.Equal("", settings.TmdbApiKey);
        Assert.Empty(settings.Excluded);
    }

    [Fact]
    public void PlayerSettingsCarryAndNormalizeTheGroup()
    {
        var settings = (new PlayerSettings() with { Metadata = null! }).Normalize();

        Assert.NotNull(settings.Metadata);
        Assert.False(settings.Metadata.TvShows);
    }

    [Fact]
    public void TheOldOnlineLookupsSwitchBecomesTheNewSwitchesOnce()
    {
        var migrated = (new PlayerSettings { OnlineLookups = true }).Normalize();
        Assert.True(migrated.Metadata is { TvShows: true, Movies: true, Music: true });
        Assert.False(migrated.OnlineLookups);

        // A kind the user already chose is their word on it; the old switch changes nothing then.
        var chosen = (new PlayerSettings { OnlineLookups = true, Metadata = new MetadataSettings { Music = true } }).Normalize();
        Assert.True(chosen.Metadata is { TvShows: false, Movies: false, Music: true });
        Assert.False(chosen.OnlineLookups);
    }
}
