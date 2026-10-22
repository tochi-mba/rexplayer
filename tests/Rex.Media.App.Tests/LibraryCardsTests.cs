using System.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Movies and TV shows as they first open, and artwork filling its frame: a letterboxed film's
/// card and episode still show picture where its recorded bars were.
/// </summary>
[Collection("desktop")]
public sealed class LibraryCardsTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void MoviesAndTvShowsOpenOnBrowseUntilAnotherLayoutIsPicked()
    {
        _scene.Film(Path.Combine("Movies", "Small.Film.2023.mp4"));
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Arrival.mp4"));

        // TV shows kept as cards only because their card size changed, as before Browse existed.
        using var app = _scene.Ready(settings => settings with
        {
            LibraryViews = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal) { ["TvShows"] = new(LibraryLook.Grid, CardSize: 260) },
        }).Start();

        app.Visit("Movies");
        Wait.For(() => app.IsShown("LibraryBillboard") && app.Text("LibraryLook") == "Browse", "Movies to open on Browse");
        Assert.True(app.IsShown("LibrarySort"), "Browse keeps sorting available.");
        Assert.True(app.IsShown("LibraryFilter"), "Browse keeps progress, type, folder and download filters available.");
        Assert.Equal("Collapse library navigation", app.Find("LibraryNavigationToggle").Current.Name);
        app.Press("LibraryNavigationToggle");
        Assert.Equal("Expand library navigation", app.Find("LibraryNavigationToggle").Current.Name);
        Wait.For(() => app.SavedSettings.LibraryNavigationCollapsed, "the compact navigation preference to be saved");
        app.Visit("TV Shows");
        Wait.For(() => app.IsShown("LibraryBillboard") && app.Text("LibraryLook") == "Browse", "TV shows to open on Browse over cards never picked");
        Assert.True(app.IsShown("LibrarySort"), "TV Browse keeps sorting available.");
        Assert.True(app.IsShown("LibraryFilter"), "TV Browse keeps filters available.");

        // A layout picked is kept as a pick, and the view opens in it from then on.
        app.Pick("LibraryLook", "Grid");
        Wait.For(() => app.SavedSettings.LibraryViews.GetValueOrDefault("TvShows") is { Look: LibraryLook.Grid, LookChosen: true, CardSize: 260 },
            "the picked cards to be kept");
        app.Visit("Movies");
        app.Visit("TV Shows");
        Wait.For(() => app.Text("LibraryLook") == "Cards" && !app.IsShown("LibraryBillboard"), "TV shows to open as the cards picked");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void ALetterboxedFilmsCardAndEpisodeStillShowPictureNotBars()
    {
        _scene.Film(Path.Combine("Movies", "Wide.Film.2024.mp4"), "h264-letterboxed.mp4");
        _scene.Video(Path.Combine("Harbour Lights", "Season 1", "Arrival.mp4"), "h264-letterboxed.mp4");
        using var app = _scene.Ready().Start();

        // A poster card is taller than the film is wide: filled from the top, its first rows are picture.
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        var film = Wait.Until(() => app.Lines().FirstOrDefault());
        var card = app.Line(film).Current.BoundingRectangle;
        LibraryPictures.WaitFor(app, LibraryPictures.Band(card, left: 0.2, right: 0.8, top: 0.05, bottom: 0.09), "the film's card to show picture at its top edge");
        // The CI decoder renders some noisy blocks differently between runs (up to 3.72%
        // in the checked CI diff, concentrated in the poster's changing decoded pixels).
        // Keep the full-card comparison and explicit top-edge colour check with a bounded
        // 4.5% threshold for decoder variance, not an unchecked screenshot skip.
        // The taller navigation/transport chrome removes 18 pixels of empty list viewport;
        // compare the shared pixels rather than requiring the obsolete full height.
        Screenshots.Match(app, "library-movie-card-letterboxed", "LibraryList", maxDifferingRatio: 0.045, maxViewportHeightChange: 24);

        // An episode still is as wide as the frame the film was recorded in, so bars would show above it.
        app.Visit("TV Shows");
        app.Pick("LibraryLook", "Grid");
        app.Open("Harbour Lights");
        Wait.For(() => app.Lines().Length == 1, "the show page's one episode", () => string.Join(", ", app.Lines()));
        var episode = app.Line(app.Lines()[0]).Current.BoundingRectangle;
        var scale = episode.Height / 115;
        LibraryPictures.WaitFor(app, new System.Windows.Rect(episode.Left + (100 * scale), episode.Top + (14 * scale), 110 * scale, 10 * scale),
            "the episode still to show picture at its top edge");
        // Decoding the still can vary slightly (0.53% on CI versus the 0.50% baseline).
        Screenshots.Match(app, "library-episode-still-letterboxed", "LibraryList", maxDifferingRatio: 0.01);
        Assert.Equal(0, app.Close());
    }
}
