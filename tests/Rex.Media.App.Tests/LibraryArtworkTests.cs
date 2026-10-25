using System.IO;
using System.Windows.Automation;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Artwork where the library shows it: every episode tile of a show page's collage gets a picture
/// while a long transcription queue waits behind, and a show with no artwork anywhere gets a
/// poster made from its first episode.
/// </summary>
[Collection("desktop")]
public sealed class LibraryArtworkTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void EveryEpisodeTileInACollageGetsItsPictureWhileALongQueueWaits()
    {
        for (var episode = 1; episode <= 8; episode++)
        {
            _scene.Video(Path.Combine("Harbour Lights", "Season 1", $"Harbour.Lights.S01E{episode:00}.mp4"));
        }

        // Many more files for library transcription to hear (songs long enough to be heard, their
        // details read), one piece taking minutes: the queue stays long and busy for the whole
        // test, as it does on a large library.
        for (var song = 1; song <= 40; song++)
        {
            _scene.Song($"Sungba {song:00}", "Mr. Money with the Vibe", 2022, "Amapiano");
        }

        using var app = _scene.Ready(settings => settings with
        {
            Transcription = new TranscriptionSettings { Library = true, OnBattery = true, WhilePlaying = true },

            // The layouts picked before: shows as cards, a show's page as a collage. The queue's pill
            // takes the toolbar's room, so the layout menu is not reached for.
            LibraryViews = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal)
            {
                ["TvShows"] = new(LibraryLook.Grid, LookChosen: true),
                ["TvShows page"] = new(LibraryLook.Collage, LookChosen: true),
            },
        }).Start(new Dictionary<string, string> { ["REXPLAYER_FAKE_SPEECH"] = "600" });
        Wait.For(() => app.IsShown("LibraryTranscriptionText") && QueueLength(app.Text("LibraryTranscriptionText")) >= 40,
            "a long transcription queue", () => app.IsShown("LibraryTranscriptionText") ? app.Text("LibraryTranscriptionText") : "no queue shown");

        app.Visit("TV Shows");
        app.Open("Harbour Lights");
        Wait.For(() => app.IsShown("LibraryCollage") && !app.IsShown("LibraryList"), "the collage");
        // Every tile on screen, by place: the fixture names every episode alike.
        Wait.For(() => app.Lines("LibraryCollage").Length == 8, "the collage's tiles");
        var collage = app.Find("LibraryCollage").Current.BoundingRectangle;
        var tiles = app.Find("LibraryCollage").FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().Select(tile => tile.Current.BoundingRectangle).Where(area => !area.IsEmpty && area.Width > 0 && area.Top >= collage.Top && area.Bottom <= collage.Bottom).ToList();
        Assert.NotEmpty(tiles);
        foreach (var (area, index) in tiles.Select((area, index) => (area, index)))
        {
            // The test pattern's colour bars, above the title's scrim: the picture itself, not the placeholder's tint.
            LibraryPictures.WaitFor(app, LibraryPictures.Band(area, left: 0.15, right: 0.85, top: 0.4, bottom: 0.55), $"tile {index + 1} to show its picture");
        }

        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-show-page-collage", "LibraryCollage");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void AShowWithNoArtAnywhereGetsAPosterFromItsFirstEpisode()
    {
        // No folder art, no catalogue (TV enrichment is off), no season art: the poster is made.
        _scene.Video(Path.Combine("Quiet Harbour", "Season 1", "Quiet.Harbour.S01E01.mp4"));
        _scene.Video(Path.Combine("Quiet Harbour", "Season 1", "Quiet.Harbour.S01E02.mp4"));
        using var app = _scene.Ready().Start();

        app.Visit("TV Shows");
        app.Pick("LibraryLook", "Grid");
        var card = app.Line("Quiet Harbour").Current.BoundingRectangle;
        LibraryPictures.WaitFor(app, LibraryPictures.Band(card, left: 0.2, right: 0.8, top: 0.45, bottom: 0.6), "the show's card to show a made poster");
        Screenshots.PinViewport(app, 1000, 760);
        Screenshots.Match(app, "library-show-card-made-poster", "LibraryList");
        Assert.Equal(0, app.Close());
    }

    /// <summary>How many files the queue's pill counts ("1 active · 41 queued", "40 waiting"), or 0.</summary>
    private static int QueueLength(string pill) =>
        System.Text.RegularExpressions.Regex.Matches(pill, "[0-9]+").Sum(number => int.Parse(number.Value, System.Globalization.CultureInfo.InvariantCulture));
}
