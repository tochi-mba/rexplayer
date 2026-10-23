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
    public void AShortSoundHasAnExplicitSingleFileReplayAction()
    {
        _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromMinutes(3));
        _scene.Audio(Path.Combine("Downloads", "ding.wav"), TimeSpan.FromSeconds(6));
        using var app = _scene.Ready().Start();

        app.Visit("Recordings");
        Wait.For(() => app.Lines().Contains("ding") && app.Lines().Contains("Recording 001"),
            "the recording and the short sound to be separate");
        Pointer.RightClick(app, app.Line("ding"));
        Wait.For(() => app.IsShown("LibraryMenuReplaySound"), "the sound's own replay action");
        app.Press("LibraryMenuReplaySound");
        Wait.For(() => app.Text("NowPlaying") == "ding", "only the chosen sound to start");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ARecordingOffersASeparateKeyboardReachableWaveformReview()
    {
        _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromSeconds(5));
        using var app = _scene.Ready().Start();
        app.Visit("Recordings");
        Wait.For(() => app.Lines().Contains("Recording 001"), "the local recording to be listed");
        Pointer.RightClick(app, app.Line("Recording 001"));
        Wait.For(() => app.IsShown("LibraryMenuWaveform"), "the waveform review menu action");
        app.Press("LibraryMenuWaveform");
        Wait.For(() => app.IsShown("RecordingWaveformStatus")
            && app.IsShown("RecordingWaveformZoom"), "waveform status and accessible zoom");
        Assert.Contains("waveform", app.Text("RecordingWaveformStatus"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void AShortSoundHasCompactReplayRepeatVolumeAndDurationControls()
    {
        _scene.Audio(Path.Combine("Downloads", "ding.wav"), TimeSpan.FromSeconds(6));
        using var app = _scene.Ready().Start();
        app.Visit("Recordings");
        Wait.For(() => app.Lines().Contains("ding"), "a local quick sound to be listed");
        Pointer.RightClick(app, app.Line("ding"));
        Wait.For(() => app.IsShown("LibraryMenuSoundControls"), "the compact sound controls menu action");
        app.Press("LibraryMenuSoundControls");
        Wait.For(() => app.IsShown("SoundReplay") && app.IsShown("SoundRepeat") &&
            app.IsShown("SoundVolume") && app.IsShown("SoundDuration"),
            "keyboard-reachable replay, scoped repeat, volume and duration");
        Assert.Contains("Duration", app.Text("SoundDuration"), StringComparison.Ordinal);
        app.Press("SoundReplay");
        Wait.For(() => app.Text("NowPlaying") == "ding", "quick replay to start the chosen sound");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ARecordingTranscriptOpensFromTheLibraryWithoutAutoplay()
    {
        var media = _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromSeconds(12));
        var cache = new Rex.Media.AppCore.Captions.TranscriptCache(
            Rex.Media.AppCore.Captions.TranscriptCache.DefaultRoot(_scene.Root));
        cache.Save(media, new Rex.Media.AppCore.Captions.Transcript("en", "base.en",
            [new Rex.Media.AppCore.Captions.TranscriptWord("review.", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))]));
        using var app = _scene.Ready().Start();

        app.Visit("Recordings");
        Pointer.RightClick(app, app.Line("Recording 001"));
        Wait.For(() => app.IsShown("LibraryMenuReadTranscript"), "the local recording transcript action");
        app.Press("LibraryMenuReadTranscript");
        Wait.For(() => app.IsShown("TranscriptReaderCue-0") &&
            app.Text("TranscriptReaderCoverage").Contains("Saved transcript", StringComparison.Ordinal),
            "saved speech to be readable without first starting playback");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void LocalPodcastNotesRemainPlainSelectableTextWithoutExecutingMarkup()
    {
        var episode = _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"),
            TimeSpan.FromMinutes(3), year: 2024);
        File.WriteAllText(Path.ChangeExtension(episode, ".notes.md"),
            "Chapter one\n<script>alert('not executed')</script>\nhttps://example.invalid/");
        using var app = _scene.Ready().Start();
        app.Visit("Podcasts");
        app.Open("Night Talk");
        Pointer.RightClick(app, app.Line("Ep 1"));
        Wait.For(() => app.IsShown("LibraryMenuPodcastNotes"), "local notes menu for a podcast episode");
        app.Press("LibraryMenuPodcastNotes");
        var notes = (System.Windows.Automation.ValuePattern)app.Find("PodcastNotesText")
            .GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
        Assert.Contains("<script>", notes.Current.Value, StringComparison.Ordinal);
        Assert.Contains("Chapter one", notes.Current.Value, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void RecordingDetailsKeepTheFilePathAndSeparateDeleteSemanticsVisible()
    {
        _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromMinutes(3));
        using var app = _scene.Ready().Start();
        app.Visit("Recordings");
        Pointer.RightClick(app, app.Line("Recording 001"));
        Wait.For(() => app.IsShown("LibraryMenuRecordingDetails")
            && app.IsShown("LibraryMenuRemove") && app.IsShown("LibraryMenuDelete"),
            "separate details, remove-only and Recycle-Bin file actions");
        app.Press("LibraryMenuRecordingDetails");
        Wait.For(() => app.IsShown("RecordingDetailPath") &&
            app.IsShown("RecordingDetailSeek") && app.IsShown("RecordingDetailBookmark"),
            "file location, precise review seek and bookmark controls");
        Assert.Contains("does not delete its file", app.Text("RecordingDetailStatus"), StringComparison.Ordinal);
        Assert.Contains("Duration", app.Text("RecordingDetailDuration"), StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void PodcastTransportGivesSpokenSkipSpeedAndSleepWithoutFeedControls()
    {
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"),
            TimeSpan.FromMinutes(5), year: 2024);
        using var app = _scene.Ready().Start();
        app.Visit("Podcasts");
        app.Open("Night Talk");
        Pointer.RightClick(app, app.Line("Ep 1"));
        Wait.For(() => app.IsShown("LibraryMenuPodcastControls"), "podcast transport menu");
        app.Press("LibraryMenuPodcastControls");
        Wait.For(() => app.IsShown("PodcastControlsPlay") && app.IsShown("PodcastControlsBack")
            && app.IsShown("PodcastControlsForward") && app.IsShown("PodcastControlsSpeed")
            && app.IsShown("PodcastControlsSleep30") && app.IsShown("PodcastControlsSleepEnd"),
            "spoken-word resume skip speed and sleep controls");
        Assert.Contains("no feed or download controls", app.Text("PodcastControlsStatus"),
            StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void HomeHasLocalPodcastShowCardsWithAWorkingSeeAllRoute()
    {
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"), TimeSpan.FromMinutes(20), year: 2024);
        _scene.Audio(Path.Combine("Podcasts", "Morning Review", "Ep 1.wav"), TimeSpan.FromMinutes(20), year: 2025);
        using var app = _scene.Ready().Start();
        app.Visit("Home");
        Wait.For(() => app.IsShown("LibraryShelf-PodcastShows"), "the podcast show shelf on Home");
        app.Press("LibraryShelf-PodcastShows");
        Wait.For(() => app.Text("LibraryTitle") == "Podcasts" &&
            app.Lines().Contains("Night Talk") && app.Lines().Contains("Morning Review"),
            "See all to open show-oriented local podcast navigation");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void LocalPodcastShowDisplaysOnlyItsOwnSafeDescriptionAndCreatorEvidence()
    {
        var episode = _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"),
            TimeSpan.FromMinutes(20), artist: "Host", album: "Night Talk");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(episode)!, "show.notes.txt"),
            "Independent local show notes. <script>literal only</script>");
        using var app = _scene.Ready().Start();
        app.Visit("Podcasts");
        app.Open("Night Talk");
        Wait.For(() => app.Text("LibraryOverview").Contains("Independent local show notes.", StringComparison.Ordinal),
            "the show description from its explicit local sidecar");
        Assert.Contains("<script>", app.Text("LibraryOverview"), StringComparison.Ordinal);
        Assert.Contains("Creator tag:", app.Text("LibraryFacts"), StringComparison.Ordinal);
        Assert.Contains("Local files only", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Pointer.RightClick(app, app.Line("Ep 1"));
        Wait.For(() => app.IsShown("LibraryMenuShowNotes"), "safe full-show local notes action");
        app.Press("LibraryMenuShowNotes");
        var full = (System.Windows.Automation.ValuePattern)app.Find("PodcastShowNotesText")
            .GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
        Assert.Contains("Independent local show notes.", full.Current.Value, StringComparison.Ordinal);
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
