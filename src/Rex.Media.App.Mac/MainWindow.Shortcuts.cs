using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Native editor for the shared command bindings.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _shortcutsWindow;
    private NSPopUpButton? _shortcutCommand;
    private NSTextField? _shortcutChord;
    private NSTextField? _shortcutStatus;
    private NSPopUpButton? _shortcutWheel;
    private NSPopUpButton? _shortcutSidewaysWheel;
    private NSPopUpButton? _shortcutMiddle;
    private NSPopUpButton? _shortcutSideButtons;
    private NSPopUpButton? _shortcutCtrlWheel;
    private NSButton? _shortcutPanWhenZoomed;
    private readonly IReadOnlyList<Command> _shortcutCommands = CommandCatalog.All
        .OrderBy(command => command.Group, StringComparer.Ordinal)
        .ThenBy(command => command.Title, StringComparer.Ordinal).ToArray();

    private void ShowShortcutEditor()
    {
        if (_shortcutsWindow is null)
        {
            _shortcutsWindow = new NSWindow(new CGRect(0, 0, 650, 580),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Keyboard shortcuts - rexplayer",
                MinSize = new CGSize(540, 480),
            };
            _shortcutsWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Keyboard shortcuts");
            heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
            var explanation = NSTextField.CreateWrappingLabel(
                "Choose a command and enter a shortcut such as Cmd+Shift+P. " +
                "A duplicate binding is moved instead of activating two commands.");
            explanation.TextColor = NSColor.SecondaryLabel;
            _shortcutCommand = new NSPopUpButton(new CGRect(0, 0, 560, 28), false);
            foreach (var command in _shortcutCommands)
            {
                _shortcutCommand.AddItem(command.Group + " - " + command.Title);
            }

            _shortcutCommand.Activated += (_, _) => ShowSelectedShortcut();
            _shortcutChord = new NSTextField(new CGRect(0, 0, 300, 26))
            {
                PlaceholderString = "Cmd+Shift+P",
                ToolTip = "Enter Cmd, Option and Shift with a letter, number, function or named key",
            };
            _shortcutStatus = NSTextField.CreateWrappingLabel("");
            _shortcutStatus.TextColor = NSColor.SecondaryLabel;
            var apply = NSButton.CreateButton("Apply binding", ApplyShortcut);
            var clear = NSButton.CreateButton("Remove binding", () => ChangeShortcut(
                ShortcutEditing.Clear(_settings.Shortcuts, SelectedShortcutId()), "Binding removed."));
            var reset = NSButton.CreateButton("Restore default", () => ChangeShortcut(
                ShortcutEditing.Reset(_settings.Shortcuts, SelectedShortcutId()), "Default restored."));
            var import = NSButton.CreateButton("Import…", ImportShortcuts);
            var export = NSButton.CreateButton("Export…", ExportShortcuts);
            var actions = NSStackView.FromViews([apply, clear, reset]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;
            _shortcutWheel = EffectChoices(ShortcutEditing.WheelChoiceTitles(_settings));
            _shortcutSidewaysWheel = EffectChoices(ShortcutEditing.WheelChoiceTitles(_settings));
            _shortcutMiddle = EffectChoices(["Play / pause", "Fullscreen", "Mute", "Do nothing"]);
            _shortcutSideButtons = EffectChoices(ShortcutEditing.SideButtonChoiceTitles(_settings));
            _shortcutCtrlWheel = EffectChoices(["Zoom", "Subtitle size"]);
            _shortcutPanWhenZoomed = NSButton.CreateCheckbox(
                "Wheel pans instead of seeking when zoomed", ApplyMouseSettings);
            foreach (var menu in new[] { _shortcutWheel, _shortcutSidewaysWheel, _shortcutMiddle,
                _shortcutSideButtons, _shortcutCtrlWheel })
            {
                menu.Activated += (_, _) => ApplyMouseSettings();
            }

            var files = NSStackView.FromViews([import, export]);
            files.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            files.Spacing = 8;
            var stack = NSStackView.FromViews([
                heading, explanation, _shortcutCommand, _shortcutChord, actions, _shortcutStatus, files,
                EffectsHeading("Mouse & trackpad"),
                EffectChoiceRow("Vertical wheel", _shortcutWheel),
                EffectChoiceRow("Sideways wheel", _shortcutSidewaysWheel),
                EffectChoiceRow("Middle-click", _shortcutMiddle),
                EffectChoiceRow("Back / forward", _shortcutSideButtons),
                EffectChoiceRow("Cmd + wheel", _shortcutCtrlWheel),
                _shortcutPanWhenZoomed,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var container = new NSView(new CGRect(0, 0, 650, 580));
            container.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor).Active = true;
            explanation.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _shortcutCommand.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _shortcutChord.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _shortcutStatus.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _shortcutsWindow.ContentView = container;
            _shortcutsWindow.Center();
        }

        LoadMouseSettings();
        ShowSelectedShortcut();
        _shortcutsWindow.MakeKeyAndOrderFront(null);
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

    private string SelectedShortcutId()
    {
        var index = (int)(_shortcutCommand?.IndexOfSelectedItem ?? 0);
        return _shortcutCommands[Math.Clamp(index, 0, _shortcutCommands.Count - 1)].Id;
    }

    private void ShowSelectedShortcut()
    {
        if (_shortcutChord is null || _shortcutStatus is null)
        {
            return;
        }

        var id = SelectedShortcutId();
        _shortcutChord.StringValue = _keymap.Label(id, ShortcutStyle.Mac);
        _shortcutStatus.StringValue = ShortcutEditing.IsChanged(_settings.Shortcuts, id)
            ? "Custom binding; changes save immediately." : "Default binding; changes save immediately.";
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
        NSApplication.SharedApplication.MainMenu = BuildMenus();
        _pictureMenu = BuildPictureMenu();
        if (_window.ContentView is { } content)
        {
            content.Menu = _pictureMenu;
        }

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
            return _shortcutsWindow?.IsVisible == true && _shortcutCommand is not null;
        }

        if (_shortcutCommand is null || _shortcutChord is null)
        {
            return false;
        }

        var index = _shortcutCommands.ToList().FindIndex(command => command.Id == CommandCatalog.GoToTime);
        if (index < 0)
        {
            return false;
        }

        _shortcutCommand.SelectItem(index);
        ShowSelectedShortcut();
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

        return false;
    }

    private void DisposeShortcuts()
    {
        _shortcutsWindow?.Close();
        _shortcutsWindow?.Dispose();
        _shortcutsWindow = null;
    }
}
