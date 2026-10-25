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
        var firstBounds = first.Current.BoundingRectangle;
        var secondBounds = second.Current.BoundingRectangle;
        Assert.True(firstBounds.Width >= 42 && firstBounds.Height >= 42
            && secondBounds.Width >= 42 && secondBounds.Height >= 42,
            "Each chapter jump must remain at least a 44 by 44 logical-unit touch target.");
        app.Press("StageChapter-1");
        Wait.For(() => Math.Abs(app.Position - 3.0) < 0.5,
            "the chapter action to seek the paused audiobook to its three-second mark");
        Wait.For(() => !app.IsShown("StageChapter-1"),
            "the first native chapter flyout to dismiss after the chosen jump");
        // A second click while the old native popup is still closing gets
        // consumed by that popup. Re-expand until the actual item is visible.
        var focusedChapter = app.OpenMenu("StageChapter-0", "StageChaptersButton");
        focusedChapter.SetFocus();
        Wait.For(() => focusedChapter.Current.HasKeyboardFocus,
            "a chapter menu item to receive keyboard focus");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.MinimalInterface);
        Wait.For(() => !app.IsShown("StageChaptersButton")
            && !app.IsShown("StageChapter-0") && !app.IsShown("StageChapter-1"),
            "minimal mode to close the old chapter flyout and remove its actions");
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
            "[00:00.00]Start\n[00:10.00]Playable\n[00:30.00]Beyond the track\n");

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
        File.WriteAllText(Path.ChangeExtension(first, ".lrc"), "[00:00.00]First stream line\n[00:03.00]First ending\n");
        File.WriteAllText(Path.ChangeExtension(second, ".lrc"), "[00:00.00]Second stream line\n[00:03.00]Second ending\n");

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
        // A new timed stream supplies replacement buttons, so focus returns
        // to its first seekable lyric rather than staying on the transport.
        // When there are no replacement lyrics, the transport is the fallback.
        Wait.For(() => app.Find("LyricLine-0").Current.HasKeyboardFocus,
            "the focused lyric navigation to return on the replacement song");
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
        Assert.False(app.IsShown("StageChapter-0"),
            "The previous audiobook's chapter flyout cannot remain in the accessibility tree.");
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.DoesNotContain("Chapter jumps", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("seek slider", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.Equal("Stage", app.Find("Stage").Current.AutomationId);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void ARealPictureHasTouchSizedSlideshowSettingsWithoutASeekTimeline()
    {
        var picture = _scene.Picture("slideshow-controls.jpg");
        using var app = AppProcess.Start([picture]);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage"
            && app.IsShown("PictureIntervalButton"),
            "picture playback and its slideshow interval picker");

        ((TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern)).Resize(420, 340);
        Wait.For(() => app.Window.Current.BoundingRectangle is { Width: var w, Height: var h }
            && Math.Abs(w - 420) < 2 && Math.Abs(h - 340) < 2,
            "a compact picture stage with accessible interval controls");
        // The native window reports its resized bounds before WinUI has necessarily
        // completed measure/arrange for the compact playback controls.
        Wait.For(() =>
        {
            var client = app.Window.Current.BoundingRectangle;
            var button = app.Find("PictureIntervalButton").Current.BoundingRectangle;
            return button.Left >= client.Left && button.Right <= client.Right
                && button.Top >= client.Top && button.Bottom <= client.Bottom;
        }, "the picture interval button to fit inside the compact window",
            () => $"Window: {app.Window.Current.BoundingRectangle}; " +
                $"slideshow picker: {app.Find("PictureIntervalButton").Current.BoundingRectangle}.");
        var window = app.Window.Current.BoundingRectangle;
        var bounds = app.Find("PictureIntervalButton").Current.BoundingRectangle;
        Assert.True(bounds.Left >= window.Left && bounds.Right <= window.Right
            && bounds.Top >= window.Top && bounds.Bottom <= window.Bottom,
            "The picture interval button must not be clipped by compact playback layout.");
        Assert.True(bounds.Width >= 42 && bounds.Height >= 42,
            "The picture interval picker must be touch-sized at standard DPI.");
        Assert.Equal("Slideshow interval, 5 seconds per picture",
            app.Find("PictureIntervalButton").Current.Name);
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Pointer.Key(app, "PictureIntervalButton", Pointer.Enter);
        Wait.For(() => app.IsShown("PictureInterval-10"),
            "picture interval options to open");
        bounds = app.Find("PictureInterval-10").Current.BoundingRectangle;
        Assert.True(bounds.Width >= 42 && bounds.Height >= 42,
            "Each slideshow interval choice must have a touch-sized target.");
        Pointer.Key(app, app.Find("PictureInterval-10"), Pointer.Enter);
        Wait.For(() => app.Find("PictureIntervalButton").Current.Name
            == "Slideshow interval, 10 seconds per picture",
            "the selected picture interval to be announced to screen readers");
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Assert.False(app.Find("StartOverButton").Current.IsEnabled);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void LeavingAPictureWithItsIntervalMenuOpenRemovesObsoleteSlideshowActions()
    {
        var picture = _scene.Picture("menu-picture.jpg");
        var audio = _scene.Audio("next-audio.wav", TimeSpan.FromSeconds(20));
        LibraryScene.WriteSettings(_scene.Root, settings => settings with { PictureSeconds = 60 });
        using var app = AppProcess.Start([picture, audio], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage"
            && app.IsShown("PictureIntervalButton"),
            "a long-lived picture before its slideshow controls are opened");
        app.Press("PictureIntervalButton");
        Wait.For(() => app.IsShown("PictureInterval-10"),
            "the old picture's interval menu to open");
        app.Find("PictureInterval-10").SetFocus();
        Wait.For(() => app.Find("PictureInterval-10").Current.HasKeyboardFocus,
            "the outgoing slideshow choice to own keyboard focus");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage"
            && !app.IsShown("PictureIntervalButton")
            && !app.IsShown("PictureInterval-10"),
            "the audio stage to remove the old picture-specific menu and choices");
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "focus to return to the permanent transport after dismissing picture options");
        Assert.Equal(60, app.SavedSettings.PictureSeconds);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SwitchingFromCaptionedLocalVideoToRemoteLocationClearsOldSubtitleUI()
    {
        var video = _scene.Video("Captioned.mp4");
        File.WriteAllText(Path.Combine(_scene.Media, "Captioned.en.srt"),
            "1\n00:00:00,000 --> 00:10:00,000\nLocal video captions\n");
        using var app = AppProcess.Start([video]);
        Wait.For(() => app.IsShown("SubtitleText")
            && app.Text("SubtitleText") == "Local video captions",
            "local video's rendered subtitles to be present");

        var originalTitle = app.Text("NowPlaying");
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLocation);
        var location = (ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern);
        // A loopback refusal proves the remote transition without depending on
        // external DNS, internet access, proxies, or a remote server's uptime.
        location.SetValue("http://127.0.0.1:1/new-stream.mp3");
        app.Press("PrimaryButton");

        // Unsupported network locations are rejected without interrupting the
        // currently captioned local video.
        Wait.For(() => app.LogText.Contains("Network streams and remote locations are not supported.", StringComparison.Ordinal),
            "a clear unsupported-network message");
        // The fixture carries a real embedded title (Basquiat), not necessarily
        // its filesystem name (Captioned). Refusal must preserve that media.
        Assert.Equal(originalTitle, app.Text("NowPlaying"));
        Assert.Equal("Local video captions", app.Text("SubtitleText"));
        Assert.Contains("Captions are available", app.Find("Stage").Current.HelpText, StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void FaultingAfterCaptionedVideoClearsThePreviousCaptionAndSeekActions()
    {
        var video = _scene.Video("BeforeFault.mp4");
        File.WriteAllText(Path.Combine(_scene.Media, "BeforeFault.en.srt"),
            "1\n00:00:00,000 --> 00:10:00,000\nCaption before fault\n");
        var bad = Path.Combine(_scene.Media, "unreadable.mp4");
        File.WriteAllText(bad, "not a playable media file");

        using var app = AppProcess.Start([video]);
        Wait.For(() => app.IsShown("SubtitleText")
            && app.Text("SubtitleText") == "Caption before fault",
            "captions from the first playable video to render");

        // Open the malformed file explicitly; the tiny H.264 fixture may
        // auto-advance before the queued Next command is delivered.
        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLocation);
        ((ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue(bad);
        app.Press("PrimaryButton");
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

    [Fact]
    public void SelectingAnAlternateMovieEncodingUpdatesTheCurrentVersionMenu()
    {
        var movie = _scene.Film("Resolution.Film.2023.1080p.mp4");
        _scene.Film("Resolution.Film.2023.720p.mp4");
        _scene.Ready(settings => settings with { Theme = ThemeChoice.Dark });
        using var app = AppProcess.Start([movie], root: _scene.Root);
        Wait.For(() => app.Find("Stage").Current.Name == "MOVIE playback stage"
            && app.IsShown("MovieVersionButton"),
            "a film with two verified local resolutions");

        ((TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern)).Resize(420, 340);
        Wait.For(() => app.Window.Current.BoundingRectangle is { Width: var w, Height: var h }
            && Math.Abs(w - 420) < 2 && Math.Abs(h - 340) < 2,
            "a compact movie-version playback layout");
        var window = app.Window.Current.BoundingRectangle;
        var picker = app.Find("MovieVersionButton").Current.BoundingRectangle;
        Assert.True(picker.Left >= window.Left && picker.Right <= window.Right
            && picker.Top >= window.Top && picker.Bottom <= window.Bottom,
            "The touch-sized movie resolution picker must stay inside the compact window.");

        app.Press("MovieVersionButton");
        Wait.For(() => app.IsShown("MovieVersion-1"),
            "the alternate resolution menu action to appear");
        app.Press("MovieVersion-1");
        // Native MenuFlyout dismissal and replacement stream startup are
        // asynchronous. Opening the picker while the prior flyout is closing
        // can reopen its stale toggle state rather than the new stream's menu.
        Wait.For(() => !app.IsShown("MovieVersion-1") && app.IsShown("MovieVersionButton"),
            "the old movie resolution flyout to close after switching streams");
        var currentVersion = app.OpenMenu("MovieVersion-1", "MovieVersionButton");
        Wait.For(() => currentVersion.Current.Name.Contains("currently playing", StringComparison.Ordinal),
            "the selected native encoding to be announced accessibly");
        Assert.True(app.Find("MovieVersion-1").Current.IsEnabled);
        var selected = (TogglePattern)app.Find("MovieVersion-1").GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.On, selected.Current.ToggleState);
        Assert.Equal("MOVIE playback stage", app.Find("Stage").Current.Name);
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
            var versionBounds = app.Find("MovieVersion-1").Current.BoundingRectangle;
            Assert.True(versionBounds.Width >= 42 && versionBounds.Height >= 42,
                "An alternate movie resolution must be touch-sized and keyboard-accessible.");
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
