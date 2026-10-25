using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Cross-platform player control paths exercised independently of a native window.</summary>
public sealed class PlaybackOptionsRegressionTests
{
    [Fact]
    [Capability("AU-15")]
    public void PitchPreferenceAppliesBeforeOpeningAndWhileAPlayerIsReady()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        var controller = harness.Controller;
        harness.Files["a.wav"] = Count(0, 8000 * 5);

        controller.SetPreservePitch(false);
        Assert.False(controller.Settings.PreservePitch);
        Assert.Equal("Speed changes pitch", harness.Messages[^1]);

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Info is not null);
        controller.SetPreservePitch(true);
        Assert.True(controller.Settings.PreservePitch);
        Assert.Equal("Pitch-preserving speed", harness.Messages[^1]);
        Assert.True(harness.Changes > 0);
    }

    [Fact]
    [Capability("PB-07")]
    public void AudioTrackPickerRejectsUnavailableTracksAndKeepsCurrentOne()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        var controller = harness.Controller;

        controller.SelectAudioTrack(0);
        Assert.Equal("There are no audio tracks to choose from.", harness.Messages[^1]);

        harness.Files["a.wav"] = Count(0, 8000 * 5);
        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack is not null);

        controller.SelectAudioTrack(77);
        Assert.Equal("That audio track is not part of this file.", harness.Messages[^1]);
        controller.SelectAudioTrack(0);
        Assert.Equal(0, controller.AudioTrack);
        Assert.Equal("That audio track is not part of this file.", harness.Messages[^1]);
    }

    [Theory]
    [InlineData(ResumeBehavior.Never)]
    [InlineData(ResumeBehavior.Ask)]
    [Capability("PB-11")]
    public void ResumeChoiceCanBeDismissedWithoutChangingStoredProgress(ResumeBehavior behavior)
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings
        {
            ResumeMode = behavior,
        });
        harness.Files["a.wav"] = Count(0, 8000 * 30);
        harness.Controller.Memory.Left("a.wav", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));

        harness.Controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item is not null);
        Assert.Equal(TimeSpan.Zero, harness.Controller.Position);
        Assert.Equal(behavior == ResumeBehavior.Ask ? TimeSpan.FromSeconds(12) : null,
            harness.Controller.PendingResumeAt);

        var before = harness.Changes;
        harness.Controller.DismissPendingResume();
        Assert.Null(harness.Controller.PendingResumeAt);
        Assert.True(harness.Changes > before);
        Assert.Equal(TimeSpan.FromSeconds(12), harness.Controller.Memory.ResumePoint("a.wav"));
    }
}
