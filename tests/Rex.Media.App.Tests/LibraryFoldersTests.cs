using System.IO;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Watched folders as the window shows them: looked through on request, kept up to date as files
/// come and go, given up again; and shows told apart from movies by their season folders.
/// </summary>
[Collection("desktop")]
public sealed class LibraryFoldersTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void AWatchedFolderIsLookedThroughKeptUpToDateAndGivenUp()
    {
        using var app = _scene.WithAsake().Watched().Start();
        app.Visit("Folders");
        Wait.For(() => app.Text("LibrarySubtitle") == "1 folder watched", "the watched folder");
        app.Line(_scene.Media);

        // Look again reads the folder; the status line clears once the scan has finished.
        app.Press("LibraryRescan");
        app.Visit("Songs");
        Wait.For(() => app.Lines().Length == 3, "the scan to find the three songs", () => string.Join(", ", app.Lines()));
        Wait.For(() => app.Text("LibraryStatus").Length == 0, "the scan status to clear");

        // A file copied in arrives by itself, and one deleted goes.
        var joha = Path.Combine(_scene.Media, "Joha.wav");
        File.WriteAllBytes(joha, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000])).Build());
        Wait.For(() => app.Lines().Contains("Joha"), "the new file to appear");
        File.Delete(joha);
        Wait.For(() => !app.Lines().Contains("Joha"), "the deleted file to go");

        // Stop watching: the folder and its media leave, and the library offers folders again.
        app.Visit("Folders");
        app.Choose(_scene.Media);
        app.Press("LibraryRemoveFolder");
        Wait.For(() => app.IsShown("LibraryEmptyAddFolder"), "the empty folders view to offer a folder");
        app.Visit("Home");
        Wait.For(() => app.IsShown("LibraryEmptyText") && app.Text("LibraryEmptyText").StartsWith("The library shows the music", StringComparison.Ordinal),
            "the welcome on an empty home");
        Assert.True(app.IsShown("LibraryEmptyAddFolder"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void SeasonFoldersMakeShowsWhereverTheyLiveAndMoviesStayApart()
    {
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Arrival.mp4"));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Departure.mp4"));
        _scene.Video(Path.Combine("Harbour Lights", "Season 2", "Return.mp4"));
        _scene.Video(Path.Combine("Movies", "Night Desk", "Season 1", "Opening.mp4"));
        _scene.Video(Path.Combine("Movies", "Small.Film.2023.mp4"));
        using var app = _scene.Ready().Start();

        app.Visit("Movies");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 movie", StringComparison.Ordinal), "only the film among the movies");
        app.Visit("TV Shows");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("2 shows", StringComparison.Ordinal), "both shows", () => string.Join(", ", app.Lines()));
        app.Line("Night Desk");

        app.Open("Harbour Lights");
        Wait.For(() => app.Text("LibraryKicker") == "TV SHOW" && app.Text("LibrarySubtitle") == "2 seasons", "the show's seasons");
        app.Open("Season 1");
        Wait.For(() => app.Text("LibraryKicker") == "SEASON" && app.Text("LibrarySubtitle") == "2 episodes", "the first season");

        // An episode's menu offers the season from there as well as the one episode.
        var first = app.Lines()[0];
        Pointer.RightClick(app, app.Line(first));
        Wait.For(() => app.IsShown("LibraryMenuPlay"), "the episode's menu");
        Assert.Equal("Play season 1 from here (2 episodes)", app.Text("LibraryMenuPlay"));
        Pointer.Key(app, app.Find("LibraryMenuPlay"), Pointer.Escape);

        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "TV SHOW", "Back to the show");
        Assert.Equal(0, app.Close());
    }
}
