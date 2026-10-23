using System.IO;
using Rex.Media.Settings;

namespace Rex.Media.App.Tests;

/// <summary>Real Windows UI Automation proof of stage context after crossing media types.</summary>
[Collection("desktop")]
public sealed class MediaStageWindowTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    public void PodcastRecordingAndSoundProfilesReplaceOneAnotherWithoutOldMediaActions()
    {
        var podcast = _scene.Audio(Path.Combine("Podcasts", "Test show", "Episode 1.wav"),
            TimeSpan.FromMinutes(30), genre: "Podcast");
        var recording = _scene.Audio(Path.Combine("Voice Memos", "Note.wav"),
            TimeSpan.FromMinutes(2));
        var sound = _scene.Audio(Path.Combine("Sound Effects", "Beep.wav"),
            TimeSpan.FromSeconds(5));
        _scene.Ready();

        using var app = AppProcess.Start([podcast, recording, sound], root: _scene.Root);
        foreach (var (label, shot, hasChapters) in new[]
        {
            ("PODCAST", "stage-podcast", false),
            ("RECORDING", "stage-recording", false),
            ("AUDIO", "stage-sound", false),
        })
        {
            Wait.For(() => app.Find("Stage").Current.Name == label + " playback stage"
                && app.Find("SeekBar").Current.IsEnabled,
                label + " stream to update the stage");
            if (app.Status().State == "Playing")
            {
                app.Run(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
                Wait.For(() => app.Status().State == "Paused",
                    "a stable " + label + " screenshot");
            }

            Assert.False(app.IsShown("Lyrics"), "The previous item's lyrics must not survive.");
            Assert.False(app.IsShown("Cover"), "No image was supplied for this stage.");
            Assert.Equal(hasChapters, app.IsShown("StageChaptersButton"));
            Screenshots.Match(app, shot);
            if (shot != "stage-sound")
            {
                app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
            }
        }

        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void ARealChapteredAudiobookExposesOnlyItsOwnChapterJumps()
    {
        var book = _scene.ChapteredAudiobook();
        _scene.Ready();
        using var app = AppProcess.Start([book], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.Find("StageChaptersButton").Current.IsEnabled,
            "parsed audiobook chapters to become available");

        var chapters = app.Find("StageChaptersButton");
        Assert.Equal("StageChaptersButton", chapters.Current.AutomationId);
        Assert.Contains("seek slider", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Screenshots.Match(app, "stage-audiobook-chapters");

        app.Press("StageChaptersButton");
        var first = app.Find("StageChapter-0");
        var second = app.Find("StageChapter-1");
        Assert.Contains("Opening", first.Current.Name, StringComparison.Ordinal);
        Assert.Contains("Continuation", second.Current.Name, StringComparison.Ordinal);
        Assert.True(second.Current.IsEnabled, "A proven chapter should offer a working jump.");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void UnreadableMediaShowsAnErrorStageWithoutAudioOrSeekAffordances()
    {
        var broken = Path.Combine(_scene.Media, "broken.mp4");
        File.WriteAllText(broken, "not a playable media file");
        using var app = AppProcess.Start([broken]);
        Wait.For(() => app.Status().State == "Faulted" &&
            app.Find("Stage").Current.Name == "PLAYBACK ERROR playback stage",
            "an unreadable file to display its failure");
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.False(app.IsShown("Lyrics"));
        Assert.False(app.IsShown("Cover"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SwitchingSongToMovieToPictureUpdatesTheAccessibleStageWithoutStaleContext()
    {
        _scene.WithAsake();
        _scene.Film(Path.Combine("Movies", "Small.Film.2023.mp4"));
        _scene.Picture("still.jpg");
        var cards = new LibraryViewChoice(LibraryLook.Grid, CardSize: 200) { LookChosen = true };
        using var app = _scene.Ready(settings => settings with
        {
            LibraryViews = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal)
            {
                ["Movies"] = cards,
            },
        }).Start();

        app.Visit("Songs");
        app.Choose("Sungba");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba" &&
            app.Find("Stage").Current.Name == "NOW PLAYING playback stage"
            && app.Find("SeekBar").Current.IsEnabled,
            "the music stage to identify the playing song");
        Screenshots.Match(app, "stage-song-windowed");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Movies");
        Wait.For(() => app.Lines().Contains("Small Film"), "the movie card");
        app.Choose("Small Film");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "MOVIE playback stage"
            && app.Find("SeekBar").Current.IsEnabled,
            "the movie stage to identify its media type",
            () => $"Stage={app.Find("Stage").Current.Name}; title={app.Text("NowPlaying")}; state={app.Status().State}");
        Assert.False(app.IsShown("Lyrics"), "The movie stage must not retain song lyrics.");
        Assert.False(app.IsShown("Cover"), "A video must not keep the song cover.");
        Screenshots.Match(app, "stage-movie-windowed");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Pictures");
        app.Choose("still");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "the picture stage to discard the old movie context",
            () => $"Stage={app.Find("Stage").Current.Name}; title={app.Text("NowPlaying")}; state={app.Status().State}");
        Screenshots.Match(app, "stage-picture-windowed");
        Assert.False(app.IsShown("Lyrics"), "Lyrics should not carry over to picture playback.");
        Assert.False(app.Find("SeekBar").Current.IsEnabled, "Pictures must not offer a fake seek timeline.");
        Assert.Equal("Stage", app.Find("Stage").Current.AutomationId);
        Assert.Equal(0, app.Close());
    }
}
