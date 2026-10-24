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
    [Fact]
    [Capability("UI-13")]
    public void QueueHighContrastCaptureUsesTheRealOperatingSystemPalette()
    {
        if (!System.Windows.SystemParameters.HighContrast)
        {
            Assert.Skip("Run the Windows UI screenshot lane with High Contrast enabled to capture this state.");
        }

        using var app = AppStartedAt(1000, 700, "Dark");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.IsShown("PlaylistPane"), "the queue to display in High Contrast");
        Screenshots.Match(app, "queue-empty-high-contrast", "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void QueueRtlLocaleCaptureUsesTheRealWindowsUiLanguage()
    {
        if (!System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
        {
            Assert.Skip("Run the Windows UI screenshot lane in an RTL UI language to capture this state.");
        }

        using var app = AppStartedAt(1000, 700, "Light");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.IsShown("PlaylistPane"), "the queue to display in the RTL UI language");
        Screenshots.Match(app, "queue-empty-rtl-locale", "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(1000, 760, "Light", "normal-light")]
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
    [InlineData(1000, 760, "Light", "normal-light")]
    [InlineData(780, 620, "Dark", "compact-dark")]
    [Capability("UI-13")]
    public void PopulatedQueueScreenshotsKeepNowNextAndMeasuredDuration(int width, int height, string theme, string scene)
    {
        var song = Song(120);
        var next = Named("Terminator", 120);
        using var app = AppProcess.Start([song, next], extraSettings: $"\"theme\": \"{theme}\"");
        Screenshots.PinViewport(app, width, height);
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
        Screenshots.PinViewport(app, 1000, 760);
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "501 items", "large queue to load");
        var view = app.Find("PlaylistView");
        var scroll = (ScrollPattern)view.GetCurrentPattern(ScrollPattern.Pattern);
        // Ctrl+End asks the native queue to ScrollIntoView its last data item,
        // rather than navigating only as far as WinUI's currently realized rows.
        Input.Focus(app);
        view.SetFocus();
        Input.Press("Ctrl+End");
        Wait.For(() =>
        {
            var last = view.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, "file-not-on-disk. Missing")));
            return last is not null && !last.Current.IsOffscreen;
        }, "the missing entry at the end of the virtualized queue",
            () => $"Queue scroll: {scroll.Current.VerticalScrollPercent:0.#}%.");
        Assert.Equal("501 items", app.Text("PlaylistQueueCount"));
        Screenshots.Match(app, "queue-long-missing-dark", "PlaylistPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-13")]
    public void PictureLibraryCapturesItsDurableUnreadableMediaState()
    {
        using var fixture = new LibraryScene();
        var image = fixture.Picture("Unusable.jpg");
        fixture.Ready();
        var library = new Rex.Media.Library.MediaLibrary(
            Rex.Media.Library.RexStore.Open(Path.Combine(fixture.Root, "library.log")));
        library.SetReadDetails([(image, null, "The image header is invalid")]);

        using var app = fixture.Start();
        Screenshots.PinViewport(app, 1000, 760);
        app.Visit("Pictures");
        Wait.For(() => app.Find("LibraryList").FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Any(item =>
                item.Current.Name.Contains("Could not be read: The image header is invalid", StringComparison.Ordinal)),
            "the unreadable media warning");
        Screenshots.Match(app, "pictures-unreadable-light", "LibraryPane");
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
        Screenshots.PinViewport(app, 1000, 760);
        app.Visit("Pictures");
        Wait.For(() => app.Text("LibraryTitle") == "Pictures" && app.Text("LibrarySubtitle").StartsWith("3 pictures", StringComparison.Ordinal),
            "the three pictures to appear");
        app.Pick("LibraryLook", look);
        Wait.For(() => app.Text("LibraryLook") == (look == "Grid" ? "Cards" : "Collage"), "the photo layout");
        Screenshots.Match(app, scene + "-light", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
