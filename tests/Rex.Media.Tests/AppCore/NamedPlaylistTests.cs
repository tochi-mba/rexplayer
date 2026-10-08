using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Playlists the user names and keeps (LIB-04): made, added to, renamed, copied, deleted and played.</summary>
public sealed class NamedPlaylistTests
{
    private static readonly byte[] Second = Count(0, 8000);

    private static ControllerHarness Harness(RexStore store)
    {
        var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, store: store);
        foreach (var name in new[] { "Sungba.wav", "Terminator.wav", "Lonely At The Top.wav" })
        {
            harness.Files[name] = Second;
        }

        return harness;
    }

    private static IEnumerable<string> Locations(NamedPlaylist playlist) => playlist.Items.Select(item => item.Location);

    [Fact]
    [Capability("LIB-04")]
    public void APlaylistIsMadeFromThePlaylistAddedToAndKeptBetweenRuns()
    {
        var store = RexStore.InMemory();
        string id;
        using (var harness = Harness(store))
        {
            var controller = harness.Controller;
            controller.Open(["Sungba.wav", "Terminator.wav"]);
            harness.PumpUntil(c => c.State == SessionState.Ready);

            var made = controller.CreatePlaylist("  Asake  ");
            id = made.Id;
            Assert.Equal("Asake", made.Name);
            Assert.Equal(["Sungba.wav", "Terminator.wav"], Locations(made));
            Assert.Equal("Made the playlist Asake (2 items)", harness.Messages[^1]);

            // Added to from the playlist's selection and from what is playing.
            controller.AddToPlaylist(id, [new PlaylistItem("Lonely At The Top.wav") { Title = "Lonely At The Top", Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) }]);
            Assert.Equal("Added 1 item to Asake", harness.Messages[^1]);
            controller.AddPlayingToPlaylist(id);
            controller.AddToPlaylist(id, []);
            Assert.Equal("Added 1 item to Asake", harness.Messages[^1]);
        }

        using var again = Harness(store);
        var kept = Assert.Single(again.Controller.NamedPlaylists);
        Assert.Equal(id, kept.Id);
        Assert.Equal(["Sungba.wav", "Terminator.wav", "Lonely At The Top.wav", "Sungba.wav"], Locations(kept));
        Assert.Equal(new QueuedItem("Lonely At The Top.wav", "Lonely At The Top", null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)), kept.Items[2]);
    }

    [Fact]
    [Capability("LIB-04")]
    public void PlaylistsAreRenamedCopiedAndDeletedAndNamesStayDistinct()
    {
        using var harness = Harness(RexStore.InMemory());
        var controller = harness.Controller;
        var empty = controller.CreatePlaylist("Mix", []);
        Assert.Equal("Made the playlist Mix (0 items)", harness.Messages[^1]);
        var second = controller.CreatePlaylist("mix", [new PlaylistItem("Sungba.wav")]);
        Assert.Equal("mix (2)", second.Name);
        Assert.Equal("Made the playlist mix (2) (1 item)", harness.Messages[^1]);

        var copy = controller.DuplicatePlaylist(second.Id)!;
        Assert.Equal("mix (2) copy", copy.Name);
        Assert.Equal(["Sungba.wav"], Locations(copy));
        Assert.NotEqual(second.Id, copy.Id);

        // Renaming to its own name keeps it; to names taken (whatever their case) gets the next free number.
        controller.RenamePlaylist(empty.Id, " Mix ");
        controller.RenamePlaylist(copy.Id, "MIX");
        Assert.Equal(["Mix", "mix (2)", "MIX (3)"], controller.NamedPlaylists.Select(p => p.Name));

        controller.DeletePlaylist(empty.Id);
        Assert.Equal("Deleted the playlist Mix", harness.Messages[^1]);
        Assert.Equal(["mix (2)", "MIX (3)"], controller.NamedPlaylists.Select(p => p.Name));

        // One deleted elsewhere (another window, say) is reported gone, and nothing else happens.
        foreach (var gone in new Action[]
        {
            () => controller.RenamePlaylist(empty.Id, "Back"),
            () => controller.AddToPlaylist(empty.Id, [new PlaylistItem("Sungba.wav")]),
            () => controller.DeletePlaylist(empty.Id),
            () => controller.PlayPlaylist(empty.Id),
            () => Assert.Null(controller.DuplicatePlaylist(empty.Id)),
        })
        {
            harness.Messages.Clear();
            gone();
            Assert.Equal(["That playlist is no longer there."], harness.Messages);
        }

        Assert.Equal(2, controller.NamedPlaylists.Count);
        Assert.Throws<ArgumentException>(() => controller.CreatePlaylist(" "));
        Assert.Throws<ArgumentException>(() => controller.RenamePlaylist(copy.Id, ""));
        Assert.Throws<ArgumentNullException>(() => controller.AddToPlaylist(copy.Id, null!));
    }

    [Fact]
    [Capability("LIB-04")]
    public void APlaylistPlaysInPlaceOfThePlaylistOrAfterIt()
    {
        using var harness = Harness(RexStore.InMemory());
        var controller = harness.Controller;
        controller.AddPlayingToPlaylist(controller.CreatePlaylist("Nothing yet", []).Id);
        Assert.Equal("Open something to play first.", harness.Messages[^1]);
        var empty = controller.NamedPlaylists[0];
        controller.PlayPlaylist(empty.Id);
        Assert.Equal("Nothing yet is empty.", harness.Messages[^1]);
        Assert.Empty(controller.Playlist.Items);

        var mix = controller.CreatePlaylist("Asake", [new PlaylistItem("Terminator.wav") { Title = "Terminator", Artist = "Asake" }, new PlaylistItem("Sungba.wav")]);
        controller.Open(["Lonely At The Top.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);

        controller.PlayPlaylist(mix.Id, enqueue: true);
        Assert.Equal(["Lonely At The Top.wav", "Terminator.wav", "Sungba.wav"], controller.Playlist.Items.Select(item => item.Location));
        Assert.Equal("Lonely At The Top.wav", controller.Item!.Location);

        controller.PlayPlaylist(mix.Id);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "Terminator.wav");
        Assert.Equal(["Terminator.wav", "Sungba.wav"], controller.Playlist.Items.Select(item => item.Location));
        Assert.Equal(("Terminator", "Asake"), (controller.Playlist.Items[0].Title, controller.Playlist.Items[0].Artist));
        Assert.Equal("Sungba", controller.Playlist.Items[1].Title);
    }

    [Fact]
    public void APlaylistThatNoLongerReadsIsLeftOut()
    {
        var store = RexStore.InMemory();
        var memory = new PlayerMemory(store);
        memory.SetPlaylist(new NamedPlaylist("a", "Asake", []));
        store.Set("playlist/b", "{ not json");

        Assert.Equal(["Asake"], memory.Playlists.Select(p => p.Name));
        Assert.Null(memory.Playlist("b"));
        Assert.True(memory.RemovePlaylist("a"));
        Assert.False(memory.RemovePlaylist("a"));
        Assert.Throws<ArgumentException>(() => memory.Playlist(" "));
        Assert.Throws<ArgumentNullException>(() => memory.SetPlaylist(null!));
    }
}
