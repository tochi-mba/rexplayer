using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

/// <summary>The library's bulk playback and watch-state operations use the same controller as the window.</summary>
public sealed class LibraryControllerActionsTests
{
    private static readonly byte[] Recording = ControllerHarness.Count(0, 8000 * 30);

    private static ControllerHarness Harness(bool autoPlay = false)
    {
        var test = new ControllerHarness(autoPlay: autoPlay, settings: new PlayerSettings { TitleSeconds = 0 });
        test.Files["a.wav"] = Recording;
        test.Files["b.wav"] = Recording;
        test.Files["c.wav"] = Recording;
        return test;
    }

    [Fact]
    public void LibraryCommandsValidateInputsAndDoNotPlayAnEmptySelection()
    {
        using var test = Harness();
        var controller = test.Controller;
        Assert.Throws<ArgumentNullException>(() => controller.PlayNext(null!));
        Assert.Throws<ArgumentNullException>(() => controller.PlayNextFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => controller.PlayFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => controller.EnqueueFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => PlayerController.ItemOf(null!));
        Assert.Throws<ArgumentNullException>(() => controller.MarkWatched(null!));
        Assert.Throws<ArgumentNullException>(() => controller.MarkUnwatched(null!));
        Assert.Throws<ArgumentNullException>(() => controller.ResetProgress(null!));
        Assert.Throws<ArgumentNullException>(() => controller.RemoveFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => controller.ForgetDeleted(null!));
        controller.PlayNext([]);
        Assert.Empty(controller.Playlist.Items);
        controller.Pause();
        controller.MarkWatched([]);
        controller.MarkUnwatched([]);
        controller.ResetProgress([]);
        controller.RemoveFromLibrary([]);
        controller.ForgetDeleted([]);
        Assert.Empty(controller.Playlist.Items);
    }

    [Fact]
    public void PlayNextInsertsAfterTheCurrentItemAndStartsWhenIdle()
    {
        using var test = Harness();
        var controller = test.Controller;
        controller.PlayNext([new PlaylistItem("a.wav") { Title = "First" }]);
        test.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "a.wav");
        Assert.Equal("a.wav", controller.Item!.Location);

        controller.PlayNext([new PlaylistItem("b.wav") { Title = "Second" }, new PlaylistItem("c.wav") { Title = "Third" }]);
        Assert.Equal(["a.wav", "b.wav", "c.wav"], controller.Playlist.Items.Select(x => x.Location));
        Assert.Equal("Playing 2 items next", test.Messages[^1]);
        controller.PlayNext([new PlaylistItem("b.wav") { Title = "Second again" }]);
        Assert.Equal("Playing next: Second again", test.Messages[^1]);
        Assert.Equal("a.wav", controller.Item!.Location);
        controller.Pause();
        Assert.False(controller.IsPlaying);
    }

    [Fact]
    public void BulkLibraryManipulationPersistsProgressAndKeepsMediaFilesIntact()
    {
        using var test = Harness();
        var controller = test.Controller;
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-controller-library");
        var a = Path.Combine(folder, "a.wav");
        var b = Path.Combine(folder, "b.wav");
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(folder);
        library.Scan(folder,
            [new LibraryFile(a, 123, DateTime.UtcNow), new LibraryFile(b, 456, DateTime.UtcNow)],
            _ => LibraryKind.Music);
        controller.Library = library;
        var item = library.Entry(a)!;
        Assert.Equal(a, PlayerController.ItemOf(item).Location);
        Assert.Equal(item.Title, PlayerController.ItemOf(item).Title);
        Assert.Equal(Rex.Media.AppCore.Library.WatchState.Unwatched, controller.WatchStateOf(item));
        controller.Memory.Left(a, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(40));
        Assert.Equal(Rex.Media.AppCore.Library.WatchState.InProgress, controller.WatchStateOf(item));

        controller.MarkWatched([a]);
        Assert.Null(controller.LeftAt(a));
        Assert.Equal(1, library.Entry(a)!.Plays);
        Assert.Equal(Rex.Media.AppCore.Library.WatchState.Finished, controller.WatchStateOf(library.Entry(a)!));
        controller.MarkUnwatched([a, b]);
        Assert.Equal(0, library.Entry(a)!.Plays);
        Assert.Null(library.Entry(a)!.LastPlayed);

        controller.Memory.Left(a, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(40));
        controller.Memory.Left(b, TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(40));
        controller.ResetProgress([a, b]);
        Assert.Null(controller.LeftAt(a));
        Assert.Null(controller.LeftAt(b));
        controller.ResetProgress([a]);
        controller.RemoveFromLibrary([a]);
        Assert.Null(library.Entry(a));
        Assert.Equal([a], library.Hidden);
        controller.RemoveFromLibrary([a]);
        controller.ForgetDeleted([b]);
        Assert.Null(library.Entry(b));
        Assert.Empty(library.Entries);
        Assert.Empty(controller.Playlist.Items);
    }

    [Fact]
    public void LibraryQueueOperationsUseTheChosenEntries()
    {
        using var test = Harness();
        var controller = test.Controller;
        var entryA = new LibraryEntry { Path = "a.wav", Title = "Alpha", Kind = LibraryKind.Music };
        var entryB = new LibraryEntry { Path = "b.wav", Title = "Beta", Kind = LibraryKind.Music };
        controller.EnqueueFromLibrary([]);
        controller.PlayNextFromLibrary([entryA]);
        test.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "a.wav");
        controller.PlayNextFromLibrary([entryB]);
        Assert.Equal("b.wav", controller.Playlist.Items[1].Location);
        controller.EnqueueFromLibrary([entryB]);
        Assert.Equal(3, controller.Playlist.Items.Count);
        controller.ForgetDeleted(["b.wav"]);
        Assert.DoesNotContain(controller.Playlist.Items, item => item.Location == "b.wav");
    }

    [Fact]
    public void ForgettingAnActivelyPlayingFileStopsTheSessionAndRemovesQueueReferences()
    {
        using var test = Harness(autoPlay: true);
        var controller = test.Controller;
        controller.Open(["a.wav", "b.wav"]);
        test.PumpUntil(c => c.State == SessionState.Playing && c.Item?.Location == "a.wav");
        Assert.Equal(2, controller.Playlist.Items.Count);
        controller.ForgetDeleted(["a.wav"]);
        Assert.Null(controller.Item);
        Assert.NotEqual(SessionState.Playing, controller.State);
        Assert.DoesNotContain(controller.Playlist.Items, item => item.Location == "a.wav");
        Assert.Single(controller.Playlist.Items);
    }

    [Fact]
    public void PausingAnActiveItemDoesNotRestartItOrChangeItsPosition()
    {
        using var test = Harness(autoPlay: true);
        var controller = test.Controller;
        controller.Open(["a.wav"]);
        test.PumpUntil(c => c.State == SessionState.Playing);
        controller.Pause();
        test.PumpUntil(c => c.State == SessionState.Paused);
        var after = controller.Position;
        controller.Pause();
        test.Pump();
        Assert.Equal(after, controller.Position);
        Assert.False(controller.IsPlaying);
        Assert.False(controller.IsPlaying);
    }
}
