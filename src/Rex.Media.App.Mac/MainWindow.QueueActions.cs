using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>
/// What Up Next does with its entries: the row menu, playing next, moving, removing and clearing
/// with their undo, jumping to the playing entry, drops onto the list, and finding missing files.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The menu for the chosen entries, in the Windows playlist menu's order.</summary>
    private NSMenu QueueEntryMenu(IReadOnlyList<PlaylistItem> chosen)
    {
        var menu = new NSMenu("Entry") { AutoEnablesItems = false };
        NSMenuItem Add(string title, Action action, bool enabled = true, string? tip = null)
        {
            var item = new NSMenuItem(title, (_, _) => action()) { Enabled = enabled, ToolTip = tip };
            menu.AddItem(item);
            return item;
        }

        var first = chosen[0];
        var playing = ReferenceEquals(first, _player.Playlist.Current);
        Add("Play", () => PlayQueueEntry(first), !_player.IsMissing(first));
        // With nothing playing, "next" is the front of the queue, as on Windows.
        Add("Play next", () => QueueChosenNext(chosen),
            chosen.Any(item => !ReferenceEquals(item, _player.Playlist.Current) && !_player.IsMissing(item)),
            chosen.Count == 1 ? "Play " + first.Title + " next" : "Make these entries play next, including when shuffle is enabled");
        var local = chosen.Select(item => item.Location).Where(File.Exists).ToArray();
        Add("Show in Finder", () => NSWorkspace.SharedWorkspace.ActivateFileViewer(local.Select(NSUrl.FromFilename).ToArray()),
            local.Length > 0);
        if (_player.NamedPlaylists.Count > 0)
        {
            var addTo = new NSMenu("Add to a playlist") { AutoEnablesItems = false };
            foreach (var playlist in _player.NamedPlaylists)
            {
                var id = playlist.Id;
                addTo.AddItem(new NSMenuItem(playlist.Name, (_, _) =>
                {
                    _player.AddToPlaylist(id, chosen);
                    RefreshSavedPlaylists();
                }));
            }

            menu.AddItem(new NSMenuItem("Add to a playlist") { Submenu = addTo });
        }

        menu.AddItem(NSMenuItem.SeparatorItem);
        if (chosen is [var lone] && _player.IsMissing(lone))
        {
            Add("Locate…", () => LocateMissingQueueItem(lone), tip: "Point this playlist entry at its new file path");
        }

        if (_player.Playlist.Items.Any(_player.IsMissing))
        {
            Add("Look for the missing files in a folder…", RelinkMissingQueueFiles, tip: "Find every missing queue file in a folder");
        }

        menu.AddItem(NSMenuItem.SeparatorItem);
        Add("Move up", () => MoveChosenQueueEntries(chosen, -1), _player.Playlist.IndexOf(first) > 0, "Move earlier in the queue");
        Add("Move down", () => MoveChosenQueueEntries(chosen, 1),
            _player.Playlist.IndexOf(chosen[^1]) < _player.Playlist.Items.Count - 1, "Move later in the queue");
        Add("Timing…", () => EditQueueTiming(first), tip: "Set start, stop or maximum run time for this entry");
        Add("Remove", () => RemoveQueueEntries(chosen), tip: playing && chosen.Count == 1
            ? "Remove the playing entry from Up Next" : "Remove from Up Next");
        return menu;
    }

    /// <summary>Moves actual queue entries next: their identity and cue marks are never duplicated.</summary>
    private void QueueChosenNext(IReadOnlyList<PlaylistItem> chosen)
    {
        var before = _player.Playlist.Items.ToArray();
        _player.Playlist.QueueNext(chosen.Where(item => !_player.IsMissing(item)).ToList());
        if (!before.SequenceEqual(_player.Playlist.Items))
        {
            _queueUndoOrder = before;
        }

        RefreshQueue();
    }

    /// <summary>Moves the chosen entries together one place and keeps them chosen where they land.</summary>
    private void MoveChosenQueueEntries(IReadOnlyList<PlaylistItem> chosen, int direction)
    {
        if (chosen.Count == 0)
        {
            return;
        }

        var indexes = chosen.Select(_player.Playlist.IndexOf).Order().ToList();
        if ((direction < 0 && indexes[0] == 0) || (direction > 0 && indexes[^1] == _player.Playlist.Items.Count - 1))
        {
            return;
        }

        var previous = _player.Playlist.Items.ToArray();
        IEnumerable<int> moving = direction < 0 ? indexes : indexes.OrderDescending();
        foreach (var index in moving)
        {
            _player.Playlist.Move(index, index + direction);
        }

        _queueUndoOrder = previous;
        var lead = _player.Playlist.IndexOf(chosen[0]);
        _queuePage = lead / QueuePageSize;
        RefreshQueue();
        SelectQueueEntries(chosen);
        Say(chosen.Count == 1 ? $"Moved to position {lead + 1}" : $"Moved {chosen.Count} entries");
    }

    private void SelectQueueEntries(IReadOnlyList<PlaylistItem> entries)
    {
        if (_queueTable is null)
        {
            return;
        }

        var rows = new NSMutableIndexSet();
        for (var i = 0; i < _queueLines.Count; i++)
        {
            if (entries.Any(item => ReferenceEquals(item, _queueLines[i].Item)))
            {
                rows.Add((nuint)i);
            }
        }

        _queueTable.SelectRows(rows, false);
        if (rows.Count > 0)
        {
            _queueTable.ScrollRowToVisible((nint)rows.FirstIndex);
        }
    }

    /// <summary>Removes the exact chosen entries in one change, keeping what they were so Undo removal brings them back.</summary>
    private void RemoveQueueEntries(IReadOnlyList<PlaylistItem> chosen)
    {
        if (chosen.Count == 0)
        {
            return;
        }

        _undoQueueRemoval = _player.Playlist.CaptureRemoval();
        _undoQueuePlayerItem = _player.Item;
        _player.Playlist.RemoveEntries(chosen);
        RefreshQueue();
        Say(chosen.Count == 1 ? "Removed entry from the queue. Undo removal is available"
            : $"Removed {chosen.Count} entries from the queue. Undo removal is available");
    }

    /// <summary>Clears the queue as the Clear the playlist command does, keeping it for Undo removal.</summary>
    private void ClearQueueWithUndo()
    {
        var saved = _player.Playlist.Items.Count > 0 ? _player.Playlist.CaptureRemoval() : null;
        Run(CommandCatalog.ClearPlaylist);
        if (saved is not null)
        {
            _undoQueueRemoval = saved;
            _undoQueuePlayerItem = _player.Item;
        }

        RefreshQueue();
    }

    private void UndoQueueReorder()
    {
        if (_queueUndoOrder is not { } before)
        {
            return;
        }

        _queueUndoOrder = null;
        if (_player.Playlist.RestoreOrder(before))
        {
            Say("Queue order restored");
        }

        RefreshQueue();
    }

    private void UndoQueueRemoval()
    {
        if (_undoQueueRemoval is not { } saved)
        {
            return;
        }

        var restored = ReferenceEquals(_undoQueuePlayerItem, _player.Item) && _player.Playlist.UndoRemoval(saved);
        _undoQueueRemoval = null;
        _undoQueuePlayerItem = null;
        RefreshQueue();
        Say(restored ? "Removed queue entries restored" : "The queue changed; removal cannot be undone");
    }

    /// <summary>Brings the playing entry into view without changing the queue, playback or the selection.</summary>
    private void JumpToPlayingQueueEntry()
    {
        if (_player.Playlist.CurrentIndex is not (>= 0 and var current) || _queueTable is null)
        {
            return;
        }

        _queuePage = current / QueuePageSize;
        RefreshQueue();
        var row = current - _queueLinesStart;
        if (row >= 0 && row < _queueLines.Count)
        {
            _queueTable.ScrollRowToVisible(row);
        }
    }

    /// <summary>Moves the lines dragged within the list to land before the line at <paramref name="row"/>.</summary>
    private void DropQueueLines(IReadOnlyList<int> draggedRows, int row)
    {
        var dragged = draggedRows.Where(index => index >= 0 && index < _queueLines.Count)
            .Select(index => _queueLines[index].Item).ToList();
        if (dragged.Count == 0)
        {
            return;
        }

        var previous = _player.Playlist.Items.ToArray();
        var target = _queueLinesStart + row;
        var wanted = previous.ToList();
        var before = dragged.Count(item => _player.Playlist.IndexOf(item) < target);
        wanted.RemoveAll(item => dragged.Any(moved => ReferenceEquals(moved, item)));
        wanted.InsertRange(Math.Clamp(target - before, 0, wanted.Count), dragged);
        var changed = false;
        // Lines are distinct entries even when the same file is queued twice; moving by identity
        // keeps duplicates in exactly the order they were dropped.
        for (var place = 0; place < wanted.Count; place++)
        {
            var from = _player.Playlist.IndexOf(wanted[place]);
            if (from != place)
            {
                _player.Playlist.Move(from, place);
                changed = true;
            }
        }

        if (changed)
        {
            _queueUndoOrder = previous;
        }

        RefreshQueue();
        SelectQueueEntries(dragged);
    }

    /// <summary>Inserts files dropped on the list at <paramref name="index"/> in the whole queue.</summary>
    private void DropIntoQueue(IReadOnlyList<string> paths, int index)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var added = _player.Insert(paths, Math.Clamp(index, 0, _player.Playlist.Items.Count));
        Say(added == 1 ? "Inserted 1 item in Up Next." : $"Inserted {added} items in Up Next.");
        RefreshQueue();
    }

    private void LocateMissingQueueItem(PlaylistItem item)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Locate " + item.Title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } path) return;
            var index = _player.Playlist.IndexOf(item);
            if (index >= 0)
            {
                _player.Relocate(index, path);
                RefreshQueue();
            }
        });
    }

    private void RelinkMissingQueueFiles()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Choose a folder containing moved media";
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.AllowsMultipleSelection = false;
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } folder) return;
            // The controller intentionally relinks only unique file-name matches.
            _player.RelinkMissing(folder);
            RefreshQueue();
        });
    }
}
