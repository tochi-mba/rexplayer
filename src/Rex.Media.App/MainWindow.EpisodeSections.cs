using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>User-confirmed intro and credits ranges: a quiet action near the player controls.</summary>
public sealed partial class MainWindow
{
    private readonly List<MenuFlyoutItem> _episodeMarkerCommands = [];

    private void AddEpisodeSectionMenus(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var menu = new MenuFlyoutSubItem { Text = "Episode sections" };
        AutomationProperties.SetAutomationId(menu, prefix + "EpisodeSections");
        void Add(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title, IsEnabled = _player.CanMarkEpisodeSections };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            _episodeMarkerCommands.Add(item);
        }

        Add("Mark intro start here", "IntroStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, true));
        Add("Mark intro end here", "IntroEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, false));
        Add("Clear intro markers", "IntroClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Intro));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Mark credits start here", "CreditsStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, true));
        Add("Mark credits end here", "CreditsEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false));
        Add("End credits at end of video", "CreditsVideoEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false, atVideoEnd: true));
        Add("Clear credits markers", "CreditsClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Credits));
        items.Add(new MenuFlyoutSeparator());
        items.Add(menu);
    }

    private void ShowEpisodeSkip()
    {
        var allowed = _player.CanMarkEpisodeSections;
        foreach (var action in _episodeMarkerCommands)
        {
            action.IsEnabled = allowed;
        }

        if (_player.AvailableEpisodeSkip is not { } skip || LibraryPane.Visibility == Visibility.Visible)
        {
            EpisodeSkipButton.Visibility = Visibility.Collapsed;
            return;
        }

        var name = skip.Kind == EpisodeSectionKind.Intro ? "Skip intro" : "Skip credits";
        EpisodeSkipButton.Content = name;
        AutomationProperties.SetName(EpisodeSkipButton, name);
        EpisodeSkipButton.Visibility = Visibility.Visible;
    }

    private void OnSkipEpisodeSection(object sender, RoutedEventArgs args)
    {
        _player.SkipEpisodeSection();
        ShowEpisodeSkip();
    }
}
