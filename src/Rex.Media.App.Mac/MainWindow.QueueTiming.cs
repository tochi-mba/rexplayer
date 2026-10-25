using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Per-queue-entry start, end and run-time editing without blocking playback.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _queueTimingWindow;
    private NSTextField? _queueTimingStart;
    private NSTextField? _queueTimingStop;
    private NSTextField? _queueTimingRun;
    private NSTextField? _queueTimingNotice;
    private PlaylistItem? _queueTimingItem;

    private static NSStackView TimingRow(string title, NSTextField field)
    {
        var label = NSTextField.CreateLabel(title);
        label.WidthAnchor.ConstraintEqualTo(130).Active = true;
        var row = NSStackView.FromViews([label, field]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 8;
        return row;
    }

    private void EditQueueTiming(PlaylistItem item)
    {
        if (_player.Playlist.IndexOf(item) < 0) return;
        if (_queueTimingWindow is null)
        {
            _queueTimingWindow = new NSWindow(new CGRect(0, 0, 470, 330),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Queue timing - rexplayer",
            };
            _queueTimingWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Set the in and out points");
            heading.Font = NSFont.SystemFontOfSize(21, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateWrappingLabel(
                "Only this queue entry changes; the media file stays untouched. " +
                "Enter an end or a run time, not both.");
            hint.TextColor = NSColor.SecondaryLabel;
            _queueTimingStart = new NSTextField(new CGRect(0, 0, 240, 24))
            {
                PlaceholderString = "0:00",
            };
            _queueTimingStop = new NSTextField(new CGRect(0, 0, 240, 24))
            {
                PlaceholderString = "End of file",
            };
            _queueTimingRun = new NSTextField(new CGRect(0, 0, 240, 24))
            {
                PlaceholderString = "Optional run time",
            };
            _queueTimingStart.AccessibilityLabel = "Queue item start time";
            _queueTimingStop.AccessibilityLabel = "Queue item stop time";
            _queueTimingRun.AccessibilityLabel = "Queue item run time";
            _queueTimingNotice = NSTextField.CreateWrappingLabel("");
            _queueTimingNotice.TextColor = NSColor.SecondaryLabel;
            var apply = NSButton.CreateButton("Apply", ApplyQueueTiming);
            apply.KeyEquivalent = "\r";
            var cancel = NSButton.CreateButton("Cancel", () => _queueTimingWindow.OrderOut(null));
            var actions = NSStackView.FromViews([apply, cancel]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 10;
            var content = NSStackView.FromViews([
                heading, hint,
                TimingRow("Start", _queueTimingStart),
                TimingRow("Stop", _queueTimingStop),
                TimingRow("Run time", _queueTimingRun),
                _queueTimingNotice, actions,
            ]);
            content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            content.Alignment = NSLayoutAttribute.Leading;
            content.Spacing = 13;
            content.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            _queueTimingWindow.ContentView = content;
            _queueTimingWindow.Center();
        }

        _queueTimingItem = item;
        _queueTimingStart!.StringValue = TimeText.Format(item.Start);
        _queueTimingStop!.StringValue = item.End is { } end ? TimeText.Format(end) : "";
        _queueTimingRun!.StringValue = "";
        _queueTimingNotice!.StringValue = "";
        _queueTimingWindow.MakeKeyAndOrderFront(null);
        _queueTimingWindow.MakeFirstResponder(_queueTimingStart);
    }

    private void ApplyQueueTiming()
    {
        var item = _queueTimingItem;
        var index = item is null ? -1 : _player.Playlist.IndexOf(item);
        if (index < 0)
        {
            _queueTimingNotice!.StringValue = "That queue entry is no longer available.";
            return;
        }

        var begin = TimeText.TryParse(_queueTimingStart!.StringValue);
        var stopText = _queueTimingStop!.StringValue;
        var runText = _queueTimingRun!.StringValue;
        var endTime = string.IsNullOrWhiteSpace(stopText) ? null : TimeText.TryParse(stopText);
        var length = string.IsNullOrWhiteSpace(runText) ? null : TimeText.TryParse(runText);
        if (begin is null || begin < TimeSpan.Zero ||
            (!string.IsNullOrWhiteSpace(stopText) && endTime is null) ||
            (!string.IsNullOrWhiteSpace(runText) && length is null) ||
            endTime is not null && length is not null ||
            length is { } duration && duration <= TimeSpan.Zero)
        {
            _queueTimingNotice!.StringValue = "Enter valid times, and use either Stop or Run time.";
            return;
        }

        var finish = length is { } span ? begin.Value + span : endTime;
        if (finish is { } endAt && endAt <= begin)
        {
            _queueTimingNotice!.StringValue = "The end must be later than the start.";
            return;
        }

        var edited = new PlaylistItem(item!.Location)
        {
            Title = item.Title,
            Artist = item.Artist,
            Start = begin.Value,
            End = finish,
        };
        _player.Playlist.Replace(index, edited);
        _queueTimingWindow!.OrderOut(null);
        RefreshQueue();
        Say("Set the entry to start at " + TimeText.Format(begin.Value));
    }

    private void DisposeQueueTiming()
    {
        _queueTimingWindow?.Close();
        _queueTimingWindow?.Dispose();
        _queueTimingWindow = null;
    }
}
