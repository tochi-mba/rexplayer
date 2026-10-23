using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// How the window looks, compared with CI's baselines (see <see cref="Screenshots"/>): the empty
/// window in both themes and Preferences at a normal and a small size. Behaviour is asserted
/// through UI Automation elsewhere; these only catch layout and drawing that goes wrong.
/// </summary>
public sealed partial class AppWindowTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [Capability("UI-13")]
    public void TheEmptyWindowLooksTheSameInEachTheme(string theme)
    {
        using var app = AppStartedAt(1000, 700, theme);
        Wait.For(() => app.IsShown("IdleTitle"), "the empty window's welcome");

        Screenshots.Match(app, "empty-window-" + theme.ToLowerInvariant());
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(1000, 760, "normal")]
    [InlineData(720, 640, "small")]
    [Capability("UI-10")]
    public void PreferencesLooksTheSameAtEachSize(int width, int height, string size)
    {
        using var app = AppStartedAt(width, height, "Light");
        app.Run(CommandCatalog.Preferences);
        Wait.For(() => app.IsShown("PreferencesSection") && app.IsShown("PrimaryButton"), "Preferences to open");

        Screenshots.Match(app, "preferences-" + size);
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("PreferencesSection"), "Preferences to close");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(720, 540, "compact")]
    [InlineData(420, 340, "minimum")]
    [Capability("UI-10")]
    public void TheMediaStageAndPrimaryTransportFitCompactAndMinimumWindows(int width, int height, string size)
    {
        using var app = AppStartedAt(width, height, "Dark");
        Wait.For(() => app.IsShown("IdleTitle") && app.IsShown("PlayPauseButton"),
            "the stage and transport to be visible");

        var window = app.Window.Current.BoundingRectangle;
        var stage = app.Find("Stage").Current.BoundingRectangle;
        var play = app.Find("PlayPauseButton").Current.BoundingRectangle;
        Assert.True(stage.Width > 0 && stage.Height > 0, "The player stage should retain a usable area.");
        Assert.True(play.Width > 0 && play.Height > 0
            && play.Left >= window.Left && play.Right <= window.Right
            && play.Top >= window.Top && play.Bottom <= window.Bottom,
            "The primary playback action must remain within the resized window.");

        Screenshots.Match(app, "stage-welcome-" + size);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void SwitchingFromAudioToVideoPreservesTransportFocusAndAutomationIdentity()
    {
        using var app = AppProcess.Start([Song(30), RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage",
            "the direct-file audio profile to become active");
        var play = app.Find("PlayPauseButton");
        play.SetFocus();
        Wait.For(() => play.Current.HasKeyboardFocus, "the primary transport to take keyboard focus");

        app.Run(CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "VIDEO playback stage",
            "the stage to adopt the video stream");
        var sameTransport = app.Find("PlayPauseButton");
        Assert.Equal("PlayPauseButton", sameTransport.Current.AutomationId);
        Assert.True(sameTransport.Current.HasKeyboardFocus,
            "Changing a media profile must not steal keyboard focus from the transport.");
        Assert.Equal(0, app.Close());
    }

    /// <summary>The app in a fixed theme, its window at a fixed size, so its shots compare run to run.</summary>
    private static AppProcess AppStartedAt(int width, int height, string theme)
    {
        var app = AppProcess.Start(extraSettings: $"\"theme\": \"{theme}\"");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(width, height);
        Wait.For(() => app.Window.Current.BoundingRectangle is { Width: var w, Height: var h } && Math.Abs(w - width) < 2 && Math.Abs(h - height) < 2,
            $"the window to be {width}x{height}");
        return app;
    }
}
