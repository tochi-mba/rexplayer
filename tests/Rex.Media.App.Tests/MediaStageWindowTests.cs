using System.IO;
using System.Windows.Automation;
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
        Screenshots.PinViewport(app, 1000, 700);
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
        Screenshots.PinViewport(app, 1000, 700);
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
            Screenshots.PinViewport(app, 1000, 700);
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
    public void ReplacingAChapteredAudiobookWithAnotherNeverLeavesFocusOnAnOldMenuItem()
    {
        var first = _scene.ChapteredAudiobook();
        var second = _scene.ChapteredAudiobook("Part 2");
        using var app = AppProcess.Start([first, second], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.IsShown("StageChaptersButton"),
            "the first chaptered stream to expose its menu");

        app.Press("StageChaptersButton");
        var oldChapter = app.Find("StageChapter-0");
        oldChapter.SetFocus();
        Wait.For(() => oldChapter.Current.HasKeyboardFocus,
            "the first stream's chapter menu item to take focus");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Status().Title == "Part 2"
            && app.Find("Stage").Current.Name == "AUDIOBOOK playback stage"
            && app.IsShown("StageChaptersButton")
            && app.Find("StageChaptersButton").Current.HelpText.Contains("2 chapter jumps", StringComparison.Ordinal),
            "the replacement audiobook to expose its own chapters");
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus
            || app.Find("StageChaptersButton").Current.HasKeyboardFocus,
            "focus to return to an active transport control when the old flyout is replaced");
        Assert.Contains("chapter jumps", app.Find("StageChaptersButton").Current.HelpText,
            StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SwitchingFromChapteredAudioToOrdinaryAudioKeepsSeekingButRemovesChapters()
    {
        var book = _scene.ChapteredAudiobook();
        var plain = _scene.Audio("Ordinary.wav", TimeSpan.FromSeconds(20));
        using var app = AppProcess.Start([book, plain]);
        Wait.For(() => app.IsShown("StageChaptersButton")
            && app.Find("SeekBar").Current.IsEnabled,
            "the first file to provide a chaptered seekable stream");

        app.Find("StageChaptersButton").SetFocus();
        Wait.For(() => app.Find("StageChaptersButton").Current.HasKeyboardFocus,
            "the outgoing chapter picker to receive focus");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Text("NowPlaying") == "Ordinary"
            && app.Find("Stage").Current.Name == "AUDIO playback stage",
            "a plain audio file to replace the chaptered audiobook");

        Assert.True(app.Find("SeekBar").Current.IsEnabled,
            "Seeking stays available for the new audio file.");
        Assert.False(app.IsShown("StageChaptersButton"));
        Assert.DoesNotContain("Chapter jumps", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "focus to return to a permanent control when only the chapter affordance disappears");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void ChapterJumpsWorkThroughRealKeyboardInput()
    {
        var book = _scene.ChapteredAudiobook();
        using var app = AppProcess.Start([book]);
        Wait.For(() => app.IsShown("StageChaptersButton")
            && app.Find("SeekBar").Current.IsEnabled,
            "a seekable chaptered audiobook for keyboard navigation");
        Wait.For(() => app.Status().State == "Playing", "chaptered audiobook playback");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "a stable keyboard chapter seek");

        Pointer.Key(app, "StageChaptersButton", Pointer.Enter);
        Wait.For(() => app.IsShown("StageChapter-1"), "the chapter menu to open by keyboard");
        Pointer.Key(app, app.Find("StageChapter-1"), Pointer.Enter);
        Wait.For(() => Math.Abs(app.Position - 3.0) < 0.5,
            "the second chapter to be invoked and seeked by keyboard");
        Assert.Equal("AUDIOBOOK playback stage", app.Find("Stage").Current.Name);
        Assert.Equal("Paused", app.Status().State);
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
        Assert.Contains("2 chapter jumps", chapters.Current.HelpText, StringComparison.Ordinal);
        Wait.For(() => app.Status().State == "Playing", "the audiobook to start");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "a stable chaptered audiobook frame");
        Screenshots.PinViewport(app, 1000, 700);
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
    public void DirectAudioOnlyOffersTimedLyricJumpsInsideItsPlayableDuration()
    {
        var song = _scene.Audio("SeekBounded.wav", TimeSpan.FromSeconds(20));
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"),
            "[00:00.00]Start\\n[00:10.00]Playable\\n[00:30.00]Beyond the track\\n");

        using var app = AppProcess.Start([song]);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage"
            && app.IsShown("Lyrics") && app.Find("SeekBar").Current.IsEnabled
            && app.IsShown("LyricLine-0"),
            "seekable direct audio to expose its playable lyric markers");
        var playable = app.Find("LyricLine-1");
        Assert.Contains("Playable", playable.Current.Name, StringComparison.Ordinal);
        var unavailable = app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LyricLine-2"));
        Assert.Null(unavailable);
        Assert.Contains("Lyrics are available", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SwitchingBetweenTwoDirectAudioFilesReplacesTimedLyricsAndRestoresFocus()
    {
        var first = _scene.Audio(Path.Combine("Songs", "First.wav"), TimeSpan.FromSeconds(20));
        var second = _scene.Audio(Path.Combine("Songs", "Second.wav"), TimeSpan.FromSeconds(20));
        File.WriteAllText(Path.ChangeExtension(first, ".lrc"), "[00:00.00]First stream line\\n[00:03.00]First ending\\n");
        File.WriteAllText(Path.ChangeExtension(second, ".lrc"), "[00:00.00]Second stream line\\n[00:03.00]Second ending\\n");

        using var app = AppProcess.Start([first, second], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Text("NowPlaying") == "First" && app.IsShown("Lyrics")
            && app.IsShown("LyricLine-0")
            && app.Find("LyricLine-0").Current.Name.Contains("First stream line", StringComparison.Ordinal),
            "the first direct-open audio file to show its own lyrics");
        app.Find("LyricLine-0").SetFocus();
        Wait.For(() => app.Find("LyricLine-0").Current.HasKeyboardFocus,
            "the first stream's timed lyric control to take focus");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Text("NowPlaying") == "Second" && app.IsShown("Lyrics")
            && app.IsShown("LyricLine-0")
            && app.Find("LyricLine-0").Current.Name.Contains("Second stream line", StringComparison.Ordinal),
            "the second direct audio file to replace the first file's timed lyrics");
        Assert.Equal("AUDIO playback stage", app.Find("Stage").Current.Name);
        Assert.DoesNotContain("First stream line", app.Find("LyricLine-0").Current.Name, StringComparison.Ordinal);
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "focus to move to stable transport when the focused lyric line disappears");
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
    public void SwitchingFromCaptionedLocalVideoToRemoteLocationClearsOldSubtitleUI()
    {
        var video = _scene.Video("Captioned.mp4");
        File.WriteAllText(Path.Combine(_scene.Media, "Captioned.en.srt"),
            "1\\n00:00:00,000 --> 00:10:00,000\\nLocal video captions\\n");
        using var app = AppProcess.Start([video]);
        Wait.For(() => app.IsShown("SubtitleText")
            && app.Text("SubtitleText") == "Local video captions",
            "local video's rendered subtitles to be present");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLocation);
        var location = (ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern);
        // A loopback refusal proves the remote transition without depending on
        // external DNS, internet access, proxies, or a remote server's uptime.
        location.SetValue("http://127.0.0.1:1/new-stream.mp3");
        app.Press("PrimaryButton");

        Wait.For(() => app.Text("NowPlaying") != "Captioned"
            && !app.IsShown("SubtitleText"),
            "the replacement remote stream to clear the previous local subtitle cues");
        Assert.DoesNotContain("Captions are available", app.Find("Stage").Current.HelpText,
            StringComparison.Ordinal);
        Assert.False(app.IsShown("KineticCaptions"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void FaultingAfterCaptionedVideoClearsThePreviousCaptionAndSeekActions()
    {
        var video = _scene.Video("BeforeFault.mp4");
        File.WriteAllText(Path.Combine(_scene.Media, "BeforeFault.en.srt"),
            "1\\n00:00:00,000 --> 00:10:00,000\\nCaption before fault\\n");
        var bad = Path.Combine(_scene.Media, "unreadable.mp4");
        File.WriteAllText(bad, "not a playable media file");

        using var app = AppProcess.Start([video, bad]);
        Wait.For(() => app.IsShown("SubtitleText")
            && app.Text("SubtitleText") == "Caption before fault",
            "captions from the first playable video to render");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Status().State == "Faulted"
            && app.Find("Stage").Current.Name == "PLAYBACK ERROR playback stage",
            "the next item to fault with an accessible error stage");
        Assert.False(app.IsShown("SubtitleText"));
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.False(app.Find("StartOverButton").Current.IsEnabled);
        Assert.False(app.IsShown("KineticCaptions"));
        Assert.DoesNotContain("Captions are available", app.Find("Stage").Current.HelpText,
            StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFocusedMovieVersionPickerOrFlyoutReturnsFocusWhenPicturePlaybackReplacesTheFilm(bool focusFlyoutItem)
    {
        var movie = _scene.Film("Focus.Film.2023.1080p.mp4");
        _scene.Film("Focus.Film.2023.720p.mp4");
        var picture = _scene.Picture("focus-replacement.jpg");
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([movie, picture], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "MOVIE playback stage"
            && app.IsShown("MovieVersionButton"),
            "two editions of the same movie to expose their resolution picker");

        var picker = app.Find("MovieVersionButton");
        Assert.True(picker.Current.BoundingRectangle.Width >= 42
            && picker.Current.BoundingRectangle.Height >= 42,
            "The movie resolution picker must remain a touch-sized playback action.");
        picker.SetFocus();
        Wait.For(() => picker.Current.HasKeyboardFocus,
            "the movie version picker to own keyboard focus");
        if (focusFlyoutItem)
        {
            app.Press("MovieVersionButton");
            Wait.For(() => app.IsShown("MovieVersion-1"),
                "a separately focusable alternate resolution menu item");
            app.Find("MovieVersion-1").SetFocus();
            Wait.For(() => app.Find("MovieVersion-1").Current.HasKeyboardFocus,
                "the movie's alternate resolution to own keyboard focus");
        }

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage"
            && !app.IsShown("MovieVersionButton")
            && !app.IsShown("MovieVersion-1"),
            "picture playback to remove the old picker and its flyout actions");
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "a permanent transport to receive focus after the focused version picker disappears");
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void GoToTimeDialogCannotSeekAPictureThatReplacedTheOriginalAudio()
    {
        var song = _scene.Audio("DialogSource.wav", TimeSpan.FromSeconds(20));
        var picture = _scene.Picture("dialog-target.jpg");
        using var app = AppProcess.Start([song, picture]);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage"
            && app.Find("SeekBar").Current.IsEnabled,
            "seekable audio before opening a time-jump dialog");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.GoToTime);
        Wait.For(() => app.IsShown("GoToTimeBox"), "the seek dialog to open on audio");
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("0:10");

        // The queued item can change while the dialog is open; accepting the old
        // text must not seek the picture or resurrect its previous timeline.
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "picture playback behind the still-open time-jump dialog");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("The media changed before the seek could be applied.",
            StringComparison.Ordinal), "the stale time-jump request to be rejected");

        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.False(app.Find("StartOverButton").Current.IsEnabled);
        Assert.False(app.IsShown("GoToTimeBox"));
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
        Screenshots.PinViewport(app, 1000, 700);
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
        Screenshots.PinViewport(app, 1000, 700);
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
        Screenshots.PinViewport(app, 1000, 700);
        Screenshots.Match(app, "stage-movie-windowed");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Pictures");
        app.Choose("still");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "the picture stage to discard the old movie context",
            () => $"Stage={app.Find("Stage").Current.Name}; title={app.Text("NowPlaying")}; state={app.Status().State}");
        Screenshots.PinViewport(app, 1000, 700);
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
