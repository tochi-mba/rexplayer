using System.Text;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Playlist files opened in the player, and the parts of a file a cue sheet plays.</summary>
public sealed class PlaylistControllerTests
{
    private static readonly PlayerSettings Quiet = new() { TitleSeconds = 0 };

    private static bool EndedOn(PlayerController controller, string title) =>
        controller.State == SessionState.Ended && controller.Item?.Title == title && controller.Playlist.Current is null;

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    [Capability("PLF-01")]
    public void APlaylistFilePlaysItsEntriesWithTheirTitles()
    {
        using var harness = new ControllerHarness(settings: Quiet);
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(500, 400);
        harness.Files["list.m3u8"] = Text("#EXTM3U\n#EXTINF:1,Asake - Sungba\na.wav\nb.wav\n");

        harness.Controller.Open(["list.m3u8"]);
        harness.PumpUntil(c => EndedOn(c, "b"));

        Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
        Assert.Equal(["Sungba", "b"], harness.Controller.Playlist.Items.Select(item => item.Title));
        Assert.Equal("Asake", harness.Controller.Playlist.Items[0].Artist);
        Assert.False(harness.Controller.Playlist.Items[0].IsPart);
    }

    [Fact]
    public void PlaylistsNamingPlaylistsAreFollowedButNotForever()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: Quiet);
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["outer.m3u"] = Text("inner.pls\nloop.m3u\n");
        harness.Files["inner.pls"] = Text("[playlist]\nFile1=a.wav\n");
        harness.Files["loop.m3u"] = Text("loop.m3u\n");

        harness.Controller.Open(["outer.m3u"]);

        Assert.Equal(["a.wav"], harness.Controller.Playlist.Items.Select(item => item.Location));
    }

    [Fact]
    public void APlaylistThatCannotBeReadSaysSoAndAddsNothing()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: Quiet);
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["notes.m3u"] = Text("#EXTM3U\n#EXT-X-TARGETDURATION:6\nseg.ts\n");

        harness.Controller.Open(["missing.xspf", "notes.m3u", "a.wav", "https://example.com/list.m3u"]);

        Assert.Equal(["a.wav", "https://example.com/list.m3u"], harness.Controller.Playlist.Items.Select(item => item.Location));
        Assert.StartsWith("missing could not be read: ", harness.Messages[0], StringComparison.Ordinal);
        Assert.Contains("stream", harness.Messages[1], StringComparison.Ordinal);
    }

    [Fact]
    [Capability("PLF-04")]
    public void ACueSheetsTracksPlayTheWholeAlbumWithoutAGap()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings());
        harness.Files["album.wav"] = Count(0, 8000);
        harness.Files["album.cue"] = Text("""
            PERFORMER "Asake"
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "Olorun"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Amapiano"
                INDEX 01 00:00:30
            """);
        var controller = harness.Controller;
        var titles = new List<string>();
        controller.Changed += (_, _) => titles.Add(controller.Title);

        controller.Open(["album.cue"]);
        harness.PumpUntil(c => EndedOn(c, "Amapiano"));

        // One session from the first sample to the last: the second track took over without reopening.
        Assert.Single(harness.Sinks);
        Assert.Equal(Expected((0, 8000)), harness.Played());
        Assert.Equal(["Olorun", "Amapiano"], titles.Where(title => title.Length > 0).Distinct());
        Assert.Equal(TimeSpan.FromSeconds(0.6), controller.Duration);
        Assert.Equal("Asake", controller.Artist);
        Assert.Equal(["Olorun", "Amapiano"], harness.Messages);
        Assert.True(controller.Item!.IsPart);
    }

    [Fact]
    [Capability("PLF-04")]
    public void ATrackPartWayThroughAFileStartsAtItsIndexAndSeeksWithinItself()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: Quiet);
        harness.Files["album.wav"] = Count(0, 8000);
        harness.Files["album.cue"] = Text("""
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:30
              TRACK 02 AUDIO
                INDEX 01 00:00:60
            """);
        var controller = harness.Controller;

        controller.Open(["album.cue"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal(("Track 01", TimeSpan.FromSeconds(0.4), TimeSpan.Zero), (controller.Title, controller.Duration, controller.Position));

        controller.Seek(TimeSpan.FromSeconds(0.1));
        controller.Execute(Rex.Media.AppCore.Commands.CommandCatalog.PlayPause);
        harness.PumpUntil(c => EndedOn(c, "Track 02"));

        // From 0.5 s (the first track's 0.1 s) to the end, in one session.
        Assert.Equal(Expected((4000, 4000)), harness.Played());
    }

    [Fact]
    [Capability("PLF-04")]
    public void APartFollowedByOtherMediaEndsThereAndTheNextStarts()
    {
        using var harness = new ControllerHarness(settings: Quiet);
        harness.Files["album.wav"] = Count(0, 8000);
        harness.Files["other.wav"] = Count(9000, 400);
        harness.Files["album.cue"] = Text("""
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "First"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second"
                INDEX 01 00:00:30
            FILE "other.wav" WAVE
              TRACK 03 AUDIO
                TITLE "Other"
                INDEX 01 00:00:00
            """);
        var controller = harness.Controller;

        controller.Open(["album.cue"]);
        controller.Playlist.RemoveAt(1);
        harness.PumpUntil(c => EndedOn(c, "Other"));

        // The first track stopped at its end (the sink may have held a little more) and the other file played whole.
        Assert.Equal(2, harness.Sinks.Count);
        var first = harness.Sinks[0].Channel(0).ToArray();
        Assert.InRange(first.Length, 3200, 8000);
        Assert.Equal(Expected((0, first.Length)), first);
        Assert.Equal(Expected((9000, 400)), harness.Sinks[1].Channel(0).ToArray());
    }

    [Fact]
    [Capability("LIB-03")]
    public void ThePlaylistIsSavedInTheFormatItsNameAsksFor()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: Quiet);
        harness.Files["list.m3u8"] = Text("#EXTM3U\n#EXTINF:1,Asake - Sungba\nmusic/a.wav\nb.wav\n");
        var controller = harness.Controller;
        controller.Open(["list.m3u8"]);

        Assert.True(controller.SavePlaylist("music/copy.xspf"));
        Assert.Equal("Playlist saved as copy", harness.Messages[^1]);
        var saved = Encoding.UTF8.GetString(harness.Files["music/copy.xspf"]);
        Assert.Contains("<title>copy</title>", saved, StringComparison.Ordinal);
        Assert.Contains("<location>a.wav</location>", saved, StringComparison.Ordinal);
        Assert.Contains("<creator>Asake</creator>", saved, StringComparison.Ordinal);
        Assert.Equal("[playlist]\nFile1=music/a.wav\nTitle1=Asake - Sungba\nLength1=-1\nFile2=b.wav\nLength2=-1\nNumberOfEntries=2\nVersion=2\n", controller.PlaylistFileText("list.pls"));

        Assert.False(controller.SavePlaylist("readonly/list.m3u8"));
        Assert.StartsWith("The playlist could not be saved: ", harness.Messages[^1], StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => controller.PlaylistFileText(null!));
    }

    [Fact]
    [Capability("LIB-03")]
    public void ASavedPlaylistOnDiskOpensAgain()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-playlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "a.wav"), Count(0, 400));
            using var harness = new ControllerHarness(autoPlay: false, settings: Quiet, openSource: path => new Rex.Media.IO.FileByteSource(path), diskFolders: true);
            harness.Controller.Open([Path.Combine(folder, "a.wav")]);
            var list = Path.Combine(folder, "list.m3u8");

            Assert.True(harness.Controller.SavePlaylist(list));
            Assert.Equal("#EXTM3U\na.wav\n", File.ReadAllText(list));
            harness.Controller.Open([list]);
            Assert.Equal([Path.Combine(folder, "a.wav")], harness.Controller.Playlist.Items.Select(item => item.Location));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APartAtTheEndOfThePlaylistEndsThePlaying()
    {
        using var harness = new ControllerHarness(settings: Quiet);
        harness.Files["album.wav"] = Count(0, 8000);
        harness.Files["album.cue"] = Text("""
            FILE "album.wav" WAVE
              TRACK 01 AUDIO
                TITLE "Only"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:30
            """);
        var controller = harness.Controller;

        controller.Open(["album.cue"]);
        controller.Playlist.RemoveAt(1);
        harness.PumpUntil(c => EndedOn(c, "Only"));

        Assert.Equal(TimeSpan.FromSeconds(0.4), controller.Position);
    }
}
