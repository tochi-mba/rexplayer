using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Per-queue-entry start, end and run-time editing without changing the original file.</summary>
public sealed partial class MainWindow
{
    private void EditQueueTiming(PlaylistItem item)
    {
        var index = _player.Playlist.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var start = new NSTextField(new CGRect(0, 0, 170, 24))
        {
            StringValue = TimeText.Format(item.Start),
            PlaceholderString = "0:00",
        };
        var stop = new NSTextField(new CGRect(0, 0, 170, 24))
        {
            StringValue = item.End is { } end ? TimeText.Format(end) : "",
            PlaceholderString = "End of file",
        };
        var run = new NSTextField(new CGRect(0, 0, 170, 24))
        {
            PlaceholderString = "Optional run time",
        };
        var fields = NSStackView.FromViews([
            EffectChoiceRow("Start", start),
            EffectChoiceRow("Stop", stop),
            EffectChoiceRow("Run time", run),
        ]);
        fields.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        fields.Spacing = 10;
        fields.Frame = new CGRect(0, 0, 390, 114);
        using var alert = new NSAlert
        {
            MessageText = "Set the in and out points",
            InformativeText = "Only this queue entry is changed; the media file stays untouched. " +
                "Set an end or a run time, not both.",
            AccessoryView = fields,
        };
        alert.AddButton("Apply");
        alert.AddButton("Cancel");
        if (alert.RunModal() != 1000)
        {
            return;
        }

        var begin = TimeText.TryParse(start.StringValue);
        var endTime = string.IsNullOrWhiteSpace(stop.StringValue) ? null : TimeText.TryParse(stop.StringValue);
        var length = string.IsNullOrWhiteSpace(run.StringValue) ? null : TimeText.TryParse(run.StringValue);
        if (begin is null || begin < TimeSpan.Zero ||
            (!string.IsNullOrWhiteSpace(stop.StringValue) && endTime is null) ||
            (!string.IsNullOrWhiteSpace(run.StringValue) && length is null) ||
            endTime is not null && length is not null ||
            length is { } duration && duration <= TimeSpan.Zero)
        {
            Say("Enter valid times, and use either Stop or Run time.");
            return;
        }

        var finish = length is { } span ? begin.Value + span : endTime;
        if (finish is { } endAt && endAt <= begin)
        {
            Say("The end must be later than the start.");
            return;
        }

        var edited = new PlaylistItem(item.Location)
        {
            Title = item.Title,
            Artist = item.Artist,
            Start = begin.Value,
            End = finish,
        };
        _player.Playlist.Replace(index, edited);
        RefreshQueue();
        Say("Set the entry to start at " + TimeText.Format(begin.Value));
    }
}
