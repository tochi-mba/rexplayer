using Rex.Media.AppCore.Player;
using Rex.Media.Diagnostics;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Episodes are analysed in the library without opening a playback session.</summary>
public sealed class LibrarySectionAnalysisTests
{
    [Fact]
    public void ASeasonIsAnalysedOnceAndChangingASiblingInvalidatesEveryDependentResult()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-background-episode-").FullName;
        try
        {
            var paths = Enumerable.Range(1, 3)
                .Select(number => Path.Combine(folder, $"Series.S01E{number:00}.mkv")).ToArray();
            foreach (var path in paths)
            {
                File.WriteAllBytes(path, [1, 2, 3]);
            }

            var memory = new PlayerMemory(RexStore.InMemory());
            var cache = new SectionAnalysisCache();
            var decoded = new List<string>();
            using var worker = new LibrarySectionAnalysis(memory, cache, (file, token) =>
            {
                token.ThrowIfCancellationRequested();
                decoded.Add(file);
                return new EpisodeAnalysis(file, SectionAnalysis.SignatureOf(file),
                    TimeSpan.FromMinutes(44), [], [], []);
            }, RexLog.InMemory(), background: false);

            worker.SetQueue(paths);
            worker.RunPending();
            Assert.Equal(3, decoded.Count);
            foreach (var path in paths)
            {
                var siblings = EpisodeGrouping.Siblings(path, paths, SectionDetector.SiblingsCompared);
                var signature = PlayerController.DetectionSignature(path, siblings);
                Assert.NotNull(memory.Proposals(path, signature));
            }

            // Empty proposals are still cached and do not decode again.
            worker.SetQueue(paths);
            worker.RunPending();
            Assert.Equal(3, decoded.Count);

            // Playback interrupts the queue and it resumes afterwards.
            worker.SetPaused(true);
            using (var changed = new FileStream(paths[1], FileMode.Append))
            {
                changed.WriteByte(4);
            }
            worker.SetQueue(paths);
            worker.RunPending();
            Assert.Equal(3, decoded.Count);
            worker.SetPaused(false);
            worker.RunPending();
            Assert.Equal(4, decoded.Count);
            Assert.Equal(paths[1], decoded[^1]);
            foreach (var path in paths)
            {
                var siblings = EpisodeGrouping.Siblings(path, paths, SectionDetector.SiblingsCompared);
                Assert.NotNull(memory.Proposals(path, PlayerController.DetectionSignature(path, siblings)));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AQueueIgnoresSpecialsAndNonEpisodeFiles()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-background-extras-").FullName;
        try
        {
            var special = Path.Combine(folder, "Series.S00E01.mkv");
            var extra = Path.Combine(folder, "Series.S01E02.sample.mkv");
            var other = Path.Combine(folder, "holiday.mp4");
            File.WriteAllBytes(special, [1]);
            File.WriteAllBytes(extra, [2]);
            File.WriteAllBytes(other, [3]);
            var calls = 0;
            using var worker = new LibrarySectionAnalysis(
                new PlayerMemory(RexStore.InMemory()), new SectionAnalysisCache(),
                (file, token) =>
                {
                    calls++;
                    return new EpisodeAnalysis(file, SectionAnalysis.SignatureOf(file),
                        TimeSpan.FromMinutes(40), [], [], []);
                }, RexLog.InMemory(), background: false);
            worker.SetQueue([special, extra, other]);
            worker.RunPending();
            Assert.Equal(0, calls);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
