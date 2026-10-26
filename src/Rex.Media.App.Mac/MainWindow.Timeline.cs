using Rex.Media.AppCore;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.Apple;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Decoded hover pictures and time labels for the main seek timeline.</summary>
public sealed partial class MainWindow
{
    private NSStackView? _seekPreview;
    private NSImageView? _seekPreviewImage;
    private NSTextField? _seekPreviewTime;
    private NSTimer? _seekPreviewDelay;
    private CancellationTokenSource? _seekPreviewCancellation;
    private NSImage? _seekPreviewPicture;

    private NSView BuildSeekPreview()
    {
        _seekPreviewImage = new NSImageView(new CGRect(0, 0, 128, 72))
        {
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
        };
        _seekPreviewImage.WidthAnchor.ConstraintEqualTo(128).Active = true;
        _seekPreviewImage.HeightAnchor.ConstraintEqualTo(72).Active = true;
        _seekPreviewTime = NSTextField.CreateLabel("0:00");
        _seekPreviewTime.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Semibold)!;
        _seekPreview = NSStackView.FromViews([_seekPreviewImage, _seekPreviewTime]);
        _seekPreview.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        _seekPreview.Spacing = 8;
        _seekPreview.Hidden = true;
        return _seekPreview;
    }

    private void ShowSeekPreview(double fraction)
    {
        if (_seekPreview is null || _seekPreviewTime is null || _player.Duration <= TimeSpan.Zero) return;
        var at = _player.Duration * fraction;
        _seekPreview.Hidden = false;
        _seekPreviewTime.StringValue = TimeText.Format(at, _player.Duration);
        _seekPreviewDelay?.Invalidate();
        _seekPreviewDelay?.Dispose();
        _seekPreviewDelay = NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(180), _ => DecodeSeekPreview(at));
    }

    private void DecodeSeekPreview(TimeSpan at)
    {
        _seekPreviewDelay?.Dispose();
        _seekPreviewDelay = null;
        _seekPreviewCancellation?.Cancel();
        _seekPreviewCancellation?.Dispose();
        _seekPreviewCancellation = new CancellationTokenSource();
        var token = _seekPreviewCancellation.Token;
        var location = _player.Item?.Location;
        if (location is null || !File.Exists(location) || _player.Info?.FirstTrack(MediaKind.Video) is null) return;
        var temp = Path.Combine(Path.GetTempPath(), "rexplayer-hover-" + Guid.NewGuid().ToString("N") + ".png");
        _ = Task.Run(() =>
        {
            try
            {
                using var source = new FileByteSource(location);
                Snapshot.SaveAsPng(source, MediaRegistries.Decoders(new AppleDecoderFactory()),
                    MediaTime.FromSeconds(at.TotalSeconds), temp, token);
                if (token.IsCancellationRequested) return;
                OnWindowThread(() =>
                {
                    using var data = NSData.FromFile(temp);
                    if (data is null || token.IsCancellationRequested) return;
                    var image = new NSImage(data);
                    _seekPreviewPicture?.Dispose();
                    _seekPreviewPicture = image;
                    if (_seekPreviewImage is not null) _seekPreviewImage.Image = image;
                });
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException
                or NotSupportedException or OperationCanceledException)
            {
                _log.Debug(LogSource, "Timeline preview was unavailable: " + ex.Message);
            }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
            }
        }, token);
    }

    private void HideSeekPreview()
    {
        if (_seekPreview is not null) _seekPreview.Hidden = true;
        _seekPreviewDelay?.Invalidate();
        _seekPreviewDelay?.Dispose();
        _seekPreviewDelay = null;
        _seekPreviewCancellation?.Cancel();
    }

    private void DisposeTimelinePreview()
    {
        HideSeekPreview();
        _seekPreviewCancellation?.Dispose();
        _seekPreviewCancellation = null;
        _seekPreviewPicture?.Dispose();
        _seekPreviewPicture = null;
    }
}
