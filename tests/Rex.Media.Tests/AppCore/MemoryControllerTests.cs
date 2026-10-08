using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>What the player remembers as it plays: where things were left, bookmarks, quick slots, recent media and the queue.</summary>
public sealed class MemoryControllerTests
{
    private static readonly byte[] HalfMinute = Count(0, 8000 * 30);

    private static ControllerHarness Harness(RexStore store, ResumeChoice resume = ResumeChoice.Ask, bool autoPlay = false, bool keepHistory = true, bool restoreQueue = true)
    {
        var harness = new ControllerHarness(autoPlay: autoPlay, settings: new PlayerSettings { TitleSeconds = 0, ResumePlayback = resume, KeepHistory = keepHistory, RestoreQueue = restoreQueue }, store: store);
        harness.Files["a.wav"] = HalfMinute;
        harness.Files["b.wav"] = HalfMinute;
        return harness;
    }

    private static void OpenReady(ControllerHarness harness, string location)
    {
        harness.Controller.Open([location]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == location);
    }

    /// <summary>Leaves a.wav at 15 s by opening b.wav, as a user moving on would.</summary>
    private static void LeaveAHalfWay(ControllerHarness harness)
    {
        OpenReady(harness, "a.wav");
        harness.Controller.Seek(TimeSpan.FromSeconds(15));
        OpenReady(harness, "b.wav");
    }

    [Fact]
    [Capability("PB-11")]
    public void TheWindowMayOfferToGoBackToWhereAFileWasLeft()
    {
        using var harness = Harness(RexStore.InMemory());
        var controller = harness.Controller;
        Assert.True(controller.Execute(CommandCatalog.Resume));
        Assert.Equal("There is nowhere to go back to.", harness.Messages[^1]);

        LeaveAHalfWay(harness);
        Assert.Null(controller.ResumeOffer);
        OpenReady(harness, "a.wav");

        Assert.Equal(TimeSpan.FromSeconds(15), controller.ResumeOffer);
        Assert.Equal(TimeSpan.Zero, controller.Position);
        Assert.True(controller.Execute(CommandCatalog.Resume));
        Assert.Equal((TimeSpan.FromSeconds(15), (TimeSpan?)null), (controller.Position, controller.ResumeOffer));
        Assert.Equal("Resumed at 0:15", harness.Messages[^1]);

        // Declined, the offer goes; stopped near the start, the point is forgotten.
        OpenReady(harness, "b.wav");
        OpenReady(harness, "a.wav");
        controller.DeclineResume();
        Assert.Null(controller.ResumeOffer);
        Assert.False(controller.AcceptResume());
        controller.Stop();
        OpenReady(harness, "a.wav");
        Assert.Null(controller.ResumeOffer);
    }

    [Fact]
    [Capability("PB-11")]
    public void ResumingCanBeAlwaysOrNever()
    {
        var store = RexStore.InMemory();
        using (var always = Harness(store, ResumeChoice.Always))
        {
            LeaveAHalfWay(always);
            OpenReady(always, "a.wav");
            Assert.Equal(TimeSpan.FromSeconds(15), always.Controller.Position);
            Assert.Equal("Resuming at 0:15", always.Messages[^1]);
            Assert.Null(always.Controller.ResumeOffer);
        }

        using var never = Harness(store, ResumeChoice.Never);
        OpenReady(never, "a.wav");
        Assert.Equal((TimeSpan.Zero, (TimeSpan?)null), (never.Controller.Position, never.Controller.ResumeOffer));
    }

    [Fact]
    [Capability("PB-11")]
    public void AFileThatPlayedToTheEndStartsFromTheTop()
    {
        using var harness = Harness(RexStore.InMemory(), autoPlay: true);
        harness.Files["short.wav"] = Count(0, 8000 * 12);
        var controller = harness.Controller;
        controller.Open(["short.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Playing);
        controller.Seek(TimeSpan.FromSeconds(11));
        controller.RememberPosition();
        harness.PumpUntil(c => c.State == SessionState.Ended);

        Assert.Null(controller.Memory.ResumePoint("short.wav"));
    }

    [Fact]
    [Capability("PB-10")]
    public void BookmarksAreAddedRenamedVisitedAndDeleted()
    {
        using var harness = Harness(RexStore.InMemory());
        var controller = harness.Controller;
        controller.AddBookmark();
        Assert.Equal("Open something to play first.", harness.Messages[^1]);
        Assert.Empty(controller.Bookmarks);

        OpenReady(harness, "a.wav");
        controller.Seek(TimeSpan.FromSeconds(20));
        Assert.True(controller.Execute(CommandCatalog.AddBookmark));
        Assert.Equal("Bookmark 1 at 0:20", harness.Messages[^1]);
        controller.Seek(TimeSpan.FromSeconds(5));
        controller.AddBookmark("  Chorus ");

        Assert.Equal([("Chorus", TimeSpan.FromSeconds(5)), ("Bookmark 1", TimeSpan.FromSeconds(20))], controller.Bookmarks.Select(b => (b.Name, b.At)));
        controller.RenameBookmark(1, "Bridge");
        controller.GoToBookmark(1);
        Assert.Equal((TimeSpan.FromSeconds(20), "Bridge"), (controller.Position, harness.Messages[^1]));
        controller.DeleteBookmark(0);
        controller.DeleteBookmark(5);
        controller.GoToBookmark(-1);
        Assert.Equal(["Bridge"], controller.Bookmarks.Select(b => b.Name));
        Assert.Throws<ArgumentException>(() => controller.RenameBookmark(0, " "));

        // They belong to the file: kept for it, absent elsewhere.
        OpenReady(harness, "b.wav");
        Assert.Empty(controller.Bookmarks);
        OpenReady(harness, "a.wav");
        Assert.Single(controller.Bookmarks);
    }

    [Fact]
    [Capability("LIB-08")]
    public void QuickSlotsRememberAPlaceAndGoBackToIt()
    {
        using var harness = Harness(RexStore.InMemory());
        var controller = harness.Controller;
        Assert.True(controller.Execute(CommandCatalog.SetSlotPrefix + "2"));
        Assert.Equal("Open something to play first.", harness.Messages[^1]);
        Assert.True(controller.Execute(CommandCatalog.PlaySlotPrefix + "2"));
        Assert.Equal("Quick slot 2 is empty. Ctrl+Shift+2 keeps what is playing there.", harness.Messages[^1]);

        OpenReady(harness, "a.wav");
        controller.Seek(TimeSpan.FromSeconds(7));
        controller.Execute(CommandCatalog.SetSlotPrefix + "2");
        Assert.Equal("Quick slot 2: a", harness.Messages[^1]);

        // From another playlist, the slot's file joins it; from one that has it, it is played there.
        OpenReady(harness, "b.wav");
        controller.Execute(CommandCatalog.PlaySlotPrefix + "2");
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "a.wav");
        Assert.Equal(["b.wav", "a.wav"], controller.Playlist.Items.Select(item => item.Location));
        Assert.Equal(TimeSpan.FromSeconds(7), controller.Position);
        controller.PlayAt(0);
        controller.PlaySlot(2);
        Assert.Equal(2, controller.Playlist.Items.Count);
        Assert.Equal(1, controller.Playlist.CurrentIndex);
    }

    [Fact]
    public void SlotCommandsSayWhichSlot()
    {
        Assert.Equal(3, CommandCatalog.SlotNumber("set-slot-3"));
        Assert.Equal(9, CommandCatalog.SlotNumber("play-slot-9"));
        Assert.Null(CommandCatalog.SlotNumber("play-slot-0"));
        Assert.Null(CommandCatalog.SlotNumber("set-slot-x"));
        Assert.Null(CommandCatalog.SlotNumber(CommandCatalog.Mute));
        Assert.Equal("Ctrl+Shift+4", CommandCatalog.Find("set-slot-4")!.DefaultShortcuts[0].ToString());
        Assert.Throws<ArgumentNullException>(() => CommandCatalog.SlotNumber(null!));
    }

    [Fact]
    [Capability("LIB-07")]
    [Capability("PRIV-03")]
    public void RecentMediaIsKeptUntilClearedOrTurnedOff()
    {
        var store = RexStore.InMemory();
        using (var harness = Harness(store))
        {
            OpenReady(harness, "a.wav");
            OpenReady(harness, "b.wav");
            Assert.Equal(["b.wav", "a.wav"], harness.Controller.Recent);

            Assert.True(harness.Controller.Execute(CommandCatalog.ClearHistory));
            Assert.Equal("History cleared", harness.Messages[^1]);
            Assert.Empty(harness.Controller.Recent);
        }

        using var off = Harness(store, keepHistory: false);
        LeaveAHalfWay(off);
        Assert.Empty(off.Controller.Recent);
        Assert.Null(off.Controller.Memory.ResumePoint("a.wav"));
    }

    [Fact]
    [Capability("LIB-03")]
    public void TheQueueComesBackReadyToPlayOnFromWhereItWas()
    {
        var store = RexStore.InMemory();
        using (var before = Harness(store, ResumeChoice.Never))
        {
            Assert.False(before.Controller.RestoreQueue());
            before.Controller.Open(["a.wav", "b.wav"]);
            before.Controller.PlayAt(1);
            before.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "b.wav");
            before.Controller.Seek(TimeSpan.FromSeconds(12));
            before.Controller.SaveQueue();
        }

        using var after = Harness(store, ResumeChoice.Never);
        var controller = after.Controller;
        Assert.True(controller.RestoreQueue());
        Assert.Equal(["a.wav", "b.wav"], controller.Playlist.Items.Select(item => item.Location));
        Assert.Equal(SessionState.Idle, controller.State);

        controller.Execute(CommandCatalog.PlayPause);
        after.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "b.wav");
        Assert.Equal(TimeSpan.FromSeconds(12), controller.Position);

        // The restored place is used once; later starts are ordinary.
        controller.PlayAt(1);
        after.PumpUntil(c => c.State == SessionState.Ready && c.Position == TimeSpan.Zero);
    }

    [Fact]
    [Capability("LIB-03")]
    public void TheQueueIsNotKeptWhenTheUserSaysNot()
    {
        var store = RexStore.InMemory();
        using var harness = Harness(store, restoreQueue: false);
        harness.Controller.Open(["a.wav"]);
        harness.Controller.SaveQueue();
        Assert.Null(harness.Controller.Memory.Queue);
        Assert.False(harness.Controller.RestoreQueue());

        // A queue whose current item is gone still comes back, starting from the top.
        harness.Controller.Memory.Queue = new QueueSnapshot([new QueuedItem("a.wav", null, null, TimeSpan.Zero, null)], 4, TimeSpan.FromSeconds(3));
        harness.Controller.Settings = harness.Controller.Settings with { RestoreQueue = true };
        Assert.True(harness.Controller.RestoreQueue());
        Assert.Equal("a", harness.Controller.Playlist.Items.Single().Title);
        Assert.Null(harness.Controller.Playlist.Current);
    }
}
