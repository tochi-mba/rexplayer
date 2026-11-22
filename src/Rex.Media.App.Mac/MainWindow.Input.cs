using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Mouse and trackpad preferences map events to the same command ids as keyboard bindings.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Whether <paramref name="pointer"/> fell on the picture itself, and not on the bar, the Up Next
    /// pane or the library over the stage: only the picture answers wheel, drag and double-click.
    /// </summary>
    private bool IsOnStage(NSEvent pointer)
    {
        if (_picture is not { } picture || LibraryShown)
        {
            return false;
        }

        var point = picture.ConvertPointFromView(pointer.LocationInWindow, null);
        if (!picture.Bounds.Contains(point))
        {
            return false;
        }

        return _chrome is not { Hidden: false } chrome
            || !chrome.Bounds.Contains(chrome.ConvertPointFromView(pointer.LocationInWindow, null));
    }

    private void HandlePictureWheel(NSEvent wheel)
    {
        if (!IsOnStage(wheel))
        {
            return;
        }

        // For trackpads use the dominant axis so a sideways gesture does not also seek vertically.
        var sideways = Math.Abs(wheel.ScrollingDeltaX) > Math.Abs(wheel.ScrollingDeltaY);
        var delta = sideways ? wheel.ScrollingDeltaX : wheel.ScrollingDeltaY;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var forward = delta > 0;
        if (!sideways && (wheel.ModifierFlags & NSEventModifierMask.CommandKeyMask) != 0)
        {
            switch (_settings.CtrlWheel)
            {
                case CtrlWheelChoice.Zoom:
                    ChangeVideoZoom(forward ? 1.1 : 1 / 1.1);
                    break;
                case CtrlWheelChoice.SubtitleSize:
                    ChangeSubtitleScale(forward ? +25 : -25);
                    break;
            }

            return;
        }

        if (_videoZoom > 1 && _settings.WheelPansWhenZoomed)
        {
            PanVideo(sideways ? (forward ? 0.12 : -0.12) : 0,
                sideways ? 0 : (forward ? -0.12 : 0.12));
            return;
        }

        var command = ShortcutEditing.WheelCommand(sideways ? _settings.SidewaysWheel : _settings.Wheel, forward);
        if (command is not null)
        {
            Run(command);
        }
    }

    private void HandlePictureDrag(NSEvent drag)
    {
        var picture = _picture;
        if (_videoZoom <= 1 || picture is null || !IsOnStage(drag))
        {
            return;
        }

        var width = Math.Max(1, (double)picture.Bounds.Width);
        var height = Math.Max(1, (double)picture.Bounds.Height);
        PanVideo((double)drag.DeltaX / width * 2, (double)drag.DeltaY / height * 2);
    }

    private void HandlePictureClick(NSEvent click)
    {
        if (click.ClickCount >= 2 && IsOnStage(click))
        {
            Run(CommandCatalog.ToggleFullScreen);
        }
    }

    private void HandlePictureOtherClick(NSEvent click)
    {
        var button = (int)click.ButtonNumber;
        var command = button switch
        {
            2 => ShortcutEditing.MiddleButtonCommand(_settings.MiddleButton),
            3 or 4 => ShortcutEditing.SideButtonCommand(_settings.SideButtons, button == 4),
            _ => null,
        };
        if (command is not null)
        {
            Run(command);
        }
    }
}
