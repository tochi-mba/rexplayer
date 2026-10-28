using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The menu bar and the picture's right-click menu: every menu and submenu opens and names its
/// commands, on-and-off items are ticked to match the player, and choosing an item does its thing.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>Each top-level menu, its submenus by automation id, and the commands found in it with them open.</summary>
    private static readonly (string Menu, string[] Submenus, string[] Commands)[] MenuBar =
    [
        ("MediaMenu", [], [CommandCatalog.OpenFile, CommandCatalog.OpenFolder, CommandCatalog.OpenLocation, CommandCatalog.PasteLocation, CommandCatalog.OpenPlaylist, CommandCatalog.SavePlaylist, CommandCatalog.Quit]),
        ("PlaybackMenu", ["Submenu-jump", "Submenu-speed", "BookmarksMenu", "Submenu-sleep"], [CommandCatalog.PlayPause, CommandCatalog.Stop, CommandCatalog.Previous, CommandCatalog.Next, CommandCatalog.SkipSection, CommandCatalog.NextEpisode,
            CommandCatalog.GoToTime, CommandCatalog.CycleRepeat, CommandCatalog.ToggleShuffle, CommandCatalog.AddBookmark, CommandCatalog.Resume,
            CommandCatalog.JumpForwardVeryShort, CommandCatalog.JumpBackVeryShort, CommandCatalog.JumpForwardShort, CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardMedium, CommandCatalog.JumpBackMedium, CommandCatalog.JumpForwardLong, CommandCatalog.JumpBackLong,
            CommandCatalog.Faster, CommandCatalog.Slower, CommandCatalog.SlightlyFaster, CommandCatalog.SlightlySlower, CommandCatalog.NormalSpeed,
            CommandCatalog.StopAfterCurrent, CommandCatalog.PauseAfterCurrent, CommandCatalog.SleepPrefix + "15", CommandCatalog.SleepPrefix + "30", CommandCatalog.SleepPrefix + "60", CommandCatalog.SleepAtEndOfItem, CommandCatalog.SleepOff]),
        ("AudioMenu", ["Submenu-audio-timing"], [CommandCatalog.CycleAudioTrack, CommandCatalog.VolumeUp, CommandCatalog.VolumeDown, CommandCatalog.Mute, CommandCatalog.Effects, CommandCatalog.CycleVisualizer, CommandCatalog.VisualizerSettings, CommandCatalog.ToggleLyrics,
            CommandCatalog.AudioEarlier, CommandCatalog.AudioLater, CommandCatalog.ResetAudioDelay]),
        ("SubtitleMenu", ["Submenu-subtitle-timing", "Submenu-subtitle-size", "Submenu-kinetic-captions"], [CommandCatalog.AddSubtitles, CommandCatalog.CycleSubtitles, CommandCatalog.ToggleSubtitles, CommandCatalog.CycleSecondarySubtitles, CommandCatalog.ToggleKineticCaptions, CommandCatalog.Transcribe, CommandCatalog.SaveTranscript,
            CommandCatalog.SubtitlesEarlier, CommandCatalog.SubtitlesLater, CommandCatalog.ResetSubtitleDelay, CommandCatalog.SubtitlesBigger, CommandCatalog.SubtitlesSmaller, CommandCatalog.ResetSubtitleSize,
            CommandCatalog.CycleCaptionStyle, CommandCatalog.CycleCaptionWords, CommandCatalog.CycleCaptionLayout, CommandCatalog.KineticCaptionsLivelier, CommandCatalog.KineticCaptionsCalmer, CommandCatalog.KineticCaptionSettings]),
        ("VideoMenu", ["Submenu-window-size"], [CommandCatalog.ToggleFullScreen, CommandCatalog.CycleAspectRatio, CommandCatalog.CycleCrop, CommandCatalog.ZoomIn, CommandCatalog.ZoomOut, CommandCatalog.ResetZoom, CommandCatalog.ToggleNavigator,
            CommandCatalog.Snapshot, CommandCatalog.ToggleStats, CommandCatalog.ToggleAlwaysOnTop, CommandCatalog.ScaleQuarter, CommandCatalog.ScaleHalf, CommandCatalog.ScaleOriginal, CommandCatalog.ScaleDouble]),
        ("ViewMenu", ["Submenu-library-backup"], [CommandCatalog.TogglePlaylist, CommandCatalog.ToggleLibrary, CommandCatalog.MinimalInterface, CommandCatalog.MediaInformation, CommandCatalog.ShowPosition, CommandCatalog.Preferences, CommandCatalog.KeyboardAndMouse,
            CommandCatalog.ClearPlaylist, CommandCatalog.ClearHistory, CommandCatalog.BackUpLibrary, CommandCatalog.RestoreLibrary]),
        ("HelpMenu", [], [CommandCatalog.ShortcutSheet, CommandCatalog.Help, CommandCatalog.ShowLog, CommandCatalog.OpenLogFolder, CommandCatalog.SaveDiagnostics, CommandCatalog.CheckForUpdates]),
    ];

    [Fact]
    [Capability("UI-02")]
    public void EveryMenuAndSubmenuOffersItsCommandsByNameAndTicksWhatIsOn()
    {
        using var app = AppProcess.Start([Song(30)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");

        foreach (var (menu, submenus, commands) in MenuBar)
        {
            var expander = (ExpandCollapsePattern)app.Find(menu).GetCurrentPattern(ExpandCollapsePattern.Pattern);
            expander.Expand();
            var found = new HashSet<string>(StringComparer.Ordinal);
            void Collect() => found.UnionWith(All(app.Window, ControlType.MenuItem)
                .Where(item => item.Current.AutomationId.StartsWith("Command-", StringComparison.Ordinal))
                .Select(item =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(item.Current.Name), item.Current.AutomationId + " has no name.");
                    Assert.True(item.Current.IsEnabled, item.Current.AutomationId + " is not enabled.");
                    return item.Current.AutomationId["Command-".Length..];
                }));
            Wait.For(() => app.IsShown("Command-" + commands[0]), menu + " to open");
            Collect();
            foreach (var submenu in submenus)
            {
                var before = found.Count;
                var sub = (ExpandCollapsePattern)app.Find(submenu).GetCurrentPattern(ExpandCollapsePattern.Pattern);
                sub.Expand();
                Wait.For(() =>
                {
                    Collect();
                    return found.Count > before;
                }, "the " + submenu + " submenu to open");
                Collect();
                sub.Collapse();
            }

            Assert.Superset(new HashSet<string>(commands, StringComparer.Ordinal), found);
            expander.Collapse();
        }

        // On-and-off items carry a tick that follows the player, however it was changed.
        Assert.Equal(ToggleState.Off, MenuTick(app, "PlaybackMenu", CommandCatalog.ToggleShuffle));
        app.Toggle("ShuffleButton");
        Assert.Equal(ToggleState.On, MenuTick(app, "PlaybackMenu", CommandCatalog.ToggleShuffle));
        app.Press("MuteButton");
        Assert.Equal(ToggleState.On, MenuTick(app, "AudioMenu", CommandCatalog.Mute));

        // Choosing an item runs its command and closes the menu.
        ((ExpandCollapsePattern)app.Find("VideoMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        app.Press("Command-" + CommandCatalog.ToggleStats);
        Wait.For(() => app.IsShown("Stats"), "the statistics to show over the picture");
        Wait.For(() => !app.IsShown("Command-" + CommandCatalog.ToggleStats), "the menu to close after a choice");
        Wait.For(() => app.SavedSettings.StatsOverlay, "the statistics choice to be kept");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-03")]
    public void ThePicturesRightClickMenuOffersTheSameCommandsAndRunsThem()
    {
        using var app = AppStartedAt(1000, 700, "Light");
        Wait.For(() => app.IsShown("IdleTitle"), "the empty window");

        ((ExpandCollapsePattern)app.Find("PlaybackMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Wait.For(() => app.IsShown("Command-" + CommandCatalog.PlayPause), "the Playback menu to open");
        Screenshots.Match(app, "menu-playback");
        ((ExpandCollapsePattern)app.Find("PlaybackMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Wait.For(() => !app.IsShown("Command-" + CommandCatalog.PlayPause), "the Playback menu to close");

        Input.Focus(app);
        Input.RightClick(app.Find("Stage"));
        Wait.For(() => app.IsShown("Context-" + CommandCatalog.PlayPause), "the right-click menu to open");
        foreach (var id in new[] { CommandCatalog.Stop, CommandCatalog.ToggleFullScreen, CommandCatalog.MinimalInterface, CommandCatalog.Preferences })
        {
            Assert.True(app.IsShown("Context-" + id), id + " is missing from the right-click menu.");
        }

        // It opens within the window, every line of it on screen.
        var window = app.Window.Current.BoundingRectangle;
        foreach (var id in new[] { CommandCatalog.PlayPause, CommandCatalog.Preferences })
        {
            Assert.True(window.Contains(app.Find("Context-" + id).Current.BoundingRectangle), id + " is outside the window.");
        }

        Screenshots.Match(app, "context-menu");
        // The right-click menu's own Video group, not the menu bar's Video menu of the same name.
        var video = Wait.Until(() => All(app.Window, ControlType.MenuItem)
            .FirstOrDefault(item => item.Current.Name == "Video" && item.Current.AutomationId != "VideoMenu"));
        ((ExpandCollapsePattern)video.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        app.Press("Context-" + CommandCatalog.ToggleStats);
        Wait.For(() => app.IsShown("Stats"), "the right-click menu's choice to run");
        Assert.Equal(0, app.Close());
    }

    /// <summary>Whether an on-and-off item in a menu is ticked, read with the menu open.</summary>
    private static ToggleState MenuTick(AppProcess app, string menu, string command)
    {
        var expander = (ExpandCollapsePattern)app.Find(menu).GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expander.Expand();
        try
        {
            return ((TogglePattern)app.Find("Command-" + command).GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState;
        }
        finally
        {
            expander.Collapse();
        }
    }
}
