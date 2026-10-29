namespace Rex.Media.App.Mac;

/// <summary>Native access to playback history and the shared remembered quick slots.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _historyWindow;
    private NSPopUpButton? _recentPicker;
    private NSTextField? _recentSummary;
    private readonly List<NSButton> _quickSlotOpenButtons = [];
    private readonly List<NSButton> _quickSlotSaveButtons = [];

    private void ShowHistory()
    {
        if (_historyWindow is null)
        {
            _historyWindow = new NSWindow(new CGRect(0, 0, 610, 550),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Recent media & quick slots - rexplayer",
                MinSize = new CGSize(520, 420),
            };
            _historyWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Recent media & quick slots");
            heading.Font = NSFont.SystemFontOfSize(23, NSFontWeight.Semibold)!;
            var explanation = NSTextField.CreateWrappingLabel(
                "Open a recently played file, or save this moment to a numbered shortcut " +
                "you can return to later.");
            explanation.TextColor = NSColor.SecondaryLabel;
            _recentPicker = new NSPopUpButton(new CGRect(0, 0, 500, 28), false);
            ((NSView)_recentPicker).AccessibilityLabel = "Recently played file";
            _recentSummary = NSTextField.CreateLabel("");
            _recentSummary.TextColor = NSColor.SecondaryLabel;
            var open = NSButton.CreateButton("Open selected", () =>
            {
                var index = (int)_recentPicker.IndexOfSelectedItem;
                if (index >= 0 && index < _player.Recent.Count)
                {
                    _player.Open([_player.Recent[index]]);
                }
            });
            var clear = NSButton.CreateButton("Clear history…", () =>
            {
                ConfirmInWindow("Clear recent media and saved positions? Your files will remain untouched.", () =>
                {
                    _player.ClearHistory();
                    RefreshHistory();
                });
            });
            var controls = NSStackView.FromViews([open, clear]);
            controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            controls.Spacing = 8;

            var items = new List<NSView>
            {
                heading, explanation, _recentPicker, _recentSummary, controls,
                EffectsHeading("Saved quick slots"),
            };
            for (var number = 1; number <= 9; number++)
            {
                var slot = number;
                var openSlot = NSButton.CreateButton(slot + ": Empty", () => _player.PlaySlot(slot));
                openSlot.Alignment = NSTextAlignment.Left;
                openSlot.WidthAnchor.ConstraintGreaterThanOrEqualTo(330).Active = true;
                _quickSlotOpenButtons.Add(openSlot);
                var saveSlot = NSButton.CreateButton("Save this moment", () =>
                {
                    _player.SetSlot(slot);
                    RefreshHistory();
                });
                _quickSlotSaveButtons.Add(saveSlot);
                var row = NSStackView.FromViews([openSlot, saveSlot]);
                row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
                row.Spacing = 8;
                items.Add(row);
            }

            var stack = NSStackView.FromViews([.. items]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 11;
            stack.EdgeInsets = new NSEdgeInsets(16, 24, 16, 24);
            stack.Frame = new CGRect(0, 0, 580, 820);
            var scroll = new NSScrollView(new CGRect(0, 0, 610, 550))
            {
                HasVerticalScroller = true,
            };
            scroll.DocumentView = stack;
            _historyWindow.ContentView = scroll;
            _historyWindow.Center();
        }

        RefreshHistory();
        _historyWindow.MakeKeyAndOrderFront(null);
    }

    private void RefreshHistory()
    {
        if (_historyWindow is null || _recentPicker is null || _recentSummary is null)
        {
            return;
        }

        var previous = _recentPicker.TitleOfSelectedItem;
        _recentPicker.RemoveAllItems();
        foreach (var path in _player.Recent)
        {
            _recentPicker.AddItem(Path.GetFileName(path));
        }

        if (_player.Recent.Count > 0)
        {
            var index = _player.Recent.ToList().FindIndex(path => Path.GetFileName(path) == previous);
            _recentPicker.SelectItem(Math.Max(0, index));
        }

        _recentPicker.Enabled = _player.Recent.Count > 0;
        _recentSummary.StringValue = _player.Recent.Count == 0
            ? "No recent files." : _player.Recent.Count + " recently played files";
        for (var i = 0; i < _quickSlotOpenButtons.Count; i++)
        {
            var button = _quickSlotOpenButtons[i];
            var slot = _player.Memory.Slot(i + 1);
            button.Title = slot is null
                ? (i + 1) + ": Empty"
                : (i + 1) + ": " + Path.GetFileName(slot.Location);
            button.Enabled = slot is not null;
            button.ToolTip = slot?.Location;
        }
    }

    private void DisposeHistory()
    {
        _historyWindow?.Close();
        _historyWindow?.Dispose();
        _historyWindow = null;
    }

    private Rex.Media.AppCore.Automation.AutomationReply QuickSlotSmokeAction(string? action)
    {
        ShowHistory();
        switch (action)
        {
            case "save-one":
                _quickSlotSaveButtons[0].PerformClick(_quickSlotSaveButtons[0]);
                break;
            case "open-one":
                _quickSlotOpenButtons[0].PerformClick(_quickSlotOpenButtons[0]);
                break;
            case "status":
                break;
            default:
                return Rex.Media.AppCore.Automation.AutomationReply.Failed("Unknown quick-slot action: " + action);
        }

        RefreshHistory();
        var slot = _player.Memory.Slot(1);
        return new Rex.Media.AppCore.Automation.AutomationReply
        {
            Ok = true,
            Text = slot is null ? "empty" : Path.GetFileName(slot.Location),
            Position = _player.Position.TotalSeconds,
        };
    }
}
