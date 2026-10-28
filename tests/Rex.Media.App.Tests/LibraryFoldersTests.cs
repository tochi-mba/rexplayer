using System.IO;
using System.Windows.Automation;
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
        File.WriteAllBytes(joha, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000])).Info(("INAM", "Joha"), ("IART", "Asake"), ("IPRD", "Lungu Boy")).Build());
        Wait.For(() => app.Lines().Contains("Joha"), "the new file to appear");
        File.Delete(joha);
        Wait.For(() => !app.Lines().Contains("Joha"), "the deleted file to go");

        // Stop watching: the folder and its media leave, and the library offers folders again.
        app.Visit("Folders");
        app.Choose(_scene.Media);
        app.Press("LibraryRemoveFolder");
        Wait.For(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Stop watching this folder?")) is not null,
            "the folder removal confirmation");
        app.Press("CloseButton");
        Assert.False(app.IsShown("LibraryEmptyAddFolder")); // Cancel leaves the watch and its files intact.
        app.Press("LibraryRemoveFolder");
        app.Press("PrimaryButton");
        Wait.For(() => app.IsShown("LibraryEmptyAddFolder"), "the empty folders view to offer a folder");
        Assert.True(File.Exists(Path.Combine(_scene.Media, "Sungba.wav")));
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
        _scene.Film(Path.Combine("Movies", "Small.Film.2023.mp4"));
        using var app = _scene.Ready().Start();

        // Movies and TV shows open on the streaming-style browse, a billboard over rows.
        app.Visit("TV Shows");
        Wait.For(() => app.IsShown("LibraryBillboard") && app.IsShown("LibraryBillboardPlay"), "the billboard");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-tv-browse", "LibraryPane");

        // As cards, the counts show which videos are films and which are episodes.
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 movie", StringComparison.Ordinal), "only the film among the movies");
        Screenshots.Match(app, "library-movies-single", "LibraryPane");
        app.Visit("TV Shows");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("2 shows", StringComparison.Ordinal), "both shows", () => string.Join(", ", app.Lines()));
        app.Line("Night Desk");

        // A show's page lands on the season to go on with, and the picker moves between seasons.
        app.Open("Harbour Lights");
        Wait.For(() => app.IsShown("LibrarySeasonPicker") && app.Lines().Length == 2, "the show page on its first season",
            () => $"{app.Text("LibraryKicker")} / {app.Text("LibraryTitle")} / {string.Join(", ", app.Lines())}");
        Screenshots.PinViewport(app, 1000, 760);
        Wait.For(() => app.Text("LibraryViewingProgress") == "0 of 2 watched this season · 0 of 3 in the series",
            "season and series completion to remain independent and accessible");
        Screenshots.Match(app, "library-show-page", "LibraryPane");
        var picker = app.Find("LibrarySeasonPicker");
        ((ExpandCollapsePattern)picker.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var second = Wait.Until(() => picker.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Season 2")));
        ((SelectionItemPattern)second.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Wait.For(() => app.Lines().Length == 1, "the second season's one episode", () => string.Join(", ", app.Lines()));
        Wait.For(() => app.Text("LibraryViewingProgress") == "0 of 1 watched this season · 0 of 3 in the series",
            "the changed season to update without claiming the series is complete");
        Screenshots.Match(app, "library-show-season-two", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ASpecialBesideASeasonFolderJoinsItsShow()
    {
        _scene.Video(Path.Combine("Futurama", "Season 7", "Futurama S07E01 The Bots and the Bees.mp4"));
        _scene.Video(Path.Combine("Futurama", "Season 7", "Futurama S07E02 A Farewell to Arms.mp4"));
        _scene.Video(Path.Combine("Futurama", "Futurama S07X01 Simpsorama.mp4"));
        using var app = _scene.Ready().Start();

        // One show, not a second one named after the special's file.
        app.Visit("TV Shows");
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 show", StringComparison.Ordinal), "a single show", () => string.Join(", ", app.Lines()));
        Wait.For(() => app.Lines().SequenceEqual(["Futurama"]),
            "the one show card to finish realizing", () => string.Join(", ", app.Lines()));

        // Season zero is named by what it holds, Specials, in the picker as in the list.
        app.Open("Futurama");
        Wait.For(() => app.IsShown("LibrarySeasonPicker") && app.Lines().Length == 2, "the show page on season 7",
            () => $"{app.Text("LibraryTitle")} / {string.Join(", ", app.Lines())}");
        var picker = app.Find("LibrarySeasonPicker");
        ((ExpandCollapsePattern)picker.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var specials = Wait.Until(() => picker.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Specials")));
        ((SelectionItemPattern)specials.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Wait.For(() => app.Lines() is ["Simpsorama"], "the special in Specials", () => string.Join(", ", app.Lines()));
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-show-specials", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
