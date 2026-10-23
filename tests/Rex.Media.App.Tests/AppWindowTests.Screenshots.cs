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

    [Fact]
    [Capability("UI-10")]
    public void SearchAndAdvancedPreferencesHaveVisualSmokeCaptures()
    {
        using var app = AppStartedAt(1000, 760, "Light");
        app.Run(CommandCatalog.Preferences);
        var search = (ValuePattern)app.Find("PreferencesCategorySearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("Artwork detail (%)");
        Wait.For(() => !app.Find("PreferencesArtworkDetails").Current.IsOffscreen, "artwork search result");
        Screenshots.Match(app, "preferences-search-artwork");

        var details = (ExpandCollapsePattern)app.Find("PreferencesArtworkDetails")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern);
        details.Expand();
        Wait.For(() => details.Current.ExpandCollapseState == ExpandCollapseState.Expanded, "advanced artwork options");
        Screenshots.Match(app, "preferences-advanced-artwork");

        search.SetValue("no matching preference 012345");
        Wait.For(() => app.IsShown("PreferencesSearchEmpty"), "search empty feedback");
        Screenshots.Match(app, "preferences-search-empty");
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void PausedPlaybackHasAVisualSmokeCapture()
    {
        var film = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
        using var app = AppProcess.Start([film], extraSettings: "\"theme\": \"Light\"");
        Wait.For(() => app.Text("NowPlaying") == "Basquiat", "the video to open");
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.IsShown("PlayPauseButton"), "playback controls");
        Screenshots.Match(app, "playback-paused");
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
