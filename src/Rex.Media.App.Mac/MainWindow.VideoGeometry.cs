using Rex.Media.AppCore.Automation;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Shared video geometry commands wired into the native Metal presenter and Mac window.</summary>
public sealed partial class MainWindow
{
    private static readonly (string Name, double? Value)[] MacAspectRatios =
    [
        ("Source", null), ("16:9", 16.0 / 9), ("4:3", 4.0 / 3),
        ("1:1", 1), ("16:10", 16.0 / 10), ("2.21:1", 2.21),
        ("2.35:1", 2.35), ("2.39:1", 2.39), ("5:4", 5.0 / 4),
        ("Custom", null),
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
    private double _customAspect = 16.0 / 9;
    private string? _autoSizedLocation;

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
        _aspectIndex == MacAspectRatios.Length - 1 ? _customAspect : MacAspectRatios[_aspectIndex].Value,
        MacCropRatios[_cropIndex].Value,
        _videoZoom, _videoPanX, _videoPanY);

    private void ShowCustomAspectRatio()
    {
        var across = new NSTextField(new CGRect(0, 28, 100, 24)) { StringValue = "16" };
        var down = new NSTextField(new CGRect(120, 28, 100, 24)) { StringValue = "9" };
        var accessory = new NSView(new CGRect(0, 0, 230, 58));
        accessory.AddSubview(across);
        accessory.AddSubview(down);
        var alert = new NSAlert { MessageText = "Custom picture aspect", InformativeText = "Enter width and height." };
        alert.AddButton("Apply");
        alert.AddButton("Cancel");
        alert.AccessoryView = accessory;
        if (alert.RunModal() == 1000 && double.TryParse(across.StringValue,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width) &&
            double.TryParse(down.StringValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var height) && width > 0 && height > 0)
        {
            SetCustomAspectRatio(width, height);
        }
    }

    private void SetCustomAspectRatio(double width, double height)
    {
        _customAspect = Math.Clamp(width / height, 0.1, 10);
        _aspectIndex = MacAspectRatios.Length - 1;
        ApplyVideoGeometry();
        Say($"Aspect ratio: {width:0.##}:{height:0.##}");
    }

    private AutomationReply VideoGeometrySmokeAction(string? action)
    {
        switch (action)
        {
            case "magnify-and-pan":
                ChangeVideoZoom(2);
                PanVideo(0.25, -0.5);
                break;
            case "reset":
                ResetVideoZoom();
                break;
            case "status":
                break;
            case "custom":
                SetCustomAspectRatio(21, 9);
                break;
            default:
                return new AutomationReply { Ok = false, Error = "Unknown video geometry action." };
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"zoom={_videoZoom:0.00};pan={_videoPanX:0.00},{_videoPanY:0.00};" +
                $"aspect={(_aspectIndex == MacAspectRatios.Length - 1 ? _customAspect : MacAspectRatios[_aspectIndex].Value):0.000}",
        };
    }

    private void SizeWindowToVideo(double scale)
    {
        var video = _player.Info?.FirstTrack(MediaKind.Video)?.Video;
        if (video is null)
        {
            Say("Open a video to choose its native window size.");
            return;
        }

        var visible = _window.Screen?.VisibleFrame ?? new CGRect(0, 0, 1280, 800);
        var factor = Math.Max(1.0, (double)_window.BackingScaleFactor);
        var width = Math.Clamp(video.Width * scale / factor + 48,
            (double)_window.MinSize.Width, Math.Max((double)_window.MinSize.Width, (double)visible.Width));
        var height = Math.Clamp(video.Height * scale / factor + 190,
            (double)_window.MinSize.Height, Math.Max((double)_window.MinSize.Height, (double)visible.Height));
        var frame = _window.Frame;
        var x = Math.Clamp((double)frame.X, (double)visible.X,
            Math.Max((double)visible.X, (double)(visible.X + visible.Width) - width));
        var y = Math.Clamp((double)frame.Y, (double)visible.Y,
            Math.Max((double)visible.Y, (double)(visible.Y + visible.Height) - height));
        _window.SetFrame(new CGRect((nfloat)x, (nfloat)y, (nfloat)width, (nfloat)height), true);
        _picture?.Presenter?.Redraw();
    }

    private void ApplyAutomaticWindowSize()
    {
        var location = _player.Item?.Location;
        if (!_settings.ResizeWindowToVideo || location is null || location == _autoSizedLocation ||
            _player.Info?.FirstTrack(MediaKind.Video)?.Video is null) return;
        _autoSizedLocation = location;
        SizeWindowToVideo(_settings.VideoWindowScale);
    }

    private void ToggleAutomaticWindowSize()
    {
        SaveAutomaticWindowSize(!_settings.ResizeWindowToVideo, _settings.VideoWindowScale);
    }

    private void ChangeAutomaticWindowScale(int direction)
    {
        double[] scales = [0.25, 0.5, 1, 2];
        var index = Array.FindIndex(scales, scale => scale >= _settings.VideoWindowScale - 0.001);
        index = Math.Clamp((index < 0 ? 2 : index) + direction, 0, scales.Length - 1);
        SaveAutomaticWindowSize(_settings.ResizeWindowToVideo, scales[index]);
        SizeWindowToVideo(scales[index]);
    }

    private void SaveAutomaticWindowSize(bool enabled, double scale)
    {
        _settings = (_settings with { ResizeWindowToVideo = enabled, VideoWindowScale = scale }).Normalize();
        _player.Settings = _settings;
        SettingsStore.Save(_roots.SettingsPath, _settings);
        _autoSizedLocation = null;
        ApplyAutomaticWindowSize();
        Say($"Automatic window sizing {(enabled ? "on" : "off")} at {scale:0.##}×");
    }

    private AutomationReply MacWindowSizingSmokeAction(string? action)
    {
        switch (action)
        {
            case "configure": SaveAutomaticWindowSize(true, 0.5); break;
            case "larger": ChangeAutomaticWindowScale(+1); break;
            case "smaller": ChangeAutomaticWindowScale(-1); break;
            case "status": break;
            default: return AutomationReply.Failed("Unknown window-sizing action.");
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"automatic={_settings.ResizeWindowToVideo};scale={_settings.VideoWindowScale:0.00}",
        };
    }
}
