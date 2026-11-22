using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>Native access to playback history and the shared remembered quick slots.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _historyWindow;
    private NSPopUpButton? _recentPicker;
    private RexLabel? _recentSummary;
    private readonly List<RexButton> _quickSlotOpenButtons = [];
    private readonly List<RexButton> _quickSlotSaveButtons = [];

    /// <summary>The history window's scrolled page, laid out from the top down as a page reads.</summary>
    private sealed class HistoryDocumentView : NSView
    {
        public HistoryDocumentView()
        {
            TranslatesAutoresizingMaskIntoConstraints = false;
        }

        public override bool IsFlipped => true;
    }

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
            MacTheme.Style(_historyWindow);
            var heading = MacTheme.Label("Recent media & quick slots", 4, semibold: true);
            var explanation = MacTheme.WrappingLabel(
                "Open a recently played file, or save this moment to a numbered shortcut " +
                "you can return to later.", 2, DesignRole.TextSecondary);
            _recentPicker = new NSPopUpButton(new CGRect(0, 0, 500, 28), false)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            ((NSView)_recentPicker).AccessibilityLabel = "Recently played file";
            _recentSummary = MacTheme.Label("", 1, DesignRole.TextSecondary);
            var open = MacTheme.TextButton("Open selected", () =>
            {
                var index = (int)_recentPicker.IndexOfSelectedItem;
                if (index >= 0 && index < _player.Recent.Count)
                {
                    _player.Open([_player.Recent[index]]);
                }
            }, RexButtonLook.Accent);
            var clear = MacTheme.TextButton("Clear history…", () =>
            {
                ConfirmInWindow("Clear recent media and saved positions? Your files will remain untouched.", () =>
                {
                    _player.ClearHistory();
                    RefreshHistory();
                });
            });
            var controls = MacTheme.Stack(false, 1, open, clear);
            var recent = MacTheme.Stack(true, 1, _recentPicker, _recentSummary, controls);

            // The nine slots sit on one card, a line each: the slot to open, then saving to it.
            var slotRows = new List<NSView>();
            var keyViews = new List<NSView> { _recentPicker, open, clear };
            for (var number = 1; number <= 9; number++)
            {
                var slot = number;
                var openSlot = MacTheme.TextButton(slot + ": Empty", () => _player.PlaySlot(slot), label: "Open quick slot " + slot);
                openSlot.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
                _quickSlotOpenButtons.Add(openSlot);
                var saveSlot = MacTheme.TextButton("Save this moment", () =>
                {
                    _player.SetSlot(slot);
                    RefreshHistory();
                }, label: "Save this moment to quick slot " + slot);
                _quickSlotSaveButtons.Add(saveSlot);
                keyViews.Add(openSlot);
                keyViews.Add(saveSlot);
                var row = MacTheme.Stack(false, 1, openSlot, saveSlot);
                slotRows.Add(row);
            }

            var slots = MacTheme.Stack(true, 1, [.. slotRows]);
            slots.EdgeInsets = new NSEdgeInsets(MacTheme.Space(2), MacTheme.Space(2), MacTheme.Space(2), MacTheme.Space(2));
            var slotCard = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(slotCard, slots);
            foreach (var row in slotRows)
            {
                row.WidthAnchor.ConstraintEqualTo(slots.WidthAnchor, 1, -MacTheme.Space(2) * 2).Active = true;
            }

            var slotSection = MacTheme.Stack(true, 1, MacTheme.Label("Saved quick slots", 3, semibold: true), slotCard);
            var words = MacTheme.Stack(true, 1, heading, explanation);
            var stack = MacTheme.Stack(true, 3, words, recent, slotSection);
            stack.EdgeInsets = new NSEdgeInsets(MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4));
            var document = new HistoryDocumentView();
            MacTheme.Fill(document, stack);
            var wide = MacTheme.Space(4) * 2;
            foreach (var group in new NSView[] { words, recent, slotSection })
            {
                group.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -wide).Active = true;
            }

            explanation.WidthAnchor.ConstraintEqualTo(words.WidthAnchor).Active = true;
            _recentPicker.WidthAnchor.ConstraintEqualTo(recent.WidthAnchor).Active = true;
            slotCard.WidthAnchor.ConstraintEqualTo(slotSection.WidthAnchor).Active = true;
            var scroll = new NSScrollView(new CGRect(0, 0, 610, 550))
            {
                HasVerticalScroller = true,
                DrawsBackground = false,
                BorderType = NSBorderType.NoBorder,
            };
            scroll.DocumentView = document;
            document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
            document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
            document.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor).Active = true;
            _historyWindow.ContentView = scroll;
            for (var i = 0; i < keyViews.Count; i++)
            {
                keyViews[i].NextKeyView = keyViews[(i + 1) % keyViews.Count];
            }

            _historyWindow.InitialFirstResponder = _recentPicker;
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
            button.Text = slot is null
                ? (i + 1) + ": Empty"
                : (i + 1) + ": " + Path.GetFileName(slot.Location);
            button.Enabled = slot is not null;
            button.Label = slot is null
                ? "Quick slot " + (i + 1) + " is empty"
                : "Open quick slot " + (i + 1) + ": " + Path.GetFileName(slot.Location);
            if (slot is not null)
            {
                button.ToolTip = slot.Location;
            }
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
