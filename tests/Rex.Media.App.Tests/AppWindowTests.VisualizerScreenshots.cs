using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    /// <summary>
    /// Real native visualizer modes at fixed viewport sizes, paused long enough that the
    /// sound-driven animation has settled. Their CI captures still require pixel review
    /// and explicitly approved Windows baselines before being considered release evidence.
    /// </summary>
    [Theory]
    [Capability("AU-18")]
    [InlineData("Spectrum", 1000, 760, "wide")]
    [InlineData("Oscilloscope", 720, 640, "compact")]
    [InlineData("Spectrogram", 1000, 760, "wide")]
    [InlineData("Vinyl", 720, 640, "compact")]
    public void PausedVisualizerModesHaveNativeViewportCaptures(string mode, int width, int height, string size)
    {
        using var app = AppProcess.Start([Music(30)], RootShowing(mode));
        Wait.For(() => app.IsShown("Visualizer"), "the selected visualisation to be visible");
        var viewport = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        viewport.Resize(width, height);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - width) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - height) < 2,
            "the fixed " + size + " visualisation viewport");
        app.Run(CommandCatalog.PlayPause);
        Thread.Sleep(2000); // VisualPacing.Settle is 1.5 s; the paused frame is now stable.
        Assert.True(app.IsShown("Visualizer"));
        Screenshots.Match(app, "visualizer-" + mode.ToLowerInvariant() + "-" + size, "Stage");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(1000, 760, "wide")]
    [InlineData(720, 640, "compact")]
    public void VisualizerSettingsHaveNamedControlsAndNativeViewportCaptures(int width, int height, string size)
    {
        using var app = AppProcess.Start();
        var viewport = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        viewport.Resize(width, height);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - width) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - height) < 2,
            "the settings viewport");
        app.Run(CommandCatalog.VisualizerSettings);
        Wait.For(() => app.IsShown("VisualizerChoice") && app.IsShown("VisualizerReset"),
            "the editable and reversible visualisation settings");
        Assert.Equal("Visualisation", app.Find("VisualizerChoice").Current.Name);
        Assert.Equal("Restore defaults for this visualisation", app.Find("VisualizerReset").Current.Name);
        Screenshots.Match(app, "visualizer-settings-" + size);
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }
}
