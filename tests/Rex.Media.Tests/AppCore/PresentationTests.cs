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
    public void OnlineLyricsMustMatchTheSongAndKeepRealTiming()
    {
        var answer = new OnlineLyricsResult(" Sungba ", "Asake", 120, false,
            "[00:03.00]Sungba", "Plain words");
        Assert.True(answer.Match("Sungba", "asake", TimeSpan.FromSeconds(121))!.IsTimed);
        Assert.Null(answer.Match("Another song", "Asake", TimeSpan.FromSeconds(120)));
        Assert.Null(answer.Match("Sungba", "Another artist", TimeSpan.FromSeconds(120)));
        Assert.Null(answer.Match("Sungba", "Asake", TimeSpan.FromSeconds(124)));
        Assert.Null((answer with { Instrumental = true }).Match("Sungba", "Asake", TimeSpan.FromSeconds(120)));
        Assert.Null((answer with { Duration = double.NaN }).Match("Sungba", "Asake", TimeSpan.FromSeconds(120)));
        Assert.False((answer with { SyncedLyrics = null }).Match("Sungba", "Asake", TimeSpan.FromSeconds(120))!.IsTimed);
        Assert.Null((answer with { SyncedLyrics = null, PlainLyrics = null }).Match("Sungba", "Asake", TimeSpan.FromSeconds(120)));
    }

    [Fact]
    public void OnlineLyricsDoNotReplaceLocalLyricsOrAChangedSong()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["song.wav"] = Count(0, 400);
        harness.Files["local.wav"] = Count(0, 400);
        harness.Files["local.lrc"] = Encoding.UTF8.GetBytes("[00:00.00]Local words");
        var controller = harness.Controller;
        var online = Lyrics.Parse("[00:00.00]Online words")!;
        controller.Open(["song.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        var first = controller.Item!;
        Assert.True(controller.UseOnlineLyrics(first, online));
        Assert.Same(online, controller.Lyrics);
        Assert.False(controller.UseOnlineLyrics(first, Lyrics.Parse("Other")!));

        controller.Open(["local.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "local.wav");
        Assert.False(controller.UseOnlineLyrics(first, online));
        Assert.False(controller.UseOnlineLyrics(controller.Item!, online));
        Assert.Equal("Local words", controller.Lyrics!.Lines[0].Text);
    }

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
    public void KaraokeWordSelectionUsesOnlyValidatedSourceTimesAcrossSeekAndPause()
    {
        var lyrics = Lyrics.Parse("[00:01.00]<00:01.00>First <00:01.50>word\n[00:03.00]line only")!;
        var wordLine = lyrics.Lines[0];
        Assert.Equal(-1, Lyrics.WordIndexAt(wordLine, TimeSpan.Zero));
        Assert.Equal(0, Lyrics.WordIndexAt(wordLine, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, Lyrics.WordIndexAt(wordLine, TimeSpan.FromMilliseconds(1499)));
        Assert.Equal(1, Lyrics.WordIndexAt(wordLine, TimeSpan.FromMilliseconds(1500)));
        Assert.Equal(1, Lyrics.WordIndexAt(wordLine, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, Lyrics.WordIndexAt(wordLine, TimeSpan.FromSeconds(1)));
        Assert.Equal(-1, Lyrics.WordIndexAt(lyrics.Lines[1], TimeSpan.FromSeconds(3)));
        Assert.Equal(-1, Lyrics.WordIndexAt(new LyricLine(null, "untimed"), TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentNullException>(() => Lyrics.WordIndexAt(null!, TimeSpan.Zero));
    }

    [Fact]
    [Capability("META-09")]
    public void LyricModesNeverInventWordTimingWhenSomeOrAllSourceMarkersAreAbsent()
    {
        Assert.Equal(LyricsDisplayMode.Plain, Lyrics.Parse("Hello\nworld")!.DisplayMode);
        Assert.Equal(LyricsDisplayMode.LineTimed,
            Lyrics.Parse("[00:01.00]Hello\n[00:02.00]world")!.DisplayMode);
        Assert.Equal(LyricsDisplayMode.WordTimed,
            Lyrics.Parse("[00:01.00]<00:01.00>Hello\n[00:02.00]<00:02.00>world")!.DisplayMode);
        Assert.Equal(LyricsDisplayMode.MixedTimed,
            Lyrics.Parse("[00:01.00]<00:01.00>Hello\n[00:02.00]world")!.DisplayMode);
        Assert.Equal(LyricsDisplayMode.LineTimed,
            Lyrics.Parse("[00:01.00]<00:00.80>bad first stamp\n[00:02.00]line")!.DisplayMode);
        Assert.Null(Lyrics.Parse("[ar:Artist]"));
    }

    [Fact]
    [Capability("META-09")]
    public void DisplayedLyricsCanBeSavedAsLrcWithoutInventingTimes()
    {
        var timed = Lyrics.Parse("[00:01.00]<00:01.00>First <00:01.50>word\n[00:03.00]line only")!;
        Assert.Equal("[00:01.00]<00:01.00>First <00:01.50>word\n[00:03.00]line only\n", timed.ToLrc());
        Assert.Equal(LyricsDisplayMode.MixedTimed, Lyrics.Parse(timed.ToLrc())!.DisplayMode);

        var plain = Lyrics.Parse("One\n\nTwo")!;
        Assert.Equal("One\n\nTwo\n", plain.ToLrc());
        Assert.Equal(LyricsDisplayMode.Plain, Lyrics.Parse(plain.ToLrc())!.DisplayMode);
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
    public void ImpossibleSecondsDoNotBecomeTimedLyrics()
    {
        var mixed = Lyrics.Parse("""
            [00:65.00]malformed
            [00:01.00]valid
            """)!;
        Assert.True(mixed.IsTimed);
        Assert.Equal("valid", Assert.Single(mixed.Lines).Text);
        Assert.False(Lyrics.Parse("[00:99]plain text")!.IsTimed);
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
    [Capability("META-09")]
    public void UnreadableLocalLyricsAndCoverDoNotAbortAudioPlayback()
    {
        using var harness = new ControllerHarness(autoPlay: false,
            settings: new PlayerSettings { TitleSeconds = 0 },
            readFile: _ => throw new IOException("An inaccessible media sidecar"));
        var song = Path.Combine("music", "album", "song.wav");
        harness.Files[song] = Count(0, 400);
        // The folder listing finds these files, but reading either one fails.
        harness.Files[Path.Combine("music", "album", "cover.jpg")] = [1, 2, 3];
        harness.Files[Path.Combine("music", "album", "song.lrc")] = Encoding.UTF8.GetBytes("[00:01.00]hello");

        harness.Controller.Open([song]);
        harness.PumpUntil(controller => controller.State == SessionState.Ready);

        Assert.Null(harness.Controller.CoverArt);
        Assert.Null(harness.Controller.Lyrics);
        Assert.Equal("The lyrics file could not be read.", harness.Controller.LyricsProblem);
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
