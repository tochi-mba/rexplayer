using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>End-to-end Preferences navigation, editing and save/cancel behavior.</summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("UI-10")]
    public void PreferencesChangeTheSettingsAndAreKept()
    {
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.Preferences);
        var jump = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        ((RangeValuePattern)jump.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(25);
        ChoosePreferencesSection(app, "Transcription");
        var waitForSubtitles = app.Find("PreferencesHoldForSubtitles");
        Assert.Equal("Wait for generated subtitles before playing", waitForSubtitles.Current.Name);
        Assert.Contains("Existing usable subtitles play without waiting", waitForSubtitles.Current.HelpText, StringComparison.Ordinal);
        Assert.Contains("also turn on \"Transcribe whatever I play\" above", waitForSubtitles.Current.HelpText, StringComparison.Ordinal);
        ChoosePreferencesSection(app, "Music and artwork");
        var details = (ExpandCollapsePattern)app.Find("PreferencesArtworkDetails").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Assert.Equal(ExpandCollapseState.Collapsed, details.Current.ExpandCollapseState);
        details.Expand();
        var artwork = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Artwork detail (%)")));
        ((RangeValuePattern)artwork.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(135);
        var generated = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Generate artwork from music when no cover exists")));
        ((TogglePattern)generated.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        details.Collapse();
        ChoosePreferencesSection(app, "Performance");
        var performance = app.Find("PreferencesPerformance");
        ((ExpandCollapsePattern)performance.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var balanced = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "Balanced"))));
        ((SelectionItemPattern)balanced.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)performance.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        ChoosePreferencesSection(app, "Library");
        var mediaOpenClick = app.Find("PreferencesMediaOpenClick");
        ((ExpandCollapsePattern)mediaOpenClick.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var singleClick = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "Single click"))));
        ((SelectionItemPattern)singleClick.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)mediaOpenClick.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings is { ShortJumpSeconds: 25, AudioArtworkDetail: 135, GenerateAudioArtwork: false, Performance: Rex.Media.Settings.PerformanceChoice.Balanced, MediaOpenClickMode: Rex.Media.Settings.MediaOpenClick.SingleClick }, "the preferences to be saved");
        Assert.Equal(0, app.Close());
    }

    private static void ChoosePreferencesSection(AppProcess app, string name)
    {
        var selector = app.Find("PreferencesSection");
        Assert.False(selector.Current.IsOffscreen);
        var expand = (ExpandCollapsePattern)selector.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand();
        var item = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name))));
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        expand.Collapse();
        Wait.For(() => !app.Find("PreferencesSection").Current.IsOffscreen, "section navigation to stay visible");
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesNavigationKeepsUnsavedValuesAndCancelDiscardsThem()
    {
        using var app = AppProcess.Start();
        var before = app.SavedSettings.ShortJumpSeconds;
        app.Run(CommandCatalog.Preferences);
        Assert.Contains("all sections", app.Text("PreferencesSaveHint"), StringComparison.Ordinal);
        var jump = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        ((RangeValuePattern)jump.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(37);
        ChoosePreferencesSection(app, "Updates");
        Wait.For(() => !app.Find("PreferencesUpdateChecks").Current.IsOffscreen, "Updates to be reached directly");
        ChoosePreferencesSection(app, "Playback");
        Assert.Equal(37, ((RangeValuePattern)jump.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value);
        app.Press("CloseButton");
        app.Run(CommandCatalog.Preferences);
        var restored = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        Assert.Equal(before, ((RangeValuePattern)restored.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value);
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }
}
