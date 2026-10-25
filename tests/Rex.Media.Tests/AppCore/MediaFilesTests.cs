using Rex.Media.AppCore.Player;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class MediaFilesTests
{
    [Fact]
    public void NamesSortTheWayPeopleNumberThem()
    {
        string[] names = ["Track 10.mp3", "track 2.mp3", "Track 1.mp3", "Track 02.mp3", "Intro.mp3", "Track 1b.mp3", "Track 1.mp3", "a", "A"];

        var sorted = names.Order(NaturalOrder.Instance).ToList();

        Assert.Equal(["A", "a", "Intro.mp3", "Track 1.mp3", "Track 1.mp3", "Track 1b.mp3", "Track 02.mp3", "track 2.mp3", "Track 10.mp3"], sorted);
    }

    [Fact]
    public void NaturalOrderHandlesNullsAndPrefixes()
    {
        var order = NaturalOrder.Instance;

        Assert.Equal(0, order.Compare(null, null));
        Assert.True(order.Compare(null, "a") < 0);
        Assert.True(order.Compare("a", null) > 0);
        Assert.True(order.Compare("Disc", "Disc 2") < 0);
        Assert.True(order.Compare("9", "10") < 0);
        Assert.True(order.Compare("007", "7") < 0);
    }

    [Theory]
    [InlineData("film.MKV", true)]
    [InlineData("song.flac", true)]
    [InlineData("C:\\a\\b.Opus", true)]
    [InlineData("cover.jpg", true)]
    [InlineData("photo.HEIC", true)]
    [InlineData("notes", false)]
    [InlineData("album.nfo", false)]
    public void MediaIsRecognisedByExtension(string path, bool media)
    {
        Assert.Equal(media, MediaFiles.IsMedia(path));
    }

    [Fact]
    public void PastedFilesAreFilteredWithoutDroppingFoldersOrSupportedMedia()
    {
        var (supported, skipped) = MediaFiles.FilterClipboardPaths(
            ["C:\\Videos", "C:\\Videos\\clip.MKV", "https://example.test/clip.mp4", "C:\\notes.txt", ""],
            path => path == "C:\\Videos");

        Assert.Equal(["C:\\Videos", "C:\\Videos\\clip.MKV"], supported);
        Assert.Equal(3, skipped);
        Assert.Throws<ArgumentNullException>(() => MediaFiles.FilterClipboardPaths(null!, _ => false));
        Assert.Throws<ArgumentNullException>(() => MediaFiles.FilterClipboardPaths([], null!));
    }

    [Fact]
    public void PastedTextIsSplitUnquotedAndFilteredAsLocalPaths()
    {
        var (supported, skipped) = MediaFiles.FilterClipboardText(
            "\"C:\\Videos\\clip.MKV\"\r\nC:\\Videos\r\nhttps://example.test/clip.mp4\r\nnotes.txt\r\n",
            path => path == "C:\\Videos");

        Assert.Equal(["C:\\Videos\\clip.MKV", "C:\\Videos"], supported);
        Assert.Equal(2, skipped);
        var (empty, emptySkipped) = MediaFiles.FilterClipboardText(" \r\n ", _ => false);
        Assert.Empty(empty);
        Assert.Equal(0, emptySkipped);
        Assert.Throws<ArgumentNullException>(() => MediaFiles.FilterClipboardText(null!, _ => false));
        Assert.Throws<ArgumentNullException>(() => MediaFiles.FilterClipboardText("", null!));
    }

    [Fact]
    [Capability("PLF-09")]
    public void AFolderExpandsToItsMediaInNaturalOrderFilesBeforeSubfolders()
    {
        var files = new Dictionary<string, string[]>
        {
            ["Album"] = ["Album/10 Outro.mp3", "Album/cover.jpg", "Album/2 Song.mp3", "Album/1 Intro.mp3"],
            ["Album/Disc 10"] = ["Album/Disc 10/a.flac"],
            ["Album/Disc 2"] = ["Album/Disc 2/b.flac"],
        };
        var folders = new Dictionary<string, string[]> { ["Album"] = ["Album/Disc 10", "Album/Disc 2"] };

        var found = MediaFiles.ExpandFolder("Album", f => files.GetValueOrDefault(f, []), f => folders.GetValueOrDefault(f, []));

        Assert.Equal(["Album/1 Intro.mp3", "Album/2 Song.mp3", "Album/10 Outro.mp3", "Album/Disc 2/b.flac", "Album/Disc 10/a.flac"], found);
        Assert.Equal(3, MediaFiles.ExpandFolder("Album", f => files.GetValueOrDefault(f, []), f => folders.GetValueOrDefault(f, []), recursive: false).Count);
    }

    [Fact]
    [Capability("FMT-C19")]
    public void AFolderOfPicturesIsASlideshowAndAnAlbumKeepsItsCoverOut()
    {
        var files = new Dictionary<string, string[]>
        {
            ["Photos"] = ["Photos/b.png", "Photos/a.JPEG", "Photos/notes.txt", "Photos/c.gif"],
            ["Album"] = ["Album/cover.jpg", "Album/1 Sungba.mp3"],
        };

        Assert.Equal(["Photos/a.JPEG", "Photos/b.png", "Photos/c.gif"], MediaFiles.ExpandFolder("Photos", f => files.GetValueOrDefault(f, []), _ => []));
        Assert.Equal(["Album/1 Sungba.mp3"], MediaFiles.ExpandFolder("Album", f => files.GetValueOrDefault(f, []), _ => []));
        var disk = Directory.CreateTempSubdirectory("rexplayer-pictures-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(disk, "b.png"), [1]);
            File.WriteAllBytes(Path.Combine(disk, "a.gif"), [1]);
            Assert.Equal([Path.Combine(disk, "a.gif"), Path.Combine(disk, "b.png")], MediaFiles.ExpandFolder(disk));
        }
        finally
        {
            Directory.Delete(disk, recursive: true);
        }

        Assert.True(MediaFiles.IsPicture("x.webp"));
        Assert.False(MediaFiles.IsPicture("x.mp4"));
        Assert.Equal(Rex.Media.Library.LibraryKind.Picture, MediaFiles.LibraryKindOf("x.png"));
        Assert.Null(MediaFiles.LibraryKindOf("x.txt"));
    }

    [Fact]
    public void UnreadableFoldersAreSkippedAndLoopsEnd()
    {
        var found = MediaFiles.ExpandFolder(
            "Root",
            folder => folder == "Root/Locked" ? throw new UnauthorizedAccessException() : [folder + "/x.mp3"],
            folder => folder == "Root" ? ["Root/Locked", "Root/Loop"] : folder.StartsWith("Root/Loop", StringComparison.Ordinal) ? [folder + "/Loop"] : []);

        Assert.Equal("Root/x.mp3", found[0]);
        Assert.Equal(33, found.Count);
    }

    [Fact]
    public void AFolderOnDiskIsListed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "b.mp3"), []);
            File.WriteAllBytes(Path.Combine(folder, "a.txt"), []);
            File.WriteAllBytes(Path.Combine(folder, "inner", "c.wav"), []);

            var found = MediaFiles.ExpandFolder(folder).Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'));

            Assert.Equal(["b.mp3", "inner/c.wav"], found);
            Assert.Empty(MediaFiles.ExpandFolder(Path.Combine(folder, "missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ListingsAreRequired()
    {
        Assert.Throws<ArgumentNullException>(() => MediaFiles.ExpandFolder("x", null!, _ => []));
        Assert.Throws<ArgumentNullException>(() => MediaFiles.ExpandFolder("x", _ => [], null!));
    }

    [Theory]
    [InlineData(0, null, "0:00")]
    [InlineData(65.9, null, "1:05")]
    [InlineData(65, 3600.0, "0:01:05")]
    [InlineData(3723, null, "1:02:03")]
    [InlineData(-5, null, "-0:05")]
    [InlineData(600, -4000.0, "0:10:00")]
    public void TimesAreWrittenAsPeopleReadThem(double seconds, double? scale, string text)
    {
        Assert.Equal(text, TimeText.Format(TimeSpan.FromSeconds(seconds), scale is { } s ? TimeSpan.FromSeconds(s) : null));
    }

    [Theory]
    [InlineData(-30, 100, 0, 100, 0)]
    [InlineData(30, 100, 30, 100, 70)]
    [InlineData(105, 100, 100, 100, 0)]
    [InlineData(65, 0, 65, 1, -1)]
    [InlineData(-10, 0, 0, 1, -1)]
    [InlineData(90, 20, 20, 20, 0)]
    [Capability("PB-05")]
    public void TimelineNeverInventsOutOfRangePositionOrRemainingTime(
        double position, double duration, double elapsed, double maximum, double remaining)
    {
        var result = TimeText.Timeline(TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration));
        Assert.Equal(elapsed, result.Elapsed.TotalSeconds);
        Assert.Equal(maximum, result.SliderMaximum);
        Assert.Equal(remaining < 0 ? 0 : elapsed, result.SliderValue);
        Assert.Equal(remaining < 0 ? null : TimeSpan.FromSeconds(remaining), result.Remaining);
    }

    [Theory]
    [InlineData("1:02:03", 3723)]
    [InlineData("62:03", 3723)]
    [InlineData(" 1 : 05 ", 65)]
    [InlineData("3723", 3723)]
    [InlineData("12.5", 12.5)]
    [InlineData("0:59.5", 59.5)]
    [Capability("PB-05")]
    public void TypedTimesAreRead(string text, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TimeText.TryParse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("1:60:00")]
    [InlineData("1:61")]
    [InlineData("-5")]
    [InlineData("1:-2")]
    [InlineData("99999999999999999999")]
    public void TextThatIsNoTimeIsRefused(string? text)
    {
        Assert.Null(TimeText.TryParse(text));
    }
}
