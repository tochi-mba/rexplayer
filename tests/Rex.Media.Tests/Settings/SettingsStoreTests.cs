using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void WithNoFileTheDefaultsAreUsed()
    {
        var settings = SettingsStore.Load(File);

        Assert.Equal(1, settings.Volume);
        Assert.Equal(RepeatMode.Off, settings.Repeat);
        Assert.True(settings.SingleInstance);
        Assert.False(settings.OnlineLookups);
        Assert.Equal(UpdateCadence.Weekly, settings.UpdateChecks);
        Assert.Empty(settings.Shortcuts);
        Assert.False(settings.SeekPreview);
    }

    [Fact]
    public void SavedSettingsComeBackTheSame()
    {
        var saved = new PlayerSettings
        {
            Volume = 0.4,
            Muted = true,
            Repeat = RepeatMode.All,
            Shuffle = true,
            Window = new WindowPlacement(10, 20, 800, 600, Maximized: true),
            Theme = ThemeChoice.Dark,
            LastUpdateCheck = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero),
            LastSeenVersion = "0.4.0",
            Shortcuts = new Dictionary<string, string> { ["play-pause"] = "Ctrl+P", ["stop"] = "" },
            SeekPreview = true,
            LibraryViews = new Dictionary<string, LibraryViewChoice> { ["Albums"] = new(LibraryLook.Wall, LibrarySort.MostPlayed, true, LibraryGrouping.Letter, 240) },
        };

        SettingsStore.Save(File, saved);
        var loaded = SettingsStore.Load(File);

        // Lists compare by reference in a record, so they are compared by content apart.
        Assert.Equal(saved with { Shortcuts = loaded.Shortcuts, EqualizerGains = loaded.EqualizerGains, GlobalShortcuts = loaded.GlobalShortcuts, VisualOptions = loaded.VisualOptions, LibraryViews = loaded.LibraryViews, VideoStyleValues = loaded.VideoStyleValues }, loaded);
        Assert.Equal(saved.Shortcuts, loaded.Shortcuts);
        Assert.Equal(saved.EqualizerGains, loaded.EqualizerGains);
        Assert.Equal(saved.LibraryViews, loaded.LibraryViews);
    }

    [Fact]
    public void EveryPictureLookIsNamedSavedAndNormalized()
    {
        Assert.Equal(9, VideoLooks.Names.Count);
        Assert.Equal("Original", VideoLooks.Name(VideoLook.Original));
        Assert.Equal("Original", VideoLooks.Name((VideoLook)1_000));
        foreach (var look in Enum.GetValues<VideoLook>())
        {
            Assert.False(string.IsNullOrWhiteSpace(VideoLooks.Name(look)));
            SettingsStore.Save(File, new PlayerSettings { VideoLook = look });
            Assert.Equal(look, SettingsStore.Load(File).VideoLook);
        }

        Assert.Equal(VideoLook.Original, new PlayerSettings { VideoLook = (VideoLook)999 }.Normalize().VideoLook);
    }

    [Fact]
    public void SpatialVideoEffectsHaveStableNamesAndSafeSettings()
    {
        Assert.Equal(13, VideoEffects.Names.Count);
        Assert.Equal("Off", VideoEffects.Name(VideoEffect.Off));
        Assert.Equal("Off", VideoEffects.Name((VideoEffect)999));
        Assert.Equal(0, VideoEffects.Strength(-1));
        Assert.Equal(100, VideoEffects.Strength(1000));
        Assert.Equal(VideoEffect.Off, new PlayerSettings().VideoEffect);
        Assert.Equal(65, new PlayerSettings().VideoEffectStrength);

        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            Assert.False(string.IsNullOrWhiteSpace(VideoEffects.Name(effect)));
            SettingsStore.Save(File, new PlayerSettings { VideoEffect = effect, VideoEffectStrength = 82 });
            var loaded = SettingsStore.Load(File);
            Assert.Equal(effect, loaded.VideoEffect);
            Assert.Equal(82, loaded.VideoEffectStrength);
        }

        var invalid = new PlayerSettings { VideoEffect = (VideoEffect)1000, VideoEffectStrength = 900 }.Normalize();
        Assert.Equal(VideoEffect.Off, invalid.VideoEffect);
        Assert.Equal(100, invalid.VideoEffectStrength);
        Assert.Equal(0, new PlayerSettings { VideoEffectStrength = -9 }.Normalize().VideoEffectStrength);
    }

    [Fact]
    public void EveryPictureStyleSavesIndependentControlsAndCanResetOnlyItself()
    {
        IReadOnlyDictionary<string, int> values = new Dictionary<string, int>();
        foreach (var look in Enum.GetValues<VideoLook>())
        {
            var options = VideoStyleOptions.ForLook(look);
            Assert.Equal(2, options.Count);
            foreach (var option in options)
            {
                Assert.InRange(option.Default, option.Min, option.Max);
                Assert.Equal(option.Default, VideoStyleOptions.Read(null, option));
                values = VideoStyleOptions.With(values, option, option.Max + 999);
                Assert.Equal(option.Max, VideoStyleOptions.Read(values, option));
            }
        }

        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            var options = VideoStyleOptions.ForEffect(effect);
            Assert.Equal(2, options.Count);
            foreach (var option in options)
            {
                Assert.InRange(option.Default, option.Min, option.Max);
                values = VideoStyleOptions.With(values, option, option.Min - 999);
                Assert.Equal(option.Min, VideoStyleOptions.Read(values, option));
            }

            Assert.False(string.IsNullOrWhiteSpace(VideoEffects.Name(effect)));
        }

        var resetOptions = VideoStyleOptions.ForEffect(VideoEffect.CursorLens);
        var reset = VideoStyleOptions.Reset(values, resetOptions);
        Assert.Equal(resetOptions[0].Default, VideoStyleOptions.Read(reset, resetOptions[0]));
        Assert.False(reset.ContainsKey(resetOptions[0].Key));
        Assert.True(reset.ContainsKey(VideoStyleOptions.ForEffect(VideoEffect.EdgeGravity)[0].Key));

        SettingsStore.Save(File, new PlayerSettings { VideoStyleValues = values });
        var loaded = SettingsStore.Load(File);
        Assert.Equal(values.Count, loaded.VideoStyleValues.Count);
        Assert.All(values, pair => Assert.Equal(pair.Value, loaded.VideoStyleValues[pair.Key]));

        var normalized = new PlayerSettings { VideoStyleValues = new Dictionary<string, int>
        {
            ["look.Cinema.intensity"] = -50,
            ["effect.Vortex.detail"] = 999,
            [""] = 11,
        } }.Normalize();
        Assert.Equal(0, normalized.VideoStyleValues["look.Cinema.intensity"]);
        Assert.Equal(200, normalized.VideoStyleValues["effect.Vortex.detail"]);
        Assert.False(normalized.VideoStyleValues.ContainsKey(""));

        Assert.Throws<ArgumentNullException>(() => VideoStyleOptions.Read(values, null!));
        Assert.Throws<ArgumentNullException>(() => VideoStyleOptions.With(values, null!, 9));
        Assert.Throws<ArgumentNullException>(() => VideoStyleOptions.Reset(values, null!));
        Assert.Equal(VideoEffect.Off, new PlayerSettings { VideoEffect = (VideoEffect)1000 }.Normalize().VideoEffect);
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.InkTrace));
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.TopographicContours));
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.ChromaticContours));
        Assert.Equal("Motion & geometry", VideoEffects.Category(VideoEffect.SliceShift));
        Assert.Equal("Interactive", VideoEffects.Category(VideoEffect.CursorLens));
        Assert.Equal("Image-aware", VideoEffects.Category(VideoEffect.EdgeGravity));
        Assert.Equal("Essentials", VideoEffects.Category(VideoEffect.NeonEdges));
        Assert.True(VideoEffects.UsesPointer(VideoEffect.CursorLens));
        Assert.False(VideoEffects.UsesPointer(VideoEffect.EdgeGravity));
        Assert.Equal(100, VideoStyleOptions.Read(null, VideoStyleOptions.ForLook((VideoLook)1000)[0]));
        Assert.Equal(0, VideoStyleOptions.Read(null, VideoStyleOptions.ForEffect((VideoEffect)1000)[0]));
    }

    [Fact]
    public void TheFileIsReadableJsonWithNamedValues()
    {
        SettingsStore.Save(File, new PlayerSettings { Repeat = RepeatMode.One });

        var text = System.IO.File.ReadAllText(File);
        Assert.Contains("\"repeat\": \"One\"", text, StringComparison.Ordinal);
        Assert.Contains($"\"schema\": {PlayerSettings.CurrentSchema}", text, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("SYS-02")]
    public void ADamagedFileFallsBackToTheBackupThenToTheDefaults()
    {
        SettingsStore.Save(File, new PlayerSettings { Volume = 0.25 });
        SettingsStore.Save(File, new PlayerSettings { Volume = 0.5 });
        System.IO.File.WriteAllText(File, "{ not json");

        Assert.Equal(0.25, SettingsStore.Load(File).Volume);

        System.IO.File.WriteAllText(File + AtomicFile.BackupSuffix, "\"a string, not settings\"");
        Assert.Equal(1, SettingsStore.Load(File).Volume);
    }

    [Fact]
    public void HandEditedFilesWithCommentsAndTrailingCommasAreRead()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "{\n  // quieter\n  \"volume\": 0.3,\n}\n", Encoding.UTF8);

        Assert.Equal(0.3, SettingsStore.Load(File).Volume);
    }

    [Fact]
    public void SettingsAFileLeavesOutKeepTheirDefaults()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, """{ "firstRunDone": true, "volume": 0.5 }""");

        var loaded = SettingsStore.Load(File);

        Assert.Equal(new PlayerSettings { FirstRunDone = true, Volume = 0.5, Shortcuts = loaded.Shortcuts, EqualizerGains = loaded.EqualizerGains, GlobalShortcuts = loaded.GlobalShortcuts, VisualOptions = loaded.VisualOptions, LibraryViews = loaded.LibraryViews }, loaded);
        Assert.Equal(new double[10], loaded.EqualizerGains);
        Assert.True(loaded.SingleInstance);
        Assert.Equal(10, loaded.ShortJumpSeconds);
    }

    [Fact]
    public void TheLoudestVolumeIs200PercentUnlessTheUserChoseAnother()
    {
        Assert.Equal(200, new PlayerSettings().MaxVolumePercent);

        // The old default, saved before schema 2, becomes the new one; a choice stays a choice.
        Assert.Equal(200, new PlayerSettings { Schema = 1, MaxVolumePercent = 125 }.Normalize().MaxVolumePercent);
        Assert.Equal(150, new PlayerSettings { Schema = 1, MaxVolumePercent = 150 }.Normalize().MaxVolumePercent);
        Assert.Equal(125, new PlayerSettings { Schema = 2, MaxVolumePercent = 125 }.Normalize().MaxVolumePercent);
    }

    [Fact]
    public void ValuesOutOfRangeAreBroughtBackIntoIt()
    {
        var wild = new PlayerSettings
        {
            Schema = 0,
            Volume = 9,
            MaxVolumePercent = 900,
            VolumeStepPercent = 0,
            Repeat = (RepeatMode)42,
            VeryShortJumpSeconds = 0,
            ShortJumpSeconds = -5,
            MediumJumpSeconds = 99_999,
            LongJumpSeconds = 7200,
            Window = new WindowPlacement(0, 0, 20, 20, false),
            AlwaysOnTop = (AlwaysOnTop)9,
            ControlsHideSeconds = double.NaN,
            TitleSeconds = 100,
            Theme = (ThemeChoice)9,
            UpdateChecks = (UpdateCadence)9,
            AudioArtworkStyle = (ArtworkStyle)9,
            AudioArtworkColor = -1,
            AudioArtworkDetail = 500,
            AudioArtworkContrast = 201,
            Shortcuts = null!,
        }.Normalize();

        Assert.Equal(PlayerSettings.CurrentSchema, wild.Schema);
        Assert.Equal(2, wild.Volume);
        Assert.Equal(200, wild.MaxVolumePercent);
        Assert.Equal(1, wild.VolumeStepPercent);
        Assert.Equal(RepeatMode.Off, wild.Repeat);
        Assert.Equal((1, 1, 3600, 3600), (wild.VeryShortJumpSeconds, wild.ShortJumpSeconds, wild.MediumJumpSeconds, wild.LongJumpSeconds));
        Assert.Null(wild.Window);
        Assert.Equal(AlwaysOnTop.Never, wild.AlwaysOnTop);
        Assert.Equal(1.5, wild.ControlsHideSeconds);
        Assert.Equal(30, wild.TitleSeconds);
        Assert.Equal(ThemeChoice.System, wild.Theme);
        Assert.Equal(UpdateCadence.Weekly, wild.UpdateChecks);
        Assert.Equal((ArtworkStyle.Prism, 0, 200, 200), (wild.AudioArtworkStyle, wild.AudioArtworkColor, wild.AudioArtworkDetail, wild.AudioArtworkContrast));
        Assert.Empty(wild.Shortcuts);

        var tame = new PlayerSettings { Volume = double.PositiveInfinity, MaxVolumePercent = 10, TitleSeconds = double.NaN, ControlsHideSeconds = 99 }.Normalize();
        Assert.Equal((1.0, 100, 3.0, 10.0), (tame.Volume, tame.MaxVolumePercent, tame.TitleSeconds, tame.ControlsHideSeconds));
        Assert.Equal(1.25, new PlayerSettings { Volume = 1.6, MaxVolumePercent = 125 }.Normalize().Volume);
    }

    [Fact]
    public void SoundSettingsAreKeptInRange()
    {
        var wild = new PlayerSettings { EqualizerPreamp = 99, EqualizerGains = [50, double.NaN], Stereo = (StereoChoice)9, Loudness = (LoudnessChoice)9, LoudnessPreamp = -99 }.Normalize();

        Assert.Equal(20, wild.EqualizerPreamp);
        Assert.Equal([20, 0, 0, 0, 0, 0, 0, 0, 0, 0], wild.EqualizerGains);
        Assert.Equal((StereoChoice.Stereo, LoudnessChoice.Off, -20.0), (wild.Stereo, wild.Loudness, wild.LoudnessPreamp));
        Assert.Equal(10, new PlayerSettings { EqualizerGains = null! }.Normalize().EqualizerGains.Count);
    }
}
