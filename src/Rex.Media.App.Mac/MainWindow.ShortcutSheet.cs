using Rex.Media.AppCore.Commands;

namespace Rex.Media.App.Mac;

/// <summary>
/// The read-only shortcut sheet (A11Y-02): every command with a shortcut, a line each under its
/// group, read from the same shared <see cref="Keymap"/> the menus and the editor use. It only
/// shows; changing a binding stays the shortcut editor's job.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _shortcutSheetWindow;
    private NSTableView? _shortcutSheetTable;
    private ShortcutSheetSource? _shortcutSheetSource;
    private IReadOnlyList<ShortcutSheetRow> _shortcutSheetRows = [];

    /// <summary>A heading for a group, or a command with its keys and how a screen reader says them.</summary>
    private readonly record struct ShortcutSheetRow(string Name, string Keys, string Spoken, bool Heading);

    private void ShowShortcutSheet()
    {
        _shortcutSheetRows = ShortcutSheetRows();
        if (_shortcutSheetWindow is null)
        {
            _shortcutSheetWindow = new NSWindow(new CGRect(0, 0, 560, 620),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Keyboard shortcut reference - rexplayer",
                MinSize = new CGSize(440, 360),
            };
            _shortcutSheetWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Keyboard shortcuts");
            heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateWrappingLabel(
                "Every command with a shortcut, under its group. " +
                "Change a binding in Keyboard shortcuts; Esc closes this sheet.");
            hint.TextColor = NSColor.SecondaryLabel;
            var nameColumn = new NSTableColumn("command") { Title = "Command", Width = 320 };
            var keysColumn = new NSTableColumn("shortcut") { Title = "Shortcut", Width = 170 };
            _shortcutSheetTable = new NSTableView(new CGRect(0, 0, 512, 460))
            {
                AllowsColumnReordering = false,
                AllowsEmptySelection = true,
                AllowsMultipleSelection = false,
                UsesAlternatingRowBackgroundColors = true,
            };
            _shortcutSheetTable.AddColumn(nameColumn);
            _shortcutSheetTable.AddColumn(keysColumn);
            _shortcutSheetSource = new ShortcutSheetSource(this);
            _shortcutSheetTable.Source = _shortcutSheetSource;
            ((NSView)_shortcutSheetTable).AccessibilityLabel = "Keyboard shortcut list";
            var scroll = new NSScrollView
            {
                HasVerticalScroller = true,
                DocumentView = _shortcutSheetTable,
            };
            scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(260).Active = true;
            var close = NSButton.CreateButton("Close", () => _shortcutSheetWindow?.Close());
            close.ToolTip = "Close the shortcut sheet (Esc)";
            var stack = NSStackView.FromViews([heading, hint, scroll, close]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var container = new ShortcutSheetView(new CGRect(0, 0, 560, 620));
            container.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor).Active = true;
            hint.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            scroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _shortcutSheetWindow.ContentView = container;
            _shortcutSheetWindow.Center();
        }

        _shortcutSheetTable?.ReloadData();
        _shortcutSheetWindow.MakeKeyAndOrderFront(null);
    }

    /// <summary>The sheet's lines from the current keymap: commands with no shortcut are left to the menus.</summary>
    private List<ShortcutSheetRow> ShortcutSheetRows()
    {
        var rows = new List<ShortcutSheetRow>();
        foreach (var group in CommandCatalog.All.GroupBy(command => command.Group))
        {
            var lines = group
                .Select(command => (Command: command, Keys: _keymap.ShortcutsFor(command.Id)))
                .Where(line => line.Keys.Count > 0).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            rows.Add(new ShortcutSheetRow(group.Key, "", group.Key + " shortcuts", Heading: true));
            foreach (var (command, keys) in lines)
            {
                var title = CommandCatalog.TitleOf(command, _settings);
                rows.Add(new ShortcutSheetRow(title,
                    string.Join(" or ", keys.Select(chord => chord.Display(ShortcutStyle.Mac))),
                    title + ": " + KeyNames.Spoken(keys, ShortcutStyle.Mac), Heading: false));
            }
        }

        return rows;
    }

    private void DisposeShortcutSheet()
    {
        _shortcutSheetWindow?.Close();
        _shortcutSheetWindow?.Dispose();
        _shortcutSheetWindow = null;
    }

    /// <summary>The sheet's content; Esc closes the window, as a read-only reference should.</summary>
    private sealed class ShortcutSheetView : NSView
    {
        public ShortcutSheetView(CGRect frame)
            : base(frame)
        {
        }

        /// <summary>The responder message Esc sends; answering it here closes the window.</summary>
        [Export("cancelOperation:")]
        public void CloseOnCancel(NSObject? sender) => Window?.Close();
    }

    /// <summary>Rows for the sheet's table: full-width group headings, then command and keys.</summary>
    private sealed class ShortcutSheetSource : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public ShortcutSheetSource(MainWindow owner) => _owner = owner;

        public override nint GetRowCount(NSTableView tableView) => _owner._shortcutSheetRows.Count;

        public override bool IsGroupRow(NSTableView tableView, nint row) => _owner._shortcutSheetRows[(int)row].Heading;

        public override bool ShouldSelectRow(NSTableView tableView, nint row) => false;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var line = _owner._shortcutSheetRows[(int)row];
            if (line.Heading)
            {
                var headingLabel = NSTextField.CreateLabel(line.Name);
                headingLabel.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold)!;
                ((NSView)headingLabel).AccessibilityLabel = line.Spoken;
                return headingLabel;
            }

            var shortcut = tableColumn?.Identifier == "shortcut";
            var label = NSTextField.CreateLabel(shortcut ? line.Keys : line.Name);
            label.LineBreakMode = NSLineBreakMode.TruncatingTail;
            if (!shortcut)
            {
                // A screen reader hears the line as one sentence, as on Windows (A11Y-02).
                ((NSView)label).AccessibilityLabel = line.Spoken;
            }

            return label;
        }
    }
}
