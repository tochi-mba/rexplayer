namespace Rex.Media.App.Mac;

/// <summary>The native Up Next window, backed directly by the shared playlist and transport.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _queueWindow;
    private NSScrollView? _queueScroll;
    private NSTextField? _queueSummary;
    private bool _closingQueue;
    private int _queuePage;
    private NSButton? _queueShuffle;
    private NSButton? _queueRepeat;
    private NSButton? _previousQueuePage;
    private NSButton? _nextQueuePage;
    private const int QueuePageSize = 75;

    private void InitializeQueue() =>
        _player.Playlist.Changed += (_, _) => OnWindowThread(() =>
        {
            RefreshQueue();
            ShowState();
        });

    private void ShowQueue()
    {
        if (_queueWindow is null)
        {
            _queuePage = Math.Max(0, _player.Playlist.CurrentIndex) / QueuePageSize;
            _queueWindow = new NSWindow(new CGRect(0, 0, 660, 560),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Up Next - rexplayer",
                MinSize = new CGSize(520, 400),
            };
            _queueWindow.ReleaseWhenClosed(false);
            var header = NSTextField.CreateLabel("Up Next");
            header.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateLabel("Play, remove, or move a track without leaving your media.");
            hint.TextColor = NSColor.SecondaryLabel;
            _queueShuffle = NSButton.CreateButton("Shuffle: Off", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleShuffle));
            _queueRepeat = NSButton.CreateButton("Repeat: Off", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.CycleRepeat));
            var open = NSButton.CreateButton("Add files…", () =>
            {
                var panel = NSOpenPanel.OpenPanel;
                panel.AllowsMultipleSelection = true;
                panel.CanChooseFiles = true;
                panel.CanChooseDirectories = false;
                if (panel.RunModal() == 1)
                {
                    _player.Open(panel.Urls.Select(url => url.Path!).ToList(), enqueue: true);
                }
            });
            _previousQueuePage = NSButton.CreateButton("Previous page", () =>
            {
                _queuePage = Math.Max(0, _queuePage - 1);
                RefreshQueue();
            });
            _nextQueuePage = NSButton.CreateButton("Next page", () =>
            {
                _queuePage++;
                RefreshQueue();
            });
            var tools = NSStackView.FromViews([_queueShuffle, _queueRepeat, open]);
            tools.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            tools.Spacing = 8;
            var pages = NSStackView.FromViews([_previousQueuePage, _nextQueuePage]);
            pages.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            pages.Spacing = 8;
            _queueSummary = NSTextField.CreateLabel("");
            _queueSummary.TextColor = NSColor.SecondaryLabel;
            _queueSummary.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _queueScroll = new NSScrollView(new CGRect(0, 0, 600, 380))
            {
                HasVerticalScroller = true,
            };
            _queueScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(170).Active = true;
            var stack = NSStackView.FromViews([header, hint, tools, pages, _queueSummary, _queueScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 8;
            stack.EdgeInsets = new NSEdgeInsets(16, 24, 16, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            _queueWindow.ContentView = stack;
            _queueScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _queueWindow.Center();
        }

        RefreshQueue();
        _queueWindow.MakeKeyAndOrderFront(null);
    }

    private void RefreshQueue()
    {
        if (_closingQueue || _queueWindow is null || _queueScroll is null || _queueSummary is null)
        {
            return;
        }

        var queue = _player.Playlist.Items;
        _queuePage = Math.Clamp(_queuePage, 0, Math.Max(0, (queue.Count - 1) / QueuePageSize));
        var start = _queuePage * QueuePageSize;
        var end = Math.Min(queue.Count, start + QueuePageSize);
        if (_previousQueuePage is not null)
        {
            _previousQueuePage.Enabled = _queuePage > 0;
        }
        if (_nextQueuePage is not null)
        {
            _nextQueuePage.Enabled = end < queue.Count;
        }
        if (_queueShuffle is not null)
        {
            _queueShuffle.Title = _player.Playlist.Shuffle ? "Shuffle: On" : "Shuffle: Off";
        }
        if (_queueRepeat is not null)
        {
            _queueRepeat.Title = "Repeat: " + _player.Playlist.Repeat;
        }

        var rows = new List<NSView>();
        if (queue.Count == 0)
        {
            var empty = NSTextField.CreateLabel("Your queue is empty. Open media or add files to get started.");
            empty.TextColor = NSColor.SecondaryLabel;
            rows.Add(empty);
        }

        for (var i = start; i < end; i++)
        {
            var index = i;
            var item = queue[i];
            var playing = i == _player.Playlist.CurrentIndex;
            var play = NSButton.CreateButton((playing ? "Now playing: " : "Play: ") + item.Title, () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0)
                {
                    _player.PlayAt(at);
                    RefreshQueue();
                }
            });
            play.Alignment = NSTextAlignment.Left;
            play.ToolTip = item.Location;
            play.WidthAnchor.ConstraintGreaterThanOrEqualTo(300).Active = true;
            var source = NSTextField.CreateLabel(item.Artist ?? Rex.Media.AppCore.Player.PlaylistItem.SourceOf(item.Location) ?? "Local media");
            source.TextColor = NSColor.SecondaryLabel;
            source.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
            source.LineBreakMode = NSLineBreakMode.TruncatingTail;
            var media = NSStackView.FromViews([play, source]);
            media.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            media.Alignment = NSLayoutAttribute.Leading;
            media.Spacing = 1;
            var next = NSButton.CreateButton("Play next", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0 && _player.Playlist.Current is not null)
                {
                    _player.Playlist.QueueNext(at);
                }
            });
            next.Enabled = _player.Playlist.Current is not null && !playing;
            next.ToolTip = "Make this entry play next, including when shuffle is enabled";
            var up = NSButton.CreateButton("Earlier", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at > 0)
                {
                    _player.Playlist.Move(at, at - 1);
                }
            });
            var down = NSButton.CreateButton("Later", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0 && at < _player.Playlist.Items.Count - 1)
                {
                    _player.Playlist.Move(at, at + 1);
                }
            });
            var remove = NSButton.CreateButton("Remove", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0)
                {
                    _player.Playlist.RemoveAt(at);
                }
            });
            up.Enabled = index > 0;
            down.Enabled = index < queue.Count - 1;
            up.ToolTip = "Move earlier in the queue";
            down.ToolTip = "Move later in the queue";
            remove.ToolTip = "Remove from Up Next";
            var actions = NSStackView.FromViews([next, up, down, remove]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;
            var row = NSStackView.FromViews([media, actions]);
            row.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            row.Alignment = NSLayoutAttribute.Leading;
            row.Spacing = 5;
            rows.Add(row);
        }

        var content = NSStackView.FromViews([.. rows]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 8;
        content.EdgeInsets = new NSEdgeInsets(12, 12, 14, 12);
        content.Frame = new CGRect(0, 0, 580, Math.Max(200, (end - start) * 90 + 35));
        _queueScroll.DocumentView = content;
        _queueSummary.StringValue = (queue.Count == 1 ? "1 item" : $"{queue.Count} items")
            + (queue.Count > QueuePageSize ? $" • viewing {start + 1}–{end}" : "")
            + (_player.Playlist.PeekNext(automatic: false) is { } nextItem ? $" • Next: {nextItem.Title}" : "");
    }

    private void CloseQueue()
    {
        _closingQueue = true;
        _queueWindow?.Close();
        _queueWindow?.Dispose();
        _queueWindow = null;
    }
}
