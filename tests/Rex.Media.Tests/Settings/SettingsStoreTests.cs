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
        Assert.Equal(UpdateCadence.Daily, settings.UpdateChecks);
        Assert.Equal(UpdateMode.DownloadAndAsk, settings.UpdateMode);
        Assert.Null(settings.PendingUpdate);
        Assert.Empty(settings.Shortcuts);
        Assert.False(settings.SeekPreview);
        Assert.Equal(MediaOpenClick.DoubleClick, settings.MediaOpenClickMode);
        Assert.False(settings.OpenLibraryAtPlayingItem);
        Assert.Empty(settings.LibraryPlaces);
        Assert.Null(settings.LibraryRoute);
        Assert.True(settings.WriteDetailsIntoFiles);
        Assert.True(settings.LeaveFullScreenWhenLibraryOpens);
        Assert.True(settings.ShowPlayerWhenLibraryMediaStarts);
    }

    [Theory]
    [InlineData("Ask")]
    [InlineData("Always")]
    [InlineData("Never")]
    public void LegacyResumePreferencesAreIgnoredWithoutLosingOtherSettings(string oldChoice)
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, $$"""{"resumePlayback":"{{oldChoice}}","resumeAudioLongerThanMinutes":180,"volume":0.4,"theme":"Dark"}""");
        var settings = SettingsStore.Load(File);
        Assert.Equal(0.4, settings.Volume);
        Assert.Equal(ThemeChoice.Dark, settings.Theme);
        SettingsStore.Save(File, settings);
        var saved = System.IO.File.ReadAllText(File);
        Assert.DoesNotContain("resumePlayback", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("resumeAudioLongerThanMinutes", saved, StringComparison.Ordinal);
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
            MediaOpenClickMode = MediaOpenClick.SingleClick,
            LastUpdateCheck = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero),
            LastSeenVersion = "0.4.0",
            Shortcuts = new Dictionary<string, string> { ["play-pause"] = "Ctrl+P", ["stop"] = "" },
            SeekPreview = true,
            OpenLibraryAtPlayingItem = true,
            LibraryPlaces = new Dictionary<string, LibraryPlace>
            {
                ["Songs"] = new("file:C:\\Music\\Terminator.wav", "file:C:\\Music\\Sungba.wav"),
                ["Home"] = new(Offset: 240.5, Shelves: [0, 360]),
                ["Albums"] = new(),
                ["Genres"] = new(" ", null, -5),
            },
            LibraryRoute = new LibraryRoute("Artists", "Asake", null, "Work of Art"),
            WriteDetailsIntoFiles = false,
            AutomaticWhenEnergySaver = false,
            LeaveFullScreenWhenLibraryOpens = false,
            ShowPlayerWhenLibraryMediaStarts = false,
            LibraryViews = new Dictionary<string, LibraryViewChoice> { ["Albums"] = new(LibraryLook.Wall, LibrarySort.MostPlayed, true, LibraryGrouping.Letter, 240) },
        };

        SettingsStore.Save(File, saved);
        var loaded = SettingsStore.Load(File);

        // Lists compare by reference in a record, so they are compared by content apart.
        Assert.Equal(saved with { Shortcuts = loaded.Shortcuts, EqualizerGains = loaded.EqualizerGains, GlobalShortcuts = loaded.GlobalShortcuts, VisualOptions = loaded.VisualOptions, LibraryViews = loaded.LibraryViews, LibraryPlaces = loaded.LibraryPlaces, VideoStyleValues = loaded.VideoStyleValues }, loaded);
        Assert.Equal(saved.Shortcuts, loaded.Shortcuts);
        Assert.Equal(saved.EqualizerGains, loaded.EqualizerGains);
        Assert.Equal(saved.LibraryViews, loaded.LibraryViews);

        // Places that say nothing (the top, nothing chosen) are not kept; the rest come back whole.
        Assert.Equal(["Songs", "Home"], loaded.LibraryPlaces.Keys);
        Assert.Equal(saved.LibraryPlaces["Songs"], loaded.LibraryPlaces["Songs"]);
        Assert.Equal(240.5, loaded.LibraryPlaces["Home"].Offset);
        Assert.Equal([0, 360], loaded.LibraryPlaces["Home"].Shelves);
        Assert.Null((saved with { LibraryRoute = new LibraryRoute(" ") }).Normalize().LibraryRoute);
        var crowded = new PlayerSettings { LibraryPlaces = Enumerable.Range(0, LibraryPlace.Most + 5).ToDictionary(i => "Songs\n" + i, i => new LibraryPlace("file:" + i)) }.Normalize();
        Assert.Equal(LibraryPlace.Most, crowded.LibraryPlaces.Count);
        Assert.False(crowded.LibraryPlaces.ContainsKey("Songs\n0"));
        Assert.Equal(MediaOpenClick.SingleClick, loaded.MediaOpenClickMode);
        Assert.Equal(MediaOpenClick.DoubleClick, new PlayerSettings { MediaOpenClickMode = (MediaOpenClick)99 }.Normalize().MediaOpenClickMode);
    }

    [Fact]
    public void EveryPictureLookIsNamedSavedAndNormalized()
    {
        Assert.Equal(40, VideoLooks.Names.Count);
        Assert.Equal("Vivid", VideoLooks.Name(VideoLook.Vivid));
        Assert.Equal("Cyanotype", VideoLooks.Name(VideoLook.Cyanotype));
        Assert.Equal("Teal & orange", VideoLooks.Name(VideoLook.TealOrange));
        Assert.Equal("Infrared", VideoLooks.Name(VideoLook.Infrared));
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
    public void TheRemovalModeIsSavedAndAnUnknownOneRemoves()
    {
        Assert.Equal(RemovalMode.Remove, new PlayerSettings().SubjectRemoval);
        foreach (var mode in Enum.GetValues<RemovalMode>())
        {
            SettingsStore.Save(File, new PlayerSettings { SubjectRemoval = mode });
            Assert.Equal(mode, SettingsStore.Load(File).SubjectRemoval);
        }

        Assert.Equal(RemovalMode.Remove, new PlayerSettings { SubjectRemoval = (RemovalMode)7 }.Normalize().SubjectRemoval);
    }

    [Fact]
    public void SpatialVideoEffectsHaveStableNamesAndSafeSettings()
    {
        Assert.Equal(Enum.GetValues<VideoEffect>().Length, VideoEffects.Names.Count);
        Assert.Equal("Off", VideoEffects.Name(VideoEffect.Off));
        Assert.Equal("Off", VideoEffects.Name((VideoEffect)999));
        Assert.Equal(0, VideoEffects.Strength(-1));
        Assert.Equal(100, VideoEffects.Strength(1000));
        Assert.Equal(VideoEffect.PrismFlow, VideoEffects.Next(VideoEffect.Off));
        Assert.Equal(VideoEffect.Off, VideoEffects.Next(Enum.GetValues<VideoEffect>()[^1]));
        Assert.Equal(VideoEffect.Off, VideoEffects.Next((VideoEffect)999));
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
    public void AdditionalVideoEffectsKeepIndependentOptionsAndHistoryPolicy()
    {
        var newEffects = new[]
        {
            VideoEffect.Halftone, VideoEffect.ChromaticRift, VideoEffect.OilPaint,
            VideoEffect.GlyphMosaic, VideoEffect.ThermalMap, VideoEffect.TapeWear,
            VideoEffect.PixelSort, VideoEffect.MotionSmear,
        };
        foreach (var effect in newEffects)
        {
            var options = VideoStyleOptions.ForEffect(effect);
            Assert.Equal(4, options.Count);
            Assert.All(options, option => Assert.InRange(option.Default, option.Min, option.Max));
            Assert.NotEqual("Essentials", VideoEffects.Category(effect));
            var extras = VideoStyleOptions.Extras(null, options);
            Assert.Equal((options[2].Default, options[3].Default), extras);
            var values = VideoStyleOptions.With(null, options[2], options[2].Max);
            values = VideoStyleOptions.With(values, options[3], options[3].Min);
            Assert.Equal((options[2].Max, options[3].Min), VideoStyleOptions.Extras(values, options));
            Assert.All(VideoStyleOptions.ModeNames(effect), name => Assert.False(string.IsNullOrWhiteSpace(name)));
        }

        Assert.True(VideoEffects.UsesHistory(VideoEffect.MotionSmear, 65, 60, 0));
        Assert.False(VideoEffects.UsesHistory(VideoEffect.MotionSmear, 65, 0, 70));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.ChromaticRift, 65, 0, 30));
        Assert.False(VideoEffects.UsesHistory(VideoEffect.ChromaticRift, 65, 90, 0));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.GhostwireMotion, 65, 35, 0));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.GhostwireMotion, 65, 35, 2));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.GhostwireMotion, 65, 35, 1));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.GhostwireMotion, 65, 35, 3));
        Assert.False(VideoEffects.UsesHistory(VideoEffect.GhostwireMotion, 0, 35, 0));
        Assert.False(VideoEffects.UsesHistory(VideoEffect.ThermalMap, 100, 100, 100));
        Assert.Empty(VideoStyleOptions.ModeNames(VideoEffect.Halftone));
        Assert.Equal(4, VideoStyleOptions.ModeNames(VideoEffect.GlyphMosaic).Count);
        Assert.Equal(4, VideoStyleOptions.ModeNames(VideoEffect.ThermalMap).Count);
        Assert.Equal(4, VideoStyleOptions.ModeNames(VideoEffect.PixelSort).Count);
        Assert.Equal(4, VideoStyleOptions.ModeNames(VideoEffect.GhostwireMotion).Count);
        Assert.Equal((0, 0), VideoStyleOptions.Extras(null, VideoStyleOptions.ForEffect(VideoEffect.Off)));
        Assert.Throws<ArgumentNullException>(() => VideoStyleOptions.Extras(null, null!));
    }

    [Fact]
    public void SixthEffectSetHasNamedOptionsCategoriesAndHistoryPolicy()
    {
        var withModes = new[]
        {
            VideoEffect.LumenWeave, VideoEffect.DepthHaze, VideoEffect.ParticleVeil,
            VideoEffect.LensBloom, VideoEffect.StippleEngrave,
        };
        foreach (var effect in withModes.Append(VideoEffect.TimeEcho))
        {
            var options = VideoStyleOptions.ForEffect(effect);
            Assert.Equal(4, options.Count);
            Assert.All(options, option => Assert.InRange(option.Default, option.Min, option.Max));
            Assert.Equal(options.Count, options.Select(option => option.Key).Distinct().Count());
            Assert.DoesNotContain("Fine detail", options[1].Label, StringComparison.Ordinal);
            Assert.Equal((options[2].Default, options[3].Default), VideoStyleOptions.Extras(null, options));
        }

        foreach (var effect in withModes)
        {
            var modes = VideoStyleOptions.ModeNames(effect);
            Assert.Equal(4, modes.Count);
            Assert.Equal(modes.Count - 1, VideoStyleOptions.ForEffect(effect)[3].Max);
            Assert.EndsWith(".mode", VideoStyleOptions.ForEffect(effect)[3].Key, StringComparison.Ordinal);
        }

        Assert.Empty(VideoStyleOptions.ModeNames(VideoEffect.TimeEcho));
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.LumenWeave));
        Assert.Equal("Image-aware", VideoEffects.Category(VideoEffect.DepthHaze));
        Assert.Equal("Playful", VideoEffects.Category(VideoEffect.ParticleVeil));
        Assert.Equal("Essentials", VideoEffects.Category(VideoEffect.LensBloom));
        Assert.Equal("Print & texture", VideoEffects.Category(VideoEffect.StippleEngrave));
        Assert.Equal("Motion & geometry", VideoEffects.Category(VideoEffect.TimeEcho));
        Assert.Equal("Lumen weave", VideoEffects.Name(VideoEffect.LumenWeave));
        Assert.Equal("Time echo", VideoEffects.Name(VideoEffect.TimeEcho));

        Assert.True(VideoEffects.UsesHistory(VideoEffect.ParticleVeil, 65, 0, 0));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.TimeEcho, 65, 50, 40));
        Assert.False(VideoEffects.UsesHistory(VideoEffect.TimeEcho, 0, 50, 40));
        Assert.True(VideoEffects.UsesHistory(VideoEffect.MotionAmplify, 65, 50, 50));
        Assert.Equal("Motion & geometry", VideoEffects.Category(VideoEffect.MotionAmplify));
        Assert.Equal("Motion amplify", VideoEffects.Name(VideoEffect.MotionAmplify));
        Assert.Empty(VideoStyleOptions.ModeNames(VideoEffect.MotionAmplify));
        foreach (var effect in new[] { VideoEffect.LumenWeave, VideoEffect.DepthHaze, VideoEffect.LensBloom, VideoEffect.StippleEngrave })
        {
            Assert.False(VideoEffects.UsesHistory(effect, 65, 50, 3));
        }
    }

    [Fact]
    public void MosaicEffectsShareAGroupAndEachHasItsOwnNamedControls()
    {
        var mosaics = new[]
        {
            VideoEffect.GlyphMosaic, VideoEffect.BrickBuild, VideoEffect.StainedGlass, VideoEffect.CrossStitch,
            VideoEffect.LedWall, VideoEffect.DigitalRain, VideoEffect.DiceMosaic, VideoEffect.CubeMosaic,
            VideoEffect.HexTiles, VideoEffect.RetroPixels, VideoEffect.LowPoly,
        };
        Assert.Equal(mosaics, Enum.GetValues<VideoEffect>().Where(effect => VideoEffects.Category(effect) == "Mosaics"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in mosaics)
        {
            var options = VideoStyleOptions.ForEffect(effect);
            Assert.Equal(4, options.Count);
            Assert.All(options, option => Assert.True(keys.Add(option.Key), option.Key));
            Assert.All(options, option => Assert.InRange(option.Default, option.Min, option.Max));
            Assert.EndsWith("size (%)", options[1].Label, StringComparison.Ordinal);
            Assert.False(VideoEffects.UsesHistory(effect, 65, options[2].Default, options[3].Default));
            Assert.False(VideoEffects.UsesPointer(effect));
            if (options[3].Key.EndsWith(".mode", StringComparison.Ordinal))
            {
                Assert.Equal(options[3].Max + 1, VideoStyleOptions.ModeNames(effect).Count);
            }
        }

        Assert.Empty(VideoStyleOptions.ModeNames(VideoEffect.StainedGlass));
        Assert.Equal("Brick build", VideoEffects.Name(VideoEffect.BrickBuild));
        Assert.Equal("Retro pixels", VideoEffects.Name(VideoEffect.RetroPixels));
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
            Assert.Equal((effect is VideoEffect.GhostwireMotion or VideoEffect.Kaleidoscope or VideoEffect.Halftone or VideoEffect.ChromaticRift
                or VideoEffect.OilPaint or VideoEffect.GlyphMosaic or VideoEffect.ThermalMap or VideoEffect.TapeWear
                or VideoEffect.PixelSort or VideoEffect.MotionSmear or VideoEffect.LumenWeave or VideoEffect.DepthHaze
                or VideoEffect.ParticleVeil or VideoEffect.LensBloom or VideoEffect.StippleEngrave or VideoEffect.TimeEcho
                or VideoEffect.MotionAmplify)
                || VideoEffects.Category(effect) == "Mosaics" || effect > VideoEffect.MotionAmplify ? 4 : effect == VideoEffect.Off ? 0 : 2, options.Count);
            foreach (var option in options)
            {
                Assert.InRange(option.Default, option.Min, option.Max);
                values = VideoStyleOptions.With(values, option, option.Min - 999);
                Assert.Equal(option.Min, VideoStyleOptions.Read(values, option));
            }

            Assert.False(string.IsNullOrWhiteSpace(VideoEffects.Name(effect)));
        }

        var motion = VideoStyleOptions.ForEffect(VideoEffect.GhostwireMotion);
        Assert.Equal("Effect intensity (%)", motion[0].Label);
        Assert.Equal("Contour sensitivity (%)", motion[1].Label);
        Assert.Equal("Motion trail length (%)", motion[2].Label);
        Assert.Equal(35, motion[2].Default);
        Assert.Equal(3, motion[3].Max);
        Assert.Equal(3, motion[3].Default);
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.GhostwireMotion));
        Assert.Equal("Ghostwire motion", VideoEffects.Name(VideoEffect.GhostwireMotion));
        var restoredMotion = VideoStyleOptions.Reset(values, motion);
        Assert.All(motion, option => Assert.Equal(option.Default, VideoStyleOptions.Read(restoredMotion, option)));
        Assert.True(restoredMotion.ContainsKey(VideoStyleOptions.ForEffect(VideoEffect.Ghostwire)[0].Key));

        var resetOptions = VideoStyleOptions.ForEffect(VideoEffect.CursorLens);
        var reset = VideoStyleOptions.Reset(values, resetOptions);
        Assert.Equal(resetOptions[0].Default, VideoStyleOptions.Read(reset, resetOptions[0]));
        Assert.Equal(resetOptions[0].Default, reset[resetOptions[0].Key]);
        Assert.False(reset.ContainsKey(resetOptions[1].Key));
        Assert.True(reset.ContainsKey(VideoStyleOptions.ForEffect(VideoEffect.EdgeGravity)[0].Key));

        SettingsStore.Save(File, new PlayerSettings { VideoStyleValues = values });
        var loaded = SettingsStore.Load(File);
        Assert.Equal(values.Count, loaded.VideoStyleValues.Count);
        Assert.All(values, pair => Assert.Equal(pair.Value, loaded.VideoStyleValues[pair.Key]));

        var normalized = new PlayerSettings
        {
            VideoStyleValues = new Dictionary<string, int>
            {
                ["look.Cinema.intensity"] = -50,
                ["effect.Vortex.detail"] = 999,
                [""] = 11,
            },
        }.Normalize();
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
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.Ghostwire));
        Assert.Equal("Motion & geometry", VideoEffects.Category(VideoEffect.SliceShift));
        Assert.Equal("Interactive", VideoEffects.Category(VideoEffect.CursorLens));
        Assert.Equal("Interactive", VideoEffects.Category(VideoEffect.ColourSpotlight));
        Assert.Equal("Image-aware", VideoEffects.Category(VideoEffect.EdgeGravity));
        Assert.Equal("Image-aware", VideoEffects.Category(VideoEffect.ReliefEtch));
        Assert.Equal("Contours", VideoEffects.Category(VideoEffect.NeonEdges));
        Assert.True(VideoEffects.UsesPointer(VideoEffect.CursorLens));
        Assert.True(VideoEffects.UsesPointer(VideoEffect.ColourSpotlight));
        Assert.False(VideoEffects.UsesPointer(VideoEffect.EdgeGravity));
        Assert.False(VideoEffects.UsesPointer(VideoEffect.Ghostwire));
        Assert.Equal(100, VideoStyleOptions.Read(null, VideoStyleOptions.ForLook((VideoLook)1000)[0]));
        Assert.Empty(VideoStyleOptions.ForEffect((VideoEffect)1000));
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

        Assert.Equal(new PlayerSettings { FirstRunDone = true, Volume = 0.5, Shortcuts = loaded.Shortcuts, EqualizerGains = loaded.EqualizerGains, GlobalShortcuts = loaded.GlobalShortcuts, VisualOptions = loaded.VisualOptions, LibraryViews = loaded.LibraryViews, LibraryPlaces = loaded.LibraryPlaces, VideoStyleValues = loaded.VideoStyleValues }, loaded);
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
    public void UpdatesAreLookedForDailyUnlessTheUserChoseOtherwise()
    {
        // The old weekly default, saved before schema 3, becomes daily; never stays never.
        Assert.Equal(UpdateCadence.Daily, new PlayerSettings { Schema = 2, UpdateChecks = UpdateCadence.Weekly }.Normalize().UpdateChecks);
        Assert.Equal(UpdateCadence.Off, new PlayerSettings { Schema = 2, UpdateChecks = UpdateCadence.Off }.Normalize().UpdateChecks);
        Assert.Equal(UpdateCadence.Weekly, new PlayerSettings { Schema = 3, UpdateChecks = UpdateCadence.Weekly }.Normalize().UpdateChecks);
    }

    [Fact]
    public void APendingUpdateSavedBeforePauseAndCancelReadsAsNeitherPausedNorCancelled()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, """
            { "schema": 3, "pendingUpdate": { "version": "0.17.0", "installerName": "rexplayer-Setup-0.17.0.exe",
              "installerUrl": "https://example.com/a.exe", "checksumUrl": "https://example.com/a.exe.sha256",
              "notes": "What's Changed\n\u2022 Six picture effects", "stage": "Downloading" } }
            """);
        var pending = SettingsStore.Load(File).PendingUpdate!;

        Assert.Equal((UpdateStage.Downloading, false, false, false), (pending.Stage, pending.Paused, pending.Declined, pending.InstallCancelled));
        Assert.Equal([new Rex.Media.AppCore.Updates.NoteBlock(Rex.Media.AppCore.Updates.NoteKind.Bullet, "Six picture effects")],
            Rex.Media.AppCore.Updates.ReleaseNotes.Blocks(pending.Notes));
    }

    [Fact]
    public void AFileFromBeforeUpdateModesReadsAsNothingPendingAndMigrates()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, """{ "schema": 2, "updateChecks": "Weekly", "lastSeenVersion": "0.15.11" }""");
        var loaded = SettingsStore.Load(File);

        Assert.Equal((UpdateCadence.Daily, UpdateMode.DownloadAndAsk, (PendingUpdate?)null, (string?)null), (loaded.UpdateChecks, loaded.UpdateMode, loaded.PendingUpdate, loaded.SkippedUpdateVersion));
    }

    [Fact]
    public void APendingUpdateComesBackTheSame()
    {
        var pending = new PendingUpdate
        {
            Version = "0.16.0",
            InstallerName = "rexplayer-Setup-0.16.0.exe",
            InstallerUrl = "https://example.com/a.exe",
            ChecksumUrl = "https://example.com/a.exe.sha256",
            ReleasePage = "https://example.com/release",
            Size = 42,
            Notes = "Faster",
            NeedsAttention = true,
            Stage = UpdateStage.Ready,
            Timing = UpdateTiming.Scheduled,
            Approved = true,
            Paused = true,
            Declined = true,
            InstallCancelled = true,
            RemindAt = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero),
            InstallAt = new DateTimeOffset(2026, 10, 13, 3, 0, 0, TimeSpan.Zero),
            DownloadFailures = 2,
            RetryAt = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
            LastError = "the disk is full",
            InstallFailures = 1,
            HandedOffAt = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero),
        };
        var saved = new PlayerSettings
        {
            PendingUpdate = pending,
            UpdateMode = UpdateMode.Automatic,
            SkippedUpdateVersion = "0.15.12",
            LastUpdatedVersion = "0.15.11",
            LastUpdatedAt = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero),
        };

        SettingsStore.Save(File, saved);
        var loaded = SettingsStore.Load(File);

        Assert.Equal(pending, loaded.PendingUpdate);
        Assert.Equal((UpdateMode.Automatic, "0.15.12", "0.15.11", saved.LastUpdatedAt), (loaded.UpdateMode, loaded.SkippedUpdateVersion, loaded.LastUpdatedVersion, loaded.LastUpdatedAt));
    }

    [Fact]
    public void APendingUpdateThatCannotBeRexplayersIsDropped()
    {
        var good = new PendingUpdate
        {
            Version = "0.16.0",
            InstallerName = "rexplayer-Setup-0.16.0.exe",
            InstallerUrl = "https://example.com/a.exe",
            ChecksumUrl = "https://example.com/a.exe.sha256",
        };
        Assert.NotNull(good.Normalize());

        Assert.Null((good with { Version = "0.16" }).Normalize());
        Assert.Null((good with { Version = "0.16.x" }).Normalize());
        Assert.Null((good with { Version = "0..0" }).Normalize());
        Assert.Null((good with { Version = null! }).Normalize());
        Assert.Null((good with { InstallerName = "evil.exe" }).Normalize());
        Assert.Null((good with { InstallerUrl = "http://example.com/a.exe" }).Normalize());
        Assert.Null((good with { ChecksumUrl = "not a link" }).Normalize());
        Assert.Null(new PlayerSettings { PendingUpdate = good with { InstallerUrl = "" } }.Normalize().PendingUpdate);

        var wild = (good with
        {
            ReleasePage = "file:///c:/x",
            Size = -5,
            Notes = new string('n', PendingUpdate.MaxNotesLength + 10),
            Stage = (UpdateStage)9,
            Timing = (UpdateTiming)9,
            DownloadFailures = -1,
            InstallFailures = 5000,
        }).Normalize()!;
        Assert.Equal("https://github.com/tochi-mba/rexplayer/releases/latest", wild.ReleasePage);
        Assert.Equal((0L, PendingUpdate.MaxNotesLength), (wild.Size, wild.Notes.Length));
        Assert.Equal((UpdateStage.Downloading, UpdateTiming.Ask, 0, 1000), (wild.Stage, wild.Timing, wild.DownloadFailures, wild.InstallFailures));
        Assert.Equal("", (good with { Notes = null! }).Normalize()!.Notes);
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
            UpdateMode = (UpdateMode)9,
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
        Assert.Equal((UpdateCadence.Daily, UpdateMode.DownloadAndAsk), (wild.UpdateChecks, wild.UpdateMode));
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
