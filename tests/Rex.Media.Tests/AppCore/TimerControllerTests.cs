using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Stopping or pausing after the current item (PB-13), and the sleep timer (PB-21).</summary>
public sealed class TimerControllerTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 23, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ControllerHarness Harness(bool autoPlay = true, SleepChoice sleep = SleepChoice.Pause, TimeProvider? time = null)
    {
        var harness = new ControllerHarness(autoPlay: autoPlay, settings: new PlayerSettings { TitleSeconds = 0, SleepAction = sleep }, time: time);
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(500, 400);
        harness.Files["long.wav"] = Count(0, 8000 * 30);
        return harness;
    }

    [Fact]
    [Capability("PB-13")]
    public void StopAfterThisItemStopsThereOnce()
    {
        using var harness = Harness();
        var controller = harness.Controller;
        controller.Open(["a.wav", "b.wav"]);
        Assert.True(controller.Execute(CommandCatalog.StopAfterCurrent));
        Assert.Equal(("Stop after this item", AfterItem.Stop), (harness.Messages[^1], controller.AfterCurrent));

        harness.PumpUntil(c => c.State == SessionState.Idle);

        Assert.Equal(Expected((0, 400)), harness.Played());
        Assert.Equal(AfterItem.Continue, controller.AfterCurrent);
        controller.ToggleAfterCurrent(AfterItem.Pause);
        controller.ToggleAfterCurrent(AfterItem.Pause);
        Assert.Equal(("Play on after this item", AfterItem.Continue), (harness.Messages[^1], controller.AfterCurrent));
    }

    [Fact]
    [Capability("PB-13")]
    public void PauseAfterThisItemLeavesTheNextReadyAtItsStart()
    {
        using var harness = Harness();
        var controller = harness.Controller;
        controller.Open(["a.wav", "b.wav"]);
        controller.Execute(CommandCatalog.PauseAfterCurrent);

        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "b.wav");
        Assert.Equal(Expected((0, 400)), harness.Played());

        controller.Execute(CommandCatalog.PlayPause);
        harness.PumpUntil(c => c.State == SessionState.Ended && c.Playlist.Current is null);
        Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
    }

    [Fact]
    public void PauseAfterTheLastItemSimplyEnds()
    {
        using var harness = Harness();
        harness.Controller.Open(["a.wav"]);
        harness.Controller.ToggleAfterCurrent(AfterItem.Pause);

        harness.PumpUntil(c => c.State == SessionState.Ended && c.Playlist.Current is null);
    }

    [Theory]
    [InlineData(AfterItem.Stop, SessionState.Idle)]
    [InlineData(AfterItem.Pause, SessionState.Paused)]
    public void AskedForAfterTheNextWasQueuedItStillHolds(AfterItem after, SessionState state)
    {
        using var harness = Harness();
        var controller = harness.Controller;
        controller.Open(["a.wav", "long.wav"]);

        // Opened, a.wav is short enough that long.wav is queued at once.
        harness.PumpUntil(c => c.Info is not null);
        controller.ToggleAfterCurrent(after);

        harness.PumpUntil(c => c.Item?.Location == "long.wav" && c.State == state);
        Assert.Equal(AfterItem.Continue, controller.AfterCurrent);
    }

    [Fact]
    [Capability("PB-21")]
    public void TheSleepTimerFadesThenPauses()
    {
        var time = new ManualTime();
        using var harness = Harness(time: time);
        var controller = harness.Controller;
        controller.SleepTick();
        Assert.False(controller.SleepTimerSet);

        controller.Open(["long.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Playing);
        Assert.True(controller.Execute(CommandCatalog.SleepPrefix + "15"));
        Assert.Equal("Sleep timer: 15 minutes", harness.Messages[^1]);
        Assert.Equal(TimeSpan.FromMinutes(15), controller.SleepRemaining);

        time.Now += TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(5);
        controller.SleepTick();
        Assert.Equal(0.5, controller.FadeLevel, 3);

        time.Now += TimeSpan.FromSeconds(6);
        controller.SleepTick();
        harness.PumpUntil(c => c.State == SessionState.Paused);
        Assert.Equal(("Sleep timer: good night", 1.0, false), (harness.Messages[^1], controller.FadeLevel, controller.SleepTimerSet));
    }

    [Fact]
    [Capability("PB-21")]
    public void TheSleepTimerCanStopAndBeTurnedOff()
    {
        var time = new ManualTime();
        using var harness = Harness(sleep: SleepChoice.Stop, time: time);
        var controller = harness.Controller;
        controller.Open(["long.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Playing);

        controller.SetSleepTimer(TimeSpan.FromMinutes(1));
        Assert.Equal("Sleep timer: 1 minute", harness.Messages[^1]);
        controller.Execute(CommandCatalog.SleepOff);
        Assert.Equal(("Sleep timer off", (TimeSpan?)null), (harness.Messages[^1], controller.SleepRemaining));

        controller.SetSleepTimer(TimeSpan.FromMinutes(-1));
        controller.SleepTick();
        Assert.Equal(SessionState.Idle, controller.State);

        // Paused already when the time comes: it stays paused, and the timer is done.
        controller.Open(["long.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Playing);
        controller.Settings = controller.Settings with { SleepAction = SleepChoice.Pause };
        controller.Execute(CommandCatalog.PlayPause);
        harness.PumpUntil(c => c.State == SessionState.Paused);
        controller.SetSleepTimer(TimeSpan.Zero);
        controller.SleepTick();
        Assert.False(controller.SleepTimerSet);
        Assert.Equal(SessionState.Paused, controller.State);
    }

    [Fact]
    [Capability("PB-21")]
    public void TheSleepTimerCanWaitForTheEndOfTheItem()
    {
        using var harness = Harness(autoPlay: false);
        var controller = harness.Controller;
        controller.Open(["long.wav", "a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.True(controller.Execute(CommandCatalog.SleepAtEndOfItem));
        Assert.Equal(("Sleep timer: at the end of this item", true, (TimeSpan?)null), (harness.Messages[^1], controller.SleepTimerSet, controller.SleepRemaining));

        // The sound fades over the item's last seconds.
        controller.Seek(TimeSpan.FromSeconds(27));
        controller.SleepTick();
        Assert.Equal(0.3, controller.FadeLevel, 3);

        controller.Execute(CommandCatalog.PlayPause);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "a.wav");
        Assert.Equal((1.0, false), (controller.FadeLevel, controller.SleepTimerSet));
    }

    [Fact]
    public void FilesFromLaunchesStartedTogetherJoinOnePlaylist()
    {
        using var harness = Harness(autoPlay: false);
        var controller = harness.Controller;
        var start = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        // Explorer opening three selected files: one launch each, started together, arriving in any order.
        controller.OpenHandedOver(["b.wav"], enqueue: false, start + TimeSpan.FromMilliseconds(40));
        controller.OpenHandedOver(["a.wav"], enqueue: false, start);
        controller.OpenHandedOver(["long.wav"], enqueue: false, start + TimeSpan.FromMilliseconds(90));
        Assert.Equal(["b.wav", "a.wav", "long.wav"], controller.Playlist.Items.Select(item => item.Location));

        // A launch started later plays on its own, unless the user asked for it to join.
        controller.OpenHandedOver(["b.wav"], enqueue: false, start + TimeSpan.FromSeconds(8));
        Assert.Equal(["b.wav"], controller.Playlist.Items.Select(item => item.Location));
        controller.OpenHandedOver(["a.wav"], enqueue: true, start + TimeSpan.FromMinutes(1));
        Assert.Equal(2, controller.Playlist.Items.Count);
        Assert.Throws<ArgumentNullException>(() => controller.OpenHandedOver(null!, false, start));
    }

    [Fact]
    public void SleepCommandsSayHowLong()
    {
        Assert.Equal(45, CommandCatalog.SleepMinutesOf("sleep-45"));
        Assert.Null(CommandCatalog.SleepMinutesOf(CommandCatalog.SleepOff));
        Assert.Null(CommandCatalog.SleepMinutesOf("sleep-0"));
        Assert.Null(CommandCatalog.SleepMinutesOf(CommandCatalog.Mute));
        Assert.Equal(PlayerController.SleepMinutes.Select(m => CommandCatalog.SleepPrefix + m), CommandCatalog.All.Select(c => c.Id).Where(id => CommandCatalog.SleepMinutesOf(id) is not null));
        Assert.Throws<ArgumentNullException>(() => CommandCatalog.SleepMinutesOf(null!));
    }
}
