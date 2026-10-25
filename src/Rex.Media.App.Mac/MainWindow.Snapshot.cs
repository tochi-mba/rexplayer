using Rex.Media.AppCore;
using Rex.Media.Codecs.Apple;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Capture the actual decoded source frame, not the window and its subtitle overlays.</summary>
public sealed partial class MainWindow
{
    private void SaveVideoSnapshot()
    {
        if (_player.Item?.Location is not { } location || !File.Exists(location) ||
            _player.Info?.FirstTrack(MediaKind.Video) is null)
        {
            Say("Open a local video before taking a picture.");
            return;
        }

        var position = _player.Position;
        var images = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var folder = Directory.Exists(images) ? images : _roots.Root;
        var suggested = Snapshot.FileFor(folder, _player.Title, position);
        var panel = NSSavePanel.SavePanel;
        panel.Title = "Save video picture as PNG";
        panel.NameFieldStringValue = Path.GetFileName(suggested);
        if (panel.RunModal() != 1 || panel.Url?.Path is not { } chosen)
        {
            return;
        }

        // A source-resolution snapshot is not a screenshot. Decode the exact time on a
        // separate source so seeking and exporting never disturb ongoing playback.
        var path = string.Equals(Path.GetExtension(chosen), ".png", StringComparison.OrdinalIgnoreCase)
            ? chosen : Path.ChangeExtension(chosen, ".png");
        Say("Saving the video frame…");
        _ = Task.Run(() =>
        {
            try
            {
                using var source = new FileByteSource(location);
                var result = Snapshot.SaveAsPng(source,
                    MediaRegistries.Decoders(new AppleDecoderFactory()),
                    MediaTime.FromSeconds(position.TotalSeconds), path, CancellationToken.None);
                OnWindowThread(() => Say("Saved " + result.Width + "×" + result.Height +
                    " picture to " + Path.GetFileName(result.Path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                or InvalidOperationException or NotSupportedException)
            {
                _log.Warning(LogSource, "Video snapshot failed: " + ex.Message);
                OnWindowThread(() => Say("Couldn't save the picture: " + ex.Message));
            }
        });
    }
}
