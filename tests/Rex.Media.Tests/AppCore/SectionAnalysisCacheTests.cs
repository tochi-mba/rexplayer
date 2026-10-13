using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Analyses kept between runs: compact, checked against the file, never in the player's store.</summary>
public sealed class SectionAnalysisCacheTests
{
    private static EpisodeAnalysis Analysis(string path, string signature = "v9:1:2") => new(path, signature, TimeSpan.FromMinutes(44),
        [new AudioLandmark(0x00ABCDEF, 3), new AudioLandmark(7, 3), new AudioLandmark(uint.MaxValue, 40_000)],
        [new AudioLandmark(5, 70_000), new AudioLandmark(6, 70_031)],
        [new CreditFrameSample(TimeSpan.FromSeconds(2500.5), 0.1, 0.92, 0.02, [0.1f, 0.5f, 1f], 0.3)]);

    private static void Same(EpisodeAnalysis expected, EpisodeAnalysis actual)
    {
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.Duration, actual.Duration);
        Assert.Equal(expected.Opening, actual.Opening);
        Assert.Equal(expected.Closing, actual.Closing);
        var (want, got) = (Assert.Single(expected.Frames), Assert.Single(actual.Frames));
        Assert.Equal(want with { Rows = [] }, got with { Rows = [] });
        Assert.Equal(want.Rows, got.Rows);
    }

    [Fact]
    public void AnAnalysisComesBackOnlyForTheSameFileAsItIsNow()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-cache-").FullName;
        try
        {
            var cache = new SectionAnalysisCache(Path.Combine(folder, "sections"));
            var episode = Analysis(@"D:\TV\Show\Show.S01E02.mkv");
            Assert.Null(cache.Get(episode.Path, episode.Signature));
            cache.Set(episode);
            Same(episode, cache.Get(episode.Path, episode.Signature)!);
            // The same file however Windows cases its path; a changed file is analysed again.
            Same(episode with { Path = @"d:\tv\show\show.s01e02.mkv" }, cache.Get(@"d:\tv\show\show.s01e02.mkv", episode.Signature)!);
            Assert.Null(cache.Get(episode.Path, "v9:1:3"));
            var file = Assert.Single(Directory.GetFiles(Path.Combine(folder, "sections")));
            Assert.True(new FileInfo(file).Length < 400);

            // Damaged, cut short or from another version: analysed again, never an error.
            File.WriteAllBytes(file, [1, 2, 3]);
            Assert.Null(cache.Get(episode.Path, episode.Signature));
            using (var stream = File.Create(file))
            {
                SectionAnalysisCache.Write(stream, episode);
            }

            var whole = File.ReadAllBytes(file);
            File.WriteAllBytes(file, whole[..(whole.Length / 2)]);
            Assert.Null(cache.Get(episode.Path, episode.Signature));
            Assert.Null(SectionAnalysisCache.Read(new MemoryStream(Packed(writer => writer.Write(0))), "x"));
            Assert.Null(SectionAnalysisCache.Read(new MemoryStream(Packed(writer =>
            {
                writer.Write(0x41535852);
                writer.Write(SectionAnalysis.FeatureVersion + 1);
            })), "x"));
            Assert.Throws<InvalidDataException>(() => SectionAnalysisCache.Read(new MemoryStream(Packed(writer =>
            {
                writer.Write(0x41535852);
                writer.Write(SectionAnalysis.FeatureVersion);
                writer.Write("sig");
                writer.Write(1L);
                writer.Write(-5);
            })), "x"));
            Assert.Throws<ArgumentNullException>(() => cache.Get(null!, "s"));
            Assert.Throws<ArgumentNullException>(() => cache.Get("a", null!));
            Assert.Throws<ArgumentNullException>(() => cache.Set(null!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static byte[] Packed(Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using (var compressed = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(compressed))
        {
            write(writer);
        }

        return output.ToArray();
    }

    [Fact]
    public void TheLeastRecentlyUsedAnalysesAreRemovedBeyondTheLimit()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-cache-trim-").FullName;
        try
        {
            var cache = new SectionAnalysisCache(folder);
            for (var n = 0; n < SectionAnalysisCache.Limit; n++)
            {
                cache.Set(Analysis($"Show.S01E{n:000}.mkv"));
            }

            // Every file is a minute older than the next, and the oldest is used again.
            var files = new DirectoryInfo(folder).GetFiles().OrderBy(file => file.Name).ToArray();
            for (var n = 0; n < files.Length; n++)
            {
                files[n].LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-1000 + n);
            }

            var oldest = files.OrderBy(file => file.LastWriteTimeUtc).First();
            var used = Enumerable.Range(0, SectionAnalysisCache.Limit).Select(n => $"Show.S01E{n:000}.mkv")
                .First(path => PlayerMemory.KeyFor(path) + ".sections" == oldest.Name);
            Assert.NotNull(cache.Get(used, "v9:1:2"));
            cache.Set(Analysis("Show.S02E01.mkv"));
            Assert.Equal(SectionAnalysisCache.Limit, Directory.GetFiles(folder).Length);
            Assert.NotNull(cache.Get(used, "v9:1:2"));
            Assert.NotNull(cache.Get("Show.S02E01.mkv", "v9:1:2"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WithoutAFolderOrAWritableDiskTheCacheOnlyCostsTime()
    {
        var memory = new SectionAnalysisCache();
        Assert.Null(memory.Folder);
        var episode = Analysis("Show.S01E01.mkv");
        Assert.Null(memory.Get(episode.Path, episode.Signature));
        memory.Set(episode);
        Same(episode, memory.Get(episode.Path, episode.Signature)!);
        Assert.Null(memory.Get(episode.Path, "other"));

        // A folder that cannot be made (a file is in the way): nothing is kept, nothing fails.
        var blocker = Path.GetTempFileName();
        try
        {
            var blocked = new SectionAnalysisCache(Path.Combine(blocker, "sections"));
            blocked.Set(episode);
            Assert.Null(blocked.Get(episode.Path, episode.Signature));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void AnalysesKeptInTheStoreByEarlierVersionsAreForgotten()
    {
        var store = RexStore.InMemory();
        store.Set("episode-analysis/abc", "{}");
        store.Set("episode-analysis/def", "{}");
        store.Set("resume/abc", "1");
        var memory = new PlayerMemory(store);
        Assert.Equal(2, memory.ForgetStoredAnalyses());
        Assert.Equal(0, memory.ForgetStoredAnalyses());
        Assert.Equal("1", store.Get("resume/abc"));
    }
}
