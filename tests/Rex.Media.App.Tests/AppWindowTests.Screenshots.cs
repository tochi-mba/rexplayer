using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// How the window looks, compared with CI's baselines (see <see cref="Screenshots"/>): the empty
/// window in both themes and Preferences at a normal and a small size. Behaviour is asserted
/// through UI Automation elsewhere; these only catch layout and drawing that goes wrong.
/// </summary>
public sealed partial class AppWindowTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [Capability("UI-13")]
    public void TheEmptyWindowLooksTheSameInEachTheme(string theme)
    {
        using var app = AppStartedAt(1000, 700, theme);
        Wait.For(() => app.IsShown("IdleTitle"), "the empty window's welcome");

        Screenshots.Match(app, "empty-window-" + theme.ToLowerInvariant());
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(1000, 760, "normal")]
    [InlineData(720, 640, "small")]
    [Capability("UI-10")]
    public void PreferencesLooksTheSameAtEachSize(int width, int height, string size)
    {
        using var app = AppStartedAt(width, height, "Light");
        app.Run(CommandCatalog.Preferences);
        Wait.For(() => app.IsShown("PreferencesSection") && app.IsShown("PrimaryButton"), "Preferences to open");

        Screenshots.Match(app, "preferences-" + size);
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("PreferencesSection"), "Preferences to close");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData(720, 540, "compact", "Dark")]
    [InlineData(420, 340, "minimum", "Dark")]
    [InlineData(720, 540, "compact", "Light")]
    [InlineData(420, 340, "minimum", "Light")]
    [Capability("UI-10")]
    public void TheMediaStageAndPrimaryTransportFitCompactAndMinimumWindows(
        int width, int height, string size, string theme)
    {
        using var app = AppStartedAt(width, height, theme);
        Wait.For(() => app.IsShown("IdleTitle") && app.IsShown("PlayPauseButton"),
            "the stage and transport to be visible");

        var window = app.Window.Current.BoundingRectangle;
        var stage = app.Find("Stage").Current.BoundingRectangle;
        var play = app.Find("PlayPauseButton").Current.BoundingRectangle;
        Assert.True(stage.Width > 0 && stage.Height > 0, "The player stage should retain a usable area.");
        Assert.True(play.Width > 0 && play.Height > 0
            && play.Left >= window.Left && play.Right <= window.Right
            && play.Top >= window.Top && play.Bottom <= window.Bottom,
            "The primary playback action must remain within the resized window.");

        Screenshots.Match(app, "stage-welcome-" + size + (theme == "Dark" ? "" : "-light"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("META-09")]
    public void HidingFocusedLyricsReturnsFocusAndRemovesUnavailableScreenReaderHelp()
    {
        var song = Song(30);
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"),
            "[00:00.00]Opening line\n[00:20.00]Ending line\n");
        using var app = AppProcess.Start([song]);
        Wait.For(() => app.IsShown("Lyrics")
            && app.Find("Stage").Current.HelpText.Contains("Lyrics are available", StringComparison.Ordinal),
            "the lyric controls and their accessible guidance");

        app.Find("LyricLine-0").SetFocus();
        Wait.For(() => app.Find("LyricLine-0").Current.HasKeyboardFocus,
            "the lyric jump to hold keyboard focus");
        app.Run(CommandCatalog.ToggleLyrics);
        Wait.For(() => !app.IsShown("Lyrics"), "the lyric controls to disappear");
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "keyboard focus to return to the permanent transport");
        Assert.DoesNotContain("Lyrics are available", app.Find("Stage").Current.HelpText,
            StringComparison.Ordinal);

        app.Run(CommandCatalog.ToggleLyrics);
        Wait.For(() => app.IsShown("Lyrics")
            && app.Find("Stage").Current.HelpText.Contains("Lyrics are available", StringComparison.Ordinal),
            "lyrics and their accessible instructions to return together");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("META-09")]
    public void MovingFromFocusedSongLyricsToAPictureRestoresTransportFocus()
    {
        var song = Song(30);
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"),
            "[00:00.00]Opening line\n[00:20.00]Ending line\n");
        var picture = RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg");
        using var app = AppProcess.Start([song, picture]);
        Wait.For(() => app.IsShown("Lyrics"), "the song lyrics to appear");
        app.Find("LyricLine-0").SetFocus();
        Wait.For(() => app.Find("LyricLine-0").Current.HasKeyboardFocus,
            "the timed lyric to receive keyboard focus");

        app.Run(CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "picture playback to replace the song");
        Assert.False(app.IsShown("Lyrics"));
        Assert.False(app.Find("SeekBar").Current.IsEnabled);
        Wait.For(() => app.Find("PlayPauseButton").Current.HasKeyboardFocus,
            "focus to return to the permanent Play/Pause control");
        Assert.DoesNotContain("Lyrics are available", app.Find("Stage").Current.HelpText,
            StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    public void SwitchingFromAudioToVideoPreservesTransportFocusAndAutomationIdentity()
    {
        using var app = AppProcess.Start([Song(30), RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")]);
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage",
            "the direct-file audio profile to become active");
        var play = app.Find("PlayPauseButton");
        play.SetFocus();
        Wait.For(() => play.Current.HasKeyboardFocus, "the primary transport to take keyboard focus");

        app.Run(CommandCatalog.Next);
        Wait.For(() => app.Find("Stage").Current.Name == "VIDEO playback stage",
            "the stage to adopt the video stream");
        var sameTransport = app.Find("PlayPauseButton");
        Assert.Equal("PlayPauseButton", sameTransport.Current.AutomationId);
        Assert.True(sameTransport.Current.HasKeyboardFocus,
            "Changing a media profile must not steal keyboard focus from the transport.");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-10")]
    [Capability("META-09")]
    public void ResizingAnAudioStageKeepsPositionFocusAndPrimaryControls()
    {
        using var app = AppProcess.Start([Song(30)], extraSettings: "\"theme\": \"Dark\"");
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage"
            && app.Position > 0, "the audio stream to become ready");
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "the player to pause before layout captures");
        var position = app.Position;
        var transport = app.Find("PlayPauseButton");
        transport.SetFocus();
        var resize = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        foreach (var (width, height, name) in new[]
        {
            (420, 340, "minimum"),
            (720, 540, "compact"),
            (1000, 720, "windowed"),
        })
        {
            resize.Resize(width, height);
            Wait.For(() => app.Window.Current.BoundingRectangle is { Width: var w, Height: var h }
                && Math.Abs(w - width) < 2 && Math.Abs(h - height) < 2,
                name + " playback layout");
            Assert.Equal("AUDIO playback stage", app.Find("Stage").Current.Name);
            Assert.Equal("Paused", app.Status().State);
            Assert.InRange(Math.Abs(app.Position - position), 0, 1);
            Assert.Equal("PlayPauseButton", app.Find("PlayPauseButton").Current.AutomationId);
            Assert.True(app.Find("PlayPauseButton").Current.HasKeyboardFocus,
                "A resize must preserve focus on the existing transport.");

            var bounds = app.Window.Current.BoundingRectangle;
            var play = app.Find("PlayPauseButton").Current.BoundingRectangle;
            Assert.True(play.Left >= bounds.Left && play.Right <= bounds.Right
                && play.Top >= bounds.Top && play.Bottom <= bounds.Bottom,
                "The playback action must remain visible in " + name + " layout.");
            Screenshots.Match(app, "stage-audio-" + name);
        }

        Pointer.Click(app, app.Find("PlayPauseButton"));
        Wait.For(() => app.Status().State == "Playing",
            "a mouse click to resume after resizing the audio stage");
        Pointer.Click(app, app.Find("PlayPauseButton"));
        Wait.For(() => app.Status().State == "Paused",
            "a mouse click to pause without losing the active player");

        Pointer.Key(app, "PlayPauseButton", 0x20);
        Wait.For(() => app.Status().State == "Playing",
            "a Space key press on the resized Play/Pause action to resume playback");
        Pointer.Key(app, "PlayPauseButton", 0x20);
        Wait.For(() => app.Status().State == "Paused",
            "a Space key press to pause without losing transport focus");
        Assert.True(app.Find("PlayPauseButton").Current.HasKeyboardFocus);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("UI-04")]
    public void FullScreenRetainsThePausedAudioStageAndRestoresWindowedTransport()
    {
        using var app = AppProcess.Start([Song(30)], RootWith(new Rex.Media.Settings.PlayerSettings
        {
            ControlsHideSeconds = 120,
            Theme = Rex.Media.Settings.ThemeChoice.Dark,
        }));
        Wait.For(() => app.Find("Stage").Current.Name == "AUDIO playback stage"
            && app.Position > 0, "audio to become ready");
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.Status().State == "Paused", "a stable audio frame");

        app.Run(CommandCatalog.ToggleFullScreen);
        Wait.For(() => !app.IsShown("Menu"), "the full-screen media stage");
        Assert.Equal("AUDIO playback stage", app.Find("Stage").Current.Name);
        Screenshots.Match(app, "stage-audio-fullscreen");

        app.Run(CommandCatalog.LeaveFullScreen);
        Wait.For(() => app.IsShown("Menu") && app.IsShown("PlayPauseButton"),
            "the normal window and transport");
        Assert.Equal("Paused", app.Status().State);
        Assert.Equal(0, app.Close());
    }

    /// <summary>The app in a fixed theme, its window at a fixed size, so its shots compare run to run.</summary>
    private static AppProcess AppStartedAt(int width, int height, string theme)
    {
        var app = AppProcess.Start(extraSettings: $"\"theme\": \"{theme}\"");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(width, height);
        Wait.For(() => app.Window.Current.BoundingRectangle is { Width: var w, Height: var h } && Math.Abs(w - width) < 2 && Math.Abs(h - height) < 2,
            $"the window to be {width}x{height}");
        return app;
    }
}
