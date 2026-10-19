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

        panel.Children.Add(new TextBlock
        {
            Text = "A media player for Windows by REX Technologies, under the MIT License. It has no account and sends nothing about you.",
            TextWrapping = TextWrapping.Wrap,
        });
        var data = new TextBlock { Text = "Settings, library and log: " + App.DataRoot, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(data, "AboutDataFolder");
        panel.Children.Add(data);

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        HyperlinkButton Link(string text, string id, Action click)
        {
            var link = new HyperlinkButton { Content = text };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, id);
            link.Click += (_, _) =>
            {
                _dialog?.Hide();
                DispatcherQueue.TryEnqueue(() => click());
            };
            links.Children.Add(link);
            return link;
        }

        Link("What's new", "AboutWhatsNew", () => _ = ShowWhatsNewAsync());
        Link("Website and help", "AboutWebsite", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(HelpUrl)));
        Link("Open the log folder", "AboutLogFolder", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLogFolder));
        panel.Children.Add(links);

        if (await Ask("About rexplayer", panel, "Check for updates"))
        {
            _ = CheckForUpdatesAsync(asked: true);
        }
    }
}
