using Rex.Media.AppCore;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.Apple;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Decoded hover pictures and time labels for the main seek timeline.</summary>
public sealed partial class MainWindow
{
    /// <summary>The preview's width, as on Windows.</summary>
    private const double SeekPreviewWidth = 192;

    private RexPanel? _seekPreview;
    private NSImageView? _seekPreviewImage;
    private RexLabel? _seekPreviewTime;
    private NSLayoutConstraint? _seekPreviewLeading;
    private NSTimer? _seekPreviewDelay;
    private CancellationTokenSource? _seekPreviewCancellation;
    private NSImage? _seekPreviewPicture;

    /// <summary>
    /// Lays the preview over <paramref name="content"/>, just above the seek bar, as Windows floats
    /// its own: a dark card with the picture at that moment and the time under it. It never moves
    /// the bar, and it follows the pointer along the track.
    /// </summary>
    private void AddSeekPreview(NSView content)
    {
        _seekPreviewImage = new NSImageView(new CGRect(0, 0, 178, 100))
        {
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
            TranslatesAutoresizingMaskIntoConstraints = false,
            WantsLayer = true,
            Hidden = true,
        };
        _seekPreviewImage.Layer!.BackgroundColor = MacTheme.Stage.CGColor;
        _seekPreviewImage.Layer.CornerRadius = 5;
        _seekPreviewImage.Layer.MasksToBounds = true;
        _seekPreviewImage.WidthAnchor.ConstraintEqualTo(178).Active = true;
        _seekPreviewImage.HeightAnchor.ConstraintEqualTo(100).Active = true;
        _seekPreviewTime = MacTheme.Label("0:00", 2, MacTheme.OverlayText, semibold: true);
        _seekPreviewTime.Font = NSFont.MonospacedDigitSystemFontOfSize(MacTheme.TypeSize(2), NSFontWeight.Semibold)!;
        _seekPreviewTime.Alignment = NSTextAlignment.Center;
        var stack = MacTheme.Stack(true, 0, _seekPreviewImage, _seekPreviewTime);
        stack.Alignment = NSLayoutAttribute.CenterX;
        stack.Spacing = 5;
        _seekPreview = MacTheme.Panel(MacTheme.StrongOverlay, 1, MacTheme.OverlayHairline);
        _seekPreview.Hidden = true;
        ((NSView)_seekPreview).AccessibilityLabel = "Timeline preview";
        _seekPreview.AccessibilityIdentifier = "SeekPreview";
        MacTheme.Fill(_seekPreview, stack, 6);
        content.AddSubview(_seekPreview);
        _seekPreview.WidthAnchor.ConstraintEqualTo((nfloat)SeekPreviewWidth).Active = true;
        _seekPreview.BottomAnchor.ConstraintEqualTo(_seek.TopAnchor, -2).Active = true;
        _seekPreviewLeading = _seekPreview.LeadingAnchor.ConstraintEqualTo(_seek.LeadingAnchor);
        _seekPreviewLeading.Active = true;
    }

    private void ShowSeekPreview(double fraction)
    {
        if (_seekPreview is null || _seekPreviewTime is null || _player.Duration <= TimeSpan.Zero) return;
        var at = _player.Duration * fraction;
        PlaceSeekPreview(fraction);
        _seekPreview.Hidden = false;
        _seekPreviewTime.StringValue = TimeText.Format(at, _player.Duration);
        _seekPreviewDelay?.Invalidate();
        _seekPreviewDelay?.Dispose();
        _seekPreviewDelay = NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(180), _ => DecodeSeekPreview(at));
    }

    /// <summary>Centres the preview over the pointer's place on the track, kept inside the window.</summary>
    private void PlaceSeekPreview(double fraction)
    {
        if (_seekPreviewLeading is null || _seek.Superview is null || _window.ContentView is not { } content) return;
        var seekLeft = (double)_seek.ConvertPointToView(CGPoint.Empty, content).X;
        var margin = (double)MacTheme.Space(1);
        var wanted = _seek.XAt(fraction) - SeekPreviewWidth / 2;
        var lowest = margin - seekLeft;
        var highest = (double)content.Bounds.Width - margin - SeekPreviewWidth - seekLeft;
        _seekPreviewLeading.Constant = (nfloat)Math.Max(lowest, Math.Min(highest, wanted));
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
        if (location is null || !File.Exists(location) || _player.Info?.FirstTrack(MediaKind.Video) is null)
        {
            if (_seekPreviewImage is not null) _seekPreviewImage.Hidden = true;
            return;
        }

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
                    if (_seekPreviewImage is not null)
                    {
                        _seekPreviewImage.Image = image;
                        _seekPreviewImage.Hidden = false;
                    }
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
