namespace Rex.Media.App.Mac;

/// <summary>The native Up Next window, backed directly by the shared playlist and transport.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _queueWindow;
    private NSScrollView? _queueScroll;
    private NSTextField? _queueSummary;
    private bool _closingQueue;

    private void InitializeQueue() =>
        _player.Playlist.Changed += (_, _) => OnWindowThread(RefreshQueue);

    private void ShowQueue()
    {
        if (_queueWindow is null)
        {
            _queueWindow = new NSWindow(new CGRect(0, 0, 660, 560),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Up Next — rexplayer",
                MinSize = new CGSize(520, 400),
            };
            _queueWindow.ReleaseWhenClosed(false);
            var header = NSTextField.CreateLabel("Up Next");
            header.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateLabel("Play, remove, or move a track without leaving your media.");
            hint.TextColor = NSColor.SecondaryLabel;
            var shuffle = NSButton.CreateButton("Shuffle", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleShuffle));
            var repeat = NSButton.CreateButton("Repeat", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.CycleRepeat));
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
            var tools = NSStackView.FromViews([shuffle, repeat, open]);
            tools.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            tools.Spacing = 10;
            _queueSummary = NSTextField.CreateLabel("");
            _queueSummary.TextColor = NSColor.SecondaryLabel;
            _queueScroll = new NSScrollView(new CGRect(0, 0, 600, 380))
            {
                HasVerticalScroller = true,
            };
            _queueScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(300).Active = true;
            var stack = NSStackView.FromViews([header, hint, tools, _queueSummary, _queueScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 14;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
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
        var rows = new List<NSView>();
        if (queue.Count == 0)
        {
            var empty = NSTextField.CreateLabel("Your queue is empty. Open media or add files to get started.");
            empty.TextColor = NSColor.SecondaryLabel;
            rows.Add(empty);
        }

        for (var i = 0; i < queue.Count; i++)
        {
            var index = i;
            var item = queue[i];
            var playing = i == _player.Playlist.CurrentIndex;
            var play = NSButton.CreateButton((playing ? "▶ " : "") + item.Title, () =>
            {
                _player.PlayAt(index);
                RefreshQueue();
            });
            play.Alignment = NSTextAlignment.Left;
            play.ToolTip = item.Location;
            play.WidthAnchor.ConstraintGreaterThanOrEqualTo(300).Active = true;
            var up = NSButton.CreateButton("↑", () => _player.Playlist.Move(index, index - 1));
            var down = NSButton.CreateButton("↓", () => _player.Playlist.Move(index, index + 1));
            var remove = NSButton.CreateButton("Remove", () => _player.Playlist.RemoveAt(index));
            up.Enabled = index > 0;
            down.Enabled = index < queue.Count - 1;
            up.ToolTip = "Move earlier in the queue";
            down.ToolTip = "Move later in the queue";
            remove.ToolTip = "Remove from Up Next";
            var row = NSStackView.FromViews([play, up, down, remove]);
            row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            row.Spacing = 8;
            row.WidthAnchor.ConstraintGreaterThanOrEqualTo(550).Active = true;
            rows.Add(row);
        }

        var content = NSStackView.FromViews([.. rows]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 8;
        content.EdgeInsets = new NSEdgeInsets(12, 12, 14, 12);
        content.Frame = new CGRect(0, 0, 580, Math.Max(310, queue.Count * 42 + 35));
        _queueScroll.DocumentView = content;
        _queueSummary.StringValue = queue.Count == 1 ? "1 item" : $"{queue.Count} items"
            + (_player.Playlist.Shuffle ? " • Shuffle on" : "");
    }

    private void CloseQueue()
    {
        _closingQueue = true;
        _queueWindow?.Close();
        _queueWindow?.Dispose();
        _queueWindow = null;
    }
}
