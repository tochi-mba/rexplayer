using Rex.Media.Settings;

namespace Rex.Media.AppCore.Commands;

/// <summary>What a turn of the wheel over the picture does.</summary>
public enum WheelEffect
{
    Nothing,

    /// <summary>Zooms the picture about the pointer (VID-07).</summary>
    Zoom,

    /// <summary>Moves about the zoomed picture.</summary>
    Pan,

    /// <summary>Runs <see cref="WheelAction.Command"/>.</summary>
    Command,
}

/// <summary>A turn of the wheel's effect, and the command it runs when it runs one.</summary>
public readonly record struct WheelAction(WheelEffect Effect, string? Command = null);

/// <summary>
/// The wheel over the picture (UI-11, VID-07). A precision touchpad's pinch reaches the window as
/// the wheel with Ctrl held, and its two-finger scroll as the wheel itself, so: Ctrl (or Alt) with
/// the wheel zooms, as pinching does; while zoomed in, the wheel moves about the picture, as
/// scrolling does; otherwise the wheel does what the settings say.
/// </summary>
public static class WheelGestures
{
    public static WheelAction Interpret(PlayerSettings settings, bool ctrl, bool alt, bool sideways, bool forward, bool zoomed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (alt || (ctrl && settings.CtrlWheel == CtrlWheelChoice.Zoom))
        {
            return new WheelAction(WheelEffect.Zoom);
        }

        if (ctrl)
        {
            return new WheelAction(WheelEffect.Command, forward ? CommandCatalog.SubtitlesBigger : CommandCatalog.SubtitlesSmaller);
        }

        if (zoomed && settings.WheelPansWhenZoomed)
        {
            return new WheelAction(WheelEffect.Pan);
        }

        return ShortcutEditing.WheelCommand(sideways ? settings.SidewaysWheel : settings.Wheel, forward) is { } command
            ? new WheelAction(WheelEffect.Command, command)
            : new WheelAction(WheelEffect.Nothing);
    }

    /// <summary>
    /// How much a wheel <paramref name="delta"/> zooms: one notch (120) by <paramref name="notch"/>,
    /// and a touchpad's many small steps by their share of it, so a pinch zooms smoothly.
    /// </summary>
    public static double ZoomFactor(int delta, double notch) => Math.Pow(notch, delta / 120.0);
}
