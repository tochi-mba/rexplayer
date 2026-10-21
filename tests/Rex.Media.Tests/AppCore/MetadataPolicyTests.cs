using Rex.Media.AppCore.Metadata;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Online lookups are strictly off by default, each kind has its own switch and needs nothing else
/// (no key, no sign-up), and a file under an excluded folder is never looked up at all.
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
        Assert.Empty(fresh.Excluded);
        foreach (var kind in Enum.GetValues<MetadataWork>())
        {
            Assert.False(MetadataPolicy.Allows(fresh, kind, Show));
        }
    }

    [Theory]
    [InlineData(MetadataWork.TvShow)]
    [InlineData(MetadataWork.Movie)]
    [InlineData(MetadataWork.Music)]
    public void EachSwitchAloneAllowsOnlyItsOwnKind(MetadataWork kind)
    {
        var settings = new MetadataSettings
        {
            TvShows = kind == MetadataWork.TvShow,
            Movies = kind == MetadataWork.Movie,
            Music = kind == MetadataWork.Music,
        };

        foreach (var asked in Enum.GetValues<MetadataWork>())
        {
            Assert.Equal(asked == kind, MetadataPolicy.Allows(settings, asked, Show));
        }
    }

    [Fact]
    public void AKeyStoredByAnEarlierVersionIsDroppedAndItsMoviesSwitchKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-metadata-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{"schema":3,"metadata":{"movies":true,"tvShows":false,"tmdbApiKey":"abc123"}}""");

            var loaded = SettingsStore.Load(path);
            SettingsStore.Save(path, loaded);

            Assert.True(loaded.Metadata.Movies);
            Assert.True(MetadataPolicy.Allows(loaded.Metadata, MetadataWork.Movie, Show));
            Assert.DoesNotContain("tmdb", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            Assert.True(SettingsStore.Load(path).Metadata.Movies);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
        Assert.False(MetadataPolicy.Allows(new MetadataSettings { TvShows = true, Movies = true, Music = true }, (MetadataWork)99, Show));

    [Fact]
    public void NullsAreRefusedPlainly()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Excludes(null!, Show));
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Excludes([], null!));
        Assert.Throws<ArgumentNullException>(() => MetadataPolicy.Allows(null!, MetadataWork.TvShow, Show));
    }

    [Fact]
    public void NormalizeCleansTheFolders()
    {
        var settings = new MetadataSettings
        {
            Excluded = [@" D:\Media ", "", "   ", @"D:\Media", @"d:\media", @"E:\Other"],
        }.Normalize();

        Assert.Equal([@"D:\Media", @"E:\Other"], settings.Excluded);
    }

    [Fact]
    public void NormalizeSurvivesNullsReadFromDisk()
    {
        var settings = (new MetadataSettings { Excluded = null! }).Normalize();

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
