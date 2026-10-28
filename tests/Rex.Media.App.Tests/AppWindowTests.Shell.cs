using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The window as a program on the desktop: shortcuts typed on the keyboard and shortcuts that work
/// from anywhere, where the window was left and how big, a second launch with nothing to hand over,
/// and every section of Preferences, each drawn and checked.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>Every section of Preferences, in order, as its rail and its picker name them.</summary>
    private static readonly string[] PreferencesSections =
    [
        "Playback", "Intros and credits", "Video and pictures", "Music and artwork", "Performance", "Languages", "Subtitles",
        "Transcription", "Window", "Library", "Metadata", "History and resume", "Keyboard and mouse", "Opening files", "Updates",
    ];

    [Fact]
    [Capability("UI-10")]
    [Capability("UI-13")]
    public void EveryPreferencesSectionShowsAloneAndTheThemeChosenIsKept()
    {
        using var app = AppStartedAt(1000, 760, "Light");
        app.Run(CommandCatalog.Preferences);

        // A wide window lists the sections on a rail beside the page. A list can leave items it has
        // not drawn out of UI Automation, so the order is checked on those it offers; the loop below
        // chooses every section and checks that its own page appears.
        var offered = Wait.Until(() =>
        {
            var names = All(app.Find("PreferencesCategoryList"), ControlType.ListItem).Select(item => item.Current.Name).Where(PreferencesSections.Contains).Distinct().ToArray();
            return names.Length > 0 ? names : null;
        });
        Assert.NotEmpty(offered);
        Assert.Equal(PreferencesSections.Take(offered.Length), offered);

        foreach (var section in PreferencesSections)
        {
            ChoosePreferencesSection(app, section);
            Wait.For(() => SectionHeadings(app).SequenceEqual([section]), section + " to show alone", () => "Headings shown: " + string.Join(", ", SectionHeadings(app)) + ".");
            Screenshots.Match(app, "preferences-section-" + section.ToLowerInvariant().Replace(" ", "-"));
        }

        ChoosePreferencesSection(app, "Window");
        var theme = (ExpandCollapsePattern)Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
            new PropertyCondition(AutomationElement.NameProperty, "Theme")))).GetCurrentPattern(ExpandCollapsePattern.Pattern);
        theme.Expand();
        var dark = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "Dark"))));
        ((SelectionItemPattern)dark.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        theme.Collapse();
        app.Press("PrimaryButton");
        Wait.For(() => app.SavedSettings.Theme == ThemeChoice.Dark, "the dark theme to be kept");
        Assert.Equal(0, app.Close());
    }

    /// <summary>
    /// Real top-level WindowPattern and native titlebar behavior matter for Snap
    /// and taskbar integration; an ordinary resizable XAML panel cannot replace them.
    /// </summary>
    [Fact]
    [Capability("UI-01")]
    [Capability("UI-13")]
    public void NativeTitleBarPreservesMaximizeRestoreAndResizeFromTheOs()
    {
        using var app = AppStartedAt(1000, 700, "Dark");
        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
        Assert.True(window.Current.CanMaximize, "Native maximize and Windows Snap eligibility must be preserved.");
        Assert.True(window.Current.CanMinimize, "The native taskbar must be able to minimize this window.");
        Assert.Contains("rexplayer", app.Window.Current.Name, StringComparison.OrdinalIgnoreCase);
        Screenshots.Match(app, "native-titlebar-restored");

        Input.Maximize(app);
        Wait.For(() => window.Current.WindowVisualState == WindowVisualState.Maximized,
            "the OS maximize button to control the actual native window");
        Assert.True(app.IsShown("PlayPauseButton"), "maximize to keep controls accessible");
        Screenshots.Match(app, "native-titlebar-maximized");

        Input.Restore(app);
        Wait.For(() => window.Current.WindowVisualState == WindowVisualState.Normal,
            "the OS restore control to return to a resizable window");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(720, 640);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2
            && Math.Abs(app.Window.Current.BoundingRectangle.Height - 640) < 2,
            "the restored native window to remain resizable");
        Assert.True(app.IsShown("PlayPauseButton"));
        Screenshots.Match(app, "native-titlebar-compact-restored");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-11")]
    public void AShortcutClearedInTheEditorIsKeptOnlyWhenSaved()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.KeyboardAndMouse);
        app.Press("ShortcutClear-" + CommandCatalog.Mute);
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("ShortcutSearch"), "the editor to close");
        Assert.False(app.SavedSettings.Shortcuts.ContainsKey(CommandCatalog.Mute));

        OpenDialog(app, CommandCatalog.KeyboardAndMouse, "ShortcutSearch");
        app.Press("ShortcutClear-" + CommandCatalog.Mute);
        app.Press("PrimaryButton");
        Wait.For(() => app.SavedSettings.Shortcuts.GetValueOrDefault(CommandCatalog.Mute) == "", "the cleared shortcut to be kept");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("A11Y-02")]
    public void AShortcutTypedInTheWindowRunsItsCommand()
    {
        using var app = AppProcess.Start([Song(30)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");
        Assert.Equal("Mute", app.Text("MuteButton"));

        Input.Focus(app);
        Input.Press("M");
        Wait.For(() => app.Text("MuteButton") == "Unmute", "M to mute");
        Input.Press("Ctrl+P");
        Wait.For(() => app.IsShown("PreferencesCategorySearch"), "Ctrl+P to open Preferences");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-11")]
    public void AGlobalShortcutWorksWhileTheWindowIsNotInFront()
    {
        var root = Path.Combine(_media, "player");
        Directory.CreateDirectory(root);
        SettingsStore.Save(Path.Combine(root, "settings.json"), new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = AppProcess.Version,
            UpdateChecks = UpdateCadence.Off,
            Shortcuts = new Dictionary<string, string> { [CommandCatalog.Mute] = "Ctrl+Alt+F9" },
            GlobalShortcuts = [CommandCatalog.Mute],
        });
        using var app = AppProcess.Start([Song(30)], root: root);
        Wait.For(() => app.LogText.Contains("1 shortcut(s) work everywhere.", StringComparison.Ordinal), "the global shortcut to be registered");

        // A hotkey reaches rexplayer whichever window has the keyboard.
        Input.Press("Ctrl+Alt+F9");
        Wait.For(() => app.Text("MuteButton") == "Unmute", "the global shortcut to mute");
        Wait.For(() => app.LogText.Contains("Global shortcut " + CommandCatalog.Mute, StringComparison.Ordinal), "the global shortcut to be logged");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-01")]
    public void TheWindowOpensWhereAndHowBigItWasLeft()
    {
        string root;
        System.Windows.Rect left;
        using (var first = AppProcess.Start())
        {
            root = first.Root;
            var transform = (TransformPattern)first.Window.GetCurrentPattern(TransformPattern.Pattern);
            transform.Move(120, 90);
            transform.Resize(900, 640);
            Wait.For(() => first.Window.Current.BoundingRectangle is { Width: var w, Height: var h } && Math.Abs(w - 900) < 2 && Math.Abs(h - 640) < 2, "the new size");
            left = first.Window.Current.BoundingRectangle;
            Assert.Equal(0, first.Close());
        }

        Assert.True(SettingsStore.Load(Path.Combine(root, "settings.json")).Window is { Maximized: false } kept && Math.Abs(kept.Width - left.Width) < 2 && Math.Abs(kept.Height - left.Height) < 2,
            "The size the window was left at was not kept.");
        using (var second = AppProcess.Start(root: root))
        {
            Wait.For(() => Near(second.Window.Current.BoundingRectangle, left), "the window where it was left", () => "It is at " + second.Window.Current.BoundingRectangle + ".");
            Input.Maximize(second);
            Wait.For(() => ((WindowPattern)second.Window.GetCurrentPattern(WindowPattern.Pattern)).Current.WindowVisualState == WindowVisualState.Maximized, "the window to fill the screen");
            Assert.Equal(0, second.Close());
        }

        Assert.True(SettingsStore.Load(Path.Combine(root, "settings.json")).Window is { Maximized: true });
        using var third = AppProcess.Start(root: root);
        Wait.For(() => ((WindowPattern)third.Window.GetCurrentPattern(WindowPattern.Pattern)).Current.WindowVisualState == WindowVisualState.Maximized, "the window to open filling the screen again");
        Input.Restore(third);
        Wait.For(() => Near(third.Window.Current.BoundingRectangle, left), "restoring to bring back the size it was left at");
        Assert.Equal(0, third.Close());
    }

    [Fact]
    [Capability("TOOL-06")]
    public void ASecondLaunchWithNothingToOpenLeavesTheFirstPlaying()
    {
        using var app = AppProcess.Start([Song(30)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song");

        Assert.Equal(0, AppProcess.Launch([], app.Root));

        Assert.Equal("Sungba", app.Text("NowPlaying"));
        Assert.Equal(0, app.Close());
    }

    /// <summary>The level-two headings Preferences shows now: one per section on screen.</summary>
    private static string[] SectionHeadings(AppProcess app) =>
        [.. All(app.Find("PreferencesScroll"), ControlType.Text).Select(text => text.Current.Name).Where(PreferencesSections.Contains).Distinct()];

    /// <summary>
    /// Opens a dialog and waits for <paramref name="shownId"/>. Just after another dialog closes the
    /// window may still count it as open and refuse the new one, so the command is asked again.
    /// </summary>
    private static void OpenDialog(AppProcess app, string command, string shownId) =>
        Wait.For(() =>
        {
            if (!app.IsShown(shownId))
            {
                app.Run(command);
            }

            return app.IsShown(shownId);
        }, "the dialog with " + shownId + " to open");

    private static bool Near(System.Windows.Rect a, System.Windows.Rect b) =>
        Math.Abs(a.X - b.X) < 2 && Math.Abs(a.Y - b.Y) < 2 && Math.Abs(a.Width - b.Width) < 2 && Math.Abs(a.Height - b.Height) < 2;
}
