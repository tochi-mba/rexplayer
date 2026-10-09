using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Playlist entries whose files have moved (LIB-09): marked, found one by one or by folder.</summary>
public sealed class MissingFilesTests
{
    private static readonly string Old = Path.Combine("old", "music");
    private static readonly string New = Path.Combine("new", "music");

    private static ControllerHarness Harness()
    {
        var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Controller.Playlist.Add(
        [
            new PlaylistItem(Path.Combine(Old, "Sungba.wav")),
            new PlaylistItem(Path.Combine(Old, "Terminator.wav")) { Title = "Terminator (Live)", Artist = "Asake" },
            new PlaylistItem(Path.Combine(Old, "Joha.wav")),
            new PlaylistItem("https://example.com/radio"),
        ]);
        foreach (var name in new[] { "Sungba.wav", "Terminator.wav" })
        {
            harness.Files[Path.Combine(New, name)] = Count(0, 8000);
        }

        return harness;
    }

    [Fact]
    [Capability("LIB-09")]
    public void EntriesWhoseFilesAreGoneAreMissingAndOneIsFoundByHand()
    {
        using var harness = Harness();
        var controller = harness.Controller;
        Assert.Equal([true, true, true, false], controller.Playlist.Items.Select(controller.IsMissing));

        controller.Relocate(0, Path.Combine(New, "Sungba.wav"));
        controller.Relocate(1, Path.Combine(New, "Terminator.wav"));

        Assert.False(controller.IsMissing(controller.Playlist.Items[0]));
        Assert.Equal(("Sungba", "Terminator (Live)", "Asake"), (controller.Playlist.Items[0].Title, controller.Playlist.Items[1].Title, controller.Playlist.Items[1].Artist));

        // Once found, it plays.
        controller.PlayAt(0);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal(Path.Combine(New, "Sungba.wav"), controller.Item!.Location);
        Assert.Throws<ArgumentException>(() => controller.Relocate(0, " "));
        Assert.Throws<ArgumentNullException>(() => controller.IsMissing(null!));
    }

    [Fact]
    [Capability("LIB-09")]
    public void AFolderIsLookedThroughForEveryMissingFileByName()
    {
        using var harness = Harness();
        var controller = harness.Controller;
        string[] listing = [Path.Combine(New, "SUNGBA.wav"), Path.Combine(New, "Live", "Terminator.wav"), Path.Combine(New, "a", "Joha.wav"), Path.Combine(New, "b", "Joha.wav")];
        foreach (var path in listing.Take(2))
        {
            harness.Files[path] = Count(0, 8000);
        }

        Assert.Equal(2, controller.RelinkMissing(New, _ => listing));

        Assert.Equal("Found 2 of 3 missing files", harness.Messages[^1]);
        Assert.Equal([Path.Combine(New, "SUNGBA.wav"), Path.Combine(New, "Live", "Terminator.wav"), Path.Combine(Old, "Joha.wav")], controller.Playlist.Items.Take(3).Select(item => item.Location));

        // Joha is in two places, so it stays missing until it is chosen by hand.
        Assert.Equal(0, controller.RelinkMissing(New, _ => listing));
        Assert.Equal("Found 0 of 1 missing files", harness.Messages[^1]);
        harness.Files[Path.Combine(New, "a", "Joha.wav")] = Count(0, 8000);
        controller.Relocate(2, Path.Combine(New, "a", "Joha.wav"));
        Assert.Equal(0, controller.RelinkMissing(New, _ => listing));
        Assert.Equal("Nothing in the playlist is missing.", harness.Messages[^1]);
    }

    [Fact]
    public void OneOrEveryMissingFileFoundIsSaidSoAndAFolderThatCannotBeReadIsToo()
    {
        using var harness = Harness();
        var controller = harness.Controller;
        controller.Playlist.RemoveAt(2);
        controller.Playlist.RemoveAt(1);

        Assert.Equal(1, controller.RelinkMissing(New, _ => [Path.Combine(New, "Sungba.wav")]));
        Assert.Equal("Found the missing file", harness.Messages[^1]);

        using var more = Harness();
        more.Controller.Playlist.RemoveAt(2);
        Assert.Equal(2, more.Controller.RelinkMissing(New, _ => [Path.Combine(New, "Sungba.wav"), Path.Combine(New, "Terminator.wav")]));
        Assert.Equal("Found all 2 missing files", more.Messages[^1]);

        using var locked = Harness();
        Assert.Equal(0, locked.Controller.RelinkMissing(New, _ => throw new UnauthorizedAccessException("no entry")));
        Assert.Equal($"{New} could not be looked through: no entry", locked.Messages[^1]);
        Assert.Throws<ArgumentException>(() => locked.Controller.RelinkMissing(""));
    }

    [Fact]
    public void ADiskFolderIsLookedThroughWhenNoListingIsGiven()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-relink-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "inner"));
            File.WriteAllBytes(Path.Combine(folder, "inner", "Sungba.wav"), [1]);
            using var harness = Harness();
            harness.Files[Path.Combine(folder, "inner", "Sungba.wav")] = [1];

            Assert.Equal(1, harness.Controller.RelinkMissing(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReplacingAnEntryKeepsItsPlaceInThePlayOrder()
    {
        var playlist = new Playlist(new Random(1)) { Shuffle = true };
        playlist.Add([new PlaylistItem("a"), new PlaylistItem("b"), new PlaylistItem("c")]);
        var current = playlist.JumpTo(1);
        var changes = 0;
        playlist.Changed += (_, _) => changes++;

        playlist.Replace(1, new PlaylistItem("b2"));

        Assert.Equal("b2", playlist.Current!.Location);
        Assert.Equal(1, playlist.CurrentIndex);
        Assert.NotSame(current, playlist.Current);
        playlist.Replace(0, new PlaylistItem("a2"));
        Assert.Equal("b2", playlist.Current!.Location);
        Assert.Equal(2, changes);
        Assert.Throws<ArgumentNullException>(() => playlist.Replace(0, null!));
    }
}
