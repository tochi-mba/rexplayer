using System.IO;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Songs holds only songs; Audiobooks, Podcasts and Recordings hold the rest, each listed only while
/// it has something in it; a book resumes where the book was left; a move lasts through a restart.
/// </summary>
[Collection("desktop")]
public sealed class LibraryListeningTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void SongsHoldsOnlySongsAndEachKindOfListeningHasItsOwnView()
    {
        _scene.WithAsake();
        _scene.Audio(Path.Combine("Audiobooks", "The Long Road", "Part 1.wav"), TimeSpan.FromMinutes(50));
        var second = _scene.Audio(Path.Combine("Audiobooks", "The Long Road", "Part 2.wav"), TimeSpan.FromMinutes(50));
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"), TimeSpan.FromMinutes(30), year: 2023);
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 2.wav"), TimeSpan.FromMinutes(30), year: 2024);
        _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromMinutes(3));
        _scene.Audio(Path.Combine("Downloads", "ding.wav"), TimeSpan.FromSeconds(2));

        // The second part was listened to last, so the book goes on from there.
        using var app = _scene.Ready(played: second).Start();
        app.Visit("Songs");
        Wait.For(() => app.Lines().Order().SequenceEqual(["Lonely At The Top", "Sungba", "Terminator"]), "only the three songs in Songs",
            () => string.Join(", ", app.Lines()));
        Assert.True(app.IsListed("Audiobooks") && app.IsListed("Podcasts") && app.IsListed("Recordings"));

        // The two parts are one book, with one place to resume from.
        app.Visit("Audiobooks");
        Wait.For(() => app.Text("LibrarySubtitle").StartsWith("1 book", StringComparison.Ordinal), "one book", () => string.Join(", ", app.Lines()));
        Assert.All(app.Lines(), line => Assert.Equal("The Long Road", line));
        app.Open("The Long Road");
        Wait.For(() => app.Lines().SequenceEqual(["Part 1", "Part 2"]), "a chapter a part", () => string.Join(", ", app.Lines()));
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Part 2", "the book to go on from its second part");
        if (!app.IsShown("LibraryPane"))
        {
            app.Run(CommandCatalog.ToggleLibrary);
        }

        Wait.For(() => app.IsShown("LibraryPane"), "the library again");

        // Episodes newest first; "Mark all played" leaves none unplayed.
        app.Visit("Podcasts");
        app.Open("Night Talk");
        Wait.For(() => app.Lines().SequenceEqual(["Ep 2", "Ep 1"]), "the episodes newest first", () => string.Join(", ", app.Lines()));
        Assert.Contains("2 unplayed", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Pointer.RightClick(app, app.Line("Ep 2"));
        Wait.For(() => app.IsShown("LibraryMenuMarkAllPlayed"), "the episode's menu");
        app.Press("LibraryMenuMarkAllPlayed");
        Wait.For(() => app.Text("LibrarySubtitle").Contains("0 unplayed", StringComparison.Ordinal), "every episode played");

        // Recordings, with the sounds at their foot.
        app.Visit("Recordings");
        Wait.For(() => app.Lines().SequenceEqual(["Recording 001", "ding"]), "the recording, then the sound", () => string.Join(", ", app.Lines()));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void LocalPodcastShowExplainsAvailabilityAndPlaysUnheardEpisodesOldestFirst()
    {
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"), TimeSpan.FromMinutes(30), year: 2023);
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 2.wav"), TimeSpan.FromMinutes(30), year: 2024);
        _scene.Audio(Path.Combine("Podcasts", "Another Show", "Ep 3.wav"), TimeSpan.FromMinutes(30), year: 2025);
        using var app = _scene.Ready().Start();

        app.Visit("Podcasts");
        app.Open("Night Talk");
        Wait.For(() => app.Lines().SequenceEqual(["Ep 2", "Ep 1"]),
            "episodes within the chosen local show");
        Assert.Contains("Local files only", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Pointer.RightClick(app, app.Line("Ep 2"));
        Wait.For(() => app.IsShown("LibraryMenuPlayUnplayed"), "the unplayed queue action");
        app.Press("LibraryMenuPlayUnplayed");
        Wait.For(() => app.Text("NowPlaying") == "Ep 1",
            "the oldest unplayed local episode to start, not an episode from another show");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABookWithAnExactCatalogueMatchHasACreditedSynopsis()
    {
        _scene.Audio(Path.Combine("Audiobooks", "The Hobbit", "Part 1.wav"), TimeSpan.FromMinutes(50),
            artist: "J. R. R. Tolkien", album: "The Hobbit");
        var cache = new Rex.Media.AppCore.Metadata.MetadataCache(Path.Combine(_scene.Root, "metadata"));
        var book = new Rex.Media.AppCore.Metadata.EnrichedBook
        {
            Schema = Rex.Media.AppCore.Metadata.EnrichedBook.CurrentSchema,
            Title = "The Hobbit",
            Author = "J. R. R. Tolkien",
            WorkId = "OL123W",
            Synopsis = "A hobbit travels across Middle-earth.",
        };
        cache.WriteText(Rex.Media.AppCore.Metadata.MetadataFetcher.BookKind,
            Rex.Media.AppCore.Metadata.OpenLibraryBooks.BookKey(book.Title, book.Author), book.ToJson());

        using var app = _scene.Ready(settings => settings with
        {
            Metadata = new Rex.Media.Settings.MetadataSettings { Music = true },
        }).Start();
        app.Visit("Audiobooks");
        app.Open("The Hobbit");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibraryOverview") == "A hobbit travels across Middle-earth.",
            "a trusted description under the audiobook title");
        Assert.Equal("Description from Open Library", app.Text("LibraryCredit"));
        Assert.True(app.IsShown("LibraryPlay"));

        // The separate Authors sidebar groups books by author and opens a real author-to-book route.
        app.Visit("Authors");
        Wait.For(() => app.Text("LibraryTitle") == "Authors" && app.Lines().Contains("J. R. R. Tolkien"),
            "the Authors shelf to list the credited author");
        ((System.Windows.Automation.ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue("Tolkien");
        Wait.For(() => app.Text("LibraryKicker") == "SEARCH" && app.Lines().SequenceEqual(["J. R. R. Tolkien"]),
            "author search to keep the semantic author result instead of turning it into an artist");
        app.Open("J. R. R. Tolkien");
        Wait.For(() => app.Text("LibraryKicker") == "AUTHOR" && app.Text("LibraryTitle") == "J. R. R. Tolkien",
            "the author page");
        Assert.Contains("1 book", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Assert.Contains("The Hobbit", app.Lines());
        app.Open("The Hobbit");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibraryOverview") == "A hobbit travels across Middle-earth.",
            "the author's book page with its synopsis");
        Assert.True(app.IsShown("LibraryPlay"));
        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryKicker") == "AUTHOR"
            && app.Text("LibraryTitle") == "J. R. R. Tolkien", "back to the author");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMoveMakesAViewAppearEmptyingItGoesHomeAndTheMoveOutlastsARestart()
    {
        using (var app = _scene.WithAsake().Ready().Start())
        {
            Assert.False(app.IsListed("Recordings"), "A library with no recordings lists no Recordings view.");
            Assert.False(app.IsListed("Audiobooks"));
            Assert.False(app.IsListed("Podcasts"));

            app.Visit("Songs");
            Pointer.RightClick(app, app.Line("Sungba"));
            Wait.For(() => app.IsShown("LibraryMenuMoveToRecordings"), "the song's menu");
            Assert.False(app.IsShown("LibraryMenuMoveToMusic"), "Only the moves that move something are offered.");
            app.Press("LibraryMenuMoveToRecordings");
            Wait.For(() => !app.Lines().Contains("Sungba") && app.Lines().Length == 2, "the song to leave Songs", () => string.Join(", ", app.Lines()));
            Wait.For(() => app.IsListed("Recordings"), "the Recordings view to appear at once");

            // Moving its last file out empties the view: the library goes Home and says why.
            app.Visit("Recordings");
            Pointer.RightClick(app, app.Line("Sungba"));
            Wait.For(() => app.IsShown("LibraryMenuMoveToMusic"), "the recording's menu");
            app.Press("LibraryMenuMoveToMusic");
            Wait.For(() => app.Text("LibraryTitle") == "Home" && !app.IsListed("Recordings"), "the library back Home without Recordings");
            Assert.Contains("Nothing is left in Recordings", app.Text("Osd"), StringComparison.Ordinal);

            app.Visit("Songs");
            Pointer.RightClick(app, app.Line("Terminator"));
            Wait.For(() => app.IsShown("LibraryMenuMoveToAudiobooks"), "the song's menu");
            app.Press("LibraryMenuMoveToAudiobooks");
            Wait.For(() => app.IsListed("Audiobooks"), "the Audiobooks view to appear");
            Assert.Equal(0, app.Close());
        }

        // The move is kept with the library, so it holds after a restart.
        using var again = AppProcess.Start(root: _scene.Root);
        again.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => again.IsShown("LibraryPane") && again.Text("LibraryTitle") == "Songs", "the library back on Songs");
        Wait.For(() => again.Lines().Order().SequenceEqual(["Lonely At The Top", "Sungba"]), "Songs without the moved song", () => string.Join(", ", again.Lines()));
        again.Visit("Audiobooks");
        Wait.For(() => again.Lines().Contains("Work of Art"), "the moved song as a book under its album", () => string.Join(", ", again.Lines()));
        Assert.Equal(0, again.Close());
    }
}
