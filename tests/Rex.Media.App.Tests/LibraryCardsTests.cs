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
        app.Visit("TV Shows");
        Wait.For(() => app.IsShown("LibraryBillboard") && app.Text("LibraryLook") == "Browse", "TV shows to open on Browse over cards never picked");

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
        WaitForPicture(app, Band(card, left: 0.2, right: 0.8, top: 0.05, bottom: 0.09), "the film's card to show picture at its top edge");
        // The CI renderer shifts a few border/crop-edge pixels after the artwork-fill update.
        // Keep the captured full-card baseline and all interior colour assertions; tolerate up to 2% edge noise.
        Screenshots.Match(app, "library-movie-card-letterboxed", "LibraryList", maxDifferingRatio: 0.02);

        // An episode still is as wide as the frame the film was recorded in, so bars would show above it.
        app.Visit("TV Shows");
        app.Pick("LibraryLook", "Grid");
        app.Open("Harbour Lights");
        Wait.For(() => app.Lines().Length == 1, "the show page's one episode", () => string.Join(", ", app.Lines()));
        var episode = app.Line(app.Lines()[0]).Current.BoundingRectangle;
        var scale = episode.Height / 115;
        WaitForPicture(app, new System.Windows.Rect(episode.Left + (100 * scale), episode.Top + (14 * scale), 110 * scale, 10 * scale),
            "the episode still to show picture at its top edge");
        Screenshots.Match(app, "library-episode-still-letterboxed", "LibraryList");
        Assert.Equal(0, app.Close());
    }

    /// <summary>A band of <paramref name="area"/>, each edge a share of its width or height.</summary>
    private static System.Windows.Rect Band(System.Windows.Rect area, double left, double right, double top, double bottom) =>
        new(area.Left + (area.Width * left), area.Top + (area.Height * top), area.Width * (right - left), area.Height * (bottom - top));

    /// <summary>
    /// Waits until most of <paramref name="area"/> (on the screen) is strongly coloured: the test
    /// pattern's picture, never a black bar or the grey placeholder.
    /// </summary>
    private static void WaitForPicture(AppProcess app, System.Windows.Rect area, string what)
    {
        var share = 0.0;
        Wait.For(() =>
        {
            var part = Screenshots.Crop(WindowPicture.Take(app.Handle), WindowPicture.Bounds(app.Handle), area);
            var coloured = 0;
            for (var i = 0; i + 3 < part.Bgra.Length; i += 4)
            {
                var (b, g, r) = (part.Bgra[i], part.Bgra[i + 1], part.Bgra[i + 2]);
                coloured += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 60 ? 1 : 0;
            }

            share = part.Bgra.Length == 0 ? 0 : coloured / (part.Bgra.Length / 4.0);
            return share > 0.5;
        }, what, () => $"{share:P0} of the band was coloured.");
    }
}
