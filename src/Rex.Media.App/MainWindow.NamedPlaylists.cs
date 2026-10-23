using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Library;
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
        _playlistsMenu.Items.Add(MenuItem("New playlist from this one...", "NewNamedPlaylist", () => Forget(NewPlaylistAsync(null))));
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
            sub.Items.Add(MenuItem("Shuffle play", "ShuffleNamed-" + playlist.Name, () => _player.PlayPlaylist(id, shuffle: true)));
            sub.Items.Add(MenuItem("Add to the end of this playlist", null, () => _player.PlayPlaylist(id, enqueue: true)));
            sub.Items.Add(new MenuFlyoutSeparator());
            sub.Items.Add(MenuItem("Edit contents...", "EditNamed-" + playlist.Name, () => Forget(EditPlaylistAsync(id))));
            sub.Items.Add(MenuItem("Rename...", null, () => Forget(RenamePlaylistAsync(id))));
            sub.Items.Add(MenuItem("Duplicate", null, () => _player.DuplicatePlaylist(id)));
            sub.Items.Add(MenuItem("Delete...", "DeleteNamed-" + playlist.Name, () => Forget(DeletePlaylistAsync(id))));
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

        _addToPlaylistMenu.Items.Add(MenuItem("New playlist...", "AddToNew", () => Forget(NewPlaylistAsync(_player.Item is { } item ? [item] : []))));
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
        var playNext = MenuItem("Move to Play next", "EntryPlayNext", () => QueueChosenNext(chosen));
        playNext.IsEnabled = chosen.Any(item => !ReferenceEquals(item, _player.Playlist.Current));
        menu.Items.Add(playNext);
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

        addTo.Items.Add(MenuItem("New playlist...", "EntryAddToNew", () => Forget(NewPlaylistAsync(chosen))));
        menu.Items.Add(addTo);
        menu.Items.Add(new MenuFlyoutSeparator());
        if (chosen is [var lone] && _player.IsMissing(lone))
        {
            var index = _player.Playlist.Items.ToList().IndexOf(lone);
            menu.Items.Add(MenuItem("Find it...", "EntryLocate", () => Forget(LocateAsync(index))));
        }

        if (_player.Playlist.Items.Any(_player.IsMissing))
        {
            menu.Items.Add(MenuItem("Look for the missing files in a folder...", "EntryRelink", () => Forget(RelinkAsync())));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var up = MenuItem("Move up", "EntryMoveUp", () => MoveSelectedPlaylistEntries(-1));
        up.IsEnabled = _player.Playlist.IndexOf(chosen[0]) > 0;
        menu.Items.Add(up);
        var down = MenuItem("Move down", "EntryMoveDown", () => MoveSelectedPlaylistEntries(1));
        down.IsEnabled = _player.Playlist.IndexOf(chosen[^1]) < _player.Playlist.Items.Count - 1;
        menu.Items.Add(down);
        menu.Items.Add(MenuItem("Remove", "EntryRemove", RemoveSelected));
    }

    /// <summary>Moves actual queue entries: their identity and cue marks are never duplicated.</summary>
    private void QueueChosenNext(IReadOnlyList<PlaylistItem> chosen)
    {
        _player.Playlist.QueueNext(chosen);
        PlaylistView.SelectedItems.Clear();
        foreach (var item in chosen)
        {
            var index = _player.Playlist.IndexOf(item);
            if (index >= 0 && !ReferenceEquals(item, _player.Playlist.Current))
            {
                PlaylistView.SelectedItems.Add(_playlistRows[index]);
            }
        }

        Say("Moved selected entries to Play next");
    }

    /// <summary>Asks where a missing active queue entry has moved (LIB-09).</summary>
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

    /// <summary>
    /// Edits a snapshot of a named playlist. Cancel discards all changes; Save persists once, and
    /// no edit touches the active playback queue. Row position identifies duplicate file entries.
    /// </summary>
    private async Task EditPlaylistAsync(string id)
    {
        if (_player.NamedPlaylists.FirstOrDefault(p => p.Id == id) is not { } playlist)
        {
            return;
        }

        var staged = playlist.Items.ToList();
        var facts = new TextBlock { Text = SavedPlaylistFacts.For(playlist, path => _library?.Entry(path)).Label };
        AutomationProperties.SetAutomationId(facts, "NamedPlaylistFacts");
        var editorOpen = true;
        var labels = new ObservableCollection<string>(staged.Select(item =>
            (item.Title ?? PlaylistItem.TitleOf(item.Location)) + " · " + (PlaylistItem.SourceOf(item.Location) ?? item.Location)));
        var list = new ListView
        {
            ItemsSource = labels,
            MinWidth = 360,
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.Single,
        };
        AutomationProperties.SetAutomationId(list, "NamedPlaylistEntries");
        AutomationProperties.SetName(list, "Saved playlist entries in playback order");

        var up = new Button { Content = "Move up" };
        var down = new Button { Content = "Move down" };
        var remove = new Button { Content = "Remove entry" };
        var locate = new Button { Content = "Locate file..." };
        var locationInfo = new TextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxWidth = 460 };
        AutomationProperties.SetAutomationId(locate, "NamedPlaylistLocateEntry");
        AutomationProperties.SetAutomationId(locationInfo, "NamedPlaylistEntryPath");
        AutomationProperties.SetAutomationId(up, "NamedPlaylistMoveUp");
        AutomationProperties.SetAutomationId(down, "NamedPlaylistMoveDown");
        AutomationProperties.SetAutomationId(remove, "NamedPlaylistRemoveEntry");
        void RefreshFacts()
        {
            facts.Text = SavedPlaylistFacts.For(playlist with { Items = staged.ToArray() },
                path => _library?.Entry(path)).Label;
        }


        void RefreshActions()
        {
            up.IsEnabled = list.SelectedIndex > 0;
            down.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < staged.Count - 1;
            remove.IsEnabled = list.SelectedIndex >= 0;
            locate.IsEnabled = list.SelectedIndex >= 0;
            locationInfo.Text = list.SelectedIndex >= 0 ? staged[list.SelectedIndex].Location : "Select an entry to see its file location.";
        }

        void Move(int direction)
        {
            var from = list.SelectedIndex;
            var to = from + direction;
            if (from < 0 || to < 0 || to >= staged.Count)
            {
                return;
            }

            var item = staged[from];
            staged.RemoveAt(from);
            staged.Insert(to, item);
            labels.Move(from, to);
            list.SelectedIndex = to;
            RefreshActions();
        }

        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);
        remove.Click += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index < 0 || index >= staged.Count)
            {
                return;
            }

            staged.RemoveAt(index);
            labels.RemoveAt(index);
            RefreshFacts();
            list.SelectedIndex = staged.Count == 0 ? -1 : Math.Min(index, staged.Count - 1);
            RefreshActions();
        };
        locate.Click += async (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index < 0 || index >= staged.Count)
            {
                return;
            }

            var prior = staged[index];
            var picker = Prepared(new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
            });
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFileAsync() is { } file && editorOpen
                && index < staged.Count && ReferenceEquals(prior, staged[index]))
            {
                var changed = PlayerController.RelocatedPlaylistEntry(prior, file.Path);
                staged[index] = changed;
                labels[index] = (changed.Title ?? PlaylistItem.TitleOf(changed.Location))
                    + " · " + (PlaylistItem.SourceOf(changed.Location) ?? changed.Location);
                RefreshFacts();
                RefreshActions();
            }
        };
        list.SelectionChanged += (_, _) => RefreshActions();
        list.SelectedIndex = staged.Count > 0 ? 0 : -1;
        RefreshActions();

        var actions = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(up);
        actions.Children.Add(down);
        actions.Children.Add(remove);
        actions.Children.Add(locate);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "Move, remove or locate saved entries. Files stay on disk; Cancel discards edits.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        content.Children.Add(facts);
        content.Children.Add(list);
        content.Children.Add(locationInfo);
        content.Children.Add(actions);
        var saved = await Ask("Edit " + playlist.Name, content, "Save changes", "Cancel");
        editorOpen = false;
        if (saved)
        {
            _player.ReplacePlaylistItems(id, staged);
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
            && LibraryActions.DeletePlaylistPrompt([playlist.Name]) is var prompt
            && await Ask(prompt.Title, new TextBlock { Text = prompt.Body, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }, "Delete"))
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
