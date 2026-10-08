using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// What the player remembers, in the window: the offer to resume (PB-11), bookmarks (PB-10) and
/// recent media (LIB-07) in the menus, and the store they are kept in (LIB-11).
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyoutSubItem? _recentMenu;
    private MenuFlyoutSubItem? _bookmarksMenu;
    private string _shownMemory = "";

    /// <summary>The store beside the settings; when it cannot be read, one in memory, so playing still works.</summary>
    private static RexStore OpenStore()
    {
        var path = Path.Combine(App.DataRoot, "store.log");
        try
        {
            var store = RexStore.Open(path);
            if (store.Skipped > 0)
            {
                App.Log.Warning(LogSource, $"{store.Skipped} damaged record(s) in the store were skipped.");
            }

            return store;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Error(LogSource, "The store could not be read, so nothing will be remembered this time: " + ex.Message);
            return RexStore.InMemory();
        }
    }

    private void WireMemory()
    {
        ResumeBar.ActionButton = new Button { Content = "Resume" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(ResumeBar.ActionButton, "ResumeButton");
        ResumeBar.ActionButton.Click += (_, _) => _player.AcceptResume();
        ResumeBar.CloseButtonClick += (_, _) => _player.DeclineResume();
    }

    /// <summary>The menus that hold remembered things; made with the others.</summary>
    private void BuildMemoryMenus()
    {
        _recentMenu = new MenuFlyoutSubItem { Text = "Recent media" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_recentMenu, "RecentMenu");
        var media = Menu.Items.First(item => item.Title == "Media");
        media.Items.Insert(Math.Max(0, media.Items.Count - 2), _recentMenu);

        _bookmarksMenu = new MenuFlyoutSubItem { Text = "Bookmarks" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_bookmarksMenu, "BookmarksMenu");
        var playback = Menu.Items.First(item => item.Title == "Playback");
        playback.Items.Add(new MenuFlyoutSeparator());
        playback.Items.Add(_bookmarksMenu);
        _shownMemory = "";
    }

    /// <summary>Brings the resume offer, recent media and bookmarks on screen in line with the player.</summary>
    private void ShowMemory()
    {
        var offer = _player.ResumeOffer is { } at && _player.State is not (SessionState.Opening or SessionState.Idle or SessionState.Faulted);
        if (offer)
        {
            ResumeBar.Message = "You stopped at " + TimeText.Format(_player.ResumeOffer!.Value, _player.Duration) + ".";
        }

        ResumeBar.IsOpen = offer;

        // The menus are made again only when what they list has changed.
        var recent = _player.Recent;
        var bookmarks = _player.Bookmarks;
        var shown = string.Join("\n", recent) + "\n--\n" + string.Join("\n", bookmarks.Select(b => b.Name + "@" + b.At.Ticks));
        if (shown == _shownMemory || _recentMenu is null || _bookmarksMenu is null)
        {
            return;
        }

        _shownMemory = shown;
        _recentMenu.Items.Clear();
        foreach (var location in recent)
        {
            var item = new MenuFlyoutItem { Text = PlaylistItem.TitleOf(location) };
            ToolTipService.SetToolTip(item, location);
            item.Click += (_, _) => _player.Open([location]);
            _recentMenu.Items.Add(item);
        }

        _recentMenu.IsEnabled = recent.Count > 0;
        _bookmarksMenu.Items.Clear();
        for (var i = 0; i < bookmarks.Count; i++)
        {
            var index = i;
            var bookmark = new MenuFlyoutSubItem { Text = $"{bookmarks[i].Name} ({TimeText.Format(bookmarks[i].At, _player.Duration)})" };
            var go = new MenuFlyoutItem { Text = "Go there" };
            go.Click += (_, _) => _player.GoToBookmark(index);
            var rename = new MenuFlyoutItem { Text = "Rename..." };
            rename.Click += (_, _) => _ = RenameBookmarkAsync(index);
            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (_, _) => _player.DeleteBookmark(index);
            bookmark.Items.Add(go);
            bookmark.Items.Add(rename);
            bookmark.Items.Add(delete);
            _bookmarksMenu.Items.Add(bookmark);
        }

        _bookmarksMenu.IsEnabled = bookmarks.Count > 0;
    }

    private async Task RenameBookmarkAsync(int index)
    {
        if (index >= _player.Bookmarks.Count)
        {
            return;
        }

        var box = new TextBox { Text = _player.Bookmarks[index].Name, MinWidth = 320 };
        if (await Ask("Rename the bookmark", box, "Rename") && !string.IsNullOrWhiteSpace(box.Text))
        {
            _player.RenameBookmark(index, box.Text);
        }
    }
}
