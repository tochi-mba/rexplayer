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

    // A toggle's accessibility name changes before WinUI has measured and
    // re-templated the ListView items. Capture only a realized rail at its
    // intended width, rather than a stable frame from an unfinished resize.
    private static bool NavigationRendered(AppProcess app, bool compact)
    {
        var sources = app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LibrarySources"));
        var toggle = app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryNavigationToggle"));
        if (sources is null || toggle is null)
        {
            return false;
        }

        try
        {
            var width = sources.Current.BoundingRectangle.Width;
            if (width < (compact ? 55 : 175) || width > (compact ? 75 : 215)
                || toggle.Current.Name != (compact ? "Expand library navigation" : "Collapse library navigation"))
            {
                return false;
            }

            var items = sources.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().Where(item => !item.Current.IsOffscreen)
                .Select(item => item.Current.Name).ToHashSet(StringComparer.Ordinal);
            return items.Contains("Home") && items.Contains("Songs");
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    [Capability("LIB-05")]
    public void EmptyWatchedLibraryShowsARealEmptyState()
    {
        using var scene = new LibraryScene();
        using var app = scene.Ready().Start();
        SetViewport(app, 1000, 760);
        Wait.For(() => app.IsShown("LibraryEmptyText") &&
            app.Text("LibraryEmptyText") == "Add a folder that holds music, videos or pictures, or put some in a folder the library already watches."
            && app.Text("LibraryEmptyHeading") == "Nothing in your library yet",
            "the empty library to finish indexing and explain what is missing");
        Assert.True(app.IsShown("LibraryEmptyAddFolder"), "An empty Home offers a folder to add right there.");
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
            app.Text("LibraryEmptyText").Contains("Reconnect its drive or check its permissions", StringComparison.Ordinal),
            "the inaccessible folder to show a recovery action rather than claiming no media exists");
        Assert.DoesNotContain("There is nothing of this kind", app.Text("LibraryEmptyText"), StringComparison.Ordinal);
        Assert.Equal("Try scanning the library folder again", app.Find("LibraryEmptyRetryScan").Current.Name);
        Assert.Equal("A library folder is out of reach", app.Text("LibraryEmptyHeading"));
        Assert.True(app.IsShown("LibraryEmptyShowFolders"), "The folder list is a second way out.");
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
        Wait.For(() => NavigationRendered(app, compact: true),
            "the compact navigation entries to finish re-templating");
        Screenshots.Match(app, "library-home-mixed-compact", "LibraryPane",
            extraReadiness: () => NavigationRendered(app, compact: true));
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
        Wait.For(() => NavigationRendered(app, compact: false),
            "the temporary 200-pixel rail and its visible navigation entries");
        Screenshots.Match(app, "library-compact-temporary-navigation", "LibraryPane",
            extraReadiness: () => NavigationRendered(app, compact: false));

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

        // The short Home viewport deliberately collapses the hero's subtitle.
        // Do not wait for that hidden UIA element (or an offscreen virtualized
        // shelf): nested app.Text/Find retries otherwise time out. The compact
        // header, real shelf viewport and measured rail must all have settled.
        bool CompactHomeReady()
        {
            var sources = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "LibrarySources"));
            var hero = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryHero"));
            var title = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryTitle"));
            var home = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryHome"));
            if (sources is null || hero is null || title is null || home is null)
            {
                return false;
            }

            try
            {
                return sources.Current.BoundingRectangle.Width is >= 55 and <= 75
                    && hero.Current.Name == "Compact library header"
                    && title.Current.Name == "Home"
                    && !home.Current.IsOffscreen && home.Current.BoundingRectangle.Height > 0;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        if (size == "compact")
        {
            Wait.For(CompactHomeReady, "the settled 720px Home header, shelves and 64px navigation rail");
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
