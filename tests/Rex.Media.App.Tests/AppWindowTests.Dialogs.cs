using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The window's dialogs, opened as people open them: a location typed in, Go to a time, the
/// shortcut sheet, What's new after an update and the update panel. One dialog shows at a time.
/// </summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("UI-17")]
    public void ALocationTypedInPlaysAndGoToATimeMovesThere()
    {
        var song = Song(60);
        using var app = AppStartedAt(1000, 700, "Light");

        // Nothing open: nothing to seek.
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.False(app.Find("StartOverButton").Current.IsEnabled);

        app.Run(CommandCatalog.OpenLocation);
        Wait.For(() => app.IsShown("LocationBox"), "the location dialog");
        Screenshots.Match(app, "dialog-open-location");
        ((ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("\"" + song + "\"");
        app.Press("PrimaryButton");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the typed location to play");
        Wait.For(() => app.Find("SeekBar").Current.IsEnabled && app.Find("StartOverButton").Current.IsEnabled, "seeking to be offered");

        app.Run(CommandCatalog.PlayPause);
        OpenDialog(app, CommandCatalog.GoToTime, "GoToTimeBox");
        var box = (ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern);
        box.SetValue("0:42");
        app.Press("PrimaryButton");
        Wait.For(() => app.Text("Elapsed") == "0:42", "the song to move to 0:42", () => "It shows " + app.Text("Elapsed") + ".");

        // A time that is not a time says so and leaves the place alone.
        OpenDialog(app, CommandCatalog.GoToTime, "GoToTimeBox");
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("soon");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("\"soon\" is not a time", StringComparison.Ordinal), "the message about the time");
        Assert.Equal("0:42", app.Text("Elapsed"));

        // Cancel changes nothing.
        OpenDialog(app, CommandCatalog.GoToTime, "GoToTimeBox");
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("0:05");
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("GoToTimeBox"), "Go to a time to close");
        Assert.Equal("0:42", app.Text("Elapsed"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("A11Y-02")]
    public void TheShortcutSheetListsEveryGroupAndOnlyOneDialogShowsAtATime()
    {
        using var app = AppStartedAt(1000, 760, "Light");

        app.Run(CommandCatalog.ShortcutSheet);
        var sheet = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Keyboard shortcuts")));
        foreach (var line in new[] { "Play or pause: Space", "Mute: M", "Preferences: Ctrl+P" })
        {
            Wait.For(() => All(app.Window, ControlType.Text).Any(text => text.Current.Name.StartsWith(line, StringComparison.Ordinal)), "the sheet to list " + line);
        }

        Screenshots.Match(app, "dialog-shortcut-sheet");

        // A second dialog asked for while one is open (a script's, say) is not stacked on top.
        app.Run(CommandCatalog.Preferences);
        Wait.For(() => app.LogText.Contains("\"Preferences\" was not shown: \"Keyboard shortcuts\" is open.", StringComparison.Ordinal), "the second dialog to be refused");
        Assert.False(app.IsShown("PreferencesSection"));
        app.Press("CloseButton");
        Wait.For(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Keyboard shortcuts")) is null, "the sheet to close");
        Assert.NotNull(sheet);
        OpenDialog(app, CommandCatalog.Preferences, "PreferencesSection");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-15")]
    public void TheFirstStartOfANewVersionOffersWhatsNew()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "settings.json"), """{ "firstRunDone": true, "lastSeenVersion": "0.0.1", "updateChecks": "Off" }""");
        using var app = AppProcess.Start(root: root);

        Wait.For(() => NoticeSays(app, "Updated to rexplayer " + AppProcess.Version), "the updated note");
        Wait.For(() => app.SavedSettings.LastSeenVersion == AppProcess.Version, "the new version to be remembered");
        app.Press("UpdatedWhatsNewButton");
        Assert.False(string.IsNullOrWhiteSpace(app.Text("WhatsNewText")));
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("WhatsNewText"), "What's new to close");

        // Preferences' Updates section offers it again.
        OpenDialog(app, CommandCatalog.Preferences, "PreferencesSection");
        ChoosePreferencesSection(app, "Updates");
        Assert.False(string.IsNullOrWhiteSpace(app.Text("PreferencesLastUpdated")));
        app.Press("PreferencesWhatsNew");
        Wait.For(() => app.IsShown("WhatsNewText"), "What's new from Preferences");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("TOOL-09")]
    public void CheckingForUpdatesOpensTheUpdatePanel()
    {
        using var app = AppProcess.Start();

        ((ExpandCollapsePattern)app.Find("HelpMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        app.Press("Command-" + CommandCatalog.CheckForUpdates);

        // Whatever the network answers, the panel opens and says something.
        Wait.For(() => app.IsShown("UpdatePanel") && app.Text("UpdatePanelTitle").Length > 0, "the update panel");
        Assert.Equal(0, app.Close());
    }

    /// <summary>Whether the notice bar shows text containing <paramref name="text"/>.</summary>
    private static bool NoticeSays(AppProcess app, string text) =>
        app.IsShown("Notice") && app.Find("Notice").FindAll(TreeScope.Subtree, Condition.TrueCondition).Cast<AutomationElement>()
            .Any(element => element.Current.Name.Contains(text, StringComparison.Ordinal));
}
