using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>Native saved-playlist editing and file import/export over shared playlist storage.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _savedPlaylistsWindow;
    private NSPopUpButton? _savedPlaylistPicker;
    private RexLabel? _savedPlaylistSummary;
    private NSStackView? _savedPlaylistNameEditor;
    private RexLabel? _savedPlaylistNamePrompt;
    private NSTextField? _savedPlaylistNameField;
    private Action<string>? _savePlaylistName;
    private RexButton? _savedPlaylistCreate;
    private RexButton? _savedPlaylistPlay;
    private RexButton? _savedPlaylistAppend;
    private RexButton? _savedPlaylistRename;
    private RexButton? _savedPlaylistDuplicate;
    private RexButton? _savedPlaylistRemove;

    private void SavePlaylistWithPanel()
    {
        if (_player.Playlist.Items.Count == 0)
        {
            Say("Add media before saving a playlist.");
            return;
        }

        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = "Playlist.m3u8";
        panel.Begin(response =>
        {
            if (response == 1 && panel.Url?.Path is { } path)
            {
                _player.SavePlaylist(path);
            }
        });
    }

    private void OpenPlaylistWithPanel()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Open a playlist file";
        panel.CanChooseDirectories = false;
        panel.CanChooseFiles = true;
        panel.AllowsMultipleSelection = false;
        panel.Begin(response =>
        {
            if (response == 1 && panel.Url?.Path is { } path)
            {
                _player.Open([path]);
            }
        });
    }

    private void ShowSavedPlaylists()
    {
        if (_savedPlaylistsWindow is null)
        {
            _savedPlaylistsWindow = new NSWindow(new CGRect(0, 0, 560, 360),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Named playlists - rexplayer",
            };
            _savedPlaylistsWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_savedPlaylistsWindow);
            var heading = MacTheme.Label("Your playlists", 4, semibold: true);
            var hint = MacTheme.WrappingLabel(
                "Saved playlists keep their own names and entries, separately from Up Next.", 2, DesignRole.TextSecondary);
            _savedPlaylistPicker = new NSPopUpButton(new CGRect(0, 0, 500, 28), false)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            ((NSView)_savedPlaylistPicker).AccessibilityLabel = "Saved playlist";
            _savedPlaylistPicker.Activated += (_, _) => RefreshSavedPlaylists();
            _savedPlaylistSummary = MacTheme.Label("", 1, DesignRole.TextSecondary);
            _savedPlaylistNamePrompt = MacTheme.Label("", 2);
            _savedPlaylistNameField = new NSTextField(new CGRect(0, 0, 465, 24))
            {
                PlaceholderString = "Playlist name",
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            ((NSView)_savedPlaylistNameField).AccessibilityLabel = "Playlist name";
            var saveName = MacTheme.TextButton("Save name", CommitSavedPlaylistName, RexButtonLook.Accent);
            saveName.KeyEquivalent = "\r";
            var cancelName = MacTheme.TextButton("Cancel", HideSavedPlaylistNameEditor);
            var nameActions = MacTheme.Stack(false, 1, saveName, cancelName);
            // The naming editor sits on a raised card, so it reads as one step apart from the list.
            var nameFields = MacTheme.Stack(true, 1, _savedPlaylistNamePrompt, _savedPlaylistNameField, nameActions);
            nameFields.EdgeInsets = new NSEdgeInsets(MacTheme.Space(2), MacTheme.Space(2), MacTheme.Space(2), MacTheme.Space(2));
            var nameCard = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(nameCard, nameFields);
            _savedPlaylistNameField.WidthAnchor.ConstraintEqualTo(nameFields.WidthAnchor, 1, -MacTheme.Space(2) * 2).Active = true;
            _savedPlaylistNameEditor = MacTheme.Stack(true, 0, nameCard);
            _savedPlaylistNameEditor.Hidden = true;

            var create = MacTheme.TextButton("Save queue as new…", () =>
            {
                ShowSavedPlaylistNameEditor("Name the playlist", "My playlist", name =>
                {
                    _player.CreatePlaylist(name);
                    RefreshSavedPlaylists();
                });
            });
            _savedPlaylistCreate = create;
            var play = MacTheme.TextButton("Play", () =>
            {
                if (SelectedSavedPlaylistId() is { } id) _player.PlayPlaylist(id);
            }, RexButtonLook.Accent);
            _savedPlaylistPlay = play;
            var append = MacTheme.TextButton("Add current queue", () =>
            {
                if (SelectedSavedPlaylistId() is { } id)
                {
                    _player.AddToPlaylist(id, _player.Playlist.Items);
                    RefreshSavedPlaylists();
                }
            });
            _savedPlaylistAppend = append;
            var rename = MacTheme.TextButton("Rename…", () =>
            {
                if (SelectedSavedPlaylistId() is not { } id) return;
                var originalName = _player.NamedPlaylists.First(p => p.Id == id).Name;
                ShowSavedPlaylistNameEditor("Rename the playlist", originalName, name =>
                {
                    _player.RenamePlaylist(id, name);
                    RefreshSavedPlaylists();
                });
            });
            _savedPlaylistRename = rename;
            var duplicate = MacTheme.TextButton("Duplicate", () =>
            {
                if (SelectedSavedPlaylistId() is { } id)
                {
                    _player.DuplicatePlaylist(id);
                    RefreshSavedPlaylists();
                }
            });
            _savedPlaylistDuplicate = duplicate;
            var remove = MacTheme.TextButton("Delete…", () =>
            {
                if (SelectedSavedPlaylistId() is not { } id) return;
                ConfirmInWindow("Delete this saved playlist? The media files will not be deleted.", () =>
                {
                    _player.DeletePlaylist(id);
                    RefreshSavedPlaylists();
                });
            });
            _savedPlaylistRemove = remove;
            var first = MacTheme.Stack(false, 1, create, play, append);
            var second = MacTheme.Stack(false, 1, rename, duplicate, remove);
            var words = MacTheme.Stack(true, 1, heading, hint);
            var choice = MacTheme.Stack(true, 1, _savedPlaylistPicker, _savedPlaylistSummary);
            var actions = MacTheme.Stack(true, 1, first, second);
            var stack = MacTheme.Stack(true, 3, words, choice, _savedPlaylistNameEditor, actions);
            stack.EdgeInsets = new NSEdgeInsets(MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4));
            var container = new NSView(new CGRect(0, 0, 560, 360));
            MacTheme.Fill(container, stack);
            _savedPlaylistsWindow.ContentView = container;
            var wide = MacTheme.Space(4) * 2;
            foreach (var group in new NSView[] { words, choice, _savedPlaylistNameEditor })
            {
                group.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -wide).Active = true;
            }

            hint.WidthAnchor.ConstraintEqualTo(words.WidthAnchor).Active = true;
            _savedPlaylistPicker.WidthAnchor.ConstraintEqualTo(choice.WidthAnchor).Active = true;
            nameCard.WidthAnchor.ConstraintEqualTo(_savedPlaylistNameEditor.WidthAnchor).Active = true;
            _savedPlaylistPicker.NextKeyView = _savedPlaylistNameField;
            _savedPlaylistNameField.NextKeyView = saveName;
            saveName.NextKeyView = cancelName;
            cancelName.NextKeyView = create;
            create.NextKeyView = play;
            play.NextKeyView = append;
            append.NextKeyView = rename;
            rename.NextKeyView = duplicate;
            duplicate.NextKeyView = remove;
            remove.NextKeyView = _savedPlaylistPicker;
            _savedPlaylistsWindow.InitialFirstResponder = _savedPlaylistPicker;
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
            _savedPlaylistCreate?.PerformClick(_savedPlaylistCreate);
            _savedPlaylistNameField!.StringValue = "Mac smoke playlist";
            CommitSavedPlaylistName();
            return _player.NamedPlaylists.Any(p => p.Name == "Mac smoke playlist");
        }

        if (action == "rename")
        {
            _savedPlaylistRename?.PerformClick(_savedPlaylistRename);
            _savedPlaylistNameField!.StringValue = "Renamed Mac playlist";
            CommitSavedPlaylistName();
            return _player.NamedPlaylists.Any(p => p.Name == "Renamed Mac playlist");
        }

        if (action == "duplicate")
        {
            var before = _player.NamedPlaylists.Count;
            _savedPlaylistDuplicate?.PerformClick(_savedPlaylistDuplicate);
            return _player.NamedPlaylists.Count == before + 1;
        }

        if (action == "append")
        {
            var before = _player.NamedPlaylists.First().Items.Count;
            _savedPlaylistPicker!.SelectItem(0);
            _savedPlaylistAppend?.PerformClick(_savedPlaylistAppend);
            return _player.NamedPlaylists.First().Items.Count > before;
        }

        if (action == "play")
        {
            _savedPlaylistPicker!.SelectItem(0);
            _savedPlaylistPlay?.PerformClick(_savedPlaylistPlay);
            return _player.Playlist.Items.Count > 0;
        }

        if (action == "delete")
        {
            var before = _player.NamedPlaylists.Count;
            _savedPlaylistPicker!.SelectItem(0);
            _savedPlaylistRemove?.PerformClick(_savedPlaylistRemove);
            var accept = _confirmationAccept;
            HideConfirmation();
            accept?.Invoke();
            return _player.NamedPlaylists.Count == before - 1;
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
