using Rex.Media.Primitives;
using Rex.Media.Settings;
using ZoomedView = Rex.Media.Video.PictureView;

namespace Rex.Media.App.Mac;

/// <summary>
/// The zoom navigator (VID-07): the whole picture, small, in the top left corner of the stage while
/// zoomed in, with a box marking the part in view; a click or a drag in it moves the view. Where it
/// goes and what the box covers come from the shared <see cref="ZoomedView"/> decisions the Windows
/// window draws from; this class only turns the Metal presenter's zoom and pan into that view and
/// back.
/// </summary>
public sealed partial class MainWindow
{
    private NavigatorView? _videoNavigator;

    private bool MacNavigatorShown => _settings.ShowNavigator && _videoZoom > 1 &&
        _player.Info?.FirstTrack(MediaKind.Video)?.Video is not null;

    private void ToggleVideoNavigator()
    {
        var candidate = (_settings with { ShowNavigator = !_settings.ShowNavigator }).Normalize();
        try
        {
            SettingsStore.Save(_roots.SettingsPath, candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "The navigator preference could not be saved: " + ex.Message);
        }

        _settings = candidate;
        _player.Settings = candidate;
        Say(_settings.ShowNavigator ? "Navigator on" : "Navigator off");
        RefreshVideoNavigator();
    }

    /// <summary>Shows or hides the overlay to match the setting, the zoom and the media; created the first time it is owed.</summary>
    private void RefreshVideoNavigator()
    {
        var shown = MacNavigatorShown;
        if (_videoNavigator is null)
        {
            if (!shown || _picture is not { } picture || picture.Superview is not { } content)
            {
                return;
            }

            _videoNavigator = new NavigatorView(this) { TranslatesAutoresizingMaskIntoConstraints = false };
            ((NSView)_videoNavigator).AccessibilityLabel = "Zoom navigator";
            _videoNavigator.AccessibilityHelp = "The whole picture while zoomed in; click or drag to move the view";
            content.AddSubview(_videoNavigator, NSWindowOrderingMode.Above, picture);
            _videoNavigator.TopAnchor.ConstraintEqualTo(picture.TopAnchor).Active = true;
            _videoNavigator.BottomAnchor.ConstraintEqualTo(picture.BottomAnchor).Active = true;
            _videoNavigator.LeadingAnchor.ConstraintEqualTo(picture.LeadingAnchor).Active = true;
            _videoNavigator.TrailingAnchor.ConstraintEqualTo(picture.TrailingAnchor).Active = true;
            // Opening or closing media changes whether the navigator is owed at all.
            _player.Changed += (_, _) => OnWindowThread(RefreshVideoNavigator);
        }

        _videoNavigator.Hidden = !shown;
        _videoNavigator.NeedsDisplay = true;
    }

    /// <summary>
    /// The shape of the picture as it is shown, across over down, mirroring the presenter's
    /// contract: a crop trims the source's shape symmetrically, and an explicit aspect overrides
    /// what is left; null when nothing visual is playing.
    /// </summary>
    private double? DisplayedPictureAspect()
    {
        if (_player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video || video.Height <= 0)
        {
            return null;
        }

        var pixel = video.PixelAspect.Numerator > 0 ? video.PixelAspect.Value : 1;
        var source = video.Width * pixel / video.Height;
        var (cropU, cropV) = (1.0, 1.0);
        if (MacCropRatios[_cropIndex].Value is { } crop)
        {
            if (crop > source)
            {
                cropV = source / crop;
            }
            else
            {
                cropU = crop / source;
            }
        }

        var aspect = _aspectIndex == MacAspectRatios.Length - 1 ? _customAspect : MacAspectRatios[_aspectIndex].Value;
        return aspect ?? source * cropU / cropV;
    }

    /// <summary>
    /// Where the navigator sits on a stage of <paramref name="stage"/> points and where the box
    /// marking the view falls inside it, in top-left coordinates; null while it has nothing to show.
    /// </summary>
    private (CGRect Frame, CGRect View)? NavigatorPlacement(CGSize stage)
    {
        if (!MacNavigatorShown || DisplayedPictureAspect() is not { } aspect || stage.Width <= 0 || stage.Height <= 0)
        {
            return null;
        }

        var (x, y, width, height) = ZoomedView.Navigator((double)stage.Width, (double)stage.Height,
            (long)Math.Round(aspect * 100000), 100000);
        var view = new ZoomedView(_videoZoom, CenterOfPan(_videoPanX), CenterOfPan(_videoPanY));
        var (left, top, viewWidth, viewHeight) = view.Visible;
        return (new CGRect((nfloat)x, (nfloat)y, (nfloat)width, (nfloat)height),
            new CGRect((nfloat)(x + (left * width)), (nfloat)(y + (top * height)),
                (nfloat)Math.Max(2, viewWidth * width), (nfloat)Math.Max(2, viewHeight * height)));
    }

    /// <summary>Centres the zoomed view on (<paramref name="x"/>, <paramref name="y"/>) of the whole picture: a click or a drag in the navigator.</summary>
    private void CenterVideoViewOn(double x, double y)
    {
        if (_videoZoom <= 1)
        {
            return;
        }

        var view = new ZoomedView(_videoZoom, x, y);
        _videoPanX = PanOfCenter(view.CenterX);
        _videoPanY = PanOfCenter(view.CenterY);
        ApplyVideoGeometry();
    }

    // The presenter's pan reaches each edge of the cropped source at ±1, so the view's centre
    // runs from half a view to one less half a view as the pan runs from -1 to 1, each way.
    private double CenterOfPan(double pan) =>
        ((1 - (1 / _videoZoom)) * (1 + pan) / 2) + (1 / (2 * _videoZoom));

    private double PanOfCenter(double center) =>
        Math.Clamp(_videoZoom * ((2 * center) - 1) / (_videoZoom - 1), -1, 1);

    /// <summary>
    /// The overlay itself: transparent over the whole stage, drawing only the small picture frame
    /// and the box, and taking the pointer only inside the frame so the stage keeps its clicks.
    /// </summary>
    private sealed class NavigatorView : NSView
    {
        private readonly MainWindow _owner;

        public NavigatorView(MainWindow owner) => _owner = owner;

        // Top-left coordinates, as the shared placement is worked out.
        public override bool IsFlipped => true;

        public override void SetFrameSize(CGSize newSize)
        {
            base.SetFrameSize(newSize);
            NeedsDisplay = true;
        }

        public override NSView? HitTest(CGPoint aPoint)
        {
            if (Hidden || _owner.NavigatorPlacement(Bounds.Size) is not { } placed)
            {
                return null;
            }

            return placed.Frame.Contains(ConvertPointFromView(aPoint, Superview)) ? this : null;
        }

        public override void MouseDown(NSEvent theEvent) => MoveView(theEvent);

        public override void MouseDragged(NSEvent theEvent) => MoveView(theEvent);

        private void MoveView(NSEvent theEvent)
        {
            if (_owner.NavigatorPlacement(Bounds.Size) is not { } placed)
            {
                return;
            }

            var local = ConvertPointFromView(theEvent.LocationInWindow, null);
            _owner.CenterVideoViewOn(
                Math.Clamp(((double)local.X - (double)placed.Frame.X) / (double)placed.Frame.Width, 0, 1),
                Math.Clamp(((double)local.Y - (double)placed.Frame.Y) / (double)placed.Frame.Height, 0, 1));
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (_owner.NavigatorPlacement(Bounds.Size) is not { } placed)
            {
                return;
            }

            NSColor.Black.ColorWithAlphaComponent((System.Runtime.InteropServices.NFloat)0.55).SetFill();
            NSBezierPath.FromRect(placed.Frame).Fill();
            NSColor.White.ColorWithAlphaComponent((System.Runtime.InteropServices.NFloat)0.25).SetFill();
            NSBezierPath.FromRect(placed.View).Fill();
            var box = NSBezierPath.FromRect(placed.View);
            box.LineWidth = (System.Runtime.InteropServices.NFloat)1.5;
            NSColor.White.SetStroke();
            box.Stroke();
            var frame = NSBezierPath.FromRect(placed.Frame);
            frame.LineWidth = 1;
            NSColor.White.ColorWithAlphaComponent((System.Runtime.InteropServices.NFloat)0.8).SetStroke();
            frame.Stroke();
        }
    }
}
