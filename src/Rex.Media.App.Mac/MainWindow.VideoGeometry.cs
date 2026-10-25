using Rex.Media.AppCore.Commands;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Shared video geometry commands wired into the native Metal presenter and Mac window.</summary>
public sealed partial class MainWindow
{
    private static readonly (string Name, double? Value)[] MacAspectRatios =
    [
        ("Source", null), ("16:9", 16.0 / 9), ("4:3", 4.0 / 3),
        ("1:1", 1), ("16:10", 16.0 / 10), ("2.21:1", 2.21),
        ("2.35:1", 2.35), ("2.39:1", 2.39), ("5:4", 5.0 / 4),
    ];
    private static readonly (string Name, double? Value)[] MacCropRatios =
    [
        ("No crop", null), ("16:9", 16.0 / 9), ("4:3", 4.0 / 3),
        ("1.85:1", 1.85), ("2.21:1", 2.21), ("2.35:1", 2.35),
        ("2.39:1", 2.39), ("16:10", 16.0 / 10), ("5:4", 5.0 / 4),
        ("1:1", 1),
    ];
    private int _aspectIndex;
    private int _cropIndex;
    private double _videoZoom = 1;
    private double _videoPanX;
    private double _videoPanY;

    private void CycleVideoAspect()
    {
        _aspectIndex = (_aspectIndex + 1) % MacAspectRatios.Length;
        ApplyVideoGeometry();
        Say("Aspect ratio: " + MacAspectRatios[_aspectIndex].Name);
    }

    private void CycleVideoCrop()
    {
        _cropIndex = (_cropIndex + 1) % MacCropRatios.Length;
        ApplyVideoGeometry();
        Say("Video crop: " + MacCropRatios[_cropIndex].Name);
    }

    private void ChangeVideoZoom(double factor)
    {
        _videoZoom = Math.Clamp(_videoZoom * factor, 1, 8);
        if (_videoZoom == 1)
        {
            _videoPanX = _videoPanY = 0;
        }

        ApplyVideoGeometry();
        Say("Video zoom " + _videoZoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×");
    }

    private void ResetVideoZoom()
    {
        _videoZoom = 1;
        _videoPanX = _videoPanY = 0;
        ApplyVideoGeometry();
    }

    private void PanVideo(double dx, double dy)
    {
        _videoPanX = Math.Clamp(_videoPanX + dx, -1, 1);
        _videoPanY = Math.Clamp(_videoPanY + dy, -1, 1);
        ApplyVideoGeometry();
    }

    private void ApplyVideoGeometry() => _picture?.Presenter?.SetViewGeometry(
        MacAspectRatios[_aspectIndex].Value, MacCropRatios[_cropIndex].Value,
        _videoZoom, _videoPanX, _videoPanY);

    private void SizeWindowToVideo(double scale)
    {
        var video = _player.Info?.FirstTrack(MediaKind.Video)?.Video;
        if (video is null)
        {
            Say("Open a video to choose its native window size.");
            return;
        }

        var visible = _window.Screen?.VisibleFrame ?? new CGRect(0, 0, 1280, 800);
        var factor = Math.Max(1.0, _window.BackingScaleFactor);
        var width = Math.Clamp(video.Width * scale / factor + 48, _window.MinSize.Width, visible.Width);
        var height = Math.Clamp(video.Height * scale / factor + 190, _window.MinSize.Height, visible.Height);
        var frame = _window.Frame;
        var x = Math.Clamp(frame.X, visible.Left, visible.Right - width);
        var y = Math.Clamp(frame.Y, visible.Bottom, visible.Top - height);
        _window.SetFrame(new CGRect(x, y, width, height), true);
        _picture?.Presenter?.Redraw();
    }
}
