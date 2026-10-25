using Rex.Media.Primitives;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Inspect the current media's tracks and metadata without interrupting playback.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _mediaInformationWindow;
    private NSTextView? _mediaInformationText;

    private void ShowMediaInformation()
    {
        if (_player.Info is not { } info)
        {
            Say("Open something to see its media information.");
            return;
        }

        var lines = new List<string>
        {
            $"Title: {_player.Title}",
            $"Location: {_player.Item?.Location}",
            $"Format: {info.FormatName}",
            $"Length: {(_player.Duration > TimeSpan.Zero ? TimeText.Format(_player.Duration) : "unknown")}",
        };
        foreach (var track in info.Tracks)
        {
            var detail = track.Video is { } video ? $"{video.Width}×{video.Height}"
                : track.Audio is { } audio ? $"{audio.SampleRate} Hz, {audio.Channels} channel(s)"
                : "";
            lines.Add($"Track {track.Id}: {track.Kind} {track.Codec.DisplayName()} {detail}".TrimEnd());
        }

        lines.AddRange(info.Metadata.Select(metadata => $"{metadata.Key}: {metadata.Value}"));
        if (_mediaInformationWindow is null)
        {
            _mediaInformationWindow = new NSWindow(new CGRect(0, 0, 640, 460),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Media information - rexplayer",
                MinSize = new CGSize(460, 280),
            };
            _mediaInformationWindow.ReleaseWhenClosed(false);
            _mediaInformationText = new NSTextView(new CGRect(0, 0, 600, 430))
            {
                Editable = false,
                Selectable = true,
                Font = NSFont.UserFixedPitchFontOfSize(13)!,
            };
            ((NSView)_mediaInformationText).AccessibilityLabel = "Media tracks and metadata";
            var scroll = new NSScrollView(new CGRect(0, 0, 640, 460))
            {
                HasVerticalScroller = true,
                AutohidesScrollers = true,
                DocumentView = _mediaInformationText,
            };
            _mediaInformationWindow.ContentView = scroll;
            _mediaInformationWindow.Center();
        }

        _mediaInformationText!.Value = string.Join(Environment.NewLine, lines);
        _mediaInformationWindow.MakeKeyAndOrderFront(null);
    }

    private void DisposeMediaInformation()
    {
        _mediaInformationWindow?.Close();
        _mediaInformationWindow?.Dispose();
        _mediaInformationWindow = null;
    }
}
