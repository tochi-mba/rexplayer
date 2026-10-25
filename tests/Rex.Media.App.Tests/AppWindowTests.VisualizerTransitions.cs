using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("AU-18")]
    public void VisualizerSurvivesPauseSeekResizeNextTrackAndMinimizeRestore()
    {
        var first = Music(30);
        var second = Song(30);
        var playlist = Path.Combine(_media, "visualizer-transitions.m3u8");
        File.WriteAllText(playlist,
            Path.GetFileName(first) + Environment.NewLine + Path.GetFileName(second) + Environment.NewLine);
        using var app = AppProcess.Start([playlist], RootShowing("Spectrum"));

        Wait.For(() => app.Text("NowPlaying") == "Terminator" && app.IsShown("Visualizer"),
            "the first local song and its visualizer");
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Play", "the player to pause");
        app.SetValue("SeekBar", 12);
        Wait.For(() => Math.Abs(app.Position - 12) < 1, "paused seeking to update the source position");

        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(720, 640);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2
            && Math.Abs(app.Window.Current.BoundingRectangle.Height - 640) < 2
            && app.IsShown("Visualizer"), "the compact stage to keep its visualizer");

        app.Run(CommandCatalog.Next);
        Wait.For(() => app.Text("NowPlaying") == "Sungba" && app.IsShown("Visualizer"),
            "the next track to keep the same selected visualization");

        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
        window.SetWindowVisualState(WindowVisualState.Minimized);
        Wait.For(() => window.Current.WindowVisualState == WindowVisualState.Minimized,
            "the window to minimize");
        Input.Restore(app);
        Wait.For(() => window.Current.WindowVisualState != WindowVisualState.Minimized
            && app.IsShown("Visualizer"), "the restored window to redraw its visualization");

        Assert.DoesNotContain("could not be drawn", app.LogText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }
}
