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

        app.Visit("Audiobooks");
        app.Filter("LibraryFilterProgress", "Unwatched");
        Wait.For(() => !app.Lines().Contains("A Book"), "a book-wide empty unread filter");
        Screenshots.Match(app, "audiobook-empty-progress-filter", "LibraryList");
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
        app.Visit("Audiobooks");
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
    public void TenThousandChaptersKeepOnlyTheViewportRealized()
    {
        var chapters = Enumerable.Range(0, 10_000).Reverse()
            .Select(i => new Chapter(MediaTime.FromSeconds(i), "Chapter " + i)).ToArray();
        _scene.Audio(Path.Combine("Audiobooks", "Ann Writer", "Huge Book", "Whole.wav"),
            TimeSpan.FromHours(4), album: "Huge Book", author: "Ann Writer",
            chapters: chapters);

        using var app = _scene.Ready().Start();
        app.Visit("Audiobooks");
        app.Open("Huge Book");
        Wait.For(() => app.Text("LibraryKicker") == "AUDIOBOOK"
            && app.Text("LibrarySubtitle").Contains("chapters", StringComparison.Ordinal)
            && app.Lines().Contains("Chapter 0"), "the first virtualized chapters");
        Assert.True(app.Lines().Length < 400,
            "Only a viewport of the 10,000 chapter rows should be realized in WinUI.");
        Screenshots.Match(app, "audiobook-huge-virtualized-chapters", "LibraryList");
        Assert.Equal(0, app.Close());
    }
}
