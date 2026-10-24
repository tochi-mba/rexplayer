using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The transport, driven as a person would: the buttons, every jump length, the seek bar, Go to a
/// time, and what each says when the media cannot be moved through.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>A quiet mono WAV of <paramref name="seconds"/> seconds, named <paramref name="name"/>.</summary>
    private string Named(string name, int seconds)
    {
        var path = Path.Combine(_media, name + ".wav");
        File.WriteAllBytes(path, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * seconds])).Build());
        return path;
    }

    /// <summary>Waits until the player reports a position within half a second of <paramref name="seconds"/>.</summary>
    private static void WaitForPosition(AppProcess app, double seconds, string what) =>
        Wait.For(() => Math.Abs(app.Position - seconds) < 0.5, what, () => $"The player is at {app.Position:0.00} s.");

    private static bool IsEnabled(AppProcess app, string automationId) => app.Find(automationId).Current.IsEnabled;

    [Fact]
    [Capability("PB-02")]
    [Capability("PB-03")]
    [Capability("PB-04")]
    public void TheTransportButtonsJumpsAndSeekBarMoveThroughAPlaylist()
    {
        using var app = AppProcess.Start([Named("Lonely At The Top", 400), Named("Terminator", 30), Named("Sungba", 30)]);
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top" && app.Position > 0, "the first song to play");

        // Paused, every jump lands exactly where its length says.
        app.Press("PlayPauseButton");
        Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Play" && app.Status().State == "Paused", "the pause");
        app.Press("StartOverButton");
        WaitForPosition(app, 0, "the start");
        (string Command, double To)[] jumps =
        [
            (CommandCatalog.JumpForwardLong, 300), (CommandCatalog.JumpForwardMedium, 360),
            (CommandCatalog.JumpForwardShort, 370), (CommandCatalog.JumpForwardVeryShort, 373),
            (CommandCatalog.JumpBackVeryShort, 370), (CommandCatalog.JumpBackShort, 360),
            (CommandCatalog.JumpBackMedium, 300), (CommandCatalog.JumpBackLong, 0),
        ];
        foreach (var (command, to) in jumps)
        {
            app.Run(command);
            WaitForPosition(app, to, command + " to land at " + to);
        }

        // A click on the seek bar goes straight there, still paused.
        app.SetValue("SeekBar", 120);
        WaitForPosition(app, 120, "the seek bar to move the song");
        Wait.For(() => app.Text("Elapsed") == "2:00", "the elapsed time to follow the seek bar");
        Wait.For(() => (app.Find("SeekBar").GetCurrentPropertyValue(AutomationElement.HelpTextProperty) as string)
            ?.Contains("2:00 of 6:40", StringComparison.Ordinal) == true,
            "keyboard and screen readers to learn the real timeline position and duration");
        Assert.Equal("Paused", app.Status().State);

        app.Press("PlayPauseButton");
        Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Pause" && app.Position > 120.2, "playing to carry on from there");

        // Previous goes back to the start of a song that has played a while, then to the song before.
        app.Press("NextButton");
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "Next to start the second song");
        app.Run(CommandCatalog.JumpForwardShort);
        Wait.For(() => app.Position > 5, "the second song to move on");
        app.Press("PreviousButton");
        Wait.For(() => app.Position < 3 && app.Text("NowPlaying") == "Terminator", "Previous to restart the song");
        app.Press("PreviousButton");
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "Previous again to go to the song before");

        app.Press("StopButton");
        Wait.For(() => app.Status().State == "Idle" && app.Text("Elapsed") == "0:00", "Stop to return to the start");
        Assert.Equal("Play", app.Find("PlayPauseButton").Current.Name);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-05")]
    public void GoToATimeTakesATypedTimeAndExplainsOneItCannotRead()
    {
        using var app = AppProcess.Start([Named("Lonely At The Top", 120)]);
        Wait.For(() => app.Position > 0, "the song to play");

        app.Run(CommandCatalog.GoToTime);
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("1:05");
        app.Press("PrimaryButton");
        Wait.For(() => app.Position is >= 65 and < 70, "the song to go to 1:05");

        app.Run(CommandCatalog.GoToTime);
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("soon");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("\"soon\" is not a time", StringComparison.Ordinal), "the reason the time was not used");
        Assert.True(app.Position >= 65, "A time that cannot be read leaves the song where it was.");

        // Cancelled, the dialog leaves everything as it was.
        app.Run(CommandCatalog.GoToTime);
        ((ValuePattern)app.Find("GoToTimeBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("0:10");
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("GoToTimeBox"), "the dialog to close");
        Assert.True(app.Position >= 65, "A cancelled Go to a time does not seek.");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [Capability("PB-05")]
    [InlineData("https://example.invalid/stream.mp4")]
    [InlineData("rtsp://camera.invalid/live")]
    [InlineData("C:\\does-not-exist\\movie.mp4")]
    public void OpenLocationRefusesUnsupportedNetworkAndMissingSourcesWithoutCrashing(string location)
    {
        using var app = AppProcess.Start();
        app.Run(CommandCatalog.OpenLocation);
        ((ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue(location);
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("could not be found", StringComparison.Ordinal)
                || app.LogText.Contains("Network streams and remote locations are not supported.", StringComparison.Ordinal),
            "a clear unsupported-local-only or missing path message");
        Assert.Equal("Idle", app.Status().State);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-05")]
    public void HoverAlwaysShowsRealSeekTimeWhenExpensivePreviewThumbnailsAreDisabled()
    {
        using var app = AppProcess.Start([Named("Hover", 200)], extraSettings: "\"seekPreview\": false");
        Wait.For(() => app.IsShown("SeekBar") && app.Position > 0, "a seekable song");
        Pointer.Hover(app, app.Find("SeekBar"), fraction: 0.75);
        Wait.For(() => app.IsShown("SeekPreview")
            && TimeText.TryParse(app.Text("SeekPreviewTime")) is { TotalSeconds: > 100 and < 200 },
            "an accurate timeline hover without a decoded picture");
        Assert.False(app.IsShown("SeekPreviewPicture"), "Thumbnails remain disabled; only honest time is shown.");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-05")]
    public void MediaThatCannotBeMovedThroughSaysSoInsteadOfAskingForATime()
    {
        // A live stream: no length, so nothing to seek in.
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mkv", "live-opus.webm")]);
        Wait.For(() => app.Status().Title is { Length: > 0 }, "the stream to open");

        Assert.False(IsEnabled(app, "SeekBar"), "The seek bar is off for media that cannot be moved through.");
        Assert.False(IsEnabled(app, "StartOverButton"), "Start over is off for media that cannot be moved through.");
        app.Run(CommandCatalog.GoToTime);
        Wait.For(() => app.LogText.Contains(PlayerController.CannotSeekNotice, StringComparison.Ordinal), "the notice that it cannot be moved through");
        Assert.False(app.IsShown("GoToTimeBox"), "No time is asked for.");
        Assert.Equal(0, app.Close());
    }
}
