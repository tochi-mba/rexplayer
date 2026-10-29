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

    [Fact]
    [Capability("AU-18")]
    public void EveryBasicVisualizerSettingHasARealAutomationNameAndResetHelp()
    {
        using var app = AppProcess.Start();
        app.Run(CommandCatalog.VisualizerSettings);
        var mode = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "VisualizerChoice")));
        Assert.Equal("Visualisation", mode.Current.Name);

        var sensitivity = app.Find("Visual-sensitivity");
        Assert.Equal("Sensitivity", sensitivity.Current.Name);
        Assert.Contains("from 0.25 to 4", sensitivity.Current.HelpText, StringComparison.Ordinal);
        Assert.Equal("Colours", app.Find("Visual-colors").Current.Name);

        // A colour of its own counts only with One colour, so it is offered only then.
        Assert.False(app.IsShown("Visual-color"));
        var one = app.OpenMenu(new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "One colour")), "One colour", false, "Visual-colors");
        ((SelectionItemPattern)one.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        try
        {
            ((ExpandCollapsePattern)app.Find("Visual-colors").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        }
        catch (InvalidOperationException)
        {
            // Choosing already closed the list.
        }

        Wait.For(() => app.IsShown("Visual-color"), "the colour to be offered with One colour");
        Assert.Equal("Colour", app.Find("Visual-color").Current.Name);
        Assert.Equal("Bars", app.Find("Visual-bars").Current.Name);
        Assert.Equal("Show the peaks", app.Find("Visual-peaks").Current.Name);

        var reset = app.Find("VisualizerReset");
        Assert.Equal("Restore defaults for this visualisation", reset.Current.Name);
        Assert.Contains("Other visualisations keep their saved values", reset.Current.HelpText, StringComparison.Ordinal);
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Capability("UI-10")]
    public void PreferencesReturnsFromShortcutEditorWithoutLosingPendingValues(bool saveShortcuts, bool savePreferences)
    {
        using var app = AppProcess.Start();
        var before = app.SavedSettings;
        app.Run(CommandCatalog.Preferences);
        var jump = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        ((RangeValuePattern)jump.GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(37);
        ChoosePreferencesSection(app, "Keyboard and mouse");
        app.Press("PreferencesKeyboard");
        Wait.For(() => app.IsShown("ShortcutSearch"), "the shortcut editor to open");
        // The editor names the jump by the length just set, though Preferences has not saved it.
        Assert.StartsWith("Jump forward 37 seconds:", app.Find("Shortcut-" + CommandCatalog.JumpForwardShort).Current.Name, StringComparison.Ordinal);
        var pans = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Zoomed in, the wheel and two-finger scrolling move about the picture")));
        ((TogglePattern)pans.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        app.Press(saveShortcuts ? "PrimaryButton" : "CloseButton");
        Wait.For(() => app.IsShown("PreferencesCategorySearch"), "Preferences to return after the shortcut editor");
        Assert.Equal("Keyboard and mouse", PreferencesSectionShown(app));
        Assert.Equal(before.ShortJumpSeconds, app.SavedSettings.ShortJumpSeconds);
        ChoosePreferencesSection(app, "Playback");
        var retained = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Short jump (seconds)")));
        Assert.Equal(37, ((RangeValuePattern)retained.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value);
        app.Press(savePreferences ? "PrimaryButton" : "CloseButton");
        Wait.For(() => !app.IsShown("PreferencesCategorySearch"), "Preferences to close");
        Wait.For(() => app.SavedSettings.ShortJumpSeconds == (savePreferences ? 37 : before.ShortJumpSeconds)
            && app.SavedSettings.WheelPansWhenZoomed == (saveShortcuts ? !before.WheelPansWhenZoomed : before.WheelPansWhenZoomed),
            "each dialog's Save or Cancel choice to apply independently");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesShowsOnlyItsChosenSectionAndFitsASmallerWindow()
    {
        using var app = AppProcess.Start();
        ((TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern)).Resize(720, 640);
        app.Run(CommandCatalog.Preferences);
        Assert.Equal("Preferences section", app.Find("PreferencesSection").Current.Name);
        Assert.False(app.IsShown("PreferencesCategoryList"));
        Assert.True(app.IsShown("PreferencesCategorySearch"), "The search is offered in a narrow window too.");
        Assert.Null(app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "PreferencesUpdateChecks")));
        ChoosePreferencesSection(app, "Subtitles");
        var scroll = (ScrollPattern)app.Find("PreferencesScroll").GetCurrentPattern(ScrollPattern.Pattern);
        Wait.For(() => scroll.Current.VerticallyScrollable, "the long section to scroll in a small window");
        Wait.For(() =>
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 60);
            return scroll.Current.VerticalScrollPercent > 30;
        }, "the subtitle section to scroll");
        var offset = scroll.Current.VerticalScrollPercent;
        ChoosePreferencesSection(app, "Updates");
        Assert.False(app.Find("PreferencesUpdateChecks").Current.IsOffscreen);
        Assert.Null(app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Subtitle font")));
        foreach (var id in new[] { "PreferencesSection", "PrimaryButton", "CloseButton" })
        {
            var control = app.Find(id);
            Assert.False(control.Current.IsOffscreen);
            Assert.True(app.Window.Current.BoundingRectangle.Contains(control.Current.BoundingRectangle), id + " must fit inside the window");
        }
        ChoosePreferencesSection(app, "Subtitles");
        Wait.For(() => Math.Abs(scroll.Current.VerticalScrollPercent - offset) < 2, "the subtitle scroll position to return");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesOffersTheEnhancedSubjectLockModelsUnderVideo()
    {
        using var app = AppProcess.Start();
        app.Run(CommandCatalog.Preferences);
        ChoosePreferencesSection(app, "Video and pictures");
        // Only the chosen section is in the window, so finding the models at all puts them under Video and
        // pictures; they sit at its foot, below the part scrolled into view. The section reaches UI
        // Automation a piece at a time as it lays out, so the note can be there before its buttons.
        bool Present(string id) => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id)) is not null;
        Wait.For(() => Present("PreferencesSubjectModelsNote") && Present("PreferencesSubjectModelsDownload"),
            "the Enhanced Subject Lock models and their download to show under Video and pictures");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesSearchFindsWrappedLabelsChoicesAndClosedExpanderOptions()
    {
        using var app = AppProcess.Start();
        ((TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern)).Resize(1000, 760);
        app.Run(CommandCatalog.Preferences);
        var search = (ValuePattern)app.Find("PreferencesCategorySearch").GetCurrentPattern(ValuePattern.Pattern);
        Assert.False(app.Find("PreferencesCategorySearch").Current.IsOffscreen);

        string? Section() => PreferencesSectionShown(app);

        // This label is a TextBlock after WrapLabels. Search must not compare its type name.
        search.SetValue("Keep audio-only items playing in the library");
        Wait.For(() => Section() == "Library", "search to find the wrapped checkbox label");

        // Choice values are searchable even when their dropdown is closed.
        search.SetValue("Fastest");
        Wait.For(() => Section() == "Performance", "search to find a choice option");

        // The artwork options sit in a closed group, which the search opens to show the setting.
        search.SetValue("Artwork detail (%)");
        Wait.For(() => Section() == "Music and artwork", "search to find a collapsed option");
        Wait.For(() => ((ExpandCollapsePattern)app.Find("PreferencesArtworkDetails")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern)).Current.ExpandCollapseState == ExpandCollapseState.Expanded,
            "the group holding the setting found to open");
        Assert.True(app.IsShown("PreferencesScroll"));

        search.SetValue("a setting that does not exist 012345");
        Wait.For(() => app.IsShown("PreferencesSearchEmpty") && !app.IsShown("PreferencesScroll"),
            "the page to say nothing matches, in place of a section");

        // Restoring the search should restore a useful section instead of the no-results state.
        search.SetValue("sUbTiTlE");
        Wait.For(() => Section() == "Subtitles" && !app.IsShown("PreferencesSearchEmpty"),
            "search to recover after no matches");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesResizeKeepsTheCompactPickerAndRestoresTheWideCategoryRail()
    {
        using var app = AppProcess.Start();
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(1000, 760);
        app.Run(CommandCatalog.Preferences);
        Wait.For(() => app.IsShown("PreferencesCategorySearch") && app.IsShown("PreferencesCategoryList"),
            "the wide Settings rail to show");
        var search = (ValuePattern)app.Find("PreferencesCategorySearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("Fastest");
        Wait.For(() => PreferencesSectionShown(app) == "Performance", "search to select Performance");

        transform.Resize(720, 640);
        Wait.For(() => !app.IsShown("PreferencesCategoryList") && app.IsShown("PreferencesSection") && app.IsShown("PreferencesCategorySearch"),
            "the compact picker to replace the wide rail, beside the search");
        Assert.Equal("Performance", PreferencesSectionShown(app));

        // A section the search did not find ends the search.
        ChoosePreferencesSection(app, "Subtitles");

        transform.Resize(1000, 760);
        Wait.For(() => app.IsShown("PreferencesCategorySearch") && app.IsShown("PreferencesCategoryList"),
            "the category rail to return after resize");
        Wait.For(() => search.Current.Value.Length == 0 && PreferencesSectionShown(app) == "Subtitles",
            "the rail to show every section again, Subtitles chosen");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    /// <summary>
    /// Chooses a Preferences section the way the window offers it: on the rail beside the page in a
    /// wide window, in the section picker above it in a narrow one.
    /// </summary>
    private static void ChoosePreferencesSection(AppProcess app, string name)
    {
        var navigation = Wait.Until(() =>
        {
            try
            {
                foreach (var id in new[] { "PreferencesCategoryList", "PreferencesSection" })
                {
                    var control = app.Window.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                    if (control is not null && !control.Current.IsOffscreen)
                    {
                        return control;
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
                // The responsive dialog replaced its navigator while it was read; try again.
            }

            return null;
        });
        if (navigation.Current.AutomationId == "PreferencesCategoryList")
        {
            var entry = Wait.Until(() => navigation.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name))));
            ((SelectionItemPattern)entry.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Wait.For(() => PreferencesSectionShown(app) == name, "the section " + name + " to be chosen on the rail");
            return;
        }

        var selector = navigation;
        Assert.False(selector.Current.IsOffscreen);
        // The list drops down on the window's thread after Expand returns, and can close again as
        // focus settles, so it is opened until the section is really on offer.
        var item = app.OpenMenu(new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name)), "the section " + name, false, "PreferencesSection");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)app.Find("PreferencesSection").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Wait.For(() => !app.Find("PreferencesSection").Current.IsOffscreen, "section navigation to stay visible");
    }

    /// <summary>The Preferences section chosen now, on the rail or in the picker, whichever is shown; null when none is.</summary>
    private static string? PreferencesSectionShown(AppProcess app)
    {
        try
        {
            var navigation = app.IsShown("PreferencesCategoryList") ? "PreferencesCategoryList" : "PreferencesSection";
            var selection = ((SelectionPattern)app.Find(navigation).GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection();
            return selection.Length == 1 ? selection[0].Current.Name : null;
        }
        catch (ElementNotAvailableException)
        {
            // Replaced while it was read (the rail's items change with a search): read again.
            return null;
        }
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
        Wait.For(() => !app.IsShown("PreferencesCategorySearch"), "Preferences to close after Cancel");
        Assert.Equal(before, app.SavedSettings.ShortJumpSeconds);
        Assert.Equal(0, app.Close());
    }
}
