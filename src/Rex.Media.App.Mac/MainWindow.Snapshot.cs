using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.Codecs.Apple;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Capture the actual decoded source frame, not the window and its subtitle overlays.</summary>
public sealed partial class MainWindow
{
    private AutomationReply SnapshotSmokeAction(string? format, string? output)
    {
        if (_player.Item?.Location is not { } location || !File.Exists(location) ||
            _player.Info?.FirstTrack(MediaKind.Video) is null || string.IsNullOrWhiteSpace(output))
        {
            return AutomationReply.Failed("Open a local video and provide a snapshot path.");
        }

        var type = format switch
        {
            "png" => NSBitmapImageFileType.Png,
            "jpeg" => NSBitmapImageFileType.Jpeg,
            "bmp" => NSBitmapImageFileType.Bmp,
            _ => (NSBitmapImageFileType?)null,
        };
        if (type is null)
        {
            return AutomationReply.Failed("Snapshot format must be png, jpeg or bmp.");
        }

        var temp = type == NSBitmapImageFileType.Png ? output :
            Path.Combine(Path.GetTempPath(), "rexplayer-snapshot-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var source = new FileByteSource(location);
            var result = Snapshot.SaveAsPng(source, MediaRegistries.Decoders(new AppleDecoderFactory()),
                MediaTime.FromSeconds(_player.Position.TotalSeconds), temp, CancellationToken.None);
            if (type != NSBitmapImageFileType.Png)
            {
                using var sourcePng = NSData.FromFile(temp);
                if (sourcePng is null) throw new IOException("The decoded picture is missing.");
                using var bitmap = NSBitmapImageRep.ImageRepFromData(sourcePng) as NSBitmapImageRep
                    ?? throw new IOException("macOS could not read the decoded picture.");
                using var encoded = bitmap.RepresentationUsingTypeProperties(type.Value)
                    ?? throw new IOException("macOS could not encode the requested picture format.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllBytes(output, encoded.ToArray());
            }

            return new AutomationReply
            {
                Ok = true,
                Text = $"format={format};width={result.Width};height={result.Height};path={output}",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or InvalidOperationException or NotSupportedException)
        {
            return AutomationReply.Failed(ex.Message);
        }
        finally
        {
            if (type != NSBitmapImageFileType.Png)
            {
                try { File.Delete(temp); }
                catch (IOException) { /* Best-effort cleanup of a temporary PNG. */ }
            }
        }
    }

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
        var preferred = _settings.SnapshotFolder;
        var folder = preferred is not null && Directory.Exists(preferred) ? preferred
            : Directory.Exists(images) ? images : _roots.Root;
        var suggested = Snapshot.FileFor(folder, _player.Title, position,
            pattern: _settings.SnapshotNamePattern, sequential: _settings.SnapshotSequential);
        var panel = NSSavePanel.SavePanel;
        panel.Title = "Save video picture";
        panel.NameFieldStringValue = Path.GetFileName(suggested);
        var picker = new NSPopUpButton(new CGRect(0, 0, 270, 26), false);
        picker.AddItem("PNG (lossless)");
        picker.AddItem("JPEG (photo)");
        picker.AddItem("BMP (uncompressed)");
        ((NSView)picker).AccessibilityLabel = "Snapshot image format";
        panel.AccessoryView = picker;
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } chosen) return;
            var selected = (int)picker.IndexOfSelectedItem;
            var suffix = selected switch { 1 => ".jpg", 2 => ".bmp", _ => ".png" };
            var output = Path.ChangeExtension(chosen, suffix);
            // Changing a PNG suggestion to JPEG must not silently overwrite an existing JPEG.
            if (!string.Equals(output, chosen, StringComparison.Ordinal) && File.Exists(output))
            {
                Say("The " + suffix + " snapshot already exists. Choose another filename.");
                return;
            }

            // The destination folder is remembered for the next snapshot.
            _settings = _settings with { SnapshotFolder = Path.GetDirectoryName(output) };
            try
            {
                SettingsStore.Save(_roots.SettingsPath, _settings);
                _player.Settings = _settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning(LogSource, "The snapshot folder preference could not be saved: " + ex.Message);
            }
            Say("Saving the video frame…");
            // Source-resolution decoding runs independently of current playback.
            _ = Task.Run(() =>
            {
                var temp = selected == 0 ? output :
                    Path.Combine(Path.GetTempPath(), "rexplayer-snapshot-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    using var source = new FileByteSource(location);
                    var result = Snapshot.SaveAsPng(source,
                        MediaRegistries.Decoders(new AppleDecoderFactory()),
                        MediaTime.FromSeconds(position.TotalSeconds), temp, CancellationToken.None);
                    if (selected == 0)
                    {
                        OnWindowThread(() => Say("Saved " + result.Width + "×" + result.Height +
                            " PNG to " + Path.GetFileName(output)));
                        return;
                    }

                    // AppKit's bitmap codec writes JPEG/BMP from an unaltered full-resolution
                    // PNG; use it on the window thread rather than manipulating native UI types
                    // on the source decoder's worker thread.
                    OnWindowThread(() =>
                    {
                        try
                        {
                            using var sourcePng = NSData.FromFile(temp);
                            if (sourcePng is null) throw new IOException("The decoded picture is missing.");
                            using var bitmap = NSBitmapImageRep.ImageRepFromData(sourcePng) as NSBitmapImageRep
                                ?? throw new IOException("macOS could not read the source-resolution picture.");
                            using var encoded = bitmap.RepresentationUsingTypeProperties(
                                selected == 1 ? NSBitmapImageFileType.Jpeg : NSBitmapImageFileType.Bmp)
                                ?? throw new IOException("macOS could not encode the requested picture format.");
                            File.WriteAllBytes(output, encoded.ToArray());
                            Say("Saved " + result.Width + "×" + result.Height + " " +
                                (selected == 1 ? "JPEG" : "BMP") + " to " + Path.GetFileName(output));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                            or InvalidOperationException or NotSupportedException)
                        {
                            Say("Couldn't export the picture: " + ex.Message);
                        }
                        finally
                        {
                            try { File.Delete(temp); }
                            catch (IOException) { /* Best-effort cleanup of a temporary PNG. */ }
                        }
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                    or InvalidOperationException or NotSupportedException)
                {
                    _log.Warning(LogSource, "Video snapshot failed: " + ex.Message);
                    OnWindowThread(() => Say("Couldn't save the picture: " + ex.Message));
                    if (selected != 0)
                    {
                        try { File.Delete(temp); }
                        catch (IOException) { /* Best-effort cleanup of a temporary PNG. */ }
                    }
                }
            });
        });
    }
}
