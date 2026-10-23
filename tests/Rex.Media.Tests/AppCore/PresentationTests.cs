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

    [Fact]
    [Capability("META-09")]
    public void EnhancedLrcKeepsExactWordTimesAndRespectsOffset()
    {
        var lyrics = Lyrics.Parse("""
            [offset:+500]
            [00:01.00]<00:01.00>안녕 <00:01.25>world
            [00:02.00]Line timing only
            [00:04.00]<00:04.00>  مرحبا <00:04.50>世界
            """)!;

        Assert.Equal(TimeSpan.FromMilliseconds(500), lyrics.Lines[0].At);
        Assert.Equal("안녕 world", lyrics.Lines[0].Text);
        Assert.Equal(
            [(TimeSpan.FromMilliseconds(500), "안녕 "), (TimeSpan.FromMilliseconds(750), "world")],
            lyrics.Lines[0].Words.Select(word => (word.At, word.Text)));
        Assert.Empty(lyrics.Lines[1].Words);
        Assert.Equal("مرحبا 世界", lyrics.Lines[2].Text);
        Assert.Equal(
            [(TimeSpan.FromMilliseconds(3500), "مرحبا "), (TimeSpan.FromMilliseconds(4000), "世界")],
            lyrics.Lines[2].Words.Select(word => (word.At, word.Text)));
    }

    [Fact]
    [Capability("META-09")]
    public void UnreliableEnhancedLrcRetainsLinesButDoesNotClaimWordTiming()
    {
        var lyrics = Lyrics.Parse("""
            [00:01.00]line only
            [00:02.00]<00:01.90>too early
            [00:03.00]<00:03.00>one<00:03.00>duplicate
            [00:04.00]<00:04.30>later<00:04.10>earlier
            [00:05.00]untimed prefix <00:05.20>rest
            [00:06.00]<00:06.00><00:06.20>no first word
            [00:07.00]<00:07.00>first<00:09.00>past next line
            [00:08.00]next line
            [00:09.00]<00:09.00>valid
            [00:10.00]<00:65.00>invalid seconds
            """)!;

        Assert.Equal(10, lyrics.Lines.Count);
        Assert.All(lyrics.Lines.Take(8), line => Assert.Empty(line.Words));
        Assert.Single(lyrics.Lines[8].Words);
        Assert.Empty(lyrics.Lines[9].Words);
        Assert.Equal("too early", lyrics.Lines[1].Text);
        Assert.Equal("untimed prefix rest", lyrics.Lines[4].Text);
    }

    [Fact]
    [Capability("META-09")]
    public void SeekSearchUsesLastDuplicateAndHandlesManyLinesAndNegativeStarts()
    {
        var builder = new StringBuilder("[offset:+2000]\n[00:01.00]<00:01.00>early\n");
        for (var second = 2; second < 5000; second++)
        {
            builder.Append('[').Append((second / 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append((second % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(".00]line\n");
        }

        builder.Append("[01:00.00]duplicate");
        var lyrics = Lyrics.Parse(builder.ToString())!;
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromSeconds(-1), Assert.Single(lyrics.Lines[0].Words).At);
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(-2)));
        Assert.Equal(4999, lyrics.IndexAt(TimeSpan.FromSeconds(6000)));
        Assert.Equal("duplicate", lyrics.Lines[lyrics.IndexAt(TimeSpan.FromSeconds(58))].Text);
    }

    [Fact]
    [Capability("META-09")]
    public void DamagedOffsetTagsCannotAbortLyricsOrEraseTheLastValidOffset()
    {
        var lyrics = Lyrics.Parse("""
            [offset:+500]
            [offset:9999999999999999999999999]
            [00:02.00]still here
            """)!;

        Assert.True(lyrics.IsTimed);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), Assert.Single(lyrics.Lines).At);
        Assert.Equal("still here", lyrics.Lines[0].Text);
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromMilliseconds(1500)));
    }

    [Fact]
    [Capability("META-09")]
    public void LongUnicodeAndRtlLinesRetainTheirCharactersAndLastDuplicateWins()
    {
        var words = string.Concat(Enumerable.Repeat("مرحبا 世界 🎵 ", 128)).Trim();
        var lyrics = Lyrics.Parse($"[ar:فنان]\n[00:01.00][00:02.00]{words}\n[00:02.00]repeated")!;

        Assert.True(lyrics.IsTimed);
        Assert.Equal(3, lyrics.Lines.Count);
        Assert.Equal(words, lyrics.Lines[0].Text);
        Assert.Equal(words, lyrics.Lines[1].Text);
        Assert.Empty(lyrics.Lines[1].Words);
        Assert.Equal("repeated", lyrics.Lines[lyrics.IndexAt(TimeSpan.FromSeconds(2))].Text);
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.Zero));
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
