namespace Rex.Media.App.Mac;

/// <summary>A retained, keyboard-accessible native list. AppKit asks only for visible rows.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The retained window owns its view tree.")]
internal sealed class MediaListWindow : IDisposable
{
    private readonly NSWindow _window;
    private readonly Rows _source = new();
    private readonly Cells _cells;
    private readonly NSTableView _table;
    private readonly NSTextField _status = NSTextField.CreateLabel("");

    public MediaListWindow(string title, NSView[] controls, Action play)
    {
        _window = new NSWindow(new CGRect(0, 0, 760, 520), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false) { Title = title, MinSize = new CGSize(640, 360) };
        _window.ReleaseWhenClosed(false);
        _cells = new Cells(_source);
        _table = new NSTableView { DataSource = _source, Delegate = _cells, RowHeight = 38, HeaderView = null };
        _table.AddColumn(new NSTableColumn("media") { Width = 710, MinWidth = 200 });
        _table.DoubleClick += (_, _) => play();
        var scroll = new NSScrollView { DocumentView = _table, HasVerticalScroller = true, AutohidesScrollers = true };
        scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(160).Active = true;
        _status.TextColor = NSColor.SecondaryLabel;
        _status.LineBreakMode = NSLineBreakMode.TruncatingTail;
        var stack = NSStackView.FromViews([.. controls, scroll, _status]);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 12;
        stack.EdgeInsets = new NSEdgeInsets(16, 16, 16, 16);
        foreach (var view in controls.Append(scroll).Append(_status))
        {
            view.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -32).Active = true;
        }
        _window.ContentView = stack;
        _window.Center();
    }

    public int Selected => (int)_table.SelectedRow;

    public void Show() => _window.MakeKeyAndOrderFront(null);

    public void Update(IReadOnlyList<string> rows, string status)
    {
        _status.StringValue = status;
        if (_source.Values.SequenceEqual(rows, StringComparer.Ordinal)) return;
        var selected = Selected;
        _source.Values = rows;
        _table.ReloadData();
        Select(selected);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _source.Values.Count) return;
        using var indexes = new NSIndexSet((nuint)index);
        _table.SelectRows(indexes, false);
        _table.ScrollRowToVisible(index);
    }

    public void Dispose()
    {
        _table.DataSource = null;
        _table.Delegate = null;
        _window.Close();
        _window.Dispose();
        _source.Dispose();
        _cells.Dispose();
    }

    private sealed class Rows : NSTableViewDataSource
    {
        public IReadOnlyList<string> Values { get; set; } = [];
        public override nint GetRowCount(NSTableView tableView) => Values.Count;
    }

    private sealed class Cells(Rows rows) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn? tableColumn, nint row)
        {
            var label = tableView.MakeView("media-label", this) as NSTextField ?? NSTextField.CreateLabel("");
            label.Identifier = "media-label";
            label.StringValue = rows.Values[(int)row];
            label.ToolTip = label.StringValue;
            label.LineBreakMode = NSLineBreakMode.TruncatingTail;
            return label;
        }
    }
}
