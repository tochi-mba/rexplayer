using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Settings;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Seeking after the queue ended, asking to move through media that cannot be, and focus in the controls bar.</summary>
public sealed class PlaybackLeftoverTests
{
    private static readonly PlayerSettings Quiet = new() { TitleSeconds = 0 };

    [Fact]
    public void SeekingAfterTheLastItemEndedMakesItCurrentInTheQueueAgain()
    {
        using var harness = new ControllerHarness(settings: Quiet);
        harness.Files["Lonely At The Top.wav"] = Count(0, 400);
        harness.Files["Sungba.wav"] = Count(500, 400);
        var controller = harness.Controller;

        controller.Open(["Lonely At The Top.wav", "Sungba.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ended && c.Item?.Title == "Sungba" && c.Playlist.Current is null);

        controller.Seek(TimeSpan.FromSeconds(0.01));

        Assert.Same(controller.Item, controller.Playlist.Current);
        Assert.Equal(1, controller.Playlist.CurrentIndex);
    }

    [Fact]
    public void SeekingWhileAnItemIsCurrentLeavesTheQueueAlone()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: Quiet);
        harness.Files["Terminator.wav"] = Count(0, 4000);
        harness.Files["Sungba.wav"] = Count(500, 400);
        var controller = harness.Controller;
        controller.Open(["Terminator.wav", "Sungba.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);

        controller.Seek(TimeSpan.FromSeconds(0.1));

        Assert.Equal(0, controller.Playlist.CurrentIndex);
    }

    [Theory]
    [InlineData(CommandCatalog.JumpForwardShort)]
    [InlineData(CommandCatalog.JumpBackShort)]
    public void JumpingInMediaThatCannotBeMovedThroughSaysSo(string command)
    {
        using var harness = new ControllerHarness(
            autoPlay: false,
            settings: Quiet,
            openSource: location => new MemoryByteSource(Count(0, 4000), location, canSeek: false));
        var controller = harness.Controller;
        controller.Open(["Terminator.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.False(controller.CanSeek);

        Assert.True(controller.Execute(command));

        Assert.Equal(PlayerController.CannotSeekNotice, harness.Messages[^1]);
        Assert.Equal(TimeSpan.Zero, controller.Position);
    }

    [Fact]
    public void JumpingWithNothingOpenSaysNothing()
    {
        using var harness = new ControllerHarness(settings: Quiet);

        harness.Controller.Execute(CommandCatalog.JumpForwardShort);

        Assert.Empty(harness.Messages);
    }

    [Fact]
    public void FocusOnAButtonInsideTheBarCountsAsFocusInTheBar()
    {
        var bar = new Node(null);
        var row = new Node(bar);
        var button = new Node(row);
        var elsewhere = new Node(new Node(null));

        Assert.True(FocusWithin.IsInside(bar, bar, n => n.Parent));
        Assert.True(FocusWithin.IsInside(button, bar, n => n.Parent));
        Assert.False(FocusWithin.IsInside(elsewhere, bar, n => n.Parent));
        Assert.False(FocusWithin.IsInside(null, bar, n => n.Parent));
    }

    private sealed class Node(Node? parent)
    {
        public Node? Parent { get; } = parent;
    }
}
