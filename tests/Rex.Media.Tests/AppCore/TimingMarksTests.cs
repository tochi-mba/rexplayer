using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.Tests.Engine;

namespace Rex.Media.Tests.AppCore;

/// <summary>The timing lines a start writes, which scripts/measure-startup.ps1 reads.</summary>
public sealed class TimingMarksTests
{
    private static readonly DateTimeOffset Launched = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static List<string> Marks(RexLog log) =>
        [.. log.Tail(1000).Where(entry => entry.Source == Milestones.LogSource).Select(entry => entry.Message.Split(' ')[0])];

    [Fact]
    public void StartingAnItemMarksEachStepUpToItsFirstPictureAndSound()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 }, pictures: true, milestones: new Milestones(log, Launched));
        harness.Files["clip.mkv"] = VideoPlaybackTests.Clip(5);

        harness.Controller.Open(["clip.mkv"]);
        harness.WaitForEngine<EndedEvent>();

        var marks = Marks(log);
        Assert.Equal(["start", "open", "demuxer", "audio-decoder", "video-decoder", "opened", "audio-device"], marks.Take(7));
        Assert.Contains("first-audio", marks);
        Assert.True(marks.IndexOf("first-frame") < marks.IndexOf("target-frame"));
        Assert.Single(marks, mark => mark == "first-frame");
    }

    [Fact]
    public void APausedSeekWritesItsTimingMark()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, milestones: new Milestones(log, Launched));
        harness.Files["clip.wav"] = ControllerHarness.Count(0, 8000 * 3);
        harness.Controller.Open(["clip.wav"]);
        harness.PumpUntil(controller => controller.State == SessionState.Ready && controller.CanSeek);

        harness.Controller.Seek(TimeSpan.FromMilliseconds(80));
        harness.PumpUntil(_ => Marks(log).Contains("seek"));

        Assert.Contains("seek", Marks(log));
    }

    [Fact]
    public void WithoutMilestonesNothingIsMarked()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(log: log);
        harness.Files["a.wav"] = ControllerHarness.Count(0, 400);

        harness.Controller.Open(["a.wav"]);
        harness.WaitForEngine<EndedEvent>();

        Assert.Null(harness.Controller.Milestones);
        Assert.Empty(Marks(log));
    }
}
