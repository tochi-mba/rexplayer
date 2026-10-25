using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Mouse and trackpad preferences map events to the same command ids as keyboard bindings.</summary>
public sealed partial class MainWindow
{
    private void HandlePictureWheel(NSEvent wheel)
    {
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

    private void HandlePictureClick(NSEvent click)
    {
        if (click.ClickCount >= 2)
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
