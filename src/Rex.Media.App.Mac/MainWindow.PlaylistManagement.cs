using Rex.Media.AppCore.Commands;

namespace Rex.Media.App.Mac;

/// <summary>Native saved-playlist editing and file import/export over shared playlist storage.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _savedPlaylistsWindow;
    private NSPopUpButton? _savedPlaylistPicker;
    private NSTextField? _savedPlaylistSummary;
    private NSStackView? _savedPlaylistNameEditor;
    private NSTextField? _savedPlaylistNamePrompt;
    private NSTextField? _savedPlaylistNameField;
    private Action<string>? _savePlaylistName;

    private void SavePlaylistWithPanel()
    {
        if (_player.Playlist.Items.Count == 0)
        {
            Say("Add media before saving a playlist.");
            return;
        }

        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = "Playlist.m3u8";
        if (panel.RunModal() == 1 && panel.Url?.Path is { } path)
        {
            _player.SavePlaylist(path);
        }
    }

    private void OpenPlaylistWithPanel()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Open a playlist file";
        panel.CanChooseDirectories = false;
        panel.CanChooseFiles = true;
        panel.AllowsMultipleSelection = false;
        if (panel.RunModal() == 1 && panel.Url?.Path is { } path)
        {
            _player.Open([path]);
        }
    }

    private void ShowSavedPlaylists()
    {
        if (_savedPlaylistsWindow is null)
        {
            _savedPlaylistsWindow = new NSWindow(new CGRect(0, 0, 560, 310),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Named playlists - rexplayer",
            };
            _savedPlaylistsWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Your playlists");
            heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateWrappingLabel(
                "Saved playlists keep their own names and entries, separately from Up Next.");
            hint.TextColor = NSColor.SecondaryLabel;
            _savedPlaylistPicker = new NSPopUpButton(new CGRect(0, 0, 500, 28), false);
            _savedPlaylistPicker.Activated += (_, _) => RefreshSavedPlaylists();
            _savedPlaylistSummary = NSTextField.CreateLabel("");
            _savedPlaylistSummary.TextColor = NSColor.SecondaryLabel;
            _savedPlaylistNamePrompt = NSTextField.CreateLabel("");
            _savedPlaylistNameField = new NSTextField(new CGRect(0, 0, 465, 24))
            {
                PlaceholderString = "Playlist name",
            };
            ((NSView)_savedPlaylistNameField).AccessibilityLabel = "Playlist name";
            var saveName = NSButton.CreateButton("Save name", CommitSavedPlaylistName);
            saveName.KeyEquivalent = "\r";
            var cancelName = NSButton.CreateButton("Cancel", HideSavedPlaylistNameEditor);
            var nameActions = NSStackView.FromViews([saveName, cancelName]);
            nameActions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            nameActions.Spacing = 8;
            _savedPlaylistNameEditor = NSStackView.FromViews([
                _savedPlaylistNamePrompt, _savedPlaylistNameField, nameActions,
            ]);
            _savedPlaylistNameEditor.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            _savedPlaylistNameEditor.Spacing = 8;
            _savedPlaylistNameEditor.Hidden = true;

            var create = NSButton.CreateButton("Save queue as new…", () =>
            {
                ShowSavedPlaylistNameEditor("Name the playlist", "My playlist", name =>
                {
                    _player.CreatePlaylist(name);
                    RefreshSavedPlaylists();
                });
            });
            var play = NSButton.CreateButton("Play", () =>
            {
                if (SelectedSavedPlaylistId() is { } id) _player.PlayPlaylist(id);
            });
            var append = NSButton.CreateButton("Add current queue", () =>
            {
                if (SelectedSavedPlaylistId() is { } id)
                {
                    _player.AddToPlaylist(id, _player.Playlist.Items);
                    RefreshSavedPlaylists();
                }
            });
            var rename = NSButton.CreateButton("Rename…", () =>
            {
                if (SelectedSavedPlaylistId() is not { } id) return;
                var originalName = _player.NamedPlaylists.First(p => p.Id == id).Name;
                ShowSavedPlaylistNameEditor("Rename the playlist", originalName, name =>
                {
                    _player.RenamePlaylist(id, name);
                    RefreshSavedPlaylists();
                });
            });
            var duplicate = NSButton.CreateButton("Duplicate", () =>
            {
                if (SelectedSavedPlaylistId() is { } id)
                {
                    _player.DuplicatePlaylist(id);
                    RefreshSavedPlaylists();
                }
            });
            var remove = NSButton.CreateButton("Delete…", () =>
            {
                if (SelectedSavedPlaylistId() is not { } id) return;
                ConfirmInWindow("Delete this saved playlist? The media files will not be deleted.", () =>
                {
                    _player.DeletePlaylist(id);
                    RefreshSavedPlaylists();
                });
            });
            var first = NSStackView.FromViews([create, play, append]);
            first.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            first.Spacing = 8;
            var second = NSStackView.FromViews([rename, duplicate, remove]);
            second.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            second.Spacing = 8;
            var stack = NSStackView.FromViews([
                heading, hint, _savedPlaylistPicker, _savedPlaylistSummary,
                _savedPlaylistNameEditor, first, second,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            _savedPlaylistsWindow.ContentView = stack;
            hint.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _savedPlaylistPicker.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _savedPlaylistsWindow.Center();
        }

        RefreshSavedPlaylists();
        _savedPlaylistsWindow.MakeKeyAndOrderFront(null);
    }

    private void ShowSavedPlaylistNameEditor(string prompt, string initial, Action<string> save)
    {
        _savePlaylistName = save;
        _savedPlaylistNamePrompt!.StringValue = prompt;
        _savedPlaylistNameField!.StringValue = initial;
        _savedPlaylistNameEditor!.Hidden = false;
        _savedPlaylistsWindow!.MakeFirstResponder(_savedPlaylistNameField);
    }

    private void CommitSavedPlaylistName()
    {
        var name = _savedPlaylistNameField?.StringValue.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _savedPlaylistNamePrompt!.StringValue = "Enter a playlist name before saving.";
            return;
        }

        var save = _savePlaylistName;
        HideSavedPlaylistNameEditor();
        save?.Invoke(name);
    }

    private void HideSavedPlaylistNameEditor()
    {
        _savePlaylistName = null;
        if (_savedPlaylistNameEditor is not null)
        {
            _savedPlaylistNameEditor.Hidden = true;
        }
    }

    private string? SelectedSavedPlaylistId()
    {
        var index = (int)(_savedPlaylistPicker?.IndexOfSelectedItem ?? -1);
        return index >= 0 && index < _player.NamedPlaylists.Count
            ? _player.NamedPlaylists[index].Id : null;
    }

    private void RefreshSavedPlaylists()
    {
        if (_savedPlaylistPicker is null || _savedPlaylistSummary is null)
        {
            return;
        }

        var previousId = SelectedSavedPlaylistId();
        _savedPlaylistPicker.RemoveAllItems();
        foreach (var playlist in _player.NamedPlaylists)
        {
            _savedPlaylistPicker.AddItem(playlist.Name);
        }

        var index = _player.NamedPlaylists.ToList().FindIndex(p => p.Id == previousId);
        if (_player.NamedPlaylists.Count > 0)
        {
            _savedPlaylistPicker.SelectItem(Math.Max(0, index));
        }

        _savedPlaylistPicker.Enabled = _player.NamedPlaylists.Count > 0;
        var selected = _player.NamedPlaylists.ElementAtOrDefault(Math.Max(0, index));
        _savedPlaylistSummary.StringValue = selected is null
            ? "No named playlists yet. Save your current queue to make one."
            : selected.Items.Count + (selected.Items.Count == 1 ? " saved item" : " saved items");
    }

    private bool SavedPlaylistSmokeAction(string? action, string? path)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_PLAYLIST_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowSavedPlaylists();
            return _savedPlaylistsWindow?.IsVisible == true;
        }

        if (action == "save-file" && !string.IsNullOrWhiteSpace(path))
        {
            return _player.SavePlaylist(path);
        }

        if (action == "create")
        {
            _player.CreatePlaylist("Mac smoke playlist");
            RefreshSavedPlaylists();
            return _player.NamedPlaylists.Any(p => p.Name == "Mac smoke playlist");
        }

        return false;
    }

    private void DisposeSavedPlaylists()
    {
        HideSavedPlaylistNameEditor();
        _savedPlaylistsWindow?.Close();
        _savedPlaylistsWindow?.Dispose();
        _savedPlaylistsWindow = null;
    }
}
