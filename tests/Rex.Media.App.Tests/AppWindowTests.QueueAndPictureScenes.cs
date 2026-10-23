using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Stable fixture-backed Windows captures for queues and picture browsing. These record real
/// rexplayer.exe pixels; a new scene is not visually accepted until CI's capture is reviewed
/// and committed as a baseline. Functional assertions are independent of the image diff.
/// </summary>
public sealed partial class AppWindowTests
{
    [Theory]
    [InlineData(1060, 760, "Light", "normal-light")]
    [InlineData(780, 620, "Dark", "compact-dark")]
    [Capability("UI-13")]
    public void EmptyQueueScreenshotsHaveAccessibleEmptyStates(int width, int height, string theme, string scene)
    {
        using var app = AppStartedAt(width, height, theme);
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.IsShown("PlaylistPane") && app.Text("PlaylistQueueCount") == "0 items",
            "the empty queue to be visible");
        Assert.Equal("Nothing playing", app.Text("PlaylistQueueNow"));
        Assert.False(app.Find("PlaylistUndoRemoval").Current.IsEnabled);
        Screenshots.Match(app, "queue-empty-" + scene, "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(1060, 760, "Light", "normal-light")]
    [InlineData(780, 620, "Dark", "compact-dark")]
    [Capability("UI-13")]
    public void PopulatedQueueScreenshotsKeepNowNextAndMeasuredDuration(int width, int height, string theme, string scene)
    {
        var song = Song(120);
        var next = Named("Terminator", 120);
        using var app = AppProcess.Start([song, next], extraSettings: $"\"theme\": \"{theme}\"");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(width, height);
        Wait.For(() => app.Text("NowPlaying") == "Sungba" && app.Find("StartOverButton").Current.IsEnabled,
            "the song and its transport controls");

        // Pause and seek to the beginning so the progress ring and transport never
        // advance while the screenshot is captured.
        if (app.Status().State == "Playing")
        {
            app.Run(CommandCatalog.PlayPause);
        }

        app.Press("StartOverButton");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "2 items"
            && app.Text("PlaylistQueueNext") == "Up next: Terminator"
            && app.Text("PlaylistQueueNow") == "Now playing: Sungba",
            "the populated queue summary");
        Screenshots.Match(app, "queue-populated-" + scene, "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void LongQueueAndMissingFileCaptureTheVirtualizedListAndStatus()
    {
        var song = Song(120);
        var playlist = Path.Combine(_media, "long-queue.m3u8");
        File.WriteAllText(playlist, string.Join("\n", Enumerable.Repeat(Path.GetFileName(song), 500)
            .Concat(["file-not-on-disk.mp3"])));
        using var app = AppProcess.Start([playlist], extraSettings: "\"theme\": \"Dark\"");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "501 items", "large queue to load");
        var view = app.Find("PlaylistView");
        var scroll = (ScrollPattern)view.GetCurrentPattern(ScrollPattern.Pattern);
        scroll.SetScrollPercent(ScrollPattern.NoScroll, 100);
        Wait.For(() => view.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "file-not-on-disk. Missing")) is not null,
            "the missing entry at the end of the virtualized queue");
        Screenshots.Match(app, "queue-long-missing-dark", "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData("Grid", "pictures-grid")]
    [InlineData("Collage", "pictures-collage")]
    [Capability("LIB-05")]
    public void PictureLibraryCapturesItsVirtualizedGalleryAndFileLabels(string look, string scene)
    {
        using var fixture = new LibraryScene();
        fixture.Picture("Lake.jpg");
        fixture.Picture("Family.jpg");
        fixture.Picture("Studio.jpg");
        using var app = fixture.Ready().Start();
        app.Visit("Pictures");
        Wait.For(() => app.Text("LibraryTitle") == "Pictures" && app.Text("LibrarySubtitle").StartsWith("3 pictures", StringComparison.Ordinal),
            "the three pictures to appear");
        app.Pick("LibraryLook", look);
        Wait.For(() => app.Text("LibraryLook") == (look == "Grid" ? "Cards" : "Collage"), "the photo layout");
        Screenshots.Match(app, scene + "-light", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
