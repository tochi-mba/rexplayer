using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rex.Media.App;

/// <summary>
/// Help, About rexplayer: which version this is, when it last updated, where its settings and log
/// live, and the way to its notes, its site and a check for a newer version, all in one place.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowAboutAsync()
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 480 };
        var version = new TextBlock { Text = $"rexplayer {Version}", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(version, "AboutVersion");
        panel.Children.Add(version);
        if (Rex.Media.AppCore.Updates.UpdateFlow.LastUpdated(_settings.LastUpdatedVersion, _settings.LastUpdatedAt, DateTimeOffset.Now) is { } updated)
        {
            panel.Children.Add(new TextBlock { Text = updated, TextWrapping = TextWrapping.Wrap });
        }

        var privacy = new TextBlock
        {
            Text = "A local media player for Windows by REX Technologies, under the MIT License. No account is required. Optional metadata lookups and update checks use the network when enabled; local files and listening history remain on this computer.",
            TextWrapping = TextWrapping.Wrap,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(privacy, "AboutPrivacy");
        panel.Children.Add(privacy);
        var data = new TextBlock { Text = "Settings, library and log: " + App.DataRoot, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(data, "AboutDataFolder");
        panel.Children.Add(data);
        var credit = new TextBlock { Text = Rex.Media.AppCore.Metadata.MetadataPolicy.DataCredit, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(credit, "AboutMetadataCredit");
        panel.Children.Add(credit);

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        HyperlinkButton Link(string text, string id, Action click)
        {
            var link = new HyperlinkButton { Content = text };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, id);
            link.Click += (_, _) =>
            {
                _dialog?.Hide();
                OnWindowThread(() => click());
            };
            links.Children.Add(link);
            return link;
        }

        Link("What's new", "AboutWhatsNew", () => Forget(ShowWhatsNewAsync()));
        Link("Website and help", "AboutWebsite", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(HelpUrl)));
        Link("Open the log folder", "AboutLogFolder", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLogFolder));
        panel.Children.Add(links);

        if (await Ask("About rexplayer", panel, "Check for updates"))
        {
            Forget(CheckForUpdatesAsync(asked: true));
        }
    }
}
