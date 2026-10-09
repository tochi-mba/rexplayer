using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// Named playlists in the window (LIB-04): Media, Playlists to play, rename, copy or delete them;
/// Playback, Add to a playlist for what is playing; and the playlist's right-click menu for the
/// entries chosen there.
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyoutSubItem? _playlistsMenu;
    private MenuFlyoutSubItem? _addToPlaylistMenu;
    private string? _shownPlaylists;

    private void BuildNamedPlaylistMenus()
    {
        _playlistsMenu = new MenuFlyoutSubItem { Text = "Playlists" };
        AutomationProperties.SetAutomationId(_playlistsMenu, "PlaylistsMenu");
        var media = Menu.Items.First(item => item.Title == "Media");
        media.Items.Insert(media.Items.IndexOf(media.Items.First(item => AutomationProperties.GetAutomationId(item) == "Command-" + CommandCatalog.SavePlaylist)) + 1, _playlistsMenu);

        _addToPlaylistMenu = new MenuFlyoutSubItem { Text = "Add to a playlist" };
        AutomationProperties.SetAutomationId(_addToPlaylistMenu, "AddToPlaylistMenu");
        var playback = Menu.Items.First(item => item.Title == "Playback");
        playback.Items.Insert(playback.Items.IndexOf(playback.Items.First(item => AutomationProperties.GetAutomationId(item) == "Command-" + CommandCatalog.AddBookmark)), _addToPlaylistMenu);

        var entries = new MenuFlyout();
        entries.Opening += (_, _) => FillPlaylistEntryMenu(entries);
        PlaylistView.ContextFlyout = entries;
        _shownPlaylists = null;
    }

    /// <summary>Brings the playlist menus in line with the playlists kept, when they have changed.</summary>
    private void ShowNamedPlaylists()
    {
        var playlists = _player.NamedPlaylists;
        var shown = string.Join("\n", playlists.Select(p => p.Id + "=" + p.Name + "#" + p.Items.Count));
        if (shown == _shownPlaylists || _playlistsMenu is null || _addToPlaylistMenu is null)
        {
            return;
        }

        _shownPlaylists = shown;
        _playlistsMenu.Items.Clear();
        _playlistsMenu.Items.Add(MenuItem("New playlist from this one...", "NewNamedPlaylist", () => _ = NewPlaylistAsync(null)));
        if (playlists.Count > 0)
        {
            _playlistsMenu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var playlist in playlists)
        {
            var id = playlist.Id;
            var sub = new MenuFlyoutSubItem { Text = $"{playlist.Name} ({playlist.Items.Count})" };
            AutomationProperties.SetAutomationId(sub, "NamedPlaylist-" + playlist.Name);
            sub.Items.Add(MenuItem("Play", "PlayNamed-" + playlist.Name, () => _player.PlayPlaylist(id)));
            sub.Items.Add(MenuItem("Add to the end of this playlist", null, () => _player.PlayPlaylist(id, enqueue: true)));
            sub.Items.Add(new MenuFlyoutSeparator());
            sub.Items.Add(MenuItem("Rename...", null, () => _ = RenamePlaylistAsync(id)));
            sub.Items.Add(MenuItem("Duplicate", null, () => _player.DuplicatePlaylist(id)));
            sub.Items.Add(MenuItem("Delete...", "DeleteNamed-" + playlist.Name, () => _ = DeletePlaylistAsync(id)));
            _playlistsMenu.Items.Add(sub);
        }

        _addToPlaylistMenu.Items.Clear();
        foreach (var playlist in playlists)
        {
            var id = playlist.Id;
            _addToPlaylistMenu.Items.Add(MenuItem(playlist.Name, "AddTo-" + playlist.Name, () => _player.AddPlayingToPlaylist(id)));
        }

        if (playlists.Count > 0)
        {
            _addToPlaylistMenu.Items.Add(new MenuFlyoutSeparator());
        }

        _addToPlaylistMenu.Items.Add(MenuItem("New playlist...", "AddToNew", () => _ = NewPlaylistAsync(_player.Item is { } item ? [item] : [])));
    }

    /// <summary>The playlist's right-click menu, for the entries chosen when it opens.</summary>
    private void FillPlaylistEntryMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var chosen = ChosenEntries();
        if (chosen.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Choose entries first", IsEnabled = false });
            return;
        }

        menu.Items.Add(MenuItem("Play", "EntryPlay", () => _player.PlayAt(_player.Playlist.Items.ToList().IndexOf(chosen[0]))));
        var addTo = new MenuFlyoutSubItem { Text = "Add to a playlist" };
        AutomationProperties.SetAutomationId(addTo, "EntryAddTo");
        foreach (var playlist in _player.NamedPlaylists)
        {
            var id = playlist.Id;
            addTo.Items.Add(MenuItem(playlist.Name, "EntryAddTo-" + playlist.Name, () => _player.AddToPlaylist(id, chosen)));
        }

        if (addTo.Items.Count > 0)
        {
            addTo.Items.Add(new MenuFlyoutSeparator());
        }

        addTo.Items.Add(MenuItem("New playlist...", "EntryAddToNew", () => _ = NewPlaylistAsync(chosen)));
        menu.Items.Add(addTo);
        menu.Items.Add(new MenuFlyoutSeparator());
        if (chosen is [var lone] && _player.IsMissing(lone))
        {
            var index = _player.Playlist.Items.ToList().IndexOf(lone);
            menu.Items.Add(MenuItem("Find it...", "EntryLocate", () => _ = LocateAsync(index)));
        }

        if (_player.Playlist.Items.Any(_player.IsMissing))
        {
            menu.Items.Add(MenuItem("Look for the missing files in a folder...", "EntryRelink", () => _ = RelinkAsync()));
        }

        menu.Items.Add(MenuItem("Remove", "EntryRemove", RemoveSelected));
    }

    /// <summary>Asks where a missing entry's file is now (LIB-09).</summary>
    private async Task LocateAsync(int index)
    {
        var picker = Prepared(new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.List });
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFileAsync() is { } file && index < _player.Playlist.Items.Count)
        {
            _player.Relocate(index, file.Path);
        }
    }

    /// <summary>Asks for a folder to look through for every missing entry's file (LIB-09).</summary>
    private async Task RelinkAsync()
    {
        var picker = Prepared(new Windows.Storage.Pickers.FolderPicker());
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            _player.RelinkMissing(folder.Path);
        }
    }

    /// <summary>The playlist entries chosen in the playlist, in order.</summary>
    private List<PlaylistItem> ChosenEntries()
    {
        var items = _player.Playlist.Items;
        return [.. PlaylistView.SelectedRanges
            .SelectMany(range => Enumerable.Range(range.FirstIndex, (int)range.Length))
            .Order()
            .Where(index => index < items.Count)
            .Select(index => items[index])];
    }

    /// <summary>Asks for a name and keeps <paramref name="items"/> (the whole playlist when null) under it.</summary>
    private async Task NewPlaylistAsync(IReadOnlyList<PlaylistItem>? items)
    {
        var box = new TextBox { Text = _player.Item?.Artist ?? "My playlist", MinWidth = 320, Header = "Name" };
        box.SelectAll();
        AutomationProperties.SetAutomationId(box, "PlaylistName");
        if (await Ask("New playlist", box, "Make") && !string.IsNullOrWhiteSpace(box.Text))
        {
            _player.CreatePlaylist(box.Text, items);
        }
    }

    private async Task RenamePlaylistAsync(string id)
    {
        if (_player.NamedPlaylists.FirstOrDefault(p => p.Id == id) is not { } playlist)
        {
            return;
        }

        var box = new TextBox { Text = playlist.Name, MinWidth = 320 };
        box.SelectAll();
        AutomationProperties.SetAutomationId(box, "PlaylistName");
        if (await Ask("Rename the playlist", box, "Rename") && !string.IsNullOrWhiteSpace(box.Text))
        {
            _player.RenamePlaylist(id, box.Text);
        }
    }

    private async Task DeletePlaylistAsync(string id)
    {
        if (_player.NamedPlaylists.FirstOrDefault(p => p.Id == id) is { } playlist
            && await Ask($"Delete {playlist.Name}?", new TextBlock { Text = "The playlist goes; the files in it stay where they are.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }, "Delete"))
        {
            _player.DeletePlaylist(id);
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string? automationId, Action click)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(item, automationId);
        }

        item.Click += (_, _) => click();
        return item;
    }
}
