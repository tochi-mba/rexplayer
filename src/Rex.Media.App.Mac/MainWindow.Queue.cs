using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>
/// Up Next: the pane at the right of the player window, as the Windows playlist pane is, backed
/// directly by the shared playlist and transport. Its header says what plays now and next, its
/// undo buttons show only while they can act, and its list pages through long queues.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The pasteboard type a line dragged within the list carries: its place on the page.</summary>
    private const string QueueLineType = "com.rextechnologies.rexplayer.queue-line";

    private const int QueuePageSize = 75;

    private QueuePaneView? _queueRoot;
    private NSStackView? _queueLayout;
    private RexLabel? _queueHeader;
    private RexLabel? _queueCount;
    private RexButton? _queueJump;
    private RexButton? _queueMore;
    private NSStackView? _queueStatus;
    private RexLabel? _queueNow;
    private RexLabel? _queueNext;
    private NSStackView? _queueUndo;
    private RexButton? _queueUndoReorder;
    private RexButton? _queueUndoRemoval;
    private RexLabel? _queueHint;
    private NSScrollView? _queueScroll;
    private QueueTableView? _queueTable;
    private QueueSource? _queueSource;
    private NSStackView? _queueEmpty;
    private NSStackView? _queuePages;
    private RexLabel? _queuePageLabel;
    private RexButton? _previousQueuePage;
    private RexButton? _nextQueuePage;
    private NSMenuItem? _queueShuffle;
    private NSMenuItem? _queueRepeat;
    private NSMenuItem? _queueRelink;
    private QueueSummary? _queueSummary;
    private IReadOnlyList<QueueLine> _queueLines = [];
    private int _queueLinesStart;
    private IReadOnlyList<PlaylistItem>? _queueUndoOrder;
    private PlaylistRemovalSnapshot? _undoQueueRemoval;
    private PlaylistItem? _undoQueuePlayerItem;
    private bool _closingQueue;
    private int _queuePage;
    private bool _queueRefreshQueued;
    private int? _queueRowsSignature;
    private CGRect? _queueSmokeFrame;

    /// <summary>
    /// The queue's one-line summary ("91 items • viewing 76–91 • Next: …"). The pane shows its
    /// parts in the header, status and page line; the whole is the list's accessibility help, so
    /// VoiceOver says which part of a long queue is in view, and it is what automation reports.
    /// </summary>
    private sealed class QueueSummary
    {
        public string StringValue { get; set; } = "";
    }

    // A burst of playlist or player changes (a removal, a reshuffle, play to pause) queues one
    // refresh, not one per change.
    private void InitializeQueue()
    {
        _player.Playlist.Changed += (_, _) => QueueRefreshSoon(showState: true);
        _player.Changed += (_, _) => QueueRefreshSoon(showState: false);
    }

    private void QueueRefreshSoon(bool showState)
    {
        if (_queueRefreshQueued)
        {
            return;
        }

        _queueRefreshQueued = true;
        OnWindowThread(() =>
        {
            _queueRefreshQueued = false;
            RefreshQueue();
            if (showState)
            {
                ShowState();
            }
        });
    }

    /// <summary>Shows Up Next at the right of the window, building it the first time.</summary>
    private void ShowQueue()
    {
        if (_queueRoot is null)
        {
            if (_queueHost is null)
            {
                return;
            }

            _queuePage = Math.Max(0, _player.Playlist.CurrentIndex) / QueuePageSize;
            BuildQueuePane(_queueHost);
        }

        RefreshQueue();
        SetQueueShown(true);
    }

    /// <summary>Hides Up Next; the stage or the library takes its room.</summary>
    private void HideQueue() => SetQueueShown(false);

    private void ToggleQueue()
    {
        if (QueueShown)
        {
            HideQueue();
        }
        else
        {
            ShowQueue();
        }
    }

    /// <summary>Escape inside the pane: hide it and hand the keyboard back to the player.</summary>
    private void HideQueueToPlayer()
    {
        HideQueue();
        _window.MakeFirstResponder(_shellQueueToggle is { } toggle ? toggle : _window.ContentView);
    }

    private void BuildQueuePane(NSView host)
    {
        _queueSummary = new QueueSummary();
        _queueHeader = MacTheme.Label("Up Next", 3, semibold: true);
        _queueCount = MacTheme.Label("0 items", 1, Rex.Media.AppCore.Design.DesignRole.TextSecondary);
        _queueCount.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        _queueCount.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _queueJump = MacTheme.IconButton("scope", "Jump to the playing item", JumpToPlayingQueueEntry);
        var moreMenu = BuildQueueMoreMenu();
        _queueMore = MacTheme.IconButton("ellipsis", "More Up Next actions", () =>
        {
            RefreshQueueMoreMenu();
            var more = _queueMore!;
            moreMenu.PopUpMenu(null, new CGPoint(0, more.IsFlipped ? more.Bounds.Height + 2 : -2), more);
        });
        var header = MacTheme.Stack(false, 1, _queueHeader, _queueCount, _queueJump, _queueMore);
        header.Spacing = 8;

        _queueNow = MacTheme.Label("Nothing playing", 1);
        _queueNow.WidthAnchor.ConstraintLessThanOrEqualTo(170).Active = true;
        var dot = MacTheme.Label(" · ", 1, Rex.Media.AppCore.Design.DesignRole.TextSecondary);
        ((NSView)dot).AccessibilityElement = false;
        _queueNext = MacTheme.Label("No upcoming items", 1, Rex.Media.AppCore.Design.DesignRole.TextSecondary);
        _queueNext.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _queueStatus = MacTheme.Stack(false, 0, _queueNow, dot, _queueNext);
        _queueStatus.Spacing = 0;

        _queueUndoReorder = MacTheme.TextButton("Undo reorder", UndoQueueReorder, label: "Undo last queue reorder");
        _queueUndoRemoval = MacTheme.TextButton("Undo removal", UndoQueueRemoval, label: "Restore removed queue entries");
        _queueUndo = MacTheme.Stack(false, 1, _queueUndoReorder, _queueUndoRemoval);

        _queueHint = MacTheme.WrappingLabel("Drag to reorder · ⌥↑/↓ to move selection", 1, Rex.Media.AppCore.Design.DesignRole.TextTertiary);

        var table = new QueueTableView
        {
            HeaderView = null,
            RowHeight = 56,
            IntercellSpacing = new CGSize(0, 2),
            AllowsMultipleSelection = true,
            AllowsEmptySelection = true,
            AllowsColumnReordering = false,
            ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
            Style = NSTableViewStyle.Plain,
            Move = direction => MoveChosenQueueEntries(ChosenQueueEntries(), direction),
            Play = () => PlayQueueEntry(ChosenQueueEntries().FirstOrDefault()),
            Remove = () => RemoveQueueEntries(ChosenQueueEntries()),
            Cancel = HideQueueToPlayer,
        };
        table.EntryMenu = () => ChosenQueueEntries() is { Count: > 0 } chosen ? QueueEntryMenu(chosen) : null;
        table.AddColumn(new NSTableColumn("entry") { Width = 320, ResizingMask = NSTableColumnResizing.Autoresizing });
        table.RegisterForDraggedTypes([QueueLineType, NSPasteboardType.FileUrl.GetConstant()!]);
        table.SetDraggingSourceOperationMask(NSDragOperation.Move, true);
        table.SetDraggingSourceOperationMask(NSDragOperation.None, false);
        table.DoubleClick += (_, _) =>
        {
            if (table.ClickedRow >= 0 && table.ClickedRow < _queueLines.Count)
            {
                PlayQueueEntry(_queueLines[(int)table.ClickedRow].Item);
            }
        };
        ((NSView)table).AccessibilityLabel = "Playlist, drag entries to reorder";
        _queueSource = new QueueSource(this);
        table.Source = _queueSource;
        _queueTable = table;
        _queueScroll = new NSScrollView
        {
            HasVerticalScroller = true,
            HasHorizontalScroller = false,
            AutohidesScrollers = true,
            DocumentView = table,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        MacTheme.Style(table, _queueScroll);

        var emptyTitle = MacTheme.Label("Your playlist is empty", 2, semibold: true);
        emptyTitle.Alignment = NSTextAlignment.Center;
        var emptyText = MacTheme.WrappingLabel("Open or drop media here to start a playlist.", 2, Rex.Media.AppCore.Design.DesignRole.TextSecondary);
        emptyText.Alignment = NSTextAlignment.Center;
        _queueEmpty = MacTheme.Stack(true, 0, emptyTitle, emptyText);
        _queueEmpty.Spacing = 6;
        _queueEmpty.Alignment = NSLayoutAttribute.CenterX;
        ((NSView)_queueEmpty).AccessibilityElement = true;
        ((NSView)_queueEmpty).AccessibilityLabel = "Your playlist is empty";
        emptyText.WidthAnchor.ConstraintLessThanOrEqualTo(250).Active = true;
        var list = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        MacTheme.Fill(list, _queueScroll);
        list.AddSubview(_queueEmpty);
        _queueEmpty.CenterXAnchor.ConstraintEqualTo(list.CenterXAnchor).Active = true;
        _queueEmpty.CenterYAnchor.ConstraintEqualTo(list.CenterYAnchor).Active = true;
        list.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);

        _previousQueuePage = MacTheme.IconButton("chevron.left", "Previous page", () =>
        {
            _queuePage = Math.Max(0, _queuePage - 1);
            RefreshQueue();
        });
        _nextQueuePage = MacTheme.IconButton("chevron.right", "Next page", () =>
        {
            _queuePage++;
            RefreshQueue();
        });
        _queuePageLabel = MacTheme.Label("Page 1 of 1", 1, Rex.Media.AppCore.Design.DesignRole.TextSecondary);
        _queuePages = MacTheme.Stack(false, 1);
        _queuePages.SetViews([_previousQueuePage, _queuePageLabel, _nextQueuePage], NSStackViewGravity.Center);

        var stack = MacTheme.Stack(true, 1, header, _queueStatus, _queueUndo, _queueHint, list, _queuePages);
        stack.EdgeInsets = new NSEdgeInsets(8, 10, 8, 10);
        stack.SetCustomSpacing(2, header);
        foreach (var wide in new NSView[] { header, _queueStatus, _queueHint, list, _queuePages })
        {
            wide.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -20).Active = true;
        }

        _queueLayout = stack;
        _queueRoot = new QueuePaneView(HideQueueToPlayer);
        ((NSView)_queueRoot).AccessibilityLabel = "Playlist";
        MacTheme.Fill(_queueRoot, stack, 0);
        MacTheme.Fill(host, _queueRoot, 0);

        // Header buttons, then the list, then the pages; hidden ones are passed over.
        _queueJump.NextKeyView = _queueMore;
        _queueMore.NextKeyView = _queueUndoReorder;
        _queueUndoReorder.NextKeyView = _queueUndoRemoval;
        _queueUndoRemoval.NextKeyView = table;
        table.NextKeyView = _previousQueuePage;
        _previousQueuePage.NextKeyView = _nextQueuePage;
    }

    /// <summary>
    /// The pane's overflow: the queue actions the old window had as buttons, kept off the pane as
    /// Windows keeps them in its menus, each calling the same action as before.
    /// </summary>
    private NSMenu BuildQueueMoreMenu()
    {
        var menu = new NSMenu("Up Next") { AutoEnablesItems = false };
        menu.AddItem(new NSMenuItem("Add files…", (_, _) => AddFilesToQueue()));
        _queueShuffle = new NSMenuItem("Shuffle: Off", (_, _) => Run(CommandCatalog.ToggleShuffle));
        _queueRepeat = new NSMenuItem("Repeat: Off", (_, _) => Run(CommandCatalog.CycleRepeat));
        menu.AddItem(_queueShuffle);
        menu.AddItem(_queueRepeat);
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Save the playlist…", (_, _) => SavePlaylistWithPanel()));
        menu.AddItem(new NSMenuItem("Open a playlist…", (_, _) => OpenPlaylistWithPanel()));
        menu.AddItem(new NSMenuItem("Named playlists…", (_, _) => ShowSavedPlaylists()));
        menu.AddItem(new NSMenuItem("Recent media & quick slots…", (_, _) => ShowHistory()));
        menu.AddItem(NSMenuItem.SeparatorItem);
        _queueRelink = new NSMenuItem("Look for the missing files in a folder…", (_, _) => RelinkMissingQueueFiles())
        {
            ToolTip = "Find every missing queue file in a folder",
        };
        menu.AddItem(_queueRelink);
        menu.AddItem(new NSMenuItem("Clear the playlist", (_, _) => ClearQueueWithUndo()));
        return menu;
    }

    private void RefreshQueueMoreMenu()
    {
        if (_queueShuffle is not null)
        {
            _queueShuffle.Title = _player.Playlist.Shuffle ? "Shuffle: On" : "Shuffle: Off";
        }

        if (_queueRepeat is not null)
        {
            _queueRepeat.Title = "Repeat: " + _player.Playlist.Repeat;
        }

        if (_queueRelink is not null)
        {
            _queueRelink.Enabled = _player.Playlist.Items.Any(_player.IsMissing);
        }
    }

    private void AddFilesToQueue()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.Begin(response =>
        {
            if (response == 1)
            {
                _player.Open(panel.Urls.Select(url => url.Path!).ToList(), enqueue: true);
            }
        });
    }

    private void RefreshQueue()
    {
        if (_closingQueue || _queueRoot is null || _queueTable is null || _queueSummary is null)
        {
            return;
        }

        var queue = _player.Playlist.Items;
        // A removed last-page entry must never leave Up Next on an empty, stale page.
        var page = LibraryPageSlice.For(queue.Count, _queuePage, QueuePageSize);
        _queuePage = page.Page;
        var (start, end) = (page.Start, page.End);
        var pages = Math.Max(1, (queue.Count + QueuePageSize - 1) / QueuePageSize);
        _previousQueuePage!.Enabled = page.HasPrevious;
        _nextQueuePage!.Enabled = page.HasNext;
        _queuePageLabel!.StringValue = $"Page {_queuePage + 1} of {pages}";
        _queuePages!.Hidden = pages <= 1;
        RefreshQueueMoreMenu();

        var presentation = QueuePresentation.Of(queue.Count, _player.Playlist.Current,
            _player.Playlist.PeekNext(automatic: true), _player.Item, _player.State);
        _queueCount!.StringValue = presentation.Count;
        _queueCount.Hidden = !presentation.ShowsStatus;
        _queueNow!.StringValue = presentation.Now;
        _queueNext!.StringValue = presentation.Next;
        _queueStatus!.Hidden = !presentation.ShowsStatus;
        _queueEmpty!.Hidden = presentation.ShowsStatus;
        _queueHint!.Hidden = !presentation.ShowsDragHint;
        _queueJump!.Hidden = !presentation.CanJump;
        ShowQueueUndo(_queueUndoReorder!, _queueUndoOrder is { } order && _player.Playlist.CanRestoreOrder(order));
        ShowQueueUndo(_queueUndoRemoval!, _undoQueueRemoval is { } removed
            && ReferenceEquals(_undoQueuePlayerItem, _player.Item) && _player.Playlist.CanUndoRemoval(removed));
        _queueUndo!.Hidden = _queueUndoReorder!.Hidden && _queueUndoRemoval!.Hidden;

        // Reloading the list happens only when what its lines show has changed; the header and
        // summary are cheap and always refreshed.
        var signature = QueueRowsSignature(queue, start, end, presentation.CurrentState);
        if (signature != _queueRowsSignature)
        {
            _queueRowsSignature = signature;
            var chosen = ChosenQueueEntries();
            _queueLines = QueueLines(queue, start, end, presentation.CurrentState);
            var newPage = _queueLinesStart != start;
            _queueLinesStart = start;
            _queueTable.ReloadData();
            var keep = new NSMutableIndexSet();
            for (var i = 0; i < _queueLines.Count; i++)
            {
                if (chosen.Any(item => ReferenceEquals(item, _queueLines[i].Item)))
                {
                    keep.Add((nuint)i);
                }
            }

            _queueTable.SelectRows(keep, false);
            if (newPage && _queueLines.Count > 0)
            {
                _queueTable.ScrollRowToVisible(0);
            }
        }

        _queueSummary.StringValue = (queue.Count == 1 ? "1 item" : $"{queue.Count} items")
            + (queue.Count > QueuePageSize ? $" • viewing {start + 1}–{end}" : "")
            + (_player.Playlist.PeekNext(automatic: false) is { } nextItem ? $" • Next: {nextItem.Title}" : "");
        ((NSView)_queueTable).AccessibilityHelp = _queueSummary.StringValue;
    }

    /// <summary>
    /// Undo reorder and Undo removal show only while they can act; one that is spent goes, handing
    /// the keyboard to the list it acted on, as on Windows.
    /// </summary>
    private void ShowQueueUndo(RexButton button, bool usable)
    {
        if (!usable && button.Window?.FirstResponder == button)
        {
            button.Window.MakeFirstResponder(_queueTable);
        }

        button.Enabled = usable;
        button.Hidden = !usable;
    }

    private int QueueRowsSignature(IReadOnlyList<PlaylistItem> queue, int start, int end, string? state)
    {
        var hash = new HashCode();
        hash.Add(start);
        hash.Add(end);
        hash.Add(queue.Count);
        hash.Add(_player.Playlist.CurrentIndex);
        hash.Add(state);
        hash.Add(_player.Duration.Ticks);
        for (var i = start; i < end; i++)
        {
            hash.Add(queue[i]);
            hash.Add(_player.IsMissing(queue[i]));
        }

        return hash.ToHashCode();
    }

    /// <summary>The page's lines: title, artist and album (or where the file is), and how long each plays.</summary>
    private List<QueueLine> QueueLines(IReadOnlyList<PlaylistItem> queue, int start, int end, string? state)
    {
        var current = _player.Playlist.CurrentIndex;
        var lines = new List<QueueLine>(end - start);
        for (var i = start; i < end; i++)
        {
            var item = queue[i];
            var entry = _mediaLibrary?.Entry(item.Location);
            var detail = string.Join(" · ", new[] { item.Artist, entry?.Album }
                .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct(StringComparer.CurrentCultureIgnoreCase));
            if (detail.Length == 0)
            {
                detail = PlaylistItem.SourceOf(item.Location) ?? "Local media";
            }

            var whole = entry?.Duration;
            TimeSpan? length = i == current && _player.Duration > TimeSpan.Zero ? _player.Duration
                : item.End is { } end1 && whole is { } full && full < end1 ? full - item.Start
                : item.End is { } cueEnd ? cueEnd - item.Start
                : whole is { } known ? known - item.Start : null;
            var duration = length is { Ticks: > 0 } shown ? TimeText.Format(shown, shown) : "";
            var symbol = MediaFiles.LibraryKindOf(item.Location) switch
            {
                Rex.Media.Library.LibraryKind.Music => "music.note",
                Rex.Media.Library.LibraryKind.Picture => "photo",
                _ => "film",
            };
            lines.Add(new QueueLine(item, item.Title, detail, duration, i == current, _player.IsMissing(item),
                i == current ? state : null, symbol));
        }

        return lines;
    }

    /// <summary>The entries chosen in the list, in queue order.</summary>
    private List<PlaylistItem> ChosenQueueEntries()
    {
        if (_queueTable is null)
        {
            return [];
        }

        return _queueTable.SelectedRows.Select(row => (int)row)
            .Where(row => row < _queueLines.Count)
            .Select(row => _queueLines[row].Item)
            .Where(item => _player.Playlist.IndexOf(item) >= 0)
            .ToList();
    }

    private void PlayQueueEntry(PlaylistItem? item)
    {
        if (item is null || _player.Playlist.IndexOf(item) is not (>= 0 and var at))
        {
            return;
        }

        if (_player.IsMissing(item))
        {
            Say("Missing file - choose Locate to point at its new location.");
            return;
        }

        _player.PlayAt(at);
        RefreshQueue();
    }

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

    /// <summary>
    /// CI-only AppKit smoke actions. These run the same menu items and buttons a user presses and
    /// capture the rendered pane without relying on screen-recording permissions.
    /// </summary>
    private bool QueueSmokeAction(string? action)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_QUEUE_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowQueue();
            return true;
        }

        if (action == "clear")
        {
            _player.Playlist.Clear();
            RefreshQueue();
            return true;
        }

        if (_queueRoot is null)
        {
            return false;
        }

        if (action == "minimum")
        {
            // The pane keeps its width; the smallest player window gives it the least height.
            _queueSmokeFrame ??= _window.Frame;
            var frame = _window.Frame;
            var size = _window.MinSize;
            _window.SetFrame(new CGRect(frame.X, frame.Y, size.Width, size.Height), true);
            _window.ContentView?.LayoutSubtreeIfNeeded();
            return true;
        }

        if (action == "verify-accessibility")
        {
            // The list's lines and their menu items must give disambiguated VoiceOver names,
            // not repeat one label for every line.
            RefreshQueue();
            var line = _queueLines.FirstOrDefault(candidate => !candidate.Current && !candidate.Missing);
            var next = line is null ? null : QueueSmokeMenuItem(line.Item, "Play next");
            var cell = line is null ? null : _queueTable?.GetView(0, _queueLines.ToList().IndexOf(line), true);
            return next is not null
                && next.ToolTip?.StartsWith("Play ", StringComparison.Ordinal) == true
                && next.ToolTip.EndsWith(" next", StringComparison.Ordinal)
                && cell?.AccessibilityLabel?.Contains(line!.Title, StringComparison.Ordinal) == true
                && _queueShuffle is not null && _queueRepeat is not null
                && ((NSView?)_queueTable)?.AccessibilityLabel == "Playlist, drag entries to reorder";
        }

        if (action == "verify-missing")
        {
            var missing = new PlaylistItem(Path.Combine(Path.GetTempPath(),
                "rexplayer-missing-" + Guid.NewGuid().ToString("N") + ".wav"))
            {
                Title = "Moved recording",
            };
            _player.Playlist.Add([missing]);
            _queuePage = _player.Playlist.IndexOf(missing) / QueuePageSize;
            RefreshQueue();
            RefreshQueueMoreMenu();
            var line = _queueLines.FirstOrDefault(candidate => ReferenceEquals(candidate.Item, missing));
            var play = QueueSmokeMenuItem(missing, "Play");
            var locate = QueueSmokeMenuItem(missing, "Locate…");
            var cell = line is null ? null : _queueTable?.GetView(0, _queueLines.ToList().IndexOf(line), true);
            var proved = line is { Missing: true }
                && play is { Enabled: false }
                && cell?.ToolTip?.StartsWith("Missing file: ", StringComparison.Ordinal) == true
                && locate is { Enabled: true }
                && _queueRelink is { Enabled: true }
                && _queueRelink.ToolTip == "Find every missing queue file in a folder";
            var index = _player.Playlist.IndexOf(missing);
            if (index >= 0) _player.Playlist.RemoveAt(index);
            RefreshQueue();
            return proved;
        }

        if (action == "verify-list-drop")
        {
            var before = _player.Playlist.Items.Count;
            DropIntoQueue(["queue-drop-1.wav", "queue-drop-2.wav"], before);
            var inserted = _player.Playlist.Items.Skip(before).Select(item => item.Location).ToArray();
            for (var index = _player.Playlist.Items.Count - 1; index >= before; index--)
            {
                _player.Playlist.RemoveAt(index);
            }
            RefreshQueue();
            return inserted.SequenceEqual(["queue-drop-1.wav", "queue-drop-2.wav"]);
        }

        if (action == "fill-long")
        {
            var smokeLocation = _player.Playlist.Items
                .FirstOrDefault(item => !_player.IsMissing(item))?.Location;
            if (smokeLocation is null)
            {
                return false;
            }

            _player.Playlist.Add(Enumerable.Range(1, 90).Select(index =>
                new PlaylistItem(smokeLocation)
                {
                    Title = $"Queue sample {index:000}",
                }));
            if (_player.Playlist.Current is null)
            {
                _player.Playlist.JumpTo(0);
            }

            RefreshQueue();
            return true;
        }

        if (action == "remove-last-page")
        {
            // Use the same Remove menu item as a real user, not Playlist.Clear(). The last page
            // has 16 entries after fill-long; removing them must return to page one rather
            // than strand an empty list.
            var pageBefore = _queuePage;
            if (pageBefore == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < QueuePageSize; attempt++)
            {
                if (_queuePage != pageBefore)
                {
                    break;
                }

                if (_queueLines.Count == 0 || QueueSmokeMenuItem(_queueLines[^1].Item, "Remove") is not { Enabled: true } remove)
                {
                    return false;
                }

                InvokeQueueMenuItem(remove);
                RefreshQueue();
            }

            return _queuePage == 0 && !_nextQueuePage!.Enabled;
        }

        if (action is "next-page" or "previous-page")
        {
            var button = action == "next-page" ? _nextQueuePage : _previousQueuePage;
            if (button is not { Enabled: true })
            {
                return false;
            }

            button.PerformClick(button);
            return true;
        }

        if (action is "shuffle" or "repeat" or "last-play-next")
        {
            RefreshQueueMoreMenu();
            var item = action switch
            {
                "shuffle" => _queueShuffle,
                "repeat" => _queueRepeat,
                _ => _queueLines.LastOrDefault(line => !line.Current && !line.Missing) is { } last
                    ? QueueSmokeMenuItem(last.Item, "Play next") : null,
            };
            if (item is not { Enabled: true })
            {
                return false;
            }

            InvokeQueueMenuItem(item);
            // Playlist.Changed schedules a refresh on the UI thread; update the result
            // synchronously so the automation reply reports the new mode / next item.
            RefreshQueue();
            return true;
        }

        if (action is "capture-empty" or "capture-current" or "capture-long"
            or "capture-page-2" or "capture-shuffled" or "capture-minimum"
            or "capture-shrunk")
        {
            var folder = Environment.GetEnvironmentVariable("REXPLAYER_MAC_QUEUE_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            ShowQueue();
            var view = _queueRoot;
            Directory.CreateDirectory(folder);
            view.LayoutSubtreeIfNeeded();
            File.WriteAllText(Path.Combine(folder, "layout.txt"), QueueSmokeLayoutReport());
            bool saved;
            using (var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds))
            {
                if (bitmap is null)
                {
                    return false;
                }

                view.CacheDisplay(view.Bounds, bitmap);
                using var data = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
                saved = data?.Save(NSUrl.FromFilename(Path.Combine(folder, action["capture-".Length..] + ".png")), true) == true;
            }

            if (action == "capture-minimum" && _queueSmokeFrame is { } restore)
            {
                _queueSmokeFrame = null;
                _window.SetFrame(restore, true);
            }

            return saved;
        }

        return false;
    }

    /// <summary>Frame measurements saved alongside CI screenshots for clipped layout diagnosis.</summary>
    private string QueueSmokeLayoutReport()
    {
        static string Frame(NSView? view)
        {
            if (view is null)
            {
                return "missing";
            }

            var frame = view.Frame;
            return $"x={frame.X:0.0} y={frame.Y:0.0} w={frame.Width:0.0} h={frame.Height:0.0}";
        }

        _queueRoot?.LayoutSubtreeIfNeeded();
        return "container " + Frame(_queueRoot) + Environment.NewLine
            + "stack " + Frame(_queueLayout) + Environment.NewLine
            + "header " + Frame(_queueHeader) + Environment.NewLine
            + "status " + Frame(_queueStatus) + Environment.NewLine
            + "hint " + Frame(_queueHint) + Environment.NewLine
            + "scroll " + Frame(_queueScroll) + Environment.NewLine
            + "document " + Frame(_queueScroll?.DocumentView) + Environment.NewLine
            + "pages " + Frame(_queuePages) + Environment.NewLine;
    }

    /// <summary>The item titled <paramref name="title"/> in the menu a right click on <paramref name="entry"/> opens.</summary>
    private NSMenuItem? QueueSmokeMenuItem(PlaylistItem entry, string title)
    {
        SelectQueueEntries([entry]);
        var menu = QueueEntryMenu([entry]);
        return menu.Items.FirstOrDefault(item => item.Title == title);
    }

    /// <summary>Runs a menu item as choosing it does.</summary>
    private static void InvokeQueueMenuItem(NSMenuItem item)
    {
        if (item.Menu is { } menu)
        {
            menu.PerformActionForItem(menu.IndexOf(item));
        }
    }

    private void CloseQueue()
    {
        _closingQueue = true;
        if (_queueTable is not null)
        {
            _queueTable.Source = null!;
        }

        _queueSource?.Dispose();
        _queueSource = null;
    }

    /// <summary>The list's lines, drags within it to reorder, and files dropped on it to insert.</summary>
    private sealed class QueueSource : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public QueueSource(MainWindow owner) => _owner = owner;

        public override nint GetRowCount(NSTableView tableView) => _owner._queueLines.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var cell = tableView.MakeView(QueueCellView.Reuse, this) as QueueCellView ?? new QueueCellView();
            cell.Show(_owner._queueLines[(int)row]);
            return cell;
        }

        public override NSTableRowView GetRowView(NSTableView tableView, nint row) => new QueueRowView();

        public override INSPasteboardWriting GetPasteboardWriterForRow(NSTableView tableView, nint row)
        {
            var item = new NSPasteboardItem();
            item.SetStringForType(row.ToString(System.Globalization.CultureInfo.InvariantCulture), QueueLineType);
            return item;
        }

        public override NSDragOperation ValidateDrop(NSTableView tableView, INSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
        {
            if (dropOperation == NSTableViewDropOperation.On)
            {
                tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
            }

            return ReferenceEquals(info.DraggingSource, tableView) ? NSDragOperation.Move : NSDragOperation.Copy;
        }

        public override bool AcceptDrop(NSTableView tableView, INSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
        {
            if (ReferenceEquals(info.DraggingSource, tableView))
            {
                var rows = (info.DraggingPasteboard.PasteboardItems ?? [])
                    .Select(item => item.GetStringForType(QueueLineType))
                    .Select(text => int.TryParse(text, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var index) ? index : -1)
                    .Where(index => index >= 0)
                    .ToList();
                _owner.DropQueueLines(rows, (int)row);
                return rows.Count > 0;
            }

            var paths = info.DraggingPasteboard
                .ReadObjectsForClasses([new ObjCRuntime.Class(typeof(NSUrl))], null)?
                .OfType<NSUrl>().Select(url => url.Path!).ToList();
            if (paths is not { Count: > 0 })
            {
                return false;
            }

            _owner.DropIntoQueue(paths, _owner._queueLinesStart + (int)row);
            return true;
        }
    }
}
