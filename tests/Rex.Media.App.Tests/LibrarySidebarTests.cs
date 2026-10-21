using System.IO;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>The library's sidebar lists Home always and every other view only while it has something in it.</summary>
[Collection("desktop")]
public sealed class LibrarySidebarTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void AnEmptyLibraryListsOnlyHomeWithItsWelcome()
    {
        LibraryScene.WriteSettings(_scene.Root);
        using var app = AppProcess.Start(root: _scene.Root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane") && app.Text("LibraryTitle") == "Home", "the library's home");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText").StartsWith("The library shows the music", StringComparison.Ordinal), "the welcome");
        Wait.For(() => app.ListedViews() is ["Home"], "Home alone in the sidebar", () => string.Join(", ", app.ListedViews()));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void SongsListTheMusicViewsAndAVideoArrivingThenGoingBringsAndTakesTheVideoViews()
    {
        using var app = _scene.WithAsake().Ready().Start();
        Wait.For(() => app.ListedViews().SequenceEqual(["Home", "Songs", "Albums", "Artists", "Genres", "Recently added", "Folders"]),
            "Home, the music views, Recently added and Folders", () => string.Join(", ", app.ListedViews()));

        // A film copied in arrives with the views for it once the scan has settled.
        var film = Path.Combine(_scene.Media, "Small.Film.2023.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), film);
        Wait.For(() => app.IsListed("Movies") && app.IsListed("All videos"), "the video views to appear", () => string.Join(", ", app.ListedViews()));
        Assert.False(app.IsListed("TV Shows"), "A film alone makes no show.");
        Assert.False(app.IsListed("Pictures"));

        // Deleting it while Movies is open takes the views away and returns Home with a notice.
        app.Visit("Movies");
        File.Delete(film);
        Wait.For(() => app.Text("LibraryTitle") == "Home" && !app.IsListed("Movies") && !app.IsListed("All videos"), "Home without the video views",
            () => string.Join(", ", app.ListedViews()));
        Assert.Contains("Nothing is left in", app.Text("Osd"), StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }
}
