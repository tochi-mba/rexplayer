using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Shell;

/// <summary>
/// The transport's every edge, through a real player and playlist: what the taskbar's Previous, Play
/// and Next, Windows' media controls and the media keys offer is exactly what the window's own
/// commands would then do.
/// </summary>
public sealed class TransportEdgeCaseTests
{
    private static ControllerHarness Playing(params string[] titles)
    {
        var harness = new ControllerHarness(autoPlay: false);
        foreach (var title in titles)
        {
            harness.Files[title + ".wav"] = ControllerHarness.Count(0, 4000);
        }

        harness.Controller.Open([.. titles.Select(title => title + ".wav")]);
        harness.PumpUntil(c => c.Item?.Location == titles[0] + ".wav" && c.State == SessionState.Ready);
        return harness;
    }

    private static void Go(ControllerHarness harness, int index)
    {
        var target = harness.Controller.Playlist.Items[index];
        harness.Controller.PlayAt(index);
        harness.PumpUntil(c => ReferenceEquals(c.Item, target) && c.State == SessionState.Ready);
    }

    private static TransportView View(ControllerHarness harness) => TransportAvailability.Decide(TransportAvailability.Read(harness.Controller));

    [Fact]
    [Capability("WIN-06")]
    public void ASingleItemHasNeitherPreviousNorNextWhateverTheRepeat()
    {
        using var harness = Playing("only");
        foreach (var repeat in new[] { RepeatMode.Off, RepeatMode.All, RepeatMode.One })
        {
            harness.Controller.Playlist.Repeat = repeat;
            harness.Controller.Playlist.Shuffle = repeat == RepeatMode.All;
            var view = View(harness);
            Assert.False(view.Previous.IsVisible);
            Assert.False(view.Next.IsVisible);
            Assert.True(view.PlayPause.IsEnabled);
            Assert.Equal("Play", view.PlayPause.Tooltip);
        }
    }

    [Fact]
    [Capability("WIN-06")]
    public void PreviousAppearsOnlyAfterTheFirstItemAndNextOnlyBeforeTheLast()
    {
        using var harness = Playing("a", "b", "c");
        var first = View(harness);
        Assert.False(first.Previous.IsVisible);
        Assert.Equal("Next: b", first.Next.Tooltip);

        Go(harness, 1);
        var middle = View(harness);
        Assert.Equal("Previous: a", middle.Previous.Tooltip);
        Assert.Equal("Next: c", middle.Next.Tooltip);

        Go(harness, 2);
        var last = View(harness);
        Assert.Equal("Previous: b", last.Previous.Tooltip);
        Assert.False(last.Next.IsVisible);
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Next, last));
    }

    [Fact]
    public void RepeatAllWrapsBothWaysAndRepeatOneChangesNothingAskedFor()
    {
        using var harness = Playing("a", "b", "c");
        harness.Controller.Playlist.Repeat = RepeatMode.All;
        Assert.Equal("Previous: c", View(harness).Previous.Tooltip);
        Go(harness, 2);
        Assert.Equal("Next: a", View(harness).Next.Tooltip);

        harness.Controller.Playlist.Repeat = RepeatMode.One;
        var one = View(harness);
        Assert.Equal("Previous: b", one.Previous.Tooltip);
        Assert.False(one.Next.IsVisible);
    }

    [Fact]
    public void ShuffleFollowsThePlayOrderNotTheList()
    {
        using var harness = Playing("a", "b", "c", "d", "e");
        harness.Controller.Playlist.Shuffle = true;

        // Just shuffled: the current item is first in the new order, so nothing is before it.
        Assert.False(View(harness).Previous.IsVisible);
        var next = harness.Controller.Playlist.PeekNext(automatic: false)!;
        Assert.Equal("Next: " + next.Title, View(harness).Next.Tooltip);

        var current = harness.Controller.Item!;
        harness.Controller.Execute(CommandCatalog.Next);
        harness.PumpUntil(c => ReferenceEquals(c.Item, next) && c.State == SessionState.Ready);
        Assert.Equal("Previous: " + current.Title, View(harness).Previous.Tooltip);
    }

    [Fact]
    public void ItemsAddedWhilePlayingTheLastOneBringNextBack()
    {
        using var harness = Playing("a", "b");
        Go(harness, 1);
        Assert.False(View(harness).Next.IsVisible);

        harness.Files["later.wav"] = ControllerHarness.Count(0, 4000);
        harness.Controller.Open(["later.wav"], enqueue: true);
        Assert.Equal("Next: later", View(harness).Next.Tooltip);
    }

    [Fact]
    public void PlayNextGoesStraightAfterWhatPlaysEvenWithShuffle()
    {
        using var harness = Playing("a", "b", "c");
        harness.Controller.Playlist.Shuffle = true;
        harness.Controller.PlayNext([new PlaylistItem("soon.wav")]);
        Assert.Equal("Next: soon", View(harness).Next.Tooltip);
    }

    [Fact]
    public void RemovingWhatPlaysLeavesItsNeighboursAsPreviousAndNext()
    {
        using var harness = Playing("a", "b", "c");
        Go(harness, 1);
        harness.Controller.Playlist.RemoveAt(1);
        var view = View(harness);
        Assert.Equal("Previous: a", view.Previous.Tooltip);
        Assert.Equal("Next: c", view.Next.Tooltip);
    }

    [Fact]
    public void AnItemAddedTwiceIsTwoEntriesSoEachIsTheOthersNeighbour()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["same.wav"] = ControllerHarness.Count(0, 4000);
        harness.Controller.Open(["same.wav", "same.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal("Next: same", View(harness).Next.Tooltip);
    }

    [Fact]
    public void AFewSecondsInPreviousGoesBackToTheStartFirstAsTheWindowsCommandDoes()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["a.wav"] = ControllerHarness.Count(0, 4000);
        harness.Files["b.wav"] = ControllerHarness.Count(0, 8000 * 6);
        harness.Controller.Open(["a.wav", "b.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Go(harness, 1);

        harness.Controller.Seek(TimeSpan.FromSeconds(4));
        Assert.True(harness.Controller.PreviousRestarts);
        var later = View(harness);
        Assert.Equal("Back to the start of b", later.Previous.Tooltip);
        Assert.Equal(CommandCatalog.Previous, TransportAvailability.CommandFor(TransportRequest.Previous, later));

        harness.Controller.Seek(TimeSpan.FromSeconds(1));
        Assert.False(harness.Controller.PreviousRestarts);
        Assert.Equal("Previous: a", View(harness).Previous.Tooltip);
    }

    [Fact]
    public void AfterTheLastItemEndsOnlyPlayAgainRemains()
    {
        using var harness = new ControllerHarness();
        harness.Files["a.wav"] = ControllerHarness.Count(0, 400);
        harness.Files["b.wav"] = ControllerHarness.Count(0, 400);
        harness.Controller.Open(["a.wav", "b.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ended && c.Playlist.Current is null);

        var view = View(harness);
        Assert.False(view.Previous.IsVisible);
        Assert.False(view.Next.IsVisible);
        Assert.Equal("Play again", view.PlayPause.Tooltip);
        Assert.Equal(TransportStatus.Stopped, view.Status);
        Assert.True(view.Stop.IsEnabled);
    }

    [Fact]
    public void StoppedKeepsPlayAndTheNeighboursButStopHasNothingToDo()
    {
        using var harness = Playing("a", "b");
        harness.Controller.Stop();
        var view = View(harness);
        Assert.Equal(TransportButtonState.Disabled, view.Stop.State);
        Assert.Equal("Play", view.PlayPause.Tooltip);
        Assert.Equal("Next: b", view.Next.Tooltip);
        Assert.Equal(TransportStatus.Stopped, view.Status);
        Assert.Equal(TransportBadge.None, view.Badge);
    }

    [Fact]
    public void AQueueRestoredButNotStartedOffersOnlyPlay()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Controller.Playlist.Add([new PlaylistItem("a.wav"), new PlaylistItem("b.wav")]);
        var view = View(harness);
        Assert.True(view.PlayPause.IsEnabled);
        Assert.False(view.Previous.IsVisible);
        Assert.False(view.Next.IsVisible);
        Assert.Equal(TransportStatus.Stopped, view.Status);
    }

    [Fact]
    public void AFileThatWillNotPlayShowsTheFailedBadgeAndTryAgain()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["broken.wav"] = [1, 2, 3, 4];
        harness.Controller.Open(["broken.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Faulted);
        var view = View(harness);
        Assert.Equal(TransportBadge.Failed, view.Badge);
        Assert.Equal("Try again", view.PlayPause.Tooltip);
        Assert.Equal(CommandCatalog.PlayPause, TransportAvailability.CommandFor(TransportRequest.Play, view));
    }

    [Theory]
    [InlineData(SessionState.Opening, true, "Pause", TransportStatus.Changing, TransportBadge.None)]
    [InlineData(SessionState.Ready, false, "Play", TransportStatus.Paused, TransportBadge.Paused)]
    [InlineData(SessionState.Playing, true, "Pause", TransportStatus.Playing, TransportBadge.None)]
    [InlineData(SessionState.Paused, false, "Play", TransportStatus.Paused, TransportBadge.Paused)]
    [InlineData(SessionState.Buffering, true, "Pause", TransportStatus.Playing, TransportBadge.None)]
    [InlineData(SessionState.Seeking, true, "Pause", TransportStatus.Playing, TransportBadge.None)]
    [InlineData(SessionState.Ended, false, "Play again", TransportStatus.Stopped, TransportBadge.None)]
    [InlineData(SessionState.Stopping, false, "Play", TransportStatus.Stopped, TransportBadge.None)]
    [InlineData(SessionState.Faulted, false, "Try again", TransportStatus.Stopped, TransportBadge.Failed)]
    [InlineData(SessionState.Idle, false, "Play", TransportStatus.Stopped, TransportBadge.None)]
    public void EveryStateHasOnePlayButtonStatusAndBadge(SessionState state, bool playing, string tooltip, TransportStatus status, TransportBadge badge)
    {
        foreach (var media in new[] { TransportMedia.Audio, TransportMedia.Video, TransportMedia.Stream, TransportMedia.Unknown })
        {
            var view = TransportAvailability.Decide(new TransportInput(state, playing, true, media, "Now", "Before", "After", null, false));
            Assert.Equal(tooltip, view.PlayPause.Tooltip);
            Assert.Equal(playing, view.ShowsPause);
            Assert.Equal(status, view.Status);
            Assert.Equal(badge, view.Badge);
            Assert.True(view.PlayPause.IsEnabled);
            Assert.Equal(state == SessionState.Idle ? TransportButtonState.Disabled : TransportButtonState.Enabled, view.Stop.State);
            Assert.Equal(playing ? CommandCatalog.PlayPause : null, TransportAvailability.CommandFor(TransportRequest.Pause, view));
            Assert.Equal(playing ? null : CommandCatalog.PlayPause, TransportAvailability.CommandFor(TransportRequest.Play, view));
        }
    }

    [Fact]
    public void ASlideshowSaysSo()
    {
        Assert.Equal("Pause the slideshow", TransportAvailability.Decide(new TransportInput(SessionState.Playing, true, true, TransportMedia.Picture, "Beach", null, "Hills", null, false)).PlayPause.Tooltip);
        Assert.Equal("Play the slideshow", TransportAvailability.Decide(new TransportInput(SessionState.Paused, false, true, TransportMedia.Picture, "Beach", null, "Hills", null, false)).PlayPause.Tooltip);
    }

    [Fact]
    [Capability("WIN-06")]
    public void AfterTheLastEpisodeNextIsTheNextEpisodeBesideIt()
    {
        var episode = Path.Combine(Path.GetTempPath(), "Show", "Show S01E03.mkv");
        var view = TransportAvailability.Decide(new TransportInput(SessionState.Playing, true, true, TransportMedia.Video, "Show S01E02", "Show S01E01", null, episode, false));
        Assert.Equal("Next episode: Show S01E03", view.Next.Tooltip);
        Assert.Equal(CommandCatalog.NextEpisode, TransportAvailability.CommandFor(TransportRequest.Next, view));

        // A playlist item that follows comes first.
        var listed = TransportAvailability.Decide(new TransportInput(SessionState.Playing, true, true, TransportMedia.Video, "Show S01E02", null, "Something else", episode, false));
        Assert.Equal(CommandCatalog.Next, listed.Next.Command);
    }

    [Fact]
    public void PeekPreviousAlwaysNamesWhatPreviousMovesTo()
    {
        var random = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var playlist = new Playlist(new Random(round));
            Assert.Null(playlist.PeekPrevious());
            var count = random.Next(1, 6);
            playlist.Add([.. Enumerable.Range(0, count).Select(i => new PlaylistItem($"{i}.wav"))]);
            playlist.Repeat = (RepeatMode)random.Next(3);
            playlist.Shuffle = random.Next(2) == 0;
            for (var step = random.Next(0, 6); step > 0; step--)
            {
                switch (random.Next(4))
                {
                    case 0:
                        playlist.Next(automatic: random.Next(2) == 0);
                        break;
                    case 1:
                        playlist.JumpTo(random.Next(playlist.Items.Count));
                        break;
                    case 2 when playlist.Items.Count > 1:
                        playlist.RemoveAt(random.Next(playlist.Items.Count));
                        break;
                    default:
                        playlist.Previous();
                        break;
                }
            }

            var peeked = playlist.PeekPrevious();
            Assert.Same(peeked, playlist.Previous());
        }
    }
}
