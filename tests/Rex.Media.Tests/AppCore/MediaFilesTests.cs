using Rex.Media.AppCore.Player;

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
    [InlineData("cover.jpg", false)]
    [InlineData("notes", false)]
    [InlineData("album.nfo", false)]
    public void MediaIsRecognisedByExtension(string path, bool media)
    {
        Assert.Equal(media, MediaFiles.IsMedia(path));
    }

    [Fact]
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
    [InlineData("1:02:03", 3723)]
    [InlineData("62:03", 3723)]
    [InlineData(" 1 : 05 ", 65)]
    [InlineData("3723", 3723)]
    [InlineData("12.5", 12.5)]
    [InlineData("0:59.5", 59.5)]
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
