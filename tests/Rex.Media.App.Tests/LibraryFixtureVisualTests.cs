using System.Runtime.InteropServices;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Offline visual fixtures for the real library shell, paired with semantic assertions.
/// A recorded screenshot alone never proves that a fixture was populated correctly.
/// </summary>
[Collection("desktop")]
public sealed partial class LibraryFixtureVisualTests
{
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    private static void SetViewport(AppProcess app, int width, int height)
    {
        ((TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern)).Resize(width, height);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - width) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - height) < 2,
            $"the {width}x{height} window viewport");
    }

    [Fact]
    [Capability("LIB-05")]
    public void EmptyWatchedLibraryShowsARealEmptyState()
    {
        using var scene = new LibraryScene();
        using var app = scene.Ready().Start();
        SetViewport(app, 1000, 760);
        Wait.For(() => app.IsShown("LibraryEmptyText") &&
            app.Text("LibraryEmptyText") == "There is nothing of this kind in the library's folders.",
            "the empty library to finish indexing and explain what is missing");
        Assert.True(app.IsShown("LibraryQuickAddFolder"), "A folder can be added without opening a menu.");
        Screenshots.Match(app, "library-home-empty", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void DisconnectedLibraryFolderShowsRecoveryInsteadOfAFalseEmptyClaim()
    {
        using var scene = new LibraryScene();
        scene.Watched();
        // The folder was connected when it was chosen and is missing at this launch.
        // Do not treat an unsuccessful enumeration as proof that its media is gone.
        System.IO.Directory.Delete(scene.Media, recursive: true);
        using var app = scene.Start();
        SetViewport(app, 1000, 760);
        // Rescan belongs to the Folders route's actions, not the Home
        // route. Drive the exposed native control, then return Home to
        // verify the disconnected-folder recovery state there.
        app.Visit("Folders");
        app.Press("LibraryRescan");
        app.Visit("Home");
        Wait.For(() => app.IsShown("LibraryEmptyText") &&
            app.Text("LibraryEmptyText").Contains("Reconnect its drive or check permissions", StringComparison.Ordinal),
            "the inaccessible folder to show a recovery action rather than claiming no media exists");
        Assert.DoesNotContain("There is nothing of this kind", app.Text("LibraryEmptyText"), StringComparison.Ordinal);
        Assert.Equal("Try scanning the library folder again", app.Find("LibraryEmptyRetryScan").Current.Name);
        Screenshots.Match(app, "library-home-disconnected-folder-error", "LibraryPane");
        app.Press("LibraryEmptyRetryScan");
        Wait.For(() => app.IsShown("LibraryEmptyRetryScan") &&
            app.Text("LibraryEmptyText").Contains("Reconnect its drive", StringComparison.Ordinal),
            "a failed retry to preserve the actionable error without discarding the library");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void OneItemLibraryKeepsTheSongBrowsableWithoutOnlineMetadata()
    {
        using var scene = new LibraryScene();
        scene.Song("Only Song", "Only Album", 2024, "Local");
        using var app = scene.Ready().Start();
        SetViewport(app, 720, 640);
        app.Visit("Songs");
        Wait.For(() => app.Lines().SequenceEqual(["Only Song"]), "the only song in the library");
        Assert.True(app.IsShown("LibrarySearch"));
        Screenshots.Match(app, "library-songs-single", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void MixedOfflineLibraryHasNavigableShelvesInWideAndCompactWindows()
    {
        using var scene = new LibraryScene();
        scene.WithAsake();
        scene.Film("Small.Film.2023.mp4");
        scene.Picture("Lagos.jpg");
        using var app = scene.Ready().Start();
        SetViewport(app, 1000, 760);
        Wait.For(() => app.IsShown("LibraryPane") &&
            app.Find("LibraryShelfCards-Albums").Current.Name == "Albums",
            "the mixed library's album shelf to be available");
        Wait.For(() => app.Find("LibrarySources").Current.BoundingRectangle.Width is >= 175 and <= 215,
            "the 200-pixel wide library rail");
        Screenshots.Match(app, "library-home-mixed", "LibraryPane");

        SetViewport(app, 720, 640);
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation",
            "the compact navigation to replace the wide rail");
        Wait.For(() => app.Find("LibrarySources").Current.BoundingRectangle.Width is >= 55 and <= 75,
            "the 64-pixel icon rail");
        Screenshots.Match(app, "library-home-mixed-compact", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    [Capability("UI-13")]
    public void NarrowNavigationReturnsContentWidthAfterAViewIsChosen()
    {
        using var scene = new LibraryScene();
        scene.WithAsake();
        using var app = scene.Ready().Start();
        SetViewport(app, 720, 640);

        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation",
            "narrow navigation to start as an icon rail");
        app.Press("LibraryNavigationToggle");
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation",
            "the temporary sidebar labels to appear");
        Screenshots.Match(app, "library-compact-temporary-navigation", "LibraryPane");

        app.Visit("Songs");
        Wait.For(() => app.Text("LibraryTitle") == "Songs" &&
            app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation",
            "the chosen Songs route to reclaim its content width");
        Assert.InRange(app.Find("LibrarySources").Current.BoundingRectangle.Width, 55, 75);
        Screenshots.Match(app, "library-compact-route-reclaims-width", "LibraryPane");

        // Temporary expansion must not change the saved desktop preference or the route.
        SetViewport(app, 1000, 760);
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation",
            "desktop navigation to restore its original expanded preference");
        Assert.Equal("Songs", app.Text("LibraryTitle"));
        Screenshots.Match(app, "library-desktop-expanded-navigation-restored", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    /// <summary>
    /// Every source-size breakpoint renders from the same indexed media, rather than
    /// separate image mockups. CI captures need native review before release approval.
    /// </summary>
    [Theory]
    [Capability("UI-13")]
    [InlineData(360, 430, "minimum")]
    [InlineData(720, 640, "compact")]
    [InlineData(1000, 760, "standard")]
    [InlineData(1280, 860, "wide")]
    public void HomeNavigationRemainsReachableAcrossNativeWindowWidths(int width, int height, string size)
    {
        using var scene = new LibraryScene();
        scene.WithAsake();
        scene.Picture("Lagos.jpg");
        using var app = scene.Ready().Start();
        SetViewport(app, width, height);
        Wait.For(() => app.IsShown("LibraryHome") && app.IsShown("LibraryNavigationToggle"),
            "the local Home shelves and library navigation to remain mounted at " + size);
        var sources = app.Find("LibrarySources").Current.BoundingRectangle;
        var pane = app.Find("LibraryPane").Current.BoundingRectangle;
        Assert.True(sources.Width > 0 && pane.Width > sources.Width + 100,
            $"The {size} window needs usable content beside the navigation rail.");
        var compact = width < 760; // LibraryNavigationNarrowBreakpoint, in XAML.
        Assert.Equal(compact ? "Expand library navigation" : "Collapse library navigation",
            app.Find("LibraryNavigationToggle").Current.Name);

        // On a freshly opened 720px window, the navigation toggle can advertise
        // its compact state before the 64px rail and populated Home shelves have
        // finished layout. Both states paint stable frames, but only one is the
        // intended screenshot. Match the same settled state across launches.
        bool CompactHomeReady() =>
            app.Find("LibrarySources").Current.BoundingRectangle.Width is >= 55 and <= 75
            && app.Text("LibrarySubtitle") == "3 songs · 0 videos · 1 picture"
            && app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryShelfCards-Albums"))
                is { } albums && albums.Current.Name == "Albums";

        if (size == "compact")
        {
            Wait.For(CompactHomeReady, "the populated 720px Home shell and 64px navigation rail",
                () => $"Rail: {app.Find("LibrarySources").Current.BoundingRectangle.Width:0.#}px; " +
                    $"subtitle: {app.Text("LibrarySubtitle")}");
        }

        Screenshots.Match(app, "library-home-shell-" + size, "LibraryPane",
            extraReadiness: size == "compact" ? CompactHomeReady : null);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void HighContrastHomeUsesLiveSystemPaletteAndRetainsAccessibleNavigation()
    {
        Assert.SkipWhen(!System.Windows.SystemParameters.HighContrast,
            "Run this fixture on a Windows runner configured for high contrast; do not silently fake the OS palette.");
        using var scene = new LibraryScene();
        scene.WithAsake();
        using var app = scene.Ready().Start();
        SetViewport(app, 1000, 760);
        Wait.For(() => app.IsShown("LibraryNavigationToggle") && app.IsShown("LibraryHome"),
            "the real high-contrast shell to expose Home and native navigation");
        Assert.False(app.Find("LibraryNavigationToggle").Current.IsOffscreen);
        Screenshots.Match(app, "library-home-system-high-contrast", "LibraryPane");
        app.Visit("Movies");
        Wait.For(() => app.IsShown("LibraryEmptyText"), "the movie empty state in system high contrast");
        Screenshots.Match(app, "library-movies-empty-high-contrast", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void HighDpiHomeKeepsNavigationReachableWithoutASecondLayout()
    {
        using var scene = new LibraryScene();
        scene.WithAsake();
        using var app = scene.Ready().Start();
        var dpi = GetDpiForWindow(app.Handle);
        Assert.SkipWhen(dpi < 120,
            "Run on a genuinely scaled 125%-or-higher Windows display; resizing alone does not create high-DPI evidence.");
        SetViewport(app, 1000, 760);
        Wait.For(() => app.IsShown("LibraryNavigationToggle") && app.IsShown("LibraryHome"),
            "the per-monitor-DPI native Home shell to render");
        Assert.False(app.Find("LibraryNavigationToggle").Current.IsOffscreen);
        var pane = app.Find("LibraryPane").Current.BoundingRectangle;
        Assert.True(pane.Width > 320 && pane.Height > 240,
            "The scaled native Home surface must remain usable.");
        Screenshots.Match(app, "library-home-high-dpi", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void UnicodeAndRtlNamesSurviveOfflineIndexingAndRemainFocusable()
    {
        using var scene = new LibraryScene();
        scene.Song("夜の音楽", "青い空", 2024, "Local", artist: "山田");
        scene.Song("أغنية هادئة", "رحلة", 2024, "Local", artist: "ليلى");
        using var app = scene.Ready().Start();
        SetViewport(app, 1000, 760);
        app.Visit("Songs");
        Wait.For(() => app.Lines().Contains("夜の音楽") && app.Lines().Contains("أغنية هادئة"),
            "both Unicode and right-to-left song names to appear");
        Assert.False(app.Line("أغنية هادئة").Current.IsOffscreen,
            "The RTL entry must be visible to keyboard and UI Automation users.");
        Screenshots.Match(app, "library-songs-unicode-rtl", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
