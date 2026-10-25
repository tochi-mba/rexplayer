using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The library remembers where the user was (LIB-05): reopening it returns to the page and line
/// left, Back returns to the parent with the opened line chosen, a view's scroll place outlives a
/// restart, and a picture on screen shows no transcript rail.
/// </summary>
[Collection("desktop")]
public sealed class LibraryPlaceTests : IDisposable
{
    private const ushort End = 0x23;
    private readonly LibraryScene _scene = new();

    /// <summary>Enough of Asake's songs that the Songs view scrolls.</summary>
    private static readonly string[] ManySongs =
    [
        "Dull", "Terminator", "Organise", "Peace Be Unto You", "Nzaza", "Dupe", "Joha", "Muse", "Reason", "Ototo",
        "Sunmomi", "Sungba", "Olorun", "Awe", "Sunshine", "Mogbe", "Basquiat", "Amapiano", "Introduction", "Lonely At The Top",
        "Remember", "I Believe", "Yoga", "Great Guy", "Start", "MMS", "Active", "Uhh Yeahh", "Ligali", "Worldwide",
        "Mentally", "Suru", "Wave", "My Heart", "Whine", "Fuji Vibe", "Skating", "Mood", "Omo Ope", "Palazzo",
    ];

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void APictureShowsNoTranscriptRailAndASongShowsItAgain()
    {
        _scene.WithAsake();
        _scene.Picture("still.jpg");
        using var app = _scene.Ready().Start();
        app.Visit("Songs");
        app.Choose("Sungba");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba" && !app.IsShown("LibraryPane"), "the song to play");
        Wait.For(() => app.IsShown("TranscriptRail"), "the transcript rail under a song");

        app.Run(CommandCatalog.ToggleLibrary);
        app.Visit("Pictures");
        app.Choose("still");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying").Contains("still", StringComparison.OrdinalIgnoreCase) && !app.IsShown("LibraryPane"), "the picture to show");
        Wait.For(() => !app.IsShown("TranscriptRail"), "the transcript rail to go with a picture on screen");
        Assert.False(app.IsShown("CaptionBufferOverlay"), "A picture has no captions to wait for.");

        app.Run(CommandCatalog.ToggleLibrary);
        app.Visit("Songs");
        app.Choose("Terminator");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Terminator" && !app.IsShown("LibraryPane"), "the next song to play");
        Wait.For(() => app.IsShown("TranscriptRail"), "the transcript rail back under a song");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Capability("LIB-05")]
    public void ReopeningTheLibraryReturnsToTheAlbumPageOrToThePlayingSongAsPreferred(bool atPlayingItem)
    {
        using var app = _scene.WithAsake().Ready(settings => settings with { OpenLibraryAtPlayingItem = atPlayingItem }).Start();
        app.Visit("Artists");
        app.Open("Asake");
        Wait.For(() => app.Text("LibraryTitle") == "Asake", "the artist page");
        app.Open("Lungu Boy");
        Wait.For(() => app.Text("LibraryTitle") == "Lungu Boy", "the album from the artist page");
        app.Choose("Lonely At The Top");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top" && !app.IsShown("LibraryPane"), "the song to play and the player to show");

        app.Run(CommandCatalog.ToggleLibrary);
        if (atPlayingItem)
        {
            Wait.For(() => app.IsShown("LibraryPane") && app.Text("LibraryTitle") == "Songs", "the library at the playing song's view");
        }
        else
        {
            Wait.For(() => app.IsShown("LibraryPane") && app.Text("LibraryTitle") == "Lungu Boy", "the library back on the album page");
            Assert.True(app.IsShown("LibraryBack"), "The album page is still a page under the artist.");
        }

        Wait.For(() => app.IsChosen("Lonely At The Top"), "the song chosen where the library reopened");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void BackReturnsToTheParentWithTheOpenedLineChosen()
    {
        using var app = _scene.WithAsake().Ready().Start();
        app.Visit("Albums");
        app.Open("Work of Art");
        Wait.For(() => app.Text("LibraryTitle") == "Work of Art", "the album");
        app.Line("Terminator");

        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "Albums", "the albums again");
        Wait.For(() => app.IsChosen("Work of Art"), "the album that was opened to be chosen");
        Assert.False(app.IsChosen("Lungu Boy"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void BackReturnsToTheExactSearchThatOpenedAGroup()
    {
        _scene.WithAsake();
        _scene.Song("Calm Down", "Rave & Roses", 2022, "Afrobeats", "Rema");
        using var app = _scene.Ready().Start();
        app.Visit("Artists");
        ((System.Windows.Automation.ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue("asa");
        Wait.For(() => app.Text("LibraryTitle") == "asa", "the filtered artists");
        app.Open("Asake");
        Wait.For(() => app.Text("LibraryTitle") == "Asake", "the artist page");

        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "asa", "the same filtered artists");
        Assert.Equal("asa", ((System.Windows.Automation.ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).Current.Value);
        Wait.For(() => app.IsChosen("Asake"), "the artist that opened the page to be chosen");
        Assert.DoesNotContain("Rema", app.Lines());
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void AViewsScrollPlaceOutlivesASearchAndARestart()
    {
        foreach (var title in ManySongs)
        {
            _scene.Song(title, "Lungu Boy", 2024, "Afrobeats");
        }

        _scene.Ready();
        string first, last;
        using (var app = _scene.Start())
        {
            app.Visit("Songs");
            first = app.Lines().First();
            Pointer.Key(app, app.Line(first), End);
            Wait.For(() => app.Lines().Length > 0 && !app.Lines().Contains(first), "the list to scroll to its end");
            last = app.Lines().Last();
            app.Choose(last);

            // A search narrows the view; clearing it returns to the place before it.
            ((System.Windows.Automation.ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue("sungba");
            Wait.For(() => app.Text("LibraryTitle") == "sungba", "the search");
            ((System.Windows.Automation.ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue("");
            Wait.For(() => app.Text("LibraryTitle") == "Songs", "the songs again");
            Wait.For(() => app.IsChosen(last) && !app.Lines().Contains(first), "the place before the search");
            Assert.Equal(0, app.Close());
        }

        using (var again = AppProcess.Start(root: _scene.Root))
        {
            again.Run(CommandCatalog.ToggleLibrary);
            Wait.For(() => again.IsShown("LibraryPane") && again.Text("LibraryTitle") == "Songs", "the library where it was left");
            Wait.For(() => again.IsChosen(last) && !again.Lines().Contains(first), "the same line chosen at the same scroll place");
            Assert.Equal(0, again.Close());
        }
    }
}
