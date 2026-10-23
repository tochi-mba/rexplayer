using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Recording details are an informational, non-destructive view. The existing separate
/// Remove-from-library and Recycle-Bin confirmations remain the only deletion actions.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowRecordingDetailsAsync(LibraryEntry recording)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = recording.Title,
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
        });
        var evidence = ListeningViews.RecordingDateEvidence(recording);
        var date = evidence is { } when
            ? (when.FromFilename ? "Timestamp in filename: " : "File last changed: ")
                + (when.At.Kind == DateTimeKind.Utc ? when.At.ToLocalTime() : when.At)
                    .ToString("g", CultureInfo.CurrentCulture)
            : "Date not available";
        var dateText = new TextBlock { Text = date, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(dateText, "RecordingDetailDate");
        panel.Children.Add(dateText);

        var duration = new TextBlock
        {
            Text = recording.Duration is { } length && length > TimeSpan.Zero
                ? "Duration: " + TimeText.Format(length, length)
                : recording.Duration == TimeSpan.Zero ? "Duration: 0:00" : "Duration unknown",
        };
        AutomationProperties.SetAutomationId(duration, "RecordingDetailDuration");
        panel.Children.Add(duration);
        var path = new TextBox
        {
            Header = "File location (select to copy)",
            Text = recording.Path,
            IsReadOnly = true,
        };
        AutomationProperties.SetAutomationId(path, "RecordingDetailPath");
        panel.Children.Add(path);
        var status = new TextBlock
        {
            Text = "Removing this entry from the library does not delete its file. Deletion to the Recycle Bin has a separate confirmation.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(status, "RecordingDetailStatus");
        panel.Children.Add(status);
        var play = new Button { Content = "Play or resume this recording" };
        AutomationProperties.SetAutomationId(play, "RecordingDetailPlay");
        play.Click += (_, _) => Play([recording], 0, resume: true);
        panel.Children.Add(play);

        var seekControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var seekTime = new TextBox { Header = "Jump to (m:ss)", Width = 150, PlaceholderText = "0:30" };
        var seek = new Button { Content = "Seek", VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetAutomationId(seekTime, "RecordingDetailSeekTime");
        AutomationProperties.SetAutomationId(seek, "RecordingDetailSeek");
        seekControls.Children.Add(seekTime);
        seekControls.Children.Add(seek);
        panel.Children.Add(seekControls);
        seek.Click += (_, _) =>
        {
            if (TimeText.TryParse(seekTime.Text) is not { } time || time < TimeSpan.Zero)
            {
                status.Text = "Enter a valid time, for example 0:30.";
            }
            else if (!_player.CanSeek || !LibraryViews.SameLocation(_player.Item?.Location, recording.Path))
            {
                status.Text = "Play this recording first to seek to an exact time.";
            }
            else
            {
                var maximum = _player.Duration > TimeSpan.Zero ? _player.Duration : time;
                _player.Seek(time > maximum ? maximum : time);
                status.Text = "Moved to " + TimeText.Format(_player.Position, maximum);
            }
        };

        var bookmark = new Button { Content = "Bookmark current position" };
        AutomationProperties.SetAutomationId(bookmark, "RecordingDetailBookmark");
        bookmark.Click += (_, _) =>
        {
            if (LibraryViews.SameLocation(_player.Item?.Location, recording.Path))
            {
                _player.AddBookmark();
                status.Text = "Saved a bookmark at the current recording position.";
            }
            else
            {
                status.Text = "Play this recording first to bookmark it.";
            }
        };
        panel.Children.Add(bookmark);

        var dialog = new ContentDialog
        {
            Title = "Recording details",
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        await ShowDialogAsync(dialog);
    }
}
