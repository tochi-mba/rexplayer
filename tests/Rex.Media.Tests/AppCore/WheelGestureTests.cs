using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The wheel over the picture, and a touchpad's pinch and scroll, which reach the window as the wheel (UI-11, VID-07).</summary>
public sealed class WheelGestureTests
{
    private static readonly PlayerSettings Defaults = new();

    private static WheelAction Turn(PlayerSettings? settings = null, bool ctrl = false, bool alt = false, bool sideways = false, bool forward = true, bool zoomed = false) =>
        WheelGestures.Interpret(settings ?? Defaults, ctrl, alt, sideways, forward, zoomed);

    [Fact]
    [Capability("VID-07")]
    public void PinchingATouchpadZoomsThePictureAsCtrlOrAltWithTheWheelDo()
    {
        // A precision touchpad's pinch arrives as the wheel with Ctrl held.
        Assert.Equal(WheelEffect.Zoom, Turn(ctrl: true).Effect);
        Assert.Equal(WheelEffect.Zoom, Turn(ctrl: true, zoomed: true, forward: false).Effect);
        Assert.Equal(WheelEffect.Zoom, Turn(alt: true).Effect);

        // Set to size the subtitles, Ctrl does that, and Alt still zooms.
        var subtitles = new PlayerSettings { CtrlWheel = CtrlWheelChoice.SubtitleSize };
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.SubtitlesBigger), Turn(subtitles, ctrl: true));
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.SubtitlesSmaller), Turn(subtitles, ctrl: true, forward: false));
        Assert.Equal(WheelEffect.Zoom, Turn(subtitles, alt: true).Effect);
    }

    [Fact]
    [Capability("VID-07")]
    public void ZoomedInTheWheelAndTwoFingerScrollingMoveAboutThePicture()
    {
        Assert.Equal(WheelEffect.Pan, Turn(zoomed: true).Effect);
        Assert.Equal(WheelEffect.Pan, Turn(zoomed: true, sideways: true).Effect);

        // Unless asked not to: then they do what they always do.
        var asUsual = new PlayerSettings { WheelPansWhenZoomed = false };
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.VolumeUp), Turn(asUsual, zoomed: true));
    }

    [Fact]
    [Capability("UI-11")]
    public void OtherwiseTheWheelDoesWhatTheSettingsSay()
    {
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.VolumeDown), Turn(forward: false));
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.JumpForwardVeryShort), Turn(sideways: true));
        Assert.Equal(new WheelAction(WheelEffect.Command, CommandCatalog.JumpBackVeryShort), Turn(new PlayerSettings { Wheel = WheelChoice.Seek }, forward: false));
        Assert.Equal(new WheelAction(WheelEffect.Nothing), Turn(new PlayerSettings { Wheel = WheelChoice.None }));
        Assert.Throws<ArgumentNullException>(() => WheelGestures.Interpret(null!, false, false, false, true, false));
    }

    [Theory]
    [InlineData(120, 1.25)]
    [InlineData(-120, 0.8)]
    [InlineData(240, 1.5625)]
    [InlineData(12, 1.0225651)]
    [InlineData(0, 1)]
    public void ATouchpadsSmallStepsZoomByTheirShareOfANotch(int delta, double factor) =>
        Assert.Equal(factor, WheelGestures.ZoomFactor(delta, 1.25), 6);

    [Fact]
    public void TheCtrlWheelChoiceIsKeptExportedAndMended()
    {
        var settings = new PlayerSettings { CtrlWheel = CtrlWheelChoice.SubtitleSize, WheelPansWhenZoomed = false };
        var imported = ShortcutEditing.Import(new PlayerSettings(), ShortcutEditing.Export(settings));

        Assert.Equal((CtrlWheelChoice.SubtitleSize, false), (imported.CtrlWheel, imported.WheelPansWhenZoomed));
        Assert.Equal(CtrlWheelChoice.Zoom, new PlayerSettings { CtrlWheel = (CtrlWheelChoice)7 }.Normalize().CtrlWheel);
    }
}
