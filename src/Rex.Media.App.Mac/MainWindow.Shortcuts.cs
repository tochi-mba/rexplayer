using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Native editor for the shared command bindings, laid out as the Windows keyboard and mouse
/// editor is: a search over a table of every command with its shortcut and whether it works
/// everywhere, the chosen command's shortcut below it, then what the mouse does. Changes save as
/// they are made.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _shortcutsWindow;
    private NSTableView? _shortcutTable;
    private ShortcutEditorSource? _shortcutSource;
    private NSSearchField? _shortcutSearch;
    private string _shortcutSelectedId = CommandCatalog.All[0].Id;
    private bool _shortcutTableUpdating;
    private RexLabel? _shortcutCaptureTitle;
    private NSStackView? _shortcutCurrentKeys;
    private RexLabel? _shortcutConflict;
    private NSTextField? _shortcutChord;
    private RexLabel? _shortcutStatus;
    private NSButton? _shortcutGlobal;
    private NSObject? _globalShortcutMonitor;
    private NSPopUpButton? _shortcutWheel;
    private NSPopUpButton? _shortcutSidewaysWheel;
    private NSPopUpButton? _shortcutMiddle;
    private NSPopUpButton? _shortcutSideButtons;
    private NSPopUpButton? _shortcutCtrlWheel;
    private NSButton? _shortcutPanWhenZoomed;
    private IReadOnlyList<ShortcutEditorLine> _shortcutLines = [];

    /// <summary>A line of the editor's table: a group's heading, or a command under it.</summary>
    private readonly record struct ShortcutEditorLine(string Group, Command? Command);

    private void ShowShortcutEditor()
    {
        if (_shortcutsWindow is null)
        {
            _shortcutsWindow = new NSWindow(new CGRect(0, 0, 720, 720),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Keyboard shortcuts - rexplayer",
                MinSize = new CGSize(600, 520),
                AutorecalculatesKeyViewLoop = true,
            };
            _shortcutsWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_shortcutsWindow);
            var page = new SettingsPage("Keyboard and mouse",
                "Choose a command and enter a shortcut such as Cmd+Shift+P. " +
                "A duplicate binding is moved instead of activating two commands. Changes save immediately.");

            _shortcutConflict = MacTheme.WrappingLabel("", 2, MacTheme.Attention);
            _shortcutConflict.Hidden = true;
            _shortcutSearch = new NSSearchField
            {
                PlaceholderString = "Find a command or a shortcut",
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            _shortcutSearch.AccessibilityLabel = "Find a command or a shortcut";
            _shortcutSearch.Changed += (_, _) => ShowShortcutLines();

            _shortcutTable = new NSTableView
            {
                AllowsColumnReordering = false,
                AllowsEmptySelection = true,
                AllowsMultipleSelection = false,
                RowHeight = 32,
                ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
            };
            _shortcutTable.AddColumn(new NSTableColumn("command") { Title = "Command", Width = 300 });
            _shortcutTable.AddColumn(new NSTableColumn("shortcut") { Title = "Shortcut", Width = 190 });
            _shortcutTable.AddColumn(new NSTableColumn("global") { Title = "Everywhere", Width = 90 });
            _shortcutSource = new ShortcutEditorSource(this);
            _shortcutTable.Source = _shortcutSource;
            ((NSView)_shortcutTable).AccessibilityLabel = "Commands and their shortcuts";
            var tableScroll = new NSScrollView
            {
                HasVerticalScroller = true,
                AutohidesScrollers = true,
                DocumentView = _shortcutTable,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            MacTheme.Style(_shortcutTable, tableScroll);
            tableScroll.HeightAnchor.ConstraintEqualTo(300).Active = true;
            var tableCard = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(tableCard, tableScroll, (double)MacTheme.Space(1));

            // The chosen command's shortcut: what it is now, the box to type a new one, and the way back.
            _shortcutCurrentKeys = MacTheme.Stack(false, 1);
            var clear = MacTheme.TextButton("No shortcut", () => ChangeShortcut(
                ShortcutEditing.Clear(_settings.Shortcuts, SelectedShortcutId()), "Binding removed."), label: "Remove the shortcut");
            var reset = MacTheme.TextButton("Its own shortcut", () => ChangeShortcut(
                ShortcutEditing.Reset(_settings.Shortcuts, SelectedShortcutId()), "Default restored."), label: "Restore the default shortcut");
            _shortcutChord = new NSTextField
            {
                PlaceholderString = "Cmd+Shift+P",
                ToolTip = "Enter Cmd, Option and Shift with a letter, number, function or named key",
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            _shortcutChord.WidthAnchor.ConstraintEqualTo(180).Active = true;
            _shortcutChord.Activated += (_, _) => ApplyShortcut();
            var apply = MacTheme.TextButton("Use it", ApplyShortcut, RexButtonLook.Accent, label: "Use the new shortcut");
            _shortcutGlobal = MacSettings.Check(false);
            _shortcutGlobal.Activated += (_, _) => ToggleSelectedGlobalShortcut();
            _shortcutStatus = MacTheme.WrappingLabel("", 1, DesignRole.TextSecondary);
            _shortcutCaptureTitle = MacTheme.Label("", 3, semibold: true);
            var capture = MacSettings.Card(null,
                _shortcutCaptureTitle,
                MacSettings.Row("Now", null, MacSettings.Buttons(_shortcutCurrentKeys, clear, reset)),
                MacSettings.Row("New shortcut", "Type it, such as Cmd+Shift+P, then press Return.", MacSettings.Buttons(_shortcutChord, apply)),
                MacSettings.Row("Everywhere", "Works while another app is in front.", _shortcutGlobal),
                _shortcutStatus);
            ((NSView)_shortcutChord).AccessibilityLabel = "New shortcut";

            var named = _settings;
            _shortcutWheel = MacSettings.Choices(ShortcutEditing.WheelChoiceTitles(named));
            _shortcutSidewaysWheel = MacSettings.Choices(ShortcutEditing.WheelChoiceTitles(named));
            _shortcutMiddle = MacSettings.Choices(["Plays or pauses", "Goes full screen", "Mutes", "Does nothing"]);
            _shortcutSideButtons = MacSettings.Choices(ShortcutEditing.SideButtonChoiceTitles(named));
            _shortcutCtrlWheel = MacSettings.Choices(["Zooms the picture", "Sizes the subtitles"]);
            _shortcutPanWhenZoomed = MacSettings.Check(false);
            _shortcutPanWhenZoomed.Activated += (_, _) => ApplyMouseSettings();
            foreach (var menu in new[] { _shortcutWheel, _shortcutSidewaysWheel, _shortcutMiddle,
                _shortcutSideButtons, _shortcutCtrlWheel })
            {
                menu.Activated += (_, _) => ApplyMouseSettings();
            }

            var mouse = MacSettings.Card("Mouse",
                MacSettings.Row("Turning the wheel over the picture", null, _shortcutWheel),
                MacSettings.Row("Tilting the wheel", null, _shortcutSidewaysWheel),
                MacSettings.Row("Cmd with the wheel, and pinching a trackpad", null, _shortcutCtrlWheel),
                MacSettings.Row("Zoomed in, the wheel and two-finger scrolling move about the picture", null, _shortcutPanWhenZoomed),
                MacSettings.Row("The middle button", null, _shortcutMiddle),
                MacSettings.Row("The back and forward buttons", null, _shortcutSideButtons));
            var import = MacTheme.TextButton("Load from a file…", ImportShortcuts, label: "Import keyboard and mouse settings");
            var export = MacTheme.TextButton("Save to a file…", ExportShortcuts, label: "Export keyboard and mouse settings");
            page.Add(_shortcutConflict, _shortcutSearch, tableCard, capture, mouse);
            page.AddFitted(MacSettings.Buttons(import, export));
            var scroll = MacSettings.Scroller();
            _shortcutsWindow.ContentView = scroll;
            MacSettings.Show(scroll, page);
            _shortcutsWindow.Center();
        }

        LoadMouseSettings();
        ShowShortcutLines();
        ShowSelectedShortcut();
        _shortcutsWindow.MakeKeyAndOrderFront(null);
    }

    /// <summary>
    /// Fills the table with the commands the search leaves, under their groups, as Windows lists
    /// them: a command matches by its name or by one of its shortcuts. The chosen command stays chosen.
    /// </summary>
    private void ShowShortcutLines()
    {
        if (_shortcutTable is null)
        {
            return;
        }

        var filter = _shortcutSearch?.StringValue.Trim() ?? "";
        var lines = new List<ShortcutEditorLine>();
        foreach (var group in CommandCatalog.All.GroupBy(command => command.Group))
        {
            var shown = group.Where(command => filter.Length == 0
                || CommandCatalog.TitleOf(command, _settings).Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || _keymap.ShortcutsFor(command.Id).Any(chord =>
                    chord.Display(ShortcutStyle.Mac).Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || chord.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            lines.Add(new ShortcutEditorLine(group.Key, null));
            lines.AddRange(shown.Select(command => new ShortcutEditorLine(group.Key, command)));
        }

        _shortcutLines = lines;
        _shortcutTableUpdating = true;
        _shortcutTable.ReloadData();
        var row = lines.FindIndex(line => line.Command?.Id == _shortcutSelectedId);
        if (row >= 0)
        {
            _shortcutTable.SelectRow(row, byExtendingSelection: false);
        }

        _shortcutTableUpdating = false;
        if (_shortcutConflict is not null)
        {
            _shortcutConflict.StringValue = _keymap.Conflicts is [var first, ..]
                ? $"{first.Chord.Display(ShortcutStyle.Mac)} is both {CommandCatalog.TitleOf(CommandCatalog.Find(first.First)!, _settings)} and " +
                    $"{CommandCatalog.TitleOf(CommandCatalog.Find(first.Second)!, _settings)}; it does the first. Give one of them another shortcut."
                : "";
            _shortcutConflict.Hidden = _keymap.Conflicts.Count == 0;
        }
    }

    /// <summary>The table's answer to a click or an arrow key: the command of the row chosen becomes the one edited.</summary>
    private void ShortcutLineSelected()
    {
        if (_shortcutTableUpdating || _shortcutTable is null)
        {
            return;
        }

        var row = (int)_shortcutTable.SelectedRow;
        if (row >= 0 && row < _shortcutLines.Count && _shortcutLines[row].Command is { } command)
        {
            _shortcutSelectedId = command.Id;
            ShowSelectedShortcut();
        }
    }

    /// <summary>Makes <paramref name="id"/> the command edited, choosing its row when the search shows it.</summary>
    private void SelectShortcutCommand(string id)
    {
        _shortcutSelectedId = id;
        ShowShortcutLines();
        ShowSelectedShortcut();
    }

    private void LoadMouseSettings()
    {
        if (_shortcutWheel is null) return;
        _shortcutWheel.SelectItem((int)_settings.Wheel);
        _shortcutSidewaysWheel!.SelectItem((int)_settings.SidewaysWheel);
        _shortcutMiddle!.SelectItem((int)_settings.MiddleButton);
        _shortcutSideButtons!.SelectItem((int)_settings.SideButtons);
        _shortcutCtrlWheel!.SelectItem((int)_settings.CtrlWheel);
        _shortcutPanWhenZoomed!.State = State(_settings.WheelPansWhenZoomed);
    }

    private void ApplyMouseSettings()
    {
        if (_shortcutWheel is null || _shortcutSidewaysWheel is null ||
            _shortcutMiddle is null || _shortcutSideButtons is null ||
            _shortcutCtrlWheel is null || _shortcutPanWhenZoomed is null) return;
        var settings = (_settings with
        {
            Wheel = (WheelChoice)(int)_shortcutWheel.IndexOfSelectedItem,
            SidewaysWheel = (WheelChoice)(int)_shortcutSidewaysWheel.IndexOfSelectedItem,
            MiddleButton = (MiddleButtonChoice)(int)_shortcutMiddle.IndexOfSelectedItem,
            SideButtons = (SideButtonChoice)(int)_shortcutSideButtons.IndexOfSelectedItem,
            CtrlWheel = (CtrlWheelChoice)(int)_shortcutCtrlWheel.IndexOfSelectedItem,
            WheelPansWhenZoomed = _shortcutPanWhenZoomed.State == NSCellStateValue.On,
        }).Normalize();
        SaveShortcutSettings(settings, "Mouse preferences saved.");
    }

    private string SelectedShortcutId() => _shortcutSelectedId;

    /// <summary>The chosen command's name, its keys as key caps, and whether it is custom.</summary>
    private void ShowSelectedShortcut()
    {
        if (_shortcutChord is null || _shortcutStatus is null)
        {
            return;
        }

        var id = SelectedShortcutId();
        var keys = _keymap.ShortcutsFor(id);
        if (_shortcutCaptureTitle is not null && CommandCatalog.Find(id) is { } command)
        {
            _shortcutCaptureTitle.StringValue = CommandCatalog.TitleOf(command, _settings);
        }

        if (_shortcutCurrentKeys is not null)
        {
            foreach (var old in _shortcutCurrentKeys.ArrangedSubviews)
            {
                _shortcutCurrentKeys.RemoveArrangedSubview(old);
                old.RemoveFromSuperview();
            }

            if (keys.Count == 0)
            {
                _shortcutCurrentKeys.AddArrangedSubview(MacTheme.Label("None", 2, DesignRole.TextSecondary));
            }

            foreach (var chord in keys)
            {
                _shortcutCurrentKeys.AddArrangedSubview(MacSettings.KeyCaps(chord));
            }

            _shortcutCurrentKeys.AccessibilityLabel = keys.Count == 0 ? "No shortcut" : KeyNames.Spoken(keys, ShortcutStyle.Mac);
        }

        _shortcutChord.StringValue = _keymap.Label(id, ShortcutStyle.Mac);
        if (_shortcutGlobal is not null)
        {
            _shortcutGlobal.State = State(_settings.GlobalShortcuts.Contains(id, StringComparer.Ordinal));
            _shortcutGlobal.Enabled = keys.Count > 0;
        }
        _shortcutStatus.StringValue = ShortcutEditing.IsChanged(_settings.Shortcuts, id)
            ? "Custom binding; changes save immediately." : "Default binding; changes save immediately.";
    }

    private void ToggleSelectedGlobalShortcut()
    {
        if (_shortcutGlobal is null) return;
        var id = SelectedShortcutId();
        var globals = _settings.GlobalShortcuts.Where(command => command != id).ToList();
        if (_shortcutGlobal.State == NSCellStateValue.On)
        {
            globals.Add(id);
        }

        SaveShortcutSettings((_settings with { GlobalShortcuts = globals }).Normalize(),
            _shortcutGlobal.State == NSCellStateValue.On
                ? "Global shortcut enabled. macOS may ask to allow keyboard monitoring."
                : "Global shortcut disabled.");
    }

    private void ApplyShortcut()
    {
        if (KeyChord.TryParse(_shortcutChord?.StringValue) is not { } chord ||
            MacKeyEquivalents.For(chord) is null)
        {
            _shortcutStatus!.StringValue = "Use a supported key, for example Cmd+Shift+P.";
            return;
        }

        var edited = ShortcutEditing.Bind(_settings.Shortcuts, SelectedShortcutId(), chord);
        var message = edited.TakenFrom is { } previous
            ? "Moved from " + CommandCatalog.Find(previous)!.Title + "."
            : "Shortcut saved.";
        ChangeShortcut(edited.Changes, message);
    }

    private void ChangeShortcut(IReadOnlyDictionary<string, string> changes, string message) =>
        SaveShortcutSettings((_settings with { Shortcuts = changes }).Normalize(), message);

    private void SaveShortcutSettings(PlayerSettings candidate, string message)
    {
        try
        {
            SettingsStore.Save(_roots.SettingsPath, candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Keyboard settings could not be saved: " + ex.Message);
            _shortcutStatus!.StringValue = "Could not save: " + ex.Message;
            return;
        }

        _settings = candidate;
        _player.Settings = candidate;
        _keymap = new Keymap(candidate.Shortcuts);
        RestartGlobalShortcutMonitor();
        NSApplication.SharedApplication.MainMenu = BuildMenus();
        _pictureMenu = BuildPictureMenu();
        if (_window.ContentView is { } content)
        {
            content.Menu = _pictureMenu;
        }

        ShowShortcutLines();
        ShowSelectedShortcut();
        _shortcutStatus!.StringValue = message;
    }

    private void ExportShortcuts()
    {
        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = "rexplayer-shortcuts.json";
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } path) return;
            try
            {
                File.WriteAllText(path, ShortcutEditing.Export(_settings));
                _shortcutStatus!.StringValue = "Keyboard and mouse settings exported.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _shortcutStatus!.StringValue = "Could not export: " + ex.Message;
            }
        });
    }

    private void ImportShortcuts()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = false;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.Title = "Import keyboard and mouse settings";
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } path) return;
            try
            {
                SaveShortcutSettings(ShortcutEditing.Import(_settings, File.ReadAllText(path)),
                    "Keyboard and mouse settings imported.");
                LoadMouseSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                _shortcutStatus!.StringValue = "Could not import: " + ex.Message;
            }
        });
    }

    private bool ShortcutSmokeAction(string? action)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_SHORTCUT_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowShortcutEditor();
            return _shortcutsWindow?.IsVisible == true && _shortcutTable is not null;
        }

        // The read-only sheet: proved open with grouped lines, then closed the way Esc closes it.
        if (action == "sheet")
        {
            return _shortcutSheetWindow?.IsVisible == true && _shortcutSheetTable is not null
                && _shortcutSheetRows.Any(row => row.Heading) && _shortcutSheetRows.Any(row => !row.Heading);
        }

        if (action == "sheet-escape")
        {
            (_shortcutSheetWindow?.ContentView as ShortcutSheetView)?.CloseOnCancel(_shortcutSheetWindow);
            return _shortcutSheetWindow?.IsVisible != true;
        }

        if (_shortcutTable is null || _shortcutChord is null
            || CommandCatalog.Find(CommandCatalog.GoToTime) is null)
        {
            return false;
        }

        SelectShortcutCommand(CommandCatalog.GoToTime);
        if (action == "bind")
        {
            _shortcutChord.StringValue = "Cmd+Shift+P";
            ApplyShortcut();
            return _keymap.Label(CommandCatalog.GoToTime, ShortcutStyle.Mac) == "Shift+Cmd+P";
        }

        if (action == "reset")
        {
            ChangeShortcut(ShortcutEditing.Reset(_settings.Shortcuts, CommandCatalog.GoToTime), "Restored.");
            return !ShortcutEditing.IsChanged(_settings.Shortcuts, CommandCatalog.GoToTime);
        }

        if (action == "global")
        {
            _shortcutGlobal!.State = NSCellStateValue.On;
            ToggleSelectedGlobalShortcut();
            return _settings.GlobalShortcuts.Contains(CommandCatalog.GoToTime, StringComparer.Ordinal)
                && _globalShortcutMonitor is not null;
        }

        return false;
    }

    private void DisposeShortcuts()
    {
        StopGlobalShortcutMonitor();
        DisposeShortcutSheet();
        _shortcutsWindow?.Close();
        _shortcutsWindow?.Dispose();
        _shortcutsWindow = null;
    }

    private void RestartGlobalShortcutMonitor()
    {
        StopGlobalShortcutMonitor();
        if (_settings.GlobalShortcuts.Count == 0) return;
        _globalShortcutMonitor = NSEvent.AddGlobalMonitorForEventsMatchingMask(NSEventMask.KeyDown,
            e => OnWindowThread(() => RunGlobalShortcut(e)));
    }

    private void RunGlobalShortcut(NSEvent keyEvent)
    {
        var key = keyEvent.CharactersIgnoringModifiers;
        if (string.IsNullOrEmpty(key)) return;
        const NSEventModifierMask relevant = NSEventModifierMask.CommandKeyMask
            | NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask;
        var actual = keyEvent.ModifierFlags & relevant;
        foreach (var command in _settings.GlobalShortcuts)
        {
            foreach (var chord in _keymap.ShortcutsFor(command))
            {
                if (MacKeyEquivalents.For(chord) is not { } expected) continue;
                var modifiers = (expected.Command ? NSEventModifierMask.CommandKeyMask : 0)
                    | (expected.Option ? NSEventModifierMask.AlternateKeyMask : 0)
                    | (expected.Shift ? NSEventModifierMask.ShiftKeyMask : 0);
                if (actual == modifiers && string.Equals(key, expected.Key, StringComparison.OrdinalIgnoreCase))
                {
                    Run(command);
                    return;
                }
            }
        }
    }

    private void StopGlobalShortcutMonitor()
    {
        if (_globalShortcutMonitor is null) return;
        NSEvent.RemoveMonitor(_globalShortcutMonitor);
        _globalShortcutMonitor.Dispose();
        _globalShortcutMonitor = null;
    }

    /// <summary>
    /// The editor's table: each group's name as a kicker across the row, then a command a line with
    /// its keys as key caps and, for those that work while another app is in front, "Everywhere".
    /// </summary>
    private sealed class ShortcutEditorSource : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public ShortcutEditorSource(MainWindow owner) => _owner = owner;

        public override nint GetRowCount(NSTableView tableView) => _owner._shortcutLines.Count;

        public override bool IsGroupRow(NSTableView tableView, nint row) => _owner._shortcutLines[(int)row].Command is null;

        public override bool ShouldSelectRow(NSTableView tableView, nint row) => _owner._shortcutLines[(int)row].Command is not null;

        public override void SelectionDidChange(NSNotification notification) => _owner.ShortcutLineSelected();

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var line = _owner._shortcutLines[(int)row];
            if (line.Command is not { } command)
            {
                var kicker = MacTheme.Kicker(line.Group);
                ((NSView)kicker).AccessibilityLabel = line.Group + " shortcuts";
                return Cell(kicker);
            }

            var keys = _owner._keymap.ShortcutsFor(command.Id);
            var title = CommandCatalog.TitleOf(command, _owner._settings);
            switch (tableColumn?.Identifier)
            {
                case "shortcut":
                    var caps = MacTheme.Stack(false, 1);
                    foreach (var chord in keys)
                    {
                        caps.AddArrangedSubview(MacSettings.KeyCaps(chord));
                    }

                    if (keys.Count == 0)
                    {
                        caps.AddArrangedSubview(MacTheme.Label("None", 1, DesignRole.TextTertiary));
                    }

                    return Cell(caps);
                case "global":
                    var everywhere = _owner._settings.GlobalShortcuts.Contains(command.Id, StringComparer.Ordinal);
                    return Cell(MacTheme.Label(everywhere ? "Everywhere" : "", 1, DesignRole.TextSecondary));
                default:
                    var name = MacTheme.Label(title, 2);
                    // A screen reader hears the line as one sentence: the command, then its keys.
                    ((NSView)name).AccessibilityLabel = title + ": " +
                        (keys.Count == 0 ? "no shortcut" : KeyNames.Spoken(keys, ShortcutStyle.Mac));
                    return Cell(name);
            }
        }

        /// <summary>A table cell holding <paramref name="content"/> at its leading edge, centred down.</summary>
        private static NSView Cell(NSView content)
        {
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
