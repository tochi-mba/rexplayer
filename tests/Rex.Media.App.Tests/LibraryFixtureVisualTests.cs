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
    [Fact]
    [Capability("LIB-05")]
    public void EmptyWatchedLibraryShowsARealEmptyState()
    {
        using var scene = new LibraryScene();
        using var app = scene.Ready().Start();
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
        Wait.For(() => app.IsShown("LibraryPane") &&
            app.Find("LibraryShelfCards-Albums").Current.Name == "Albums",
            "the mixed library's album shelf to be available");
        Screenshots.Match(app, "library-home-mixed", "LibraryPane");

        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(720, 640);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2 &&
            app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation",
            "the compact navigation to replace the wide rail");
        Screenshots.Match(app, "library-home-mixed-compact", "LibraryPane");
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
        app.Visit("Songs");
        Wait.For(() => app.Lines().Contains("夜の音楽") && app.Lines().Contains("أغنية هادئة"),
            "both Unicode and right-to-left song names to appear");
        Assert.False(app.Line("أغنية هادئة").Current.IsOffscreen,
            "The RTL entry must be visible to keyboard and UI Automation users.");
        Screenshots.Match(app, "library-songs-unicode-rtl", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
