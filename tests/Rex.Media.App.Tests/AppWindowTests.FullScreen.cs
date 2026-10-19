using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// What the window does out of the way of the picture: full-screen controls that leave before the
/// pointer and stay while they have focus, a minimised window that plays on, and visualisations
/// that settle when the music pauses.
/// </summary>
public sealed partial class AppWindowTests
{
    private const string PointerHidden = "Hid the pointer over the full-screen picture.";

    /// <summary>A scratch data folder whose settings are <paramref name="settings"/>, as someone who has used rexplayer before.</summary>
    private string RootWith(PlayerSettings settings)
    {
        var root = Path.Combine(_media, "data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SettingsStore.Save(Path.Combine(root, "settings.json"), settings with
        {
            FirstRunDone = true,
            LastSeenVersion = AppProcess.Version,
            UpdateChecks = UpdateCadence.Off,
        });
        return root;
    }

    /// <summary>Waits for the media clock to run on by <paramref name="seconds"/>, the only fair measure of time passing in the window.</summary>
    private static void PlayOn(AppProcess app, double seconds, string what)
    {
        var from = app.Position;
        Wait.For(() => app.Position >= from + seconds, what);
    }

    private static System.Windows.Rect AreaOf(AppProcess app, string automationId)
    {
        var part = app.Find(automationId).Current.BoundingRectangle;
        var window = app.Window.Current.BoundingRectangle;
        return new System.Windows.Rect(part.Left - window.Left, part.Top - window.Top, part.Width, part.Height);
    }

    /// <summary>Waits until two pictures of <paramref name="area"/> taken one after the other satisfy <paramref name="between"/>.</summary>
    private static void WaitForPictures(AppProcess app, System.Windows.Rect area, Func<double, bool> between, string what)
    {
        var last = WindowPicture.Take(app.Handle);
        var change = 0.0;
        Wait.For(() =>
        {
            var next = WindowPicture.Take(app.Handle);
            change = WindowPicture.Change(last, next, area);
            last = next;
            return between(change);
        }, what, () => $"The last change was {change:0.0000}.");
    }

    [Fact]
    [Capability("UI-04")]
    [Capability("VID-03")]
    public void FullScreenControlsLeaveFirstThenThePointerAndStayWhileFocused()
    {
        using var app = AppProcess.Start([Named("Sungba", 120)], RootWith(new PlayerSettings { ControlsHideSeconds = 0.5 }));
        Wait.For(() => app.Position > 0, "the song to play");

        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "full screen");
        Wait.For(() => !app.IsShown("PlayPauseButton"), "the controls to leave");
        Assert.DoesNotContain(PointerHidden, app.LogText, StringComparison.Ordinal);
        Wait.For(() => app.LogText.Contains(PointerHidden, StringComparison.Ordinal), "the pointer to leave after the controls");

        app.Run(CommandCatalog.LeaveFullScreen);
        Wait.For(() => app.IsShown("Menu") && app.IsShown("PlayPauseButton"), "the menus and controls to come back");

        // A control with focus keeps the bar up for as long as it has it.
        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "full screen again");
        app.Find("PlayPauseButton").SetFocus();
        PlayOn(app, 2, "the song to play on past the hiding time");
        Assert.True(app.IsShown("PlayPauseButton"), "The controls stay while one of them has focus.");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-18")]
    public void AVisualisationSettlesWhenTheMusicPausesAndMovesAgainOnPlay()
    {
        using var app = AppProcess.Start([Music(60)], RootShowing("Halo"));
        Wait.For(() => app.IsShown("Visualizer"), "the visualisation");
        var area = AreaOf(app, "Visualizer");
        WaitForPictures(app, area, change => change > 0.002, "the visualisation to move with the music");

        app.Press("PlayPauseButton");
        Wait.For(() => app.Status().State == "Paused", "the pause");
        WaitForPictures(app, area, change => change == 0, "the visualisation to settle");

        app.Press("PlayPauseButton");
        WaitForPictures(app, area, change => change > 0.002, "the visualisation to move again");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-18")]
    public void AMinimisedWindowPlaysOnAndDrawsAgainWhenRestored()
    {
        using var app = AppProcess.Start([Music(60)], RootShowing("Halo"));
        Wait.For(() => app.IsShown("Visualizer") && app.Position > 0, "the visualisation");
        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);

        window.SetWindowVisualState(WindowVisualState.Minimized);
        Wait.For(() => window.Current.WindowVisualState == WindowVisualState.Minimized, "the window to minimise");
        PlayOn(app, 1.5, "the music to play on while minimised");
        Assert.Equal("Playing", app.Status().State);

        window.SetWindowVisualState(WindowVisualState.Normal);
        Wait.For(() => window.Current.WindowVisualState == WindowVisualState.Normal, "the window to come back");
        WaitForPictures(app, AreaOf(app, "Visualizer"), change => change > 0.002, "the visualisation to draw again");
        PlayOn(app, 0.5, "the music to carry on after restoring");
        Assert.Equal(0, app.Close());
    }
}
