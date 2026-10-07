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
