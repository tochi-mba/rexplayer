using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The player's own background threads (the library's scanner, the library's and the playing
/// episode's section analysis) survive a fault of a reader's or their own: an exception that
/// escaped one of them ended the whole player. Each is written down with its stack instead.
/// </summary>
public sealed class WorkerFaultTests
{
    [Fact]
    public void TheLibraryScannerWritesDownItsOwnFaultAndStartsAgainWhenAsked()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-scanner-fault");
        library.AddFolder(folder);
        var log = RexLog.InMemory();
        var lists = 0;
        using var rested = new SemaphoreSlim(0);
        using var scanner = new LibraryScanner(library, _ => ++lists == 1 ? throw new OverflowException("the scanner's own fault") : Array.Empty<LibraryFile>(), (_, _) => null, log);
        scanner.StatusChanged += (_, _) =>
        {
            if (scanner.Status.Length == 0)
            {
                rested.Release();
            }
        };

        scanner.Request();
        Assert.True(rested.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Contains(log.Tail(10), entry => entry.Level == LogLevel.Error && entry.Message.Contains("the scanner's own fault", StringComparison.Ordinal)
            && entry.Message.Contains(" at ", StringComparison.Ordinal));
        Assert.False(scanner.IsBusy);

        scanner.Request();
        Assert.True(rested.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(2, lists);
    }

    [Fact]
    public void LibrarySectionAnalysisPassesOverAnEpisodeWhoseReaderFaultsAndCarriesOn()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-sections-fault-").FullName;
        try
        {
            var one = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "One")).FullName, "Series.S01E01.mkv");
            var two = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "Two")).FullName, "Other.S01E01.mkv");
            File.WriteAllBytes(one, [1]);
            File.WriteAllBytes(two, [2]);
            var log = RexLog.InMemory();
            var memory = new PlayerMemory(RexStore.InMemory());
            using var worker = new LibrarySectionAnalysis(memory, new SectionAnalysisCache(), (path, _) => path == one
                ? throw new OverflowException("a reader's own fault")
                : new EpisodeAnalysis(path, SectionAnalysis.SignatureOf(path), TimeSpan.FromMinutes(42), [], [], []), log, background: false);

            worker.SetQueue([one, two]);
            worker.RunPending();

            Assert.Contains(log.Tail(10), entry => entry.Level == LogLevel.Error && entry.Message.Contains("a reader's own fault", StringComparison.Ordinal));
            Assert.NotNull(memory.Proposals(two, PlayerController.DetectionSignature(two, [])));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ThePlayingEpisodesAnalysisWritesDownAReadersFaultAndPlayingGoesOn()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-sections-fault-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E01.mkv");
            var bytes = SectionDetectionControllerTests.TestVideo();
            File.WriteAllBytes(path, bytes);
            var log = RexLog.InMemory();
            using var reported = new ManualResetEventSlim();
            log.EntryWritten += (_, entry) =>
            {
                if (entry.Source == "sections" && entry.Level == LogLevel.Error && entry.Message.Contains("a reader's own fault", StringComparison.Ordinal))
                {
                    reported.Set();
                }
            };
            using var test = new ControllerHarness(autoPlay: false, pictures: true, log: log);
            test.Files[path] = bytes;
            test.Controller.SectionAnalyzer = (_, _) => throw new OverflowException("a reader's own fault");
            test.Controller.Open([path]);
            test.PumpUntil(x => x.State == SessionState.Ready);
            Assert.True(reported.Wait(ControllerHarness.Patience, TestContext.Current.CancellationToken));
            Assert.Equal(SessionState.Ready, test.Controller.State);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
