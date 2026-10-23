using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Offline visual fixtures for the real library shell, paired with semantic assertions.
/// A recorded screenshot alone never proves that a fixture was populated correctly.
/// </summary>
[Collection("desktop")]
public sealed class LibraryFixtureVisualTests
{
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

        app.Visit("Songs");
        Wait.For(() => app.Text("LibraryTitle") == "Songs" &&
            app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation",
            "the chosen Songs route to reclaim its content width");
        Assert.InRange(app.Find("LibrarySources").Current.BoundingRectangle.Width, 55, 75);

        // Temporary expansion must not change the saved desktop preference or the route.
        SetViewport(app, 1000, 760);
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation",
            "desktop navigation to restore its original expanded preference");
        Assert.Equal("Songs", app.Text("LibraryTitle"));
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
