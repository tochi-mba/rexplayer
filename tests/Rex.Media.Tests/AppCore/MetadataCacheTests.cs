using System.Text;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The metadata cache on disk: any key becomes a safe file name, what is written is read back,
/// the downloads are measured and cleared as one thing, and the user's choices live outside them.
/// </summary>
public sealed class MetadataCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-metadata-" + Guid.NewGuid().ToString("N"));

    private MetadataCache Cache => new(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AnyKeyBecomesASafeUniqueFileName()
    {
        var show = MetadataCache.FileName("The Show: Part 2?");
        Assert.Matches("^[a-z0-9]+-[a-f0-9]{16}$", show);
        Assert.Equal(show, MetadataCache.FileName("the show: part 2?"));
        Assert.NotEqual(show, MetadataCache.FileName("The Show: Part 3?"));
        Assert.Matches("^[a-f0-9]{16}$", MetadataCache.FileName("..."));
        Assert.Equal(16 + 1 + 40, MetadataCache.FileName(new string('a', 200)).Length);
        Assert.Throws<ArgumentNullException>(() => MetadataCache.FileName(null!));
    }

    [Fact]
    public void WhatIsWrittenIsReadBackAndCanBeForgotten()
    {
        var cache = Cache;
        Assert.Null(cache.ReadText("tv", "The Show"));
        Assert.Null(cache.ReadBytes("art", "The Show", ".jpg"));

        cache.WriteText("tv", "The Show", """{"id":1}""");
        cache.Write("art", "The Show", ".jpg", [1, 2, 3]);

        Assert.Equal("""{"id":1}""", cache.ReadText("tv", "The Show"));
        Assert.Equal(new byte[] { 1, 2, 3 }, cache.ReadBytes("art", "The Show", ".jpg"));
        Assert.StartsWith(cache.Downloads, cache.PathFor("tv", "The Show", ".json"), StringComparison.OrdinalIgnoreCase);

        cache.Remove("tv", "The Show", ".json");
        cache.Remove("tv", "The Show", ".json");
        Assert.Null(cache.ReadText("tv", "The Show"));
        Assert.Throws<ArgumentNullException>(() => cache.WriteText("tv", "The Show", null!));
        Assert.Throws<ArgumentNullException>(() => cache.Write("tv", "The Show", ".json", null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataCache(null!));
    }

    [Fact]
    public void AWriteReplacesWholeAndLeavesNoStagingBehind()
    {
        var cache = Cache;
        cache.WriteText("tv", "The Show", "old");
        cache.WriteText("tv", "The Show", "new");

        Assert.Equal("new", cache.ReadText("tv", "The Show"));
        Assert.Single(Directory.GetFiles(Path.Combine(cache.Downloads, "tv")));
    }

    [Fact]
    public void TheDownloadsAreMeasuredAndClearedAsOneThing()
    {
        var cache = Cache;
        Assert.Equal(0, cache.Size());
        cache.Clear();

        cache.Write("art", "a", ".jpg", new byte[100]);
        cache.Write("tv", "b", ".json", Encoding.UTF8.GetBytes("{}"));
        Assert.Equal(102, cache.Size());

        // The user's own choices sit outside the downloads and survive a clear.
        Directory.CreateDirectory(cache.Folder);
        File.WriteAllText(cache.ChoicesPath, "{}");

        cache.Clear();
        Assert.Equal(0, cache.Size());
        Assert.False(Directory.Exists(cache.Downloads));
        Assert.True(File.Exists(cache.ChoicesPath));
    }

    [Fact]
    public void ChosenArtworkLivesOutsideTheDownloadsAndSurvivesAClear()
    {
        var cache = Cache;
        Assert.False(File.Exists(cache.ChosenArtPath("asake\u001fwork of art")));
        cache.WriteChosenArt("asake\u001fwork of art", [1, 2]);
        cache.WriteChosenArt("asake\u001fwork of art", [3, 4, 5]);
        cache.Write("tv", "b", ".json", [9]);

        Assert.Equal(new byte[] { 3, 4, 5 }, File.ReadAllBytes(cache.ChosenArtPath("asake\u001fwork of art")));
        Assert.Equal(1, cache.Size());

        cache.Clear();
        Assert.True(File.Exists(cache.ChosenArtPath("asake\u001fwork of art")));

        cache.RemoveChosenArt("asake\u001fwork of art");
        cache.RemoveChosenArt("asake\u001fwork of art");
        Assert.False(File.Exists(cache.ChosenArtPath("asake\u001fwork of art")));
        Assert.Throws<ArgumentNullException>(() => cache.WriteChosenArt("k", null!));
    }

    [Theory]
    [InlineData(0, "nothing yet")]
    [InlineData(-5, "nothing yet")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(400_000, "391 KB")]
    [InlineData(3_355_443, "3.2 MB")]
    [InlineData(2_576_980_378, "2.4 GB")]
    public void SizesAreDescribedInWords(long bytes, string words) => Assert.Equal(words, MetadataCache.Describe(bytes));
}
