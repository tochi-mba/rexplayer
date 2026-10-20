using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Performance;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Windows' energy saver and the performance mode: the Preferences choice, and with the status
/// forced on through REXPLAYER_FAKE_ENERGY_SAVER, the notice and the mode Preferences says is in effect.
/// </summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("UI-10")]
    public void PreferencesOfferAutomaticWhileEnergySaverIsOnAndKeepTheChoice()
    {
        using var app = AppProcess.Start();
        Assert.True(app.SavedSettings.AutomaticWhenEnergySaver);

        app.Run(CommandCatalog.Preferences);
        ChoosePreferencesSection(app, "Performance");
        var automatic = app.Find("PreferencesEnergySaverAutomatic");
        Assert.Equal("When Energy Saver is on, use the Automatic performance mode", automatic.Current.Name);
        Assert.Equal("The mode chosen above returns when Energy Saver turns off.", automatic.Current.HelpText);
        var toggle = (TogglePattern)automatic.GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.On, toggle.Current.ToggleState);
        toggle.Toggle();
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings is { AutomaticWhenEnergySaver: false, Performance: PerformanceChoice.Automatic }, "the preference to be saved");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void EnergySaverPutsAutomaticInEffectSaysSoAndYieldsToAModeChosenMeanwhile()
    {
        using var app = AppProcess.Start(extraSettings: "\"performance\": \"BestQuality\"", environment: new Dictionary<string, string> { ["REXPLAYER_FAKE_ENERGY_SAVER"] = "on" });
        Wait.For(() => app.LogText.Contains(EnergySaverMode.StartedNotice, StringComparison.Ordinal), "the notice that Automatic is in effect");
        Wait.For(() => app.LogText.Contains("Performance: Automatic, because Energy Saver is on. Automatic: ", StringComparison.Ordinal), "the budget to be applied for Automatic");

        app.Run(CommandCatalog.Preferences);
        ChoosePreferencesSection(app, "Performance");
        Assert.StartsWith("Automatic, because Energy Saver is on. Automatic: ", app.Text("PreferencesPerformanceNow"), StringComparison.Ordinal);
        var performance = app.Find("PreferencesPerformance");
        Assert.Equal("Best quality", ((SelectionPattern)performance.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection().Single().Current.Name);

        // A mode chosen now wins until Energy Saver changes again, and the dialog says so before saving.
        // Fastest, because Automatic under Energy Saver is already Balanced on a capable computer.
        ((ExpandCollapsePattern)performance.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var fastest = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, "Fastest"))));
        ((SelectionItemPattern)fastest.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)performance.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Wait.For(() => app.Text("PreferencesPerformanceNow") == "Fastest, as chosen.", "the dialog to show the chosen mode in effect");
        app.Press("PrimaryButton");

        Wait.For(() => app.SavedSettings is { Performance: PerformanceChoice.Fastest, AutomaticWhenEnergySaver: true }, "the choice to be saved");
        Wait.For(() => app.LogText.Contains("Performance: Fastest, as chosen.", StringComparison.Ordinal), "the chosen mode to be applied");
        Assert.Equal(0, app.Close());
    }
}
