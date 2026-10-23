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
    public void ATelevisionEpisodeGetsItsOwnAccessibleContextAndStillFrame()
    {
        var episode = _scene.Video("North.Shore.S01E01.Pilot.mp4",
            length: TimeSpan.FromMinutes(42));
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([episode], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "TV EPISODE playback stage"
            && app.Find("SeekBar").Current.IsEnabled,
            "the proven episode stream");
        Wait.For(() => app.Status().State == "Ended", "a settled final episode frame");
        Assert.Contains("North Shore", app.Text("NowPlayingContext"), StringComparison.OrdinalIgnoreCase);
        Assert.False(app.IsShown("Lyrics"));
        Assert.False(app.IsShown("StageChaptersButton"));
        Screenshots.Match(app, "stage-television");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void DirectVideoRetainsItsAccessibleProfileAfterTheShortFixtureEnds()
    {
        var file = Rex.Media.TestKit.RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
        using var app = AppProcess.Start([file], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Status().State == "Ended"
            && app.Find("Stage").Current.Name == "VIDEO playback stage",
            "the completed video to retain its media profile");
        Assert.False(app.IsShown("Lyrics"));
        Assert.False(app.IsShown("Cover"));
        Assert.False(app.IsShown("StageChaptersButton"));
        Screenshots.Match(app, "stage-video-ended");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void PodcastRecordingAndSoundProfilesReplaceOneAnotherWithoutOldMediaActions()
    {
        var podcast = _scene.Audio(Path.Combine("Podcasts", "Test show", "Episode 1.wav"),
            TimeSpan.FromMinutes(30), genre: "Podcast");
        var recording = _scene.Audio(Path.Combine("Voice Memos", "Note.wav"),
            TimeSpan.FromMinutes(2));
        var sound = _scene.Audio(Path.Combine("Sound Effects", "Beep.wav"),
            TimeSpan.FromSeconds(5));
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });

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
    public void OpeningATaggedAudiobookDirectlyShowsItsChaptersWithoutLibraryClassification()
    {
        var book = _scene.ChapteredAudiobook();
        using var app = AppProcess.Start([book], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.IsShown("StageChaptersButton"),
            "a directly opened tagged audiobook to expose its parsed chapter markers");
        Assert.Contains("Chapter jumps", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.False(app.IsShown("Lyrics"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void AChapterFocusedDuringStreamSwitchReturnsFocusToThePermanentTransport()
    {
        var book = _scene.ChapteredAudiobook();
        var clip = _scene.Video(Path.Combine("Clips", "short.mp4"));
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([book, clip], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.Find("StageChaptersButton").Current.IsEnabled,
            "chapter controls to be ready");

        var chapters = app.Find("StageChaptersButton");
        chapters.SetFocus();
        Wait.For(() => chapters.Current.HasKeyboardFocus, "the chapter picker to own keyboard focus");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "VIDEO playback stage",
            "the new video profile");
        Assert.False(app.IsShown("StageChaptersButton"));
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "the permanent playback action to inherit focus when chapters go away");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void ARealChapteredAudiobookExposesOnlyItsOwnChapterJumps()
    {
        var book = _scene.ChapteredAudiobook();
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([book], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.Find("StageChaptersButton").Current.IsEnabled,
            "parsed audiobook chapters to become available");

        var chapters = app.Find("StageChaptersButton");
        Assert.Equal("StageChaptersButton", chapters.Current.AutomationId);
        Assert.True(chapters.Current.BoundingRectangle.Height >= 42,
            "The chapter picker must remain a touch-sized target at standard DPI.");
        Assert.Contains("seek slider", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.Contains("Chapter jumps", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Wait.For(() => app.Status().State == "Playing", "the audiobook to start");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "a stable chaptered audiobook frame");
        Screenshots.Match(app, "stage-audiobook-chapters");

        app.Press("StageChaptersButton");
        var first = app.Find("StageChapter-0");
        var second = app.Find("StageChapter-1");
        Assert.Contains("Opening", first.Current.Name, StringComparison.Ordinal);
        Assert.Contains("Continuation", second.Current.Name, StringComparison.Ordinal);
        Assert.True(second.Current.IsEnabled, "A proven chapter should offer a working jump.");
        Assert.True(first.Current.BoundingRectangle.Height >= 42
            && second.Current.BoundingRectangle.Height >= 42,
            "Each chapter jump must remain a touch-sized menu action.");
        app.Press("StageChapter-1");
        Wait.For(() => Math.Abs(app.Position - 3.0) < 0.5,
            "the chapter action to seek the paused audiobook to its three-second mark");
        app.Press("StageChaptersButton");
        var focusedChapter = app.Find("StageChapter-0");
        focusedChapter.SetFocus();
        Wait.For(() => focusedChapter.Current.HasKeyboardFocus,
            "a chapter menu item to receive keyboard focus");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.MinimalInterface);
        Wait.For(() => !app.IsShown("StageChaptersButton"),
            "optional chapter controls to hide in minimal player mode");
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "keyboard focus to return to Play/Pause on entering minimal mode");
        Assert.Equal("AUDIOBOOK playback stage", app.Find("Stage").Current.Name);
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.MinimalInterface);
        Wait.For(() => app.IsShown("StageChaptersButton"),
            "the same chapter picker to return with the full controls");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SwitchingFromChapteredAudioToPictureRemovesChapterInstructionsAndSeek()
    {
        var book = _scene.ChapteredAudiobook();
        var picture = _scene.Picture("illustration.jpg");
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([book, picture], root: _scene.Root);
        Wait.For(() => app.IsShown("StageChaptersButton")
            && app.Find("Stage").Current.HelpText.Contains("Chapter jumps", StringComparison.Ordinal),
            "the chaptered audiobook instructions");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "the picture to replace the spoken-audio stream");

        Assert.False(app.IsShown("StageChaptersButton"));
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.DoesNotContain("Chapter jumps", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("seek slider", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.Equal("Stage", app.Find("Stage").Current.AutomationId);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void UnreadableMediaShowsAnErrorStageWithoutAudioOrSeekAffordances()
    {
        var broken = Path.Combine(_scene.Media, "broken.mp4");
        File.WriteAllText(broken, "not a playable media file");
        using var app = AppProcess.Start([broken], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Status().State == "Faulted" &&
            app.Find("Stage").Current.Name == "PLAYBACK ERROR playback stage",
            "an unreadable file to display its failure");
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.Contains("could not be played", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.False(app.IsShown("Lyrics"));
        Assert.False(app.IsShown("Cover"));
        Screenshots.Match(app, "stage-error");
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
            Theme = ThemeChoice.Dark,
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
        Wait.For(() => app.Status().State == "Playing", "the song to start playback");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "a stable song stage frame");
        Screenshots.Match(app, "stage-song-windowed");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Movies");
        Wait.For(() => app.Lines().Contains("Small Film"), "the movie card");
        app.Choose("Small Film");
        // Movies open their details page first; Play there starts the actual stream.
        // The earlier test incorrectly expected the grid's Play action to bypass it.
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("LibraryKicker") == "MOVIE"
            && app.Text("LibraryTitle") == "Small Film" && app.IsShown("LibraryPlay"),
            "the movie's dedicated details page");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "MOVIE playback stage"
            && app.Find("SeekBar").Current.IsEnabled,
            "the movie stage to identify its media type",
            () => $"Stage={app.Find("Stage").Current.Name}; title={app.Text("NowPlaying")}; state={app.Status().State}");
        Assert.False(app.IsShown("Lyrics"), "The movie stage must not retain song lyrics.");
        Assert.False(app.IsShown("Cover"), "A video must not keep the song cover.");
        Wait.For(() => app.Status().State == "Ended", "a stable final video frame");
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
        Assert.Contains("Picture view", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.False(app.Find("SeekBar").Current.IsEnabled, "Pictures must not offer a fake seek timeline.");
        Assert.False(app.Find("StartOverButton").Current.IsEnabled,
            "Picture playback has no timed start-over action.");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.GoToTime);
        Wait.For(() => app.LogText.Contains(
            Rex.Media.AppCore.Player.PlayerController.CannotSeekNotice, StringComparison.Ordinal),
            "the picture stage to reject the time-jump command");
        Assert.False(app.IsShown("GoToTimeBox"),
            "Pictures must never prompt for a timestamp.");
        Assert.Equal("Stage", app.Find("Stage").Current.AutomationId);
        Assert.Equal(0, app.Close());
    }
}
