using System.IO;
using Rex.Media.AppCore.Commands;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Audiobook presentation contracts: accessible book details, contextual actions,
/// progress states and screenshot evidence from realized Windows list items.
/// The library panel remains virtualized even for an unusually large chaptered file.
/// </summary>
[Collection("desktop")]
public sealed class AudiobookPresentationTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void NativeCiHasCommittedPixelBaselinesForEveryAudiobookPresentation()
    {
        // A screenshot without a declared semantic and viewport contract cannot
        // be tested, even if a native PNG was captured on an earlier commit.
        var required = new[]
        {
            "audiobook-unread-card",
            "audiobook-credited-book",
            "audiobook-finished-book",
            "audiobook-empty-progress-filter",
            "audiobook-active-malformed-chapters",
            "audiobook-huge-virtualized-chapters",
        };
        foreach (var name in required)
        {
            var scene = ScreenshotScenes.Find(name);
            Assert.Equal("ci-windows", scene.RequiredLane);
            Assert.Equal("1016x760", scene.Dimensions);
            Assert.Contains(name == "audiobook-empty-progress-filter" ? "LibraryEmpty" : "LibraryList",
                scene.ExpectedAutomationIds);
        }

        if (Screenshots.Lane != "ci-windows")
        {
            // A developer's own fonts and graphics driver are a different
            // screenshot lane; only the Windows CI renderer can set goldens.
            return;
        }

        var missing = required.Where(name => !File.Exists(RepoPaths.Combine(
            "tests", "screenshot-baselines", "ci-windows", name + ".png"))).ToArray();
        Assert.True(missing.Length == 0,
            "The audiobook visual regression gate requires CI-produced pixel baselines: " +
            string.Join(", ", missing) + ". Promote actual captures from the screenshots artifact.");

        // A stale or misleading committed image is not an accepted golden. Keep
        // this gate red until every native capture has been inspected and the
        // corresponding inventory entry explicitly promoted for pixel comparison.
        var unreviewed = required.Where(name => ScreenshotScenes.Find(name).Baseline != "ci-windows").ToArray();
        Assert.True(unreviewed.Length == 0,
            "Audiobook scenes still need native image review and promotion: " +
            string.Join(", ", unreviewed) + ".");
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABookDisplaysTrustedCreditsAndPicturesForUnreadFinishedAndEmptyStates()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "A Book", "Whole.wav"),
            TimeSpan.FromMinutes(90), album: "A Book", author: "Ann Writer",
            narrator: "Lee Reader", chapters:
            [
                new Chapter(MediaTime.Zero, "Opening"),
                new Chapter(MediaTime.FromSeconds(600), "Middle"),
                new Chapter(MediaTime.FromSeconds(1200), "End"),
            ]);

        using var app = _scene.Ready().Start();
        Screenshots.PinViewport(app, 1016, 760);
        app.Visit("Audiobooks");
        Wait.For(() => app.Lines().Contains("A Book"), "the unread audiobook card");
        Assert.Contains("Not started", app.Line("A Book").Current.HelpText, StringComparison.Ordinal);
        Screenshots.Match(app, "audiobook-unread-card", "LibraryList");

        app.Open("A Book");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibrarySubtitle").Contains("Narrated by Lee Reader", StringComparison.Ordinal),
            "the credited book page");
        Assert.Contains("Ann Writer", app.Text("LibrarySubtitle"), StringComparison.Ordinal);
        Wait.For(() => app.Lines().Contains("Opening") && app.Lines().Contains("Middle"),
            "virtualized chapter rows");
        Assert.Contains("0:00", app.Line("Opening").Current.HelpText, StringComparison.Ordinal);
        Screenshots.Match(app, "audiobook-credited-book", "LibraryList");

        Pointer.RightClick(app, app.Line("Opening"));
        Wait.For(() => app.IsShown("LibraryMenuBookFinished"), "marking only the book finished");
        app.Press("LibraryMenuBookFinished");
        Wait.For(() => app.Text("LibrarySubtitle").Contains("Finished", StringComparison.Ordinal),
            "the explicitly finished book");
        Screenshots.Match(app, "audiobook-finished-book", "LibraryList");

        app.Press("LibraryBack");
        Wait.For(() => app.Text("LibraryTitle") == "Audiobooks", "back to the book shelf");
        app.Filter("LibraryFilterProgress", "Unwatched");
        Wait.For(() => !app.Lines().Contains("A Book")
            && app.IsShown("LibraryEmptyText") && app.IsShown("LibraryEmptyClearFilters")
            && app.Text("LibraryEmptyText").Contains("Nothing here matches these filters:", StringComparison.Ordinal),
            "the genuinely empty unread-book filter with its visible recovery action");
        // Capture the actual empty-state panel. A picture of LibraryList can still
        // contain a stale card while that list is being replaced after filtering.
        Screenshots.Match(app, "audiobook-empty-progress-filter", "LibraryEmpty");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void OutOfOrderAndBeyondEndMarkersRemainAccessibleAndDeterministic()
    {
        var path = _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Marker Book", "Whole.wav"),
            TimeSpan.FromMinutes(30), album: "Marker Book", author: "Ann Writer",
            chapters:
            [
                new Chapter(MediaTime.FromSeconds(3600), "Beyond duration"),
                new Chapter(MediaTime.Zero, ""),
                new Chapter(MediaTime.FromSeconds(600), "Middle"),
            ]);
        using var app = _scene.Ready(played: path).Start();
        Screenshots.PinViewport(app, 1016, 760);
        app.Visit("Audiobooks");
        Wait.For(() => app.Lines().Contains("Marker Book"), "the started book card");
        Assert.Contains("0% listened", app.Line("Marker Book").Current.HelpText,
            StringComparison.Ordinal);
        app.Open("Marker Book");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Lines().Contains("Whole") && app.Lines().Contains("Middle"),
            "malformed chapter markers to produce bounded, ordered rows");
        Assert.Contains("Up to here", app.Line("Whole").Current.HelpText, StringComparison.Ordinal);
        Assert.Contains("30:00", app.Line("Beyond duration").Current.HelpText, StringComparison.Ordinal);
        Screenshots.Match(app, "audiobook-active-malformed-chapters", "LibraryList");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void CompletedAudiobookCardsUseBookPlaybackAndTheSidebarUpdatesImmediately()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Complete Me", "Part 1.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Complete Me");
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Complete Me", "Part 2.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Complete Me");

        // Keep the library visible while replay begins so the sidebar's immediate
        // completed-state update is observable; the separate transport tests cover
        // the preference that hides the library when playback starts.
        using var app = _scene.Ready(settings => settings with
        {
            ShowPlayerWhenLibraryMediaStarts = false,
        }).Start();
        Assert.False(app.IsListed("Completed"), "an unplayed library has no Completed view");
        app.Visit("Audiobooks");
        app.Open("Complete Me");
        Pointer.RightClick(app, app.Line("Part 1"));
        Wait.For(() => app.IsShown("LibraryMenuBookFinished"), "the book completion action");
        app.Press("LibraryMenuBookFinished");
        Wait.For(() => app.IsListed("Completed"), "the Completed sidebar to appear immediately");

        app.Visit("Completed");
        Wait.For(() => app.Lines().Contains("Complete Me"), "the finished book's card");
        app.Choose("Complete Me");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Part 1", "completed book to start again");
        Wait.For(() => !app.IsListed("Completed"),
            "replaying the only finished audiobook to clear the Completed sidebar promptly");
        var queue = app.Queue();
        Assert.Equal(2, queue.Length);
        Assert.Contains(queue, item => item.Contains("Part 1", StringComparison.Ordinal));
        Assert.Contains(queue, item => item.Contains("Part 2", StringComparison.Ordinal));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void CompletedBookCardCanBeMarkedUnfinishedWithoutTouchingItsPart()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Undo Me", "Whole.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Undo Me");

        using var app = _scene.Ready().Start();
        app.Visit("Audiobooks");
        app.Open("Undo Me");
        Pointer.RightClick(app, app.Line("Whole"));
        Wait.For(() => app.IsShown("LibraryMenuBookFinished"), "the initial book completion action");
        app.Press("LibraryMenuBookFinished");

        app.Visit("Completed");
        Wait.For(() => app.Lines().Contains("Undo Me"), "the completed book card");
        Pointer.RightClick(app, app.Line("Undo Me"));
        Wait.For(() => app.IsShown("LibraryMenuBookUnfinished"),
            "the book-scoped unfinished action directly on its Completed card");
        app.Press("LibraryMenuBookUnfinished");
        Wait.For(() => !app.IsListed("Completed"),
            "the only completed book to leave the Completed sidebar");

        app.Visit("Audiobooks");
        Wait.For(() => app.Lines().Contains("Undo Me")
            && app.Line("Undo Me").Current.HelpText.Contains("Not started", StringComparison.Ordinal),
            "undoing explicit completion without marking the physical file played");
        Assert.Empty(app.Queue());
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void CompletedSameTitleEditionsReplayOnlyTheExactSelectedBook()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Edition A", "Alpha.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Edition B", "Beta.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");

        using var app = _scene.Ready(settings => settings with
        {
            ShowPlayerWhenLibraryMediaStarts = false,
        }).Start();
        app.Visit("Audiobooks");
        app.Open("Same Title (Edition A)");
        Pointer.RightClick(app, app.Line("Alpha"));
        Wait.For(() => app.IsShown("LibraryMenuBookFinished"), "marking edition A finished");
        app.Press("LibraryMenuBookFinished");
        app.Press("LibraryBack");
        Wait.For(() => app.Lines().Contains("Same Title (Edition B)"), "the other edition card");
        app.Open("Same Title (Edition B)");
        Pointer.RightClick(app, app.Line("Beta"));
        Wait.For(() => app.IsShown("LibraryMenuBookFinished"), "marking edition B finished");
        app.Press("LibraryMenuBookFinished");

        app.Visit("Completed");
        Wait.For(() => app.Lines().Contains("Same Title (Edition A)")
            && app.Lines().Contains("Same Title (Edition B)"),
            "two independently finished editions with identical author and title");
        // Double-click/activation of Completed must not match a book by credits
        // alone, and the resulting playlist must not include the sibling edition.
        app.Open("Same Title (Edition B)");
        Wait.For(() => app.Text("NowPlaying") == "Beta", "replaying only edition B");
        Assert.Single(app.Queue());
        Assert.DoesNotContain(app.Queue(), item => item.Contains("Alpha", StringComparison.Ordinal));
        Wait.For(() => app.Lines().Contains("Same Title (Edition A)")
            && !app.Lines().Contains("Same Title (Edition B)"),
            "only the replayed edition to leave Completed");

        app.Choose("Same Title (Edition A)");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Alpha", "playing the remaining finished edition");
        Assert.Single(app.Queue());
        Assert.DoesNotContain(app.Queue(), item => item.Contains("Beta", StringComparison.Ordinal));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TwoEditionsWithTheSameTitleAreDistinguishableAndNeverQueueEachOther()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Edition A", "Alpha.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Edition B", "Beta.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");

        using var app = _scene.Ready().Start();
        app.Visit("Audiobooks");
        Wait.For(() => app.Lines().Contains("Same Title (Edition A)")
            && app.Lines().Contains("Same Title (Edition B)"),
            "separately accessible edition cards despite identical title and author");
        Assert.Contains("Edition B", app.Line("Same Title (Edition B)").Current.HelpText, StringComparison.Ordinal);
        app.Open("Same Title (Edition B)");
        Wait.For(() => app.Text("LibraryTitle") == "Same Title"
            && app.Lines().SequenceEqual(["Beta"]), "only the chosen edition's chapters");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Beta", "the selected edition to play");
        Assert.DoesNotContain(app.Queue(), item => item.Contains("Alpha", StringComparison.Ordinal));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void MatchingEditionFolderNamesStillHaveDistinctAccessibleBookCards()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Series One", "Shared Edition", "Alpha.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");
        _scene.Audio(Path.Combine("Audiobooks", "Series Two", "Shared Edition", "Beta.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Same Title");

        using var app = _scene.Ready().Start();
        app.Visit("Audiobooks");
        Wait.For(() => app.Lines().Contains("Same Title (Series One / Shared Edition)")
            && app.Lines().Contains("Same Title (Series Two / Shared Edition)"),
            "uniquely announced cards when two editions have matching final directory names");
        app.Open("Same Title (Series Two / Shared Edition)");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Lines().SequenceEqual(["Beta"]), "the selected edition's own chapter only");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void OpeningLibraryAtPlayingMediaReturnsToItsBookAndVirtualizedChapter()
    {
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Returning Book", "Whole.wav"),
            TimeSpan.FromMinutes(50), album: "Returning Book", author: "Ann Writer",
            chapters:
            [
                new Chapter(MediaTime.Zero, "Opening"),
                new Chapter(MediaTime.FromSeconds(5), "Next"),
            ]);
        using var app = _scene.Ready(settings => settings with
        {
            OpenLibraryAtPlayingItem = true,
            ShowPlayerWhenLibraryMediaStarts = true,
        }).Start();
        app.Visit("Audiobooks");
        app.Open("Returning Book");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Whole", "the audiobook to start");

        if (app.IsShown("LibraryPane"))
        {
            app.Run(CommandCatalog.ToggleLibrary);
        }

        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibraryTitle") == "Returning Book"
            && app.Lines().Contains("Opening"), "the active audiobook's chapter view");
        Wait.For(() => app.IsChosen("Opening") || app.IsChosen("Next"),
            "the active virtualized chapter to be revealed");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void SlideshowActionTextAndQueueControlsDoNotLeakIntoAuthorOrBookViews()
    {
        _scene.Picture(Path.Combine("Pictures", "Cover.jpg"));
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Only Book", "Whole.wav"),
            TimeSpan.FromMinutes(50), author: "Ann Writer", album: "Only Book");

        using var app = _scene.Ready().Start();
        app.Visit("Pictures");
        Wait.For(() => app.Text("LibraryPlayText") == "Start slideshow",
            "the gallery's distinctive slideshow button");

        app.Visit("Authors");
        Wait.For(() => app.Text("LibraryPlayText") == "Open author",
            "the author directory to replace the slideshow action");
        Assert.False(app.IsShown("LibraryShuffle"));
        Assert.False(app.IsShown("LibraryEnqueue"));

        app.Visit("Audiobooks");
        Wait.For(() => app.Text("LibraryPlayText") == "Play",
            "the audiobook shelf to restore the ordinary play action");
        Assert.False(app.IsShown("LibraryShuffle"),
            "sequential audiobook parts must never become a shuffled multi-book queue");
        Assert.True(app.IsShown("LibraryEnqueue"));
        app.Press("LibraryPlay");
        Assert.Empty(app.Queue());
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TenThousandChaptersKeepOnlyTheViewportRealized()
    {
        var chapters = Enumerable.Range(0, 10_000).Reverse()
            .Select(i => new Chapter(MediaTime.FromSeconds(i), "Chapter " + i)).ToArray();
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Huge Book", "Part 1.wav"),
            TimeSpan.FromHours(4), album: "Huge Book", author: "Ann Writer",
            chapters: chapters);
        // A second physical part exercises the code path that displays part
        // context for every chapter; it must not decode 10,000 markers per row.
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Huge Book", "Part 2.wav"),
            TimeSpan.FromHours(1), album: "Huge Book", author: "Ann Writer");

        using var app = _scene.Ready().Start();
        Screenshots.PinViewport(app, 1016, 760);
        app.Visit("Audiobooks");
        app.Open("Huge Book");
        // Capture with the screenshot harness first: it waits for the declared UIA
        // scene, records a diagnostic frame if readiness fails, and compares real
        // pixels. A separate pre-capture wait would discard the crucial evidence
        // from an unresponsive 10,000-chapter page.
        Screenshots.Match(app, "audiobook-huge-virtualized-chapters", "LibraryList",
            extraReadiness: () => app.Lines().Contains("Chapter 0"));
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibrarySubtitle").Contains("chapters", StringComparison.Ordinal)
            && app.Lines().Contains("Chapter 0"), "the first virtualized chapters");
        Assert.True(app.Lines().Length < 400,
            "Only a viewport of the 10,000 chapter rows should be realized in WinUI.");
        Assert.Equal(0, app.Close());
    }
}
