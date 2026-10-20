using System.IO;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Starting media from the library: the queue it builds, what a click or a right-click acts on,
/// and the Preferences that decide where the library opens and what shows once media starts.
/// </summary>
[Collection("desktop")]
public sealed class LibraryPlayingTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private string SongPath(string title) => Path.Combine(_scene.Media, title + ".wav");

    [Fact]
    [Capability("LIB-05")]
    public void PlayingFromAFilteredSortedViewQueuesWhatTheViewShowsInItsOrder()
    {
        using var app = _scene.WithAsake().Ready(played: [SongPath("Lonely At The Top"), SongPath("Terminator")]).Start();
        app.Visit("Songs");
        app.Filter("LibraryFilterProgress", "Finished");
        app.Pick("LibrarySort", "Title");
        app.Pick("LibrarySort", "Reverse");
        Wait.For(() => app.Lines().SequenceEqual(["Terminator", "Lonely At The Top"]), "the played songs, reversed", () => string.Join(", ", app.Lines()));

        app.Choose("Lonely At The Top");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the chosen song to play");
        Wait.For(() => !app.IsShown("LibraryPane"), "the player to show");
        var queue = app.Queue();
        Assert.Equal(2, queue.Length);
        Assert.Contains("Terminator", queue[0], StringComparison.Ordinal);
        Assert.Contains("Lonely At The Top", queue[1], StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ARightClickActsOnTheLineUnderThePointerNotAnOlderChoice()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Songs");
        app.Choose("Lonely At The Top");
        app.Choose("Terminator", alone: false);
        Wait.For(() => app.IsChosen("Lonely At The Top") && app.IsChosen("Terminator"), "two songs chosen");

        Pointer.RightClick(app, app.Line("Sungba"));
        Wait.For(() => app.IsShown("LibraryMenuPlayNext"), "the line's menu");
        Assert.Equal("Play next", app.Text("LibraryMenuPlayNext"));
        Assert.True(app.IsChosen("Sungba"));
        Assert.False(app.IsChosen("Terminator"), "The right-click chooses the line under the pointer alone.");

        // Activate this item's action to close the flyout deterministically; synthesized
        // Escape is unreliable for WinUI's separate popup window on CI desktops.
        app.Press("LibraryMenuPlayNext");
        Wait.For(() => !app.IsShown("LibraryMenuPlayNext"), "the menu to close");
        // Right-clicking a line already among the chosen keeps them all.
        app.Choose("Terminator", alone: false);
        Pointer.RightClick(app, app.Line("Terminator"));
        Wait.For(() => app.IsShown("LibraryMenuEnqueue"), "the menu for both lines");
        Assert.Equal("Add 2 items to queue", app.Text("LibraryMenuEnqueue"));

        // The menu makes a playlist of what it acts on.
        app.Press("LibraryMenuAddTo");
        app.Press("LibraryMenuAddToNew");
        ((System.Windows.Automation.ValuePattern)app.Find("PlaylistName").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue("Asake");
        app.Press("PrimaryButton");
        app.Visit("Playlists");
        app.Line("Asake");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(MediaOpenClick.DoubleClick)]
    [InlineData(MediaOpenClick.SingleClick)]
    public void ALineOpensOnTheClickPreferencesChoose(MediaOpenClick mode)
    {
        using var app = _scene.WithAsake().Ready(settings => settings with { MediaOpenClickMode = mode }).Start();
        app.Visit("Songs");

        Pointer.Click(app, app.Line("Terminator"));
        if (mode == MediaOpenClick.DoubleClick)
        {
            // One click only chooses the line; opening takes two.
            Wait.For(() => app.IsChosen("Terminator"), "a click to choose the line");
            Assert.NotEqual("Terminator", app.Text("NowPlaying"));
            Pointer.DoubleClick(app, app.Line("Terminator"));
        }

        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the song to open");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheLibraryOpensAtThePlayingItemAndShowsThePlayerByDefault()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Songs");
        app.Choose("Sungba");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba" && !app.IsShown("LibraryPane"), "the player to show the song");

        app.Run(CommandCatalog.ToggleLibrary);
        app.Visit("Albums");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => !app.IsShown("LibraryPane"), "the library to close");

        // Full screen is left when the library opens, which then shows the playing song.
        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "full screen");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane") && app.IsShown("Menu"), "the library out of full screen");
        Wait.For(() => app.Text("LibraryTitle") == "Songs", "the library at the playing song");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheOtherChoicesKeepBrowsingReturnToThePreviousPlaceAndStayInFullScreen()
    {
        using var app = _scene.WithAsake().Ready(settings => settings with
        {
            OpenLibraryAtPlayingItem = false,
            ShowPlayerWhenLibraryMediaStarts = false,
            LeaveFullScreenWhenLibraryOpens = false,
        }).Start();
        app.Visit("Songs");
        app.Choose("Sungba");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song to play");
        Assert.True(app.IsShown("LibraryPane"), "Audio keeps the library open for browsing.");

        app.Visit("Albums");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => !app.IsShown("LibraryPane"), "the library to close");
        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "full screen");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane"), "the library in full screen");
        Assert.False(app.IsShown("Menu"), "The library opened without leaving full screen.");
        Wait.For(() => app.Text("LibraryTitle") == "Albums", "the library where it was left");
        Assert.Equal(0, app.Close());
    }
}
