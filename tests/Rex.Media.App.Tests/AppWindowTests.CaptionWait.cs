using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The wait for generated captions: playback pauses behind its overlay until enough is heard, Play
/// now trades the wait away, and media that already has subtitles never waits. CI has no speech
/// model, so REXPLAYER_FAKE_SPEECH stands in for one, taking a set time over each piece.
/// </summary>
public sealed partial class AppWindowTests
{
    private string HoldingRoot() => RootWith(new PlayerSettings { Transcription = new TranscriptionSettings { HoldForSubtitles = true } });

    private static Dictionary<string, string> FakeSpeech(double secondsPerPiece) =>
        new() { ["REXPLAYER_FAKE_SPEECH"] = secondsPerPiece.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    [Fact]
    [Capability("SUB-21")]
    public void PlaybackWaitsBehindTheOverlayUntilEnoughCaptionsAreReady()
    {
        using var app = AppProcess.Start([Music(120, audiobook: true)], HoldingRoot(), environment: FakeSpeech(3));
        Wait.For(() => app.Position > 0, "the audiobook to play");

        app.Run(CommandCatalog.Transcribe);
        Wait.For(() => app.IsShown("CaptionBufferOverlay") && app.Status().State == "Paused", "playback to wait for captions",
            () => $"State: {app.Status().State}; rail: {app.Text("SubtitleBufferState")}; log: {app.LogText}");
        // The overlay's heading says what is happening once; its words add how far it has got.
        Assert.DoesNotContain("Preparing captions", app.Text("CaptionBufferOverlayText"), StringComparison.Ordinal);
        Assert.Contains("Playback will start automatically", app.Text("CaptionBufferOverlayText"), StringComparison.Ordinal);
        Assert.True(app.IsShown("CaptionBufferPlayNow"), "The wait can always be skipped.");

        // The first piece heard leads far enough: playing carries on by itself.
        Wait.For(() => !app.IsShown("CaptionBufferOverlay") && app.Status().State == "Playing", "playback to carry on once captions lead");
        Assert.DoesNotContain("Preparing captions", app.Text("SubtitleBufferState"), StringComparison.Ordinal);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("SUB-21")]
    public void PlayNowEndsTheCaptionWaitAtOnce()
    {
        // Pieces slower than the test's patience: only Play now can end this wait.
        using var app = AppProcess.Start([Music(120, audiobook: true)], HoldingRoot(), environment: FakeSpeech(600));
        Wait.For(() => app.Position > 0, "the audiobook to play");

        app.Run(CommandCatalog.Transcribe);
        Wait.For(() => app.IsShown("CaptionBufferOverlay") && app.Status().State == "Paused", "playback to wait for captions");
        var viewport = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        viewport.Resize(1000, 700);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1000) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 700) < 2,
            "the pinned caption-loading viewport");
        Assert.Contains("Playback will start automatically", app.Text("CaptionBufferOverlayText"), StringComparison.Ordinal);
        Screenshots.Match(app, "caption-buffer-loading");
        app.Press("CaptionBufferPlayNow");
        Wait.For(() => !app.IsShown("CaptionBufferOverlay") && app.Status().State == "Playing", "Play now to start playback");
        PlayOn(app, 1, "the audiobook to play on without captions");
        Assert.False(app.IsShown("CaptionBufferOverlay"), "Once declined, the wait does not come back for the same media.");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("SUB-21")]
    public void MediaWithItsOwnSubtitlesNeverWaitsForGeneratedOnes()
    {
        var audiobook = Music(120, audiobook: true);
        File.WriteAllText(Path.ChangeExtension(audiobook, ".srt"), "1\n00:00:00,000 --> 00:02:00,000\nTerminator\n");
        using var app = AppProcess.Start([audiobook], HoldingRoot(), environment: FakeSpeech(600));
        Wait.For(() => app.Position > 0 && app.IsShown("SubtitleText"), "the audiobook and its own subtitles");

        app.Run(CommandCatalog.Transcribe);
        Wait.For(() => app.LogText.Contains("Transcribing from here", StringComparison.Ordinal), "transcription to start");
        PlayOn(app, 2, "the audiobook to play on while transcription runs");
        Assert.False(app.IsShown("CaptionBufferOverlay"), "Subtitles already here mean there is nothing to wait for.");
        Assert.Equal("Playing", app.Status().State);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("SUB-21")]
    public void SongsOfferLyricsInsteadOfStartingSpeechTranscription()
    {
        using var app = AppProcess.Start([Music(20)], HoldingRoot(), environment: FakeSpeech(600));
        Wait.For(() => app.Position > 0, "the song to play");

        app.Run(CommandCatalog.Transcribe);
        Wait.For(() => app.LogText.Contains("Songs are not transcribed", StringComparison.Ordinal),
            "the song to be directed to lyrics");
        Assert.False(app.IsShown("CaptionBufferOverlay"), "Songs do not wait for generated speech captions.");
        Assert.Equal("Playing", app.Status().State);
        Assert.Equal(0, app.Close());
    }
}
