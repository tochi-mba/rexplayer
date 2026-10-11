using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The window's own features, each driven through the menus or the automation pipe and checked
/// on the real window: menus, full screen, always on top, the minimal interface, subtitles, sizing
/// to the video, preferences, media information, the log, and names for everything a screen reader meets.
/// </summary>
[Collection("desktop")]
public sealed class AppWindowTests : IDisposable
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "rexplayer-ui-media-" + Guid.NewGuid().ToString("N"));

    public AppWindowTests() => Directory.CreateDirectory(_media);

    public void Dispose() => Directory.Delete(_media, recursive: true);

    private string Song(int seconds)
    {
        var path = Path.Combine(_media, "Sungba.wav");
        File.WriteAllBytes(path, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * seconds])).Build());
        return path;
    }

    /// <summary>
    /// A song with a beat: a kick on every beat at 120 a minute, hardest on the first of each bar,
    /// a snare on two and four, over a held chord. 48 kHz stereo, so the visualisations hear it all.
    /// </summary>
    private string Music(int seconds)
    {
        const int Rate = 48_000;
        var random = new Random(5);
        var samples = new float[Rate * seconds * 2];
        for (var i = 0; i < Rate * seconds; i++)
        {
            var t = (double)i / Rate;
            var (beat, since) = ((int)(t / 0.5), t % 0.5);
            var kick = (beat % 4 == 0 ? 0.85 : 0.5) * Math.Sin(Math.Tau * (50 + (90 * Math.Exp(-since * 30))) * since) * Math.Exp(-since * 12);
            var snare = beat % 2 == 1 ? 0.25 * ((random.NextDouble() * 2) - 1) * Math.Exp(-since * 25) : 0;
            var pad = 0.06 * (Math.Sin(Math.Tau * 220 * t) + Math.Sin(Math.Tau * 277.2 * t) + Math.Sin(Math.Tau * 329.6 * t));
            samples[i * 2] = samples[(i * 2) + 1] = (float)Math.Clamp(kick + snare + pad, -1, 1);
        }

        var path = Path.Combine(_media, "Terminator.wav");
        File.WriteAllBytes(path, WavBuilder.Pcm(Rate, 2, 16, Pcm.Int16(samples)).Build());
        return path;
    }

    /// <summary>A scratch data folder whose settings start with <paramref name="visualisation"/> showing.</summary>
    private static string RootShowing(string visualisation)
    {
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), "{ \"firstRunDone\": true, \"lastSeenVersion\": \"" + AppProcess.Version + "\", \"updateChecks\": \"Off\", \"visualizer\": \"" + visualisation + "\" }");
        return root;
    }

    private static AutomationElement[] All(AutomationElement root, ControlType type) =>
        [.. root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type)).Cast<AutomationElement>()];

    [Fact]
    [Capability("UI-02")]
    public void EveryMenuOffersItsCommandsByName()
    {
        using var app = AppProcess.Start();
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var menu in new[] { "MediaMenu", "PlaybackMenu", "AudioMenu", "SubtitleMenu", "VideoMenu", "ViewMenu", "HelpMenu" })
        {
            var expander = (ExpandCollapsePattern)app.Find(menu).GetCurrentPattern(ExpandCollapsePattern.Pattern);
            expander.Expand();
            var items = Wait.Until(() => All(app.Window, ControlType.MenuItem).Where(item => item.Current.AutomationId.StartsWith("Command-", StringComparison.Ordinal)).ToArray() is { Length: > 0 } some ? some : null);
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Current.Name), item.Current.AutomationId));
            found.UnionWith(items.Select(item => item.Current.AutomationId["Command-".Length..]));
            expander.Collapse();
        }

        Assert.Superset(
            new HashSet<string>([CommandCatalog.OpenFile, CommandCatalog.PlayPause, CommandCatalog.CycleAudioTrack, CommandCatalog.AddSubtitles, CommandCatalog.ToggleFullScreen, CommandCatalog.CycleCrop, CommandCatalog.Preferences, CommandCatalog.ShowLog, CommandCatalog.CheckForUpdates]),
            found);
        Assert.Equal(0, app.Close());
    }


    [Fact]
    [Capability("VID-23")]
    public void VideoLookMenuChangesTheSavedStyleAndReturnsToOriginal()
    {
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        var videoMenu = (ExpandCollapsePattern)app.Find("VideoMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        videoMenu.Expand();
        var looks = (ExpandCollapsePattern)app.Find("VideoLook-Menu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        looks.Expand();
        app.Press("VideoLook-Monochrome");
        Wait.For(() => app.SavedSettings.VideoLook == VideoLook.Monochrome, "the picture look to be saved");

        videoMenu.Expand();
        looks = (ExpandCollapsePattern)app.Find("VideoLook-Menu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        looks.Expand();
        app.Press("VideoLook-Original");
        Wait.For(() => app.SavedSettings.VideoLook == VideoLook.Original, "Original to restore the video");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("VID-23")]
    public void VideoEffectsAreSelectedInTheirOwnMenuAndSavedIndependentlyFromLooks()
    {
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        var menu = (ExpandCollapsePattern)app.Find("VideoMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        var effects = (ExpandCollapsePattern)app.Find("VideoEffect-Menu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        effects.Expand();
        app.Press("VideoEffect-NeonEdges");
        Wait.For(() => app.SavedSettings.VideoEffect == VideoEffect.NeonEdges, "the effect choice to be saved");
        Assert.Equal(VideoLook.Original, app.SavedSettings.VideoLook);

        menu.Expand();
        effects = (ExpandCollapsePattern)app.Find("VideoEffect-Menu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        effects.Expand();
        app.Press("VideoEffect-Off");
        Wait.For(() => app.SavedSettings.VideoEffect == VideoEffect.Off, "effect Off to be restored");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SubjectLockControlsAreIndependentOfPictureEffectsAndCanCloseCleanly()
    {
        // The sub-second movie fixture can finish during the welcome animation. A still
        // picture keeps the presentation surface available for deterministic toolbar checks.
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.snapshot-0.24.png")]);
        Wait.For(() => !app.IsShown("VideoBlank"), "the picture to be visible");

        var videoMenu = (ExpandCollapsePattern)app.Find("VideoMenu")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern);
        videoMenu.Expand();
        app.Press("VideoSubjectLock");
        Wait.For(() => app.IsShown("SubjectSelect"), "Subject Lock tools to open");
        Assert.Contains("Drag", app.Text("SubjectStatusText"), StringComparison.Ordinal);
        Assert.Equal(3.0, ((RangeValuePattern)app.Find("SubjectFeather")
            .GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value);
        Assert.Equal(50.0, ((RangeValuePattern)app.Find("SubjectTolerance")
            .GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value);
        Assert.Equal(VideoEffect.Off, app.SavedSettings.VideoEffect);

        // The primary actions must fit horizontally inside the picture. The remaining
        // controls are in a vertically scrollable card, never in a clipped action row.
        var stage = app.Find("Stage").Current.BoundingRectangle;
        foreach (var id in new[] { "SubjectSelect", "SubjectClose" })
        {
            var action = app.Find(id).Current.BoundingRectangle;
            Assert.True(action.Left >= stage.Left && action.Right <= stage.Right + 1,
                $"{id} is clipped by the right edge of the picture");
        }

        Assert.Equal(ToggleState.On, ((TogglePattern)app.Find("SubjectFollow")
            .GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        Assert.Equal(ToggleState.On, ((TogglePattern)app.Find("SubjectShowBox")
            .GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        app.Toggle("SubjectShowBox");
        Assert.Equal(ToggleState.Off, ((TogglePattern)app.Find("SubjectShowBox")
            .GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        app.Toggle("SubjectFollow");
        Assert.Equal(ToggleState.Off, ((TogglePattern)app.Find("SubjectFollow")
            .GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        app.Toggle("SubjectShowBox");
        app.Toggle("SubjectFollow");

        // This invokes actual presenter selection, rather than merely opening the toolbar.
        // Even a low-texture fixture must respond (either a lock or an explicit refusal).
        app.Press("SubjectCenter");
        Wait.For(() =>
        {
            var status = app.Text("SubjectStatusText");
            return status.Contains("Tracking selected region", StringComparison.Ordinal)
                || status.Contains("Selected region locked", StringComparison.Ordinal)
                || status.Contains("too little distinguishing detail", StringComparison.Ordinal);
        },
            "the selection command to reach the video presenter");

        app.Press("SubjectReset");
        Assert.True(app.IsShown("SubjectSelect"));
        app.Press("SubjectClose");
        Wait.For(() => !app.IsShown("SubjectSelect"), "the selection tools to close");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-06")]
    public void PlaybackSpeedCanBeSlowedRaisedAndRestoredFromItsControl()
    {
        using var app = AppProcess.Start([Song(30)]);

        Wait.For(() => app.Text("SpeedButton") == "Playback speed, 1\u00D7", "the normal speed to be shown");
        app.Press("SpeedButton");
        Assert.Contains("current speed", app.Find("SpeedRate-1").Current.Name, StringComparison.Ordinal);
        app.Press("SpeedRate-0.5");
        Wait.For(() => app.Text("SpeedButton") == "Playback speed, 0.5\u00D7", "the slower speed to be shown");

        app.Run(CommandCatalog.SlightlyFaster);
        Wait.For(() => app.Text("SpeedButton") == "Playback speed, 0.6\u00D7", "the fine speed step to be shown");
        app.Press("SpeedButton");
        app.Press("SpeedRate-2");
        Wait.For(() => app.Text("SpeedButton") == "Playback speed, 2\u00D7", "the faster speed to be shown");

        app.Run(CommandCatalog.NormalSpeed);
        Wait.For(() => app.Text("SpeedButton") == "Playback speed, 1\u00D7", "the reset speed to be shown");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("VID-03")]
    [Capability("UI-04")]
    public void FullScreenHidesTheMenusAndLeavingBringsThemBack()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "the menus to go in full screen");
        Assert.True(app.IsShown("PlayPauseButton"), "Moving into full screen shows the controls for a moment.");

        app.Run(CommandCatalog.LeaveFullScreen);
        Wait.For(() => app.IsShown("Menu"), "the menus to come back");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("VID-27")]
    public void AlwaysOnTopKeepsTheWindowAboveOthersUntilTurnedOff()
    {
        using var app = AppProcess.Start();
        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
        Assert.False(window.Current.IsTopmost);

        app.Run(CommandCatalog.ToggleAlwaysOnTop);
        Wait.For(() => window.Current.IsTopmost, "the window to stay on top");
        app.Run(CommandCatalog.ToggleAlwaysOnTop);
        Wait.For(() => !window.Current.IsTopmost, "the window to stop staying on top");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("SUB-14")]
    [Capability("OSD-01")]
    public void SubtitlesBesideAFilmShowOverItsPicture()
    {
        var film = Path.Combine(_media, "Film.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), film);
        File.WriteAllText(Path.Combine(_media, "Film.en.srt"), """
            1
            00:00:00,000 --> 00:10:00,000
            Hello from the subtitles
            """);
        using var app = AppProcess.Start([film]);

        Wait.For(() => app.IsShown("SubtitleText") && app.Text("SubtitleText") == "Hello from the subtitles", "the subtitle to show");

        app.Run(CommandCatalog.ToggleSubtitles);
        Wait.For(() => !app.IsShown("SubtitleText"), "the subtitle to go");
        app.Run(CommandCatalog.SubtitlesBigger);
        Wait.For(() => app.SavedSettings.SubtitleSize == 125, "the bigger size to be kept");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-18")]
    public void ASongShowsAVisualisationThatCanBeChanged()
    {
        var root = Path.Combine(_media, ".visualisations");
        Directory.CreateDirectory(root);
        Rex.Media.Settings.SettingsStore.Save(Path.Combine(root, "settings.json"), new Rex.Media.Settings.PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = AppProcess.Version,
            UpdateChecks = Rex.Media.Settings.UpdateCadence.Off,
            CameraAllowed = true,
        });
        using var app = AppProcess.Start([Song(30)], root);

        Wait.For(() => app.IsShown("Visualizer"), "the visualisation");
        app.Run(CommandCatalog.CycleVisualizer);
        Wait.For(() => app.SavedSettings.Visualizer == Rex.Media.Settings.VisualizerChoice.Oscilloscope, "the next visualisation to be kept");
        Assert.True(app.IsShown("Visualizer"));
        for (var i = Enum.GetValues<Rex.Media.Settings.VisualizerChoice>().Length - 2; i > 0; i--)
        {
            app.Run(CommandCatalog.CycleVisualizer);
        }

        Wait.For(() => !app.IsShown("Visualizer"), "the visualisation to go");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData("Vinyl")]
    [InlineData("Halo")]
    [InlineData("Mirror")]
    [InlineData("Aurora")]
    [InlineData("Embers")]
    [InlineData("Ripples")]
    [InlineData("Strobe")]
    [InlineData("Resonance")]
    public void EachVisualisationLightsTheStageAndMovesWithTheMusic(string visualisation)
    {
        using var app = AppProcess.Start([Music(30)], RootShowing(visualisation));

        Wait.For(() => app.IsShown("Visualizer"), "the visualisation");
        var stage = app.Find("Visualizer").Current.BoundingRectangle;
        var window = app.Window.Current.BoundingRectangle;
        var area = new System.Windows.Rect(stage.Left - window.Left, stage.Top - window.Top, stage.Width, stage.Height);

        // Some seconds in, the picture is lit and keeps changing from one moment to the next.
        Thread.Sleep(3000);
        var frames = Enumerable.Range(0, 6).Select(_ =>
        {
            Thread.Sleep(170);
            return WindowPicture.Take(app.Handle);
        }).ToList();
        var lit = frames.Average(frame => WindowPicture.Brightness(frame, area));
        var change = frames.Zip(frames.Skip(1), (a, b) => WindowPicture.Change(a, b, area)).Average();
        Assert.True(lit is > 0.01 and < 0.8, $"{visualisation} lit {lit:0.000} of the stage.");
        Assert.True(change > 0.002, $"{visualisation} changed {change:0.0000} between moments.");
        Assert.DoesNotContain("could not be drawn", app.LogText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-18")]
    public void TheCameraIsAskedForAndNotUsedWhenTheAnswerIsNo()
    {
        using var app = AppProcess.Start([Music(30)], RootShowing("Strobe"));
        Wait.For(() => app.IsShown("Visualizer"), "the visualisation");

        // The next is the silhouette, which asks first; "Not now" passes over the
        // camera's two effects and lands on Resonance, which needs no camera.
        _ = Task.Run(() => app.Run(CommandCatalog.CycleVisualizer));
        app.Press("CloseButton");
        Wait.For(() => app.SavedSettings.Visualizer == Rex.Media.Settings.VisualizerChoice.Resonance, "the next non-camera visualisation");
        Assert.False(app.SavedSettings.CameraAllowed);
        Assert.DoesNotContain("The camera is on", app.LogText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-18")]
    public void SilhouetteShowsAnAccessibleRecalibrationActionAndRemovesItWhenChangingVisuals()
    {
        var root = RootShowing("Silhouette");
        var settingsPath = Path.Combine(root, "settings.json");
        var settings = Rex.Media.Settings.SettingsStore.Load(settingsPath);
        Rex.Media.Settings.SettingsStore.Save(settingsPath, settings with { CameraAllowed = true });
        using var app = AppProcess.Start([Music(12)], root);

        Wait.For(() => app.IsShown("SilhouetteRecalibrate"), "camera silhouette calibration button");
        app.Press("SilhouetteRecalibrate"); // safe even on runners without a camera
        Assert.True(app.IsShown("Visualizer"));
        app.Run(CommandCatalog.CycleVisualizer);
        Wait.For(() => app.SavedSettings.Visualizer == Rex.Media.Settings.VisualizerChoice.BeatEdit,
            "the next camera visualisation");
        Wait.For(() => !app.IsShown("SilhouetteRecalibrate"),
            "the silhouette-only calibration button to disappear");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void CoverOnlyBeatEditDoesNotStartAnAllowedCameraAndStopsCleanly()
    {
        var root = RootShowing("BeatEdit");
        var path = Path.Combine(root, "settings.json");
        var settings = Rex.Media.Settings.SettingsStore.Load(path);
        Rex.Media.Settings.SettingsStore.Save(path, settings with
        {
            CameraAllowed = true,
            VisualOptions = new Dictionary<string, string> { ["beatedit.source"] = "1" },
        });
        using var app = AppProcess.Start([Music(30)], root);
        Wait.For(() => app.IsShown("Visualizer"), "the cover-only beat edit");
        Thread.Sleep(1000);
        app.Run(CommandCatalog.Stop);
        Wait.For(() => !app.IsShown("Visualizer"), "the visualisation to stop");
        Assert.DoesNotContain("The camera is on", app.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("The camera could not be used", app.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be drawn", app.LogText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void MusicCoversThePictureThatPlayedBeforeIt()
    {
        var picture = Path.Combine(_media, "photo.png");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.snapshot-0.24.png"), picture);
        using var app = AppProcess.Start([picture]);
        // Assert the actual visual state, not the timing or wording of a decoder log message.
        Wait.For(() => !app.IsShown("VideoBlank"), "the picture to appear");

        Assert.Equal(0, AppProcess.Launch([Song(30)], app.Root));
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");
        Assert.True(app.IsShown("Visualizer"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-19")]
    [Capability("META-06")]
    [Capability("META-09")]
    public void ASongShowsTheFoldersCoverAndItsLyricsOverTheVisualisation()
    {
        var song = Song(30);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.snapshot-0.24.png"), Path.Combine(_media, "cover.png"));
        File.WriteAllText(Path.Combine(_media, "Sungba.lrc"), "[00:00.00]Sungba\n[00:20.00]Ọmọ ọlọ́run\n");
        using var app = AppProcess.Start([song]);

        Wait.For(() => app.IsShown("Cover"), "the folder's cover");
        Wait.For(() => app.IsShown("Lyrics"), "the lyrics");
        Assert.True(app.IsShown("Visualizer"));

        // The lyrics can be put away, leaving the visualisation, and brought back.
        app.Run(CommandCatalog.ToggleLyrics);
        Wait.For(() => !app.IsShown("Lyrics"), "the lyrics to go");
        Assert.True(app.IsShown("Visualizer"));
        Wait.For(() => !app.SavedSettings.ShowLyrics, "no lyrics to be kept");
        app.Run(CommandCatalog.ToggleLyrics);
        Wait.For(() => app.IsShown("Lyrics"), "the lyrics to come back");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("META-09")]
    public void AClickOnATimedLyricGoesToThatPartOfTheSong()
    {
        var song = Song(30);
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"), "[00:00.00]First line\n[00:20.00]The part I want\n");
        using var app = AppProcess.Start([song]);

        Wait.For(() => app.IsShown("Lyrics"), "the timed lyrics to be ready");
        var lyric = app.Find("LyricLine-1");
        Assert.Contains("Go to 0:20", lyric.Current.Name, StringComparison.Ordinal);
        ((InvokePattern)lyric.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var position = (RangeValuePattern)app.Find("SeekBar").GetCurrentPattern(RangeValuePattern.Pattern);
        Wait.For(() => position.Current.Value >= 19.5, "the song to seek to the lyric");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-09")]
    public void AnEntryWhoseFileHasGoneIsMarkedMissingAndOffersToFindIt()
    {
        Song(30);
        var playlist = Path.Combine(_media, "Asake.m3u8");
        File.WriteAllText(playlist, "Sungba.wav\nTerminator.wav\n");
        using var app = AppProcess.Start([playlist]);
        app.Run(CommandCatalog.TogglePlaylist);

        var gone = Wait.Until(() => app.Find("PlaylistView").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Terminator (missing)")));
        Assert.NotNull(app.Find("PlaylistView").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "\u25B6 Sungba")));
        ((SelectionItemPattern)gone!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-11")]
    public void AShortcutIsChangedInTheEditorAndKept()
    {
        using var app = AppProcess.Start();

        AutomationElement Wanted(string automationId) =>
            Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)))!;

        app.Run(CommandCatalog.KeyboardAndMouse);
        ((InvokePattern)Wanted("Shortcut-" + CommandCatalog.Mute).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        ((ValuePattern)Wanted("ShortcutKeys").GetCurrentPattern(ValuePattern.Pattern)).SetValue("Ctrl+O");
        app.Press("ShortcutApply");

        // Ctrl+O was opening a file's: the editor says so.
        Wait.For(() => app.IsShown("ShortcutNote") && Wanted("ShortcutNote").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Ctrl+O was the shortcut for Open files; it has none now.")) is not null, "the note about the move");
        ((TogglePattern)Wanted("ShortcutGlobal-" + CommandCatalog.PlayPause).GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings.Shortcuts.GetValueOrDefault(CommandCatalog.Mute) == "Ctrl+O", "the new shortcut to be kept");
        Assert.Equal("", app.SavedSettings.Shortcuts[CommandCatalog.OpenFile]);
        Assert.Equal([CommandCatalog.PlayPause], app.SavedSettings.GlobalShortcuts);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("FMT-C19")]
    public void PicturesShowInTurnAsASlideshow()
    {
        foreach (var name in new[] { "still.png", "anim.gif", "still.jpg" })
        {
            File.Copy(RepoPaths.Combine("tests", "fixtures", "picture", name), Path.Combine(_media, name.Replace("still.", "photo.", StringComparison.Ordinal)));
        }

        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""{ "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off", "pictureSeconds": 1 }""");
        using var app = AppProcess.Start([_media], root);

        // The folder is a slideshow: each picture in turn, a second each, the GIF moving.
        Wait.For(() => app.Text("NowPlaying") == "anim", "the GIF");
        Wait.For(() => app.Text("NowPlaying") == "photo", "the next picture");
        Wait.For(() => app.LogText.Contains("Opened photo.png as PNG", StringComparison.Ordinal), "the last picture");
        Assert.Contains("Video: GIF via rexplayer GIF into Direct3D 11", app.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be played", app.LogText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheLibraryFindsTheSongsInItsFoldersAndPlaysThem()
    {
        var songPath = Song(30);
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log")));
        library.AddFolder(_media);
        // The window tests navigation, not a background scan's timing; persist its one media
        // file first. The scanner's own suite independently tests discovering new files.
        var file = new FileInfo(songPath);
        Assert.Equal(1, library.Scan(_media,
            [new Rex.Media.Library.LibraryFile(songPath, file.Length, file.LastWriteTimeUtc)],
            _ => Rex.Media.Library.LibraryKind.Music).Added);
        using var app = AppProcess.Start(root: root);

        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane"), "the library");

        // Home first, with the song on its shelf of what is new; then the songs.
        Wait.For(() => app.Text("LibraryTitle") == "Home", "the library's home");
        // The shelf may be outside the scroll viewport; it must exist, not be onscreen.
        var newSongs = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryShelfCards-RecentlyAdded")));
        Assert.Equal("New in your library", newSongs.Current.Name);
        var songs = Wait.Until(() => app.Find("LibrarySources").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Songs")));
        ((SelectionItemPattern)songs!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Wait.For(() => app.Text("LibraryTitle") == "Songs", "the songs");
        var song = Wait.Until(() => app.Find("LibraryList").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Sungba")));
        ((SelectionItemPattern)song!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song to play");

        ((ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern)).SetValue("no such song");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText") == "Nothing in the library matches that.", "no results");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => !app.IsShown("LibraryPane"), "the library to close");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void LibraryArtworkLeavesRoomToBrowseAndScrollPictures()
    {
        var example = RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg");
        var pictures = Enumerable.Range(1, 36).Select(index => Path.Combine(_media, $"Photo-{index:00}.jpg")).ToList();
        foreach (var picture in pictures)
        {
            File.Copy(example, picture);
        }

        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log")));
        library.AddFolder(_media);
        var files = pictures.Select(path =>
        {
            var info = new FileInfo(path);
            return new Rex.Media.Library.LibraryFile(path, info.Length, info.LastWriteTimeUtc);
        }).ToList();
        Assert.Equal(pictures.Count, library.Scan(_media, files, _ => Rex.Media.Library.LibraryKind.Picture).Added);

        using var app = AppProcess.Start(root: root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.Text("LibraryTitle") == "Home", "the library home");
        // Artwork must not stretch the banner and push the scrolling content out of the window.
        Assert.True(app.IsShown("LibraryHome"), "The home shelves must have a visible viewport.");

        var picturesView = Wait.Until(() => app.Find("LibrarySources").FindFirst(
            TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Pictures")));
        ((SelectionItemPattern)picturesView!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Wait.For(() => app.Text("LibraryTitle") == "Pictures", "the pictures view");
        Assert.True(app.IsShown("LibraryList"), "The picture grid must have a visible viewport.");

        var scroll = (ScrollPattern)app.Find("LibraryList").GetCurrentPattern(ScrollPattern.Pattern);
        Wait.For(() => scroll.Current.VerticallyScrollable, "the picture grid to be vertically scrollable");
        scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        Wait.For(() => scroll.Current.VerticalScrollPercent > 0, "the picture grid to scroll");

        // Collage uses a separate virtualizing panel with justified, variable-width tiles;
        // switching away restores the normal grid and the choice is saved for this view.
        app.Press("LibraryLook");
        app.Press("LibraryLook-Collage");
        Wait.For(() => app.IsShown("LibraryCollage"), "the collage to be visible");
        Assert.False(app.IsShown("LibraryList"));
        Wait.Until(() => app.Find("LibraryCollage").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Photo-01")));
        Wait.For(() => app.SavedSettings.LibraryViews.GetValueOrDefault("Pictures")?.Look == Rex.Media.Settings.LibraryLook.Collage,
            "the collage layout to be saved");

        app.Press("LibraryLook");
        app.Press("LibraryLook-Grid");
        Wait.For(() => app.IsShown("LibraryList") && !app.IsShown("LibraryCollage"), "the card grid to return");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void MoviesAndSeriesBrowseSeparatelyWithAccessibleArtwork()
    {
        var example = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
        var paths = new[]
        {
            Path.Combine(_media, "Small.Film.2023.mp4"),
            Path.Combine(_media, "North.Shore.S01E01.Pilot.mp4"),
            Path.Combine(_media, "North.Shore.S02E03.Return.mp4"),
        };
        foreach (var path in paths)
        {
            File.Copy(example, path);
        }

        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log")));
        library.AddFolder(_media);
        Assert.Equal(paths.Length, library.Scan(_media, paths.Select(path =>
        {
            var info = new FileInfo(path);
            return new Rex.Media.Library.LibraryFile(path, info.Length, info.LastWriteTimeUtc);
        }), _ => Rex.Media.Library.LibraryKind.Video).Added);

        using var app = AppProcess.Start(root: root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.Text("LibraryTitle") == "Home", "the media home");

        void Visit(string name)
        {
            var source = Wait.Until(() => app.Find("LibrarySources").FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name)));
            ((SelectionItemPattern)source!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Wait.For(() => app.Text("LibraryTitle") == name, name + " to appear");
            Assert.True(app.IsShown("LibraryList"));
        }

        Visit("Movies");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 movie", StringComparison.Ordinal),
            "only standalone video files to appear in Movies");
        Visit("TV Shows");
        Wait.Until(() => app.Find("LibraryList").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "North Shore")));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-04")]
    public void APlaylistIsNamedFromTheMenuAndDeletedAgain()
    {
        using var app = AppProcess.Start([Song(30)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song to start");

        void Open(params string[] menus)
        {
            foreach (var menu in menus)
            {
                Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, menu)));
                ((ExpandCollapsePattern)app.Find(menu).GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            }
        }

        AutomationElement Wanted(string automationId) =>
            Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)))!;

        Open("MediaMenu", "PlaylistsMenu");
        ((InvokePattern)Wanted("NewNamedPlaylist").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        ((ValuePattern)Wanted("PlaylistName").GetCurrentPattern(ValuePattern.Pattern)).SetValue("Asake");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("Made the playlist Asake", StringComparison.Ordinal), "the playlist to be made");

        Open("MediaMenu", "PlaylistsMenu", "NamedPlaylist-Asake");
        ((InvokePattern)Wanted("DeleteNamed-Asake").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wanted("PrimaryButton");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("Deleted the playlist Asake", StringComparison.Ordinal), "the playlist to be deleted");

        Open("MediaMenu", "PlaylistsMenu");
        Wanted("NewNamedPlaylist");
        Assert.Null(app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "NamedPlaylist-Asake")));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-11")]
    public void ASongLeftPartWayOffersToCarryOnFromThere()
    {
        var song = Song(60);
        string root;
        using (var first = AppProcess.Start([song]))
        {
            root = first.Root;
            Wait.For(() => first.Text("NowPlaying") == "Sungba", "the song to start");
            first.Run(CommandCatalog.JumpForwardShort);
            first.Run(CommandCatalog.JumpForwardShort);
            Wait.For(() => first.Text("Elapsed").StartsWith("0:2", StringComparison.Ordinal), "the jump");
            Assert.Equal(0, first.Close());
        }

        using var second = AppProcess.Start([song], root);
        Wait.For(() => second.IsShown("ResumeBar"), "the offer to carry on");
        second.Press("ResumeButton");
        Wait.For(() => !second.IsShown("ResumeBar"), "the offer to go");
        Wait.For(() => second.Text("Elapsed") is { } elapsed && (elapsed.StartsWith("0:2", StringComparison.Ordinal) || elapsed.StartsWith("0:3", StringComparison.Ordinal)), "the song to carry on from where it was");
        Assert.Equal(0, second.Close());
    }

    [Fact]
    [Capability("VID-07")]
    public void ZoomingInShowsTheNavigatorAndTheWholePictureHidesIt()
    {
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        Wait.For(() => app.Text("NowPlaying") == "Basquiat", "the video");

        app.Run(CommandCatalog.ZoomIn);
        Wait.For(() => app.IsShown("Navigator"), "the navigator");
        app.Run(CommandCatalog.ResetZoom);
        Wait.For(() => !app.IsShown("Navigator"), "the navigator to go");

        app.Run(CommandCatalog.ToggleNavigator);
        Wait.For(() => !app.SavedSettings.ShowNavigator, "the navigator to be turned off");
        app.Run(CommandCatalog.ZoomIn);
        Assert.False(app.IsShown("Navigator"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-05")]
    public void TheMinimalInterfaceLeavesOnlyThePicture()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.MinimalInterface);
        Wait.For(() => !app.IsShown("PlayPauseButton") && !app.IsShown("Menu"), "the controls and menus to go");
        app.Run(CommandCatalog.MinimalInterface);
        Wait.For(() => app.IsShown("PlayPauseButton") && app.IsShown("Menu"), "the controls and menus to come back");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("VID-04")]
    public void TheWindowSizesItselfToTheVideo()
    {
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        // The file's own title replaces its name once its details are read; only then is its size known.
        Wait.For(() => app.Text("NowPlaying") == "Basquiat", "the video's details");
        // Half, actual and double size: each step adds the video's height times the step, whatever
        // the display's scale. (Widths are not compared: a window this narrow meets Windows' minimum.)
        var half = Resize(app, CommandCatalog.ScaleHalf);
        var actual = Resize(app, CommandCatalog.ScaleOriginal);
        var doubled = Resize(app, CommandCatalog.ScaleDouble);

        Assert.InRange((doubled - actual) / (actual - half), 1.9, 2.1);
        Assert.Equal(0, app.Close());
    }

    /// <summary>Runs a window-size command and gives the window's height once it has settled.</summary>
    private static double Resize(AppProcess app, string command)
    {
        var before = app.Window.Current.BoundingRectangle.Height;
        app.Run(command);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Height - before) > 1, "the window to change size");
        Thread.Sleep(300);
        return app.Window.Current.BoundingRectangle.Height;
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesChangeTheSettingsAndAreKept()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.Preferences);
        var jump = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        ((RangeValuePattern)jump.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(25);
        var artwork = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Artwork detail (%)")));
        ((RangeValuePattern)artwork.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(135);
        var generated = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Generate artwork from music when no cover exists")));
        ((TogglePattern)generated.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings is { ShortJumpSeconds: 25, AudioArtworkDetail: 135, GenerateAudioArtwork: false }, "the preferences to be saved");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-07")]
    public void AnEqualiserPresetChosenInTheEffectsPanelIsKept()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.Effects);
        var presets = app.Find("EqualizerPresets");
        ((ExpandCollapsePattern)presets.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var rock = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "Rock"))));
        ((SelectionItemPattern)rock.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.Press("CloseButton");

        Wait.For(() => app.SavedSettings is { EqualizerEnabled: true, EqualizerPreset: "Rock" }, "the preset to be kept");
        Assert.Equal(5, app.SavedSettings.EqualizerGains[0]);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-08")]
    public void MediaInformationDescribesWhatIsPlaying()
    {
        using var app = AppProcess.Start([Song(5)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");

        app.Run(CommandCatalog.MediaInformation);
        var text = app.Text("MediaInformationText");
        app.Press("CloseButton");

        Assert.Contains("Title: Sungba", text, StringComparison.Ordinal);
        Assert.Contains("Format: WAVE", text, StringComparison.Ordinal);
        Assert.Contains("8000 Hz, 1 channel(s)", text, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-09")]
    public void TheLogShowsWhatHappened()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.ShowLog);
        var log = ((ValuePattern)app.Find("LogText").GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
        app.Press("CloseButton");

        Assert.Contains("[INFO] app: rexplayer", log, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("A11Y-01")]
    [Capability("UI-16")]
    public void EveryControlHasANameAScreenReaderCanSay()
    {
        using var app = AppProcess.Start([Song(5)]);
        Wait.For(() => app.Title == "Sungba \u2014 rexplayer", "the title to name what plays");

        var controls = new[] { ControlType.Button, ControlType.Slider, ControlType.MenuItem, ControlType.List, ControlType.ComboBox, ControlType.CheckBox }
            .SelectMany(type => All(app.Window, type))
            .ToList();

        Assert.NotEmpty(controls);
        Assert.All(controls, control => Assert.False(string.IsNullOrWhiteSpace(control.Current.Name), $"{control.Current.ControlType.ProgrammaticName} {control.Current.AutomationId} has no name."));
        Assert.Equal(0, app.Close());
    }
}
