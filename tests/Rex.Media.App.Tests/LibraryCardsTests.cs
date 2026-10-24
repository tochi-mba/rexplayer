using System.IO;
using System.Windows.Automation;
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

        // Responsive compaction remains reversible. Expanding a narrow window is temporary and
        // must not turn an automatic layout choice into the person's saved desktop preference.
        var original = app.Window.Current.BoundingRectangle;
        var transform = (System.Windows.Automation.TransformPattern)app.Window.GetCurrentPattern(System.Windows.Automation.TransformPattern.Pattern);
        transform.Resize(720, Math.Max(640, original.Height));
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation", "narrow navigation to compact");
        Assert.Empty(app.Find("LibrarySources").FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>());
        Assert.Contains("Movies", app.ListedViews());
        app.Press("LibraryNavigationToggle");
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation", "narrow navigation to expand on request");
        Wait.For(() => app.Find("LibrarySources").FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Any(text => text.Current.Name == "Movies"),
            "expanded navigation to realize the Movies text after switching templates");
        Assert.False(app.SavedSettings.LibraryNavigationCollapsed, "temporarily expanding a narrow rail must not change the saved preference");
        app.Press("LibraryNavigationToggle");
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation", "narrow navigation to collapse on request");
        transform.Resize(original.Width, original.Height);
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation", "wide navigation to expand again");

        app.Press("LibraryNavigationToggle");
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation", "wide navigation to collapse on request");
        Wait.For(() => app.SavedSettings.LibraryNavigationCollapsed, "the compact navigation preference to be saved");
        transform.Resize(720, Math.Max(640, original.Height));
        Wait.For(() => app.Window.Current.BoundingRectangle.Width < 760, "the window to become narrow again");
        // The saved compact preference already says "Expand" in a wide window, so that
        // label alone cannot prove the second resize reached the actual library layout.
        Wait.For(() =>
        {
            var pane = app.Find("LibraryPane").Current.BoundingRectangle;
            var window = app.Window.Current.BoundingRectangle;
            return pane.Width > 0 && pane.Width < window.Width;
        }, "the library viewport to settle before expanding its temporary navigation",
            () => $"Window: {app.Window.Current.BoundingRectangle}; library: {app.Find("LibraryPane").Current.BoundingRectangle}");
        app.Press("LibraryNavigationToggle");
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Collapse library navigation", "saved compact navigation to expand temporarily");
        Assert.True(app.SavedSettings.LibraryNavigationCollapsed, "expanding the narrow rail preserves the saved collapsed preference");
        transform.Resize(original.Width, original.Height);
        Wait.For(() => app.Find("LibraryNavigationToggle").Current.Name == "Expand library navigation", "wide navigation to restore the saved collapsed preference");
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
        // These two existing baselines were captured at the original 1280x800
        // host-window size with PrintWindow, independent of the visible desktop.
        // Do not silently rebaseline them at the smaller composited CI viewport.
        var window = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        window.Resize(1280, 800);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1280) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 800) < 2,
            "the pinned 1280x800 legacy card-baseline viewport");

        // A poster card is taller than the film is wide: filled from the top, its first rows are picture.
        app.Visit("Movies");
        app.Pick("LibraryLook", "Grid");
        var film = Wait.Until(() => app.Lines().FirstOrDefault());
        var card = app.Line(film).Current.BoundingRectangle;
        LibraryPictures.WaitFor(app, LibraryPictures.Band(card, left: 0.2, right: 0.8, top: 0.05, bottom: 0.09), "the film's card to show picture at its top edge");
        // The CI decoder renders some noisy blocks differently between runs (3.72% in
        // the checked CI diff, concentrated in the poster's decoded picture).
        // Keep the full-card pixel comparison and explicit top-edge colour check with
        // a bounded 4.5% budget for this decoder variance; never skip the screenshot.
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
        // The same 18-pixel viewport reduction covered by the movie-card assertion also
        // affects an opened episode: compare the visible pixels, not the old empty tail.
        Screenshots.Match(app, "library-episode-still-letterboxed", "LibraryList", maxDifferingRatio: 0.01, maxViewportHeightChange: 24);
        Assert.Equal(0, app.Close());
    }
}
