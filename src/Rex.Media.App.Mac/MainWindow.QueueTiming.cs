using Rex.Media.AppCore.Design;
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
        var label = MacTheme.Label(title);
        label.WidthAnchor.ConstraintEqualTo(130).Active = true;
        field.TranslatesAutoresizingMaskIntoConstraints = false;
        field.WidthAnchor.ConstraintEqualTo(240).Active = true;
        return MacTheme.Stack(false, 1, label, field);
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
            MacTheme.Style(_queueTimingWindow);
            var heading = MacTheme.Label("Set the in and out points", 4, semibold: true);
            var hint = MacTheme.WrappingLabel(
                "Only this queue entry changes; the media file stays untouched. " +
                "Enter an end or a run time, not both.", 2, DesignRole.TextSecondary);
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
            _queueTimingNotice = MacTheme.WrappingLabel("", 2, DesignRole.TextSecondary);
            var apply = MacTheme.TextButton("Apply", ApplyQueueTiming, RexButtonLook.Accent);
            apply.KeyEquivalent = "\r";
            var cancel = MacTheme.TextButton("Cancel", () => _queueTimingWindow.OrderOut(null));
            var actions = MacTheme.Stack(false, 1, apply, cancel);
            var fields = MacTheme.Stack(true, 1,
                TimingRow("Start", _queueTimingStart),
                TimingRow("Stop", _queueTimingStop),
                TimingRow("Run time", _queueTimingRun));
            var words = MacTheme.Stack(true, 1, heading, hint);
            var content = MacTheme.Stack(true, 3, words, fields, _queueTimingNotice, actions);
            content.EdgeInsets = new NSEdgeInsets(MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4));
            var container = new NSView(new CGRect(0, 0, 470, 330));
            MacTheme.Fill(container, content);
            var wide = MacTheme.Space(4) * 2;
            words.WidthAnchor.ConstraintEqualTo(content.WidthAnchor, 1, -wide).Active = true;
            hint.WidthAnchor.ConstraintEqualTo(words.WidthAnchor).Active = true;
            _queueTimingNotice.WidthAnchor.ConstraintEqualTo(content.WidthAnchor, 1, -wide).Active = true;
            _queueTimingWindow.ContentView = container;
            _queueTimingStart.NextKeyView = _queueTimingStop;
            _queueTimingStop.NextKeyView = _queueTimingRun;
            _queueTimingRun.NextKeyView = apply;
            apply.NextKeyView = cancel;
            cancel.NextKeyView = _queueTimingStart;
            _queueTimingWindow.InitialFirstResponder = _queueTimingStart;
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
