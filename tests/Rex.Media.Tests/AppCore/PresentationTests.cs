using System.Text;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>What shows while music plays: its picture and its lyrics (AU-19, META-06, META-09).</summary>
public sealed class PresentationTests
{
    [Fact]
    [Capability("META-09")]
    public void TimedLyricsAreReadFromLrc()
    {
        var lyrics = Lyrics.Parse("""
            [ar:Asake]
            [ti:Sungba]
            [offset:+500]
            [00:12.30]Ọmọ ọlọ́run
            [00:05.00][00:20.5]Sungba
            [01:02:7]<01:02.70>word <01:03.00>by word
            [00:00]
            """)!;

        Assert.True(lyrics.IsTimed);
        Assert.Equal(
            [(4.5, "Sungba"), (11.8, "Ọmọ ọlọ́run"), (20.0, "Sungba"), (62.2, "word by word")],
            lyrics.Lines.Where(line => line.Text.Length > 0).Select(line => (line.At!.Value.TotalSeconds, line.Text)));
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(-1)));
        Assert.Equal("Sungba", lyrics.Lines[lyrics.IndexAt(TimeSpan.FromSeconds(5))].Text);
        Assert.Equal("Ọmọ ọlọ́run", lyrics.Lines[lyrics.IndexAt(TimeSpan.FromSeconds(15))].Text);
        Assert.Equal("word by word", lyrics.Lines[lyrics.IndexAt(TimeSpan.FromMinutes(5))].Text);
    }

    [Fact]
    [Capability("META-09")]
    public void PlainLyricsKeepTheirVersesAndNothingIsNothing()
    {
        var lyrics = Lyrics.Parse("\n\nFirst verse\n\nSecond verse\n\n")!;

        Assert.False(lyrics.IsTimed);
        Assert.Equal(["First verse", "", "Second verse"], lyrics.Lines.Select(line => line.Text));
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(1)));
        Assert.Null(Lyrics.Parse(null));
        Assert.Null(Lyrics.Parse("  "));
        Assert.Null(Lyrics.Parse("[ar:Asake]\n[ti:Sungba]"));
        Assert.Equal(-1, Lyrics.Parse("[00:01]x\n[offset:-1000]")!.IndexAt(TimeSpan.Zero));
    }

    [Theory]
    [Capability("META-06")]
    [InlineData(new[] { "b.jpg", "Folder.PNG", "front.jpg", "AlbumArtSmall.jpg" }, "Folder.PNG")]
    [InlineData(new[] { "COVER.webp", "folder.jpg" }, "COVER.webp")]
    [InlineData(new[] { "AlbumArtSmall.jpg", "AlbumArt_{X}_Large.jpg", "song.mp3" }, "AlbumArt_{X}_Large.jpg")]
    [InlineData(new[] { "front.jpeg", "cover.gif" }, "front.jpeg")]
    [InlineData(new[] { "song.mp3", "cover.txt" }, null)]
    public void TheFolderPictureIsChosenByName(string[] files, string? chosen)
    {
        var folder = Path.Combine("music", "album");
        var found = FolderArt.Find(Path.Combine(folder, "song.mp3"), _ => files.Select(name => Path.Combine(folder, name)));

        Assert.Equal(chosen, found is null ? null : Path.GetFileName(found));
    }

    [Fact]
    public void NoFolderNoUrlAndAnUnreadableFolderHaveNoPicture()
    {
        Assert.Null(FolderArt.Find("song.mp3", _ => ["cover.jpg"]));
        Assert.Null(FolderArt.Find("https://example.com/a/song.mp3", _ => ["cover.jpg"]));
        Assert.Null(FolderArt.Find(Path.Combine("a", "song.mp3"), _ => throw new UnauthorizedAccessException()));
        Assert.Null(FolderArt.Find(Path.Combine(Path.GetTempPath(), "rexplayer-no-such-folder", "song.mp3")));
        Assert.Throws<ArgumentNullException>(() => FolderArt.Find(null!));
    }

    [Fact]
    [Capability("AU-19")]
    public void TheControllerFindsThePictureAndTheLyricsOfWhatPlays()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        var album = Path.Combine("music", "album");
        var song = Path.Combine(album, "sungba.wav");
        harness.Files[song] = Count(0, 400);
        harness.Files[Path.Combine(album, "cover.jpg")] = [1, 2, 3];
        harness.Files[Path.Combine(album, "sungba.lrc")] = Encoding.UTF8.GetBytes("[00:00.00]Sungba");
        harness.Files["other.wav"] = Count(0, 400);
        var controller = harness.Controller;

        controller.Open([song]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal([1, 2, 3], controller.CoverArt);
        Assert.Equal("Sungba", controller.Lyrics!.Lines[controller.LyricIndex].Text);

        // Another item forgets them; one with nothing beside it has none.
        controller.Open(["other.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "other.wav");
        Assert.Null(controller.CoverArt);
        Assert.Null(controller.Lyrics);
        Assert.Equal(-1, controller.LyricIndex);
    }
}
