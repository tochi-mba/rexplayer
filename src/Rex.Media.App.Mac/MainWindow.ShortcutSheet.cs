using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// The read-only shortcut sheet (A11Y-02): every command with a shortcut, a line each under its
/// group, read from the same shared <see cref="Keymap"/> the menus and the editor use. As on
/// Windows, each group's name heads its lines and each line gives what the command does and its
/// keys as key caps. It only shows; changing a binding stays the shortcut editor's job.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _shortcutSheetWindow;
    private NSTableView? _shortcutSheetTable;
    private ShortcutSheetSource? _shortcutSheetSource;
    private IReadOnlyList<ShortcutSheetRow> _shortcutSheetRows = [];

    /// <summary>A heading for a group, or a command with its keys and how a screen reader says them.</summary>
    private readonly record struct ShortcutSheetRow(string Name, IReadOnlyList<KeyChord> Keys, string Spoken, bool Heading);

    private void ShowShortcutSheet()
    {
        _shortcutSheetRows = ShortcutSheetRows();
        if (_shortcutSheetWindow is null)
        {
            _shortcutSheetWindow = new NSWindow(new CGRect(0, 0, 620, 660),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Keyboard shortcut reference - rexplayer",
                MinSize = new CGSize(460, 380),
                AutorecalculatesKeyViewLoop = true,
            };
            _shortcutSheetWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_shortcutSheetWindow);
            var heading = MacTheme.Label("Keyboard shortcuts", 5, semibold: true);
            var hint = MacTheme.WrappingLabel(
                "Every command with a shortcut, under its group. " +
                "Change a binding in Keyboard shortcuts; Esc closes this sheet.", 2, DesignRole.TextSecondary);
            var nameColumn = new NSTableColumn("command") { Title = "Command", Width = 300 };
            var keysColumn = new NSTableColumn("shortcut") { Title = "Shortcut", Width = 220 };
            _shortcutSheetTable = new NSTableView
            {
                AllowsColumnReordering = false,
                AllowsEmptySelection = true,
                AllowsMultipleSelection = false,
                HeaderView = null,
                RowHeight = 32,
                ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
            };
            _shortcutSheetTable.AddColumn(nameColumn);
            _shortcutSheetTable.AddColumn(keysColumn);
            _shortcutSheetSource = new ShortcutSheetSource(this);
            _shortcutSheetTable.Source = _shortcutSheetSource;
            ((NSView)_shortcutSheetTable).AccessibilityLabel = "Keyboard shortcut list";
            var scroll = new NSScrollView
            {
                HasVerticalScroller = true,
                AutohidesScrollers = true,
                DocumentView = _shortcutSheetTable,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            MacTheme.Style(_shortcutSheetTable, scroll);
            var card = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(card, scroll, (double)MacTheme.Space(1));
            var close = MacTheme.TextButton("Close", () => _shortcutSheetWindow?.Close(), label: "Close the shortcut sheet (Esc)");
            var container = new ShortcutSheetView(new CGRect(0, 0, 620, 660));
            foreach (var view in new NSView[] { heading, hint, card, close })
            {
                container.AddSubview(view);
            }

            var pad = MacTheme.Space(4);
            heading.TopAnchor.ConstraintEqualTo(container.TopAnchor, pad + 12).Active = true;
            heading.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor, pad).Active = true;
            hint.TopAnchor.ConstraintEqualTo(heading.BottomAnchor, MacTheme.Space(1)).Active = true;
            hint.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor, pad).Active = true;
            hint.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor, -pad).Active = true;
            card.TopAnchor.ConstraintEqualTo(hint.BottomAnchor, MacTheme.Space(3)).Active = true;
            card.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor, pad).Active = true;
            card.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor, -pad).Active = true;
            card.BottomAnchor.ConstraintEqualTo(close.TopAnchor, -MacTheme.Space(3)).Active = true;
            close.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor, -pad).Active = true;
            close.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -pad).Active = true;
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

            rows.Add(new ShortcutSheetRow(group.Key, [], group.Key + " shortcuts", Heading: true));
            foreach (var (command, keys) in lines)
            {
                var title = CommandCatalog.TitleOf(command, _settings);
                rows.Add(new ShortcutSheetRow(title, keys,
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
        public void CloseOnCancel(NSObject? _) => Window?.Close();
    }

    /// <summary>Rows for the sheet's table: group names as kickers across the row, then command and key caps.</summary>
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
            NSView content;
            if (line.Heading)
            {
                var kicker = MacTheme.Kicker(line.Name);
                ((NSView)kicker).AccessibilityLabel = line.Spoken;
                content = kicker;
            }
            else if (tableColumn?.Identifier == "shortcut")
            {
                var caps = MacTheme.Stack(false, 1);
                for (var i = 0; i < line.Keys.Count; i++)
                {
                    if (i > 0)
                    {
                        var either = MacTheme.Label("or", 1, DesignRole.TextSecondary);
                        either.AccessibilityElement = false;
                        caps.AddArrangedSubview(either);
                    }

                    caps.AddArrangedSubview(MacSettings.KeyCaps(line.Keys[i]));
                }

                content = caps;
            }
            else
            {
                var name = MacTheme.Label(line.Name, 2);
                // A screen reader hears the line as one sentence, as on Windows (A11Y-02).
                ((NSView)name).AccessibilityLabel = line.Spoken;
                content = name;
            }

            var cell = new NSView();
            content.TranslatesAutoresizingMaskIntoConstraints = false;
            cell.AddSubview(content);
            content.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, MacTheme.Space(0)).Active = true;
            content.TrailingAnchor.ConstraintLessThanOrEqualTo(cell.TrailingAnchor).Active = true;
            content.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor).Active = true;
            return cell;
        }
    }
}
