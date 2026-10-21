using System.IO;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Movies holds only the films, Other videos the clips, recordings and music videos, and a person's
/// "This is a movie" moves a video over for good, through a restart.
/// </summary>
[Collection("desktop")]
public sealed class LibraryOtherVideosTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void MoviesHoldsTheFilmsOtherVideosTheRestAndThisIsAMovieMovesOneForGood()
    {
        _scene.Film(Path.Combine("Movies", "Small.Film.2023.mp4"));
        _scene.Film(Path.Combine("Movies", "Harbour Night (2021)", "harbour-night.mp4"));
        _scene.Video(Path.Combine("Phone", "IMG_1234.mp4"), length: TimeSpan.FromMinutes(2));
        _scene.Video("Screen Recording 2024-01-05 at 10.00.00.mp4", length: TimeSpan.FromMinutes(6));
        _scene.Video("Asake - Lonely At The Top.mp4", length: TimeSpan.FromMinutes(3));
        _scene.Video("Lake Weekend.mp4", length: TimeSpan.FromMinutes(95));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Arrival.mp4"), length: TimeSpan.FromMinutes(22));

        // Cards, so each view's lines and count can be read rather than a billboard.
        var cards = new LibraryViewChoice(LibraryLook.Grid, CardSize: 200) { LookChosen = true };
        using (var app = _scene.Ready(settings => settings with
        {
            LibraryViews = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal) { ["Movies"] = cards },
        }).Start())
        {
            // Home offers a shelf of the other videos, since there are some.
            Wait.Until(() => app.Find("LibraryShelfCards-OtherVideos"));

            app.Visit("Movies");
            Wait.For(() => app.Lines() is ["harbour-night", "Small Film"], "only the two films in Movies",
                () => string.Join(", ", app.Lines()));
            Assert.StartsWith("2 movies", app.Text("LibrarySubtitle"), StringComparison.Ordinal);

            app.Visit("Other videos");
            Wait.For(() => app.Text("LibrarySubtitle").StartsWith("4 videos", StringComparison.Ordinal), "the four other videos",
                () => string.Join(", ", app.Lines()));
            Assert.Equal("Cards", app.Text("LibraryLook"));
            Assert.False(app.IsShown("LibraryBillboard"), "Other videos is a plain grid.");
            Assert.Contains("IMG_1234", app.Lines());
            Assert.Contains("Lake Weekend", app.Lines());

            // The episode stays with its show; All videos still lists everything.
            app.Visit("All videos");
            Wait.For(() => app.Text("LibrarySubtitle").StartsWith("7 videos", StringComparison.Ordinal), "every video under All videos");

            app.Visit("Other videos");
            Pointer.RightClick(app, app.Line("Lake Weekend"));
            Wait.For(() => app.IsShown("LibraryMenuIsMovie"), "the video's menu");
            Assert.False(app.IsShown("LibraryMenuNotMovie"), "Only the choice that moves something is offered.");
            app.Press("LibraryMenuIsMovie");
            Wait.For(() => app.Text("LibrarySubtitle").StartsWith("3 videos", StringComparison.Ordinal) && !app.Lines().Contains("Lake Weekend"),
                "the video to leave Other videos", () => string.Join(", ", app.Lines()));
            app.Visit("Movies");
            Wait.For(() => app.Lines().Contains("Lake Weekend"), "the video to join Movies", () => string.Join(", ", app.Lines()));
            Assert.Equal(0, app.Close());
        }

        // The choice is kept with the library, so it holds after a restart.
        // The library opens where it was left: Movies.
        using var again = AppProcess.Start(root: _scene.Root);
        again.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => again.IsShown("LibraryPane") && again.Text("LibraryTitle") == "Movies", "the library back on Movies");
        Wait.For(() => again.Text("LibrarySubtitle").StartsWith("3 movies", StringComparison.Ordinal) && again.Lines().Contains("Lake Weekend"),
            "the choice to hold after a restart", () => string.Join(", ", again.Lines()));
        again.Visit("Other videos");
        Wait.For(() => again.Text("LibrarySubtitle").StartsWith("3 videos", StringComparison.Ordinal), "three other videos after the restart",
            () => string.Join(", ", again.Lines()));
        Assert.Equal(0, again.Close());
    }
}
