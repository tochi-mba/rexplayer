using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
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
        using var app = AppProcess.Start([Song(30)]);

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

    [Fact]
    public void MusicCoversThePictureThatPlayedBeforeIt()
    {
        var picture = Path.Combine(_media, "photo.png");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.snapshot-0.24.png"), picture);
        using var app = AppProcess.Start([picture]);
        Wait.For(() => app.LogText.Contains("Opened photo.png as PNG", StringComparison.Ordinal), "the picture");
        Assert.False(app.IsShown("VideoBlank"));

        Assert.Equal(0, AppProcess.Launch([Song(30)], app.Root));
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");
        Assert.True(app.IsShown("Visualizer"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-19")]
    [Capability("META-06")]
    [Capability("META-09")]
    public void ASongShowsTheFoldersCoverAndItsLyricsInPlaceOfTheVisualisation()
    {
        var song = Song(30);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.snapshot-0.24.png"), Path.Combine(_media, "cover.png"));
        File.WriteAllText(Path.Combine(_media, "Sungba.lrc"), "[00:00.00]Sungba\n[00:20.00]Ọmọ ọlọ́run\n");
        using var app = AppProcess.Start([song]);

        Wait.For(() => app.IsShown("Cover"), "the folder's cover");
        Wait.For(() => app.IsShown("Lyrics"), "the lyrics");
        Assert.False(app.IsShown("Visualizer"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("META-09")]
    public void AClickOnATimedLyricGoesToThatPartOfTheSong()
    {
        var song = Song(30);
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"), "[00:00.00]First line\n[00:20.00]The part I want\n");
        using var app = AppProcess.Start([song]);

        Assert.True(app.IsShown("Lyrics"));
        var lyric = app.Find("LyricLine-1");
        Assert.Contains("Go to 0:20", lyric.Current.Name, StringComparison.Ordinal);
        ((InvokePattern)lyric.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var position = (RangeValuePattern)app.Find("SeekBar").GetCurrentPattern(RangeValuePattern.Pattern);
        Wait.For(() => position.Current.Value >= 19.5, "the song to seek to the lyric");
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
        Song(30);
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log"))).AddFolder(_media);
        using var app = AppProcess.Start(root: root);

        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane"), "the library");
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
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings.ShortJumpSeconds == 25, "the new jump to be saved");
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
