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
