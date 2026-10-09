using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
using Rex.Media.Interop.Windowing;
using Rex.Media.Settings;
using Windows.Storage.Pickers;
using Windows.System;

namespace Rex.Media.App;

/// <summary>
/// The keyboard and the mouse (UI-11): the editor where any command gets the shortcut the user
/// presses, with conflicts settled as they happen; shortcuts that work while another program is in
/// front; what the wheel and the mouse buttons do; and tooltips that name the shortcuts in use.
/// </summary>
public sealed partial class MainWindow
{
    private GlobalHotkeys? _globalHotkeys;
    private readonly Dictionary<int, string> _globalCommands = [];

    /// <summary>Each button that runs a command, and what its tooltip says before the shortcut.</summary>
    private (FrameworkElement Button, string Text, string Command)[] TooltipButtons =>
    [
        (PreviousButton, "Previous", CommandCatalog.Previous),
        (PlayPauseButton, "Play or pause", CommandCatalog.PlayPause),
        (StopButton, "Stop", CommandCatalog.Stop),
        (NextButton, "Next", CommandCatalog.Next),
        (ShuffleButton, "Shuffle", CommandCatalog.ToggleShuffle),
        (RepeatButton, "Repeat", CommandCatalog.CycleRepeat),
        (PlaylistButton, "Playlist", CommandCatalog.TogglePlaylist),
        (LibraryButton, "Library", CommandCatalog.ToggleLibrary),
        (FullScreenButton, "Full screen", CommandCatalog.ToggleFullScreen),
        (MuteButton, "Mute", CommandCatalog.Mute),
    ];

    /// <summary>Tooltips naming each button's shortcut as it now is, or none when it has none.</summary>
    private void ApplyTooltips()
    {
        foreach (var (button, text, command) in TooltipButtons)
        {
            var label = _keymap.Label(command);
            ToolTipService.SetToolTip(button, label.Length > 0 ? $"{text} ({label})" : text);
        }
    }

    /// <summary>Registers the shortcuts chosen to work everywhere; says which another program already has.</summary>
    private void ApplyGlobalShortcuts()
    {
        _globalHotkeys ??= new GlobalHotkeys(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _globalHotkeys.Clear();
        _globalHotkeys.Pressed -= OnGlobalShortcut;
        _globalHotkeys.Pressed += OnGlobalShortcut;
        _globalCommands.Clear();
        var taken = new List<string>();
        foreach (var command in _settings.GlobalShortcuts)
        {
            foreach (var chord in _keymap.ShortcutsFor(command))
            {
                var id = _globalCommands.Count + 1;
                if (VirtualKeys.Code(chord.Key) is { } code
                    && _globalHotkeys.Register(id, code, chord.Modifiers.HasFlag(KeyModifiers.Ctrl), chord.Modifiers.HasFlag(KeyModifiers.Alt), chord.Modifiers.HasFlag(KeyModifiers.Shift)))
                {
                    _globalCommands[id] = command;
                }
                else
                {
                    taken.Add(chord.ToString());
                }
            }
        }

        App.Log.Info(LogSource, $"{_globalCommands.Count} shortcut(s) work everywhere.");
        if (taken.Count > 0)
        {
            Say($"Another program already has {string.Join(", ", taken)}, so {(taken.Count == 1 ? "it works" : "they work")} only while rexplayer is in front.");
        }
    }

    private void OnGlobalShortcut(object? sender, int id)
    {
        if (_globalCommands.TryGetValue(id, out var command))
        {
            App.Log.Debug(LogSource, "Global shortcut " + command);
            Run(command);
        }
    }

    /// <summary>Makes the keymap, menus, tooltips and global shortcuts agree with the settings.</summary>
    private void ApplyInputSettings()
    {
        _keymap = new Keymap(_settings.Shortcuts);
        BuildMenus();
        ApplyTooltips();
        ApplyGlobalShortcuts();
    }

    /// <summary>The wheel over the picture, as the settings say; true when it did something.</summary>
    private bool RunWheel(PointerPoint point)
    {
        var delta = point.Properties.MouseWheelDelta;
        var command = point.Properties.IsHorizontalMouseWheel
            ? ShortcutEditing.WheelCommand(_settings.SidewaysWheel, forward: delta > 0)
            : ShortcutEditing.WheelCommand(_settings.Wheel, forward: delta > 0);
        if (delta == 0 || command is null)
        {
            return false;
        }

        Run(command);
        return true;
    }

    /// <summary>The middle, back and forward buttons, as the settings say; null for any other button.</summary>
    private string? ButtonCommand(PointerPoint point) =>
        point.Properties.IsMiddleButtonPressed ? ShortcutEditing.MiddleButtonCommand(_settings.MiddleButton)
        : point.Properties.IsXButton1Pressed ? ShortcutEditing.SideButtonCommand(_settings.SideButtons, forward: false)
        : point.Properties.IsXButton2Pressed ? ShortcutEditing.SideButtonCommand(_settings.SideButtons, forward: true)
        : null;

    /// <summary>The keyboard and mouse editor: nothing applies until Save.</summary>
    private async Task ShowKeyboardAndMouseAsync()
    {
        var changes = new Dictionary<string, string>(_settings.Shortcuts, StringComparer.Ordinal);
        var global = new HashSet<string>(_settings.GlobalShortcuts, StringComparer.Ordinal);
        string? editing = null;

        var search = new TextBox { PlaceholderText = "Find a command or a shortcut", MinWidth = 420 };
        AutomationProperties.SetAutomationId(search, "ShortcutSearch");
        AutomationProperties.SetName(search, "Find a command or a shortcut");
        var note = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Informational };
        AutomationProperties.SetAutomationId(note, "ShortcutNote");
        var capture = new TextBox { Header = "Press the new shortcut, or type it (such as Ctrl+M), then Enter", MinWidth = 260, IsSpellCheckEnabled = false };
        AutomationProperties.SetAutomationId(capture, "ShortcutKeys");
        AutomationProperties.SetName(capture, "New shortcut");
        var apply = new Button { Content = "Use it" };
        AutomationProperties.SetAutomationId(apply, "ShortcutApply");
        var cancel = new Button { Content = "Cancel" };
        var capturePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
        capturePanel.Children.Add(capture);
        capturePanel.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } });
        var rows = new StackPanel { Spacing = 2 };

        var wheel = Choice("Turning the wheel over the picture", ["Changes the volume", "Jumps a little", "Does nothing"], (int)_settings.Wheel);
        var sideways = Choice("Tilting the wheel", ["Changes the volume", "Jumps a little", "Does nothing"], (int)_settings.SidewaysWheel);
        var middle = Choice("The middle button", ["Plays or pauses", "Goes full screen", "Mutes", "Does nothing"], (int)_settings.MiddleButton);
        var side = Choice("The back and forward buttons", ["Go to the previous and next item", "Jump back and forward", "Do nothing"], (int)_settings.SideButtons);

        void Say(string text)
        {
            note.Message = text;
            note.IsOpen = true;
        }

        void Show()
        {
            var keymap = new Keymap(changes);
            var filter = search.Text.Trim();
            rows.Children.Clear();
            foreach (var group in CommandCatalog.All.GroupBy(command => command.Group))
            {
                var shown = group.Where(command => filter.Length == 0
                    || command.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                    || keymap.ShortcutsFor(command.Id).Any(chord => chord.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
                if (shown.Count == 0)
                {
                    continue;
                }

                rows.Children.Add(Heading(group.Key));
                foreach (var command in shown)
                {
                    rows.Children.Add(Row(command, keymap));
                }
            }

            if (keymap.Conflicts is [var first, ..])
            {
                Say($"{first.Chord} is both {CommandCatalog.Find(first.First)!.Title} and {CommandCatalog.Find(first.Second)!.Title}; it does the first. Give one of them another shortcut.");
            }
        }

        UIElement Row(Command command, Keymap keymap)
        {
            var chords = keymap.ShortcutsFor(command.Id);
            var row = new Grid { ColumnSpacing = 8 };
            foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            }

            var title = new TextBlock { Text = command.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var change = new Button { Content = chords.Count > 0 ? string.Join(", ", chords) : "None", MinWidth = 120 };
            AutomationProperties.SetAutomationId(change, "Shortcut-" + command.Id);
            AutomationProperties.SetName(change, $"{command.Title}: {(chords.Count > 0 ? string.Join(", ", chords) : "no shortcut")}. Change");
            change.Click += (_, _) =>
            {
                editing = command.Id;
                capture.Header = $"Press the new shortcut for {command.Title}, or type it (such as Ctrl+M), then Enter";
                capture.Text = "";
                capturePanel.Visibility = Visibility.Visible;
                capture.Focus(FocusState.Programmatic);
            };
            var clear = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, IsEnabled = chords.Count > 0 };
            AutomationProperties.SetAutomationId(clear, "ShortcutClear-" + command.Id);
            AutomationProperties.SetName(clear, "Remove the shortcut of " + command.Title);
            ToolTipService.SetToolTip(clear, "No shortcut");
            clear.Click += (_, _) =>
            {
                changes = new Dictionary<string, string>(ShortcutEditing.Clear(changes, command.Id), StringComparer.Ordinal);
                Show();
            };
            var reset = new Button { Content = new FontIcon { Glyph = "\uE777", FontSize = 12 }, IsEnabled = ShortcutEditing.IsChanged(changes, command.Id) };
            AutomationProperties.SetAutomationId(reset, "ShortcutReset-" + command.Id);
            AutomationProperties.SetName(reset, "Give " + command.Title + " its own shortcut back");
            ToolTipService.SetToolTip(reset, "Its own shortcut");
            reset.Click += (_, _) =>
            {
                changes = new Dictionary<string, string>(ShortcutEditing.Reset(changes, command.Id), StringComparer.Ordinal);
                Show();
            };
            var everywhere = new CheckBox { Content = "Everywhere", IsChecked = global.Contains(command.Id), MinWidth = 0 };
            AutomationProperties.SetAutomationId(everywhere, "ShortcutGlobal-" + command.Id);
            AutomationProperties.SetName(everywhere, command.Title + " works while another program is in front");
            ToolTipService.SetToolTip(everywhere, "Works while another program is in front");
            everywhere.Checked += (_, _) => global.Add(command.Id);
            everywhere.Unchecked += (_, _) => global.Remove(command.Id);

            row.Children.Add(title);
            Grid.SetColumn(change, 1);
            Grid.SetColumn(clear, 2);
            Grid.SetColumn(reset, 3);
            Grid.SetColumn(everywhere, 4);
            row.Children.Add(change);
            row.Children.Add(clear);
            row.Children.Add(reset);
            row.Children.Add(everywhere);
            return row;
        }

        void Apply()
        {
            if (editing is null)
            {
                return;
            }

            if (KeyChord.TryParse(capture.Text.Trim()) is not { } chord)
            {
                Say($"\"{capture.Text.Trim()}\" is not a shortcut. Press the keys, or type them such as Ctrl+Shift+M.");
                return;
            }

            var made = ShortcutEditing.Bind(changes, editing, chord);
            changes = new Dictionary<string, string>(made.Changes, StringComparer.Ordinal);
            if (made.TakenFrom is { } from)
            {
                var left = new Keymap(changes).ShortcutsFor(from);
                Say($"{chord} was the shortcut for {CommandCatalog.Find(from)!.Title}; {(left.Count > 0 ? "it keeps " + string.Join(", ", left) : "it has none now")}.");
            }
            else
            {
                note.IsOpen = false;
            }

            editing = null;
            capturePanel.Visibility = Visibility.Collapsed;
            Show();
        }

        // The keys pressed in the box become the shortcut; Enter uses it, Escape gives up.
        capture.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Apply();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                editing = null;
                capturePanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows or VirtualKey.Tab)
            {
                return;
            }

            if (VirtualKeys.Chord((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift)) is { } pressed)
            {
                capture.Text = pressed.ToString();
                capture.SelectionStart = capture.Text.Length;
                e.Handled = true;
            }
        };
        apply.Click += (_, _) => Apply();
        cancel.Click += (_, _) =>
        {
            editing = null;
            capturePanel.Visibility = Visibility.Collapsed;
        };
        search.TextChanged += (_, _) => Show();

        var resetAll = new Button { Content = "Everything as it came" };
        AutomationProperties.SetAutomationId(resetAll, "ShortcutResetAll");
        resetAll.Click += (_, _) =>
        {
            changes.Clear();
            global.Clear();
            (wheel.SelectedIndex, sideways.SelectedIndex, middle.SelectedIndex, side.SelectedIndex) = (0, 1, 0, 0);
            Show();
        };
        var export = new Button { Content = "Save to a file..." };
        AutomationProperties.SetAutomationId(export, "ShortcutExport");
        export.Click += async (_, _) =>
        {
            var picker = Prepared(new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "rexplayer keyboard and mouse" });
            picker.FileTypeChoices.Add("Keyboard and mouse", [".json"]);
            if (await picker.PickSaveFileAsync() is { } file)
            {
                await File.WriteAllTextAsync(file.Path, ShortcutEditing.Export(Edited()));
                Say("Saved to " + file.Path);
            }
        };
        var import = new Button { Content = "Load from a file..." };
        AutomationProperties.SetAutomationId(import, "ShortcutImport");
        import.Click += async (_, _) =>
        {
            var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
            picker.FileTypeFilter.Add(".json");
            if (await picker.PickSingleFileAsync() is not { } file)
            {
                return;
            }

            try
            {
                var loaded = ShortcutEditing.Import(Edited(), await File.ReadAllTextAsync(file.Path));
                changes = new Dictionary<string, string>(loaded.Shortcuts, StringComparer.Ordinal);
                global.Clear();
                global.UnionWith(loaded.GlobalShortcuts);
                (wheel.SelectedIndex, sideways.SelectedIndex, middle.SelectedIndex, side.SelectedIndex) = ((int)loaded.Wheel, (int)loaded.SidewaysWheel, (int)loaded.MiddleButton, (int)loaded.SideButtons);
                Show();
                Say("Loaded " + file.Name + ". Save to keep it.");
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                Say(ex.Message);
            }
        };

        PlayerSettings Edited() => _settings with
        {
            Shortcuts = changes,
            GlobalShortcuts = [.. CommandCatalog.All.Select(command => command.Id).Where(global.Contains)],
            Wheel = (WheelChoice)wheel.SelectedIndex,
            SidewaysWheel = (WheelChoice)sideways.SelectedIndex,
            MiddleButton = (MiddleButtonChoice)middle.SelectedIndex,
            SideButtons = (SideButtonChoice)side.SelectedIndex,
        };

        Show();
        var content = new StackPanel { Spacing = 8, MinWidth = 560 };
        content.Children.Add(note);
        content.Children.Add(search);
        content.Children.Add(capturePanel);
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 360, Padding = new Thickness(0, 0, 16, 0) });
        content.Children.Add(Heading("Mouse"));
        foreach (var control in new UIElement[] { wheel, sideways, middle, side })
        {
            content.Children.Add(control);
        }

        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { resetAll, export, import } });
        if (!await Ask("Keyboard and mouse", content, "Save"))
        {
            return;
        }

        _settings = Edited().Normalize();
        SaveSettings();
        ApplyInputSettings();
        App.Log.Info(LogSource, $"Keyboard and mouse saved: {changes.Count} shortcut change(s), {_settings.GlobalShortcuts.Count} everywhere.");
    }
}
