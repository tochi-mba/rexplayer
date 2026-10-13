using Rex.Media.AppCore.Library;
using Rex.Media.Diagnostics;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The library's scanner (LIB-05): folders listed, new files read in batches, off the window's thread.</summary>
public sealed class LibraryScannerTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Music = Path.Combine(Path.GetTempPath(), "rexplayer-library-test", "Music");

    private static MediaInfo Info(string path) => new()
    {
        FormatName = "WAVE",
        Tracks = [new TrackInfo { Id = 1, Codec = CodecId.Pcm }],
        Metadata = new Dictionary<string, string> { [MetadataKeys.Title] = Path.GetFileNameWithoutExtension(path).ToUpperInvariant() },
    };

    private static List<LibraryFile> Files(int count) => [.. Enumerable.Range(0, count).Select(i => new LibraryFile(Path.Combine(Music, $"song {i}.wav"), 10, Monday))];

    [Fact]
    [Capability("LIB-05")]
    public void EveryWatchedFolderIsListedAndItsNewFilesRead()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        var probed = new List<string>();
        var log = RexLog.InMemory();
        using var scanner = new LibraryScanner(library, _ => Files(LibraryScanner.Batch + 3), (path, _) =>
        {
            probed.Add(path);
            return path.EndsWith("song 1.wav", StringComparison.Ordinal) ? throw new MediaFormatException("damaged") : path.EndsWith("song 2.wav", StringComparison.Ordinal) ? throw new InvalidOperationException("a reader's own fault") : Info(path);
        }, log, background: false);
        var statuses = new List<string>();
        scanner.StatusChanged += (_, _) => statuses.Add(scanner.Status);

        scanner.Request();
        scanner.RunPending(TestContext.Current.CancellationToken);

        Assert.Equal(LibraryScanner.Batch + 3, library.Entries.Count);
        Assert.Empty(library.Unprobed);
        Assert.Equal("SONG 0", library.Entry(Path.Combine(Music, "song 0.wav"))!.Title);
        Assert.Equal("song 1", library.Entry(Path.Combine(Music, "song 1.wav"))!.Title);
        Assert.Equal(LibraryScanner.Batch + 3, probed.Count);
        Assert.Equal(["Looking in " + Music, $"Reading details: 0 of {LibraryScanner.Batch + 3}", $"Reading details: {LibraryScanner.Batch} of {LibraryScanner.Batch + 3}", ""], statuses);
        Assert.Contains(log.Tail(100), entry => entry.Message.Contains("could not be read: a reader's own fault", StringComparison.Ordinal));

        // Asked again with nothing new, nothing is read.
        scanner.Request(Path.Combine(Music, "Live"));
        scanner.Request(Path.Combine(Path.GetTempPath(), "not watched"));
        scanner.RunPending(TestContext.Current.CancellationToken);
        Assert.Equal(LibraryScanner.Batch + 3, probed.Count);
        scanner.RunPending(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFolderThatCannotBeListedKeepsWhatWasKnown()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        library.Scan(Music, Files(2), _ => LibraryKind.Music);
        var log = RexLog.InMemory();
        using var scanner = new LibraryScanner(library, _ => throw new DirectoryNotFoundException("unplugged"), (path, _) => null, log, background: false);

        scanner.Request(Music);
        scanner.RunPending(TestContext.Current.CancellationToken);

        Assert.Equal(2, library.Entries.Count);
        Assert.Contains(log.Tail(100), entry => entry.Message.Contains("could not be looked in", StringComparison.Ordinal));
    }

    [Fact]
    public void AListingCutShortForgetsNothing()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        library.Scan(Music, Files(3), _ => LibraryKind.Music);
        using var stop = new CancellationTokenSource();

        // Stopped after the first file, as when rexplayer closes mid-listing: the other two are not "gone".
        IEnumerable<LibraryFile> CutShort(string folder)
        {
            yield return Files(3)[0];
            stop.Cancel();
            yield return Files(3)[1];
        }

        using var scanner = new LibraryScanner(library, CutShort, (path, _) => null, RexLog.InMemory(), background: false);

        scanner.Request(Music);
        scanner.RunPending(stop.Token);

        Assert.Equal(3, library.Entries.Count);
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheWorkerRunsOnItsOwnThreadAndStopsWhenAsked()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var threads = new HashSet<int>();
        var count = 3;
        // The worker runs below normal priority, so a busy machine can hold it back for seconds.
        var scanner = new LibraryScanner(library, _ => Files(count), (path, token) =>
        {
            lock (threads)
            {
                threads.Add(Environment.CurrentManagedThreadId);
            }

            started.Set();
            release.Wait(TimeSpan.FromMinutes(1), token);
            return Info(path);
        }, RexLog.InMemory());

        scanner.Request();
        Assert.True(started.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        Assert.True(scanner.IsBusy);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);

        // Asked for while it works, the work is done before the worker ends.
        scanner.Request(Music);
        release.Set();
        SpinWait.SpinUntil(() => !scanner.IsBusy, TimeSpan.FromMinutes(1));
        Assert.False(scanner.IsBusy);
        Assert.Empty(library.Unprobed);

        // Stopped mid-read, what was not read waits for the next scan.
        release.Reset();
        started.Reset();
        count = 5;
        scanner.Request();
        Assert.True(started.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        scanner.Dispose();
        SpinWait.SpinUntil(() => !scanner.IsBusy, TimeSpan.FromMinutes(1));
        Assert.False(scanner.IsBusy);
        Assert.NotEmpty(library.Unprobed);
        scanner.Request();
        Assert.False(scanner.IsBusy);
    }

    [Fact]
    public void FilesOnDiskAreListedAndRead()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-library-").FullName;
        try
        {
            var inner = Directory.CreateDirectory(Path.Combine(folder, "Album")).FullName;
            var song = Path.Combine(inner, "Sungba.wav");
            File.WriteAllBytes(song, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[800])).Build());
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");

            var files = LibraryScanner.ListFolder(folder).ToList();

            Assert.Equal(new[] { song, Path.Combine(folder, "notes.txt") }.Order(StringComparer.Ordinal), files.Select(file => file.Path).Order(StringComparer.Ordinal));
            Assert.Equal(new FileInfo(song).Length, files.Single(file => file.Path == song).Size);
            Assert.Equal("WAVE", LibraryScanner.ProbeFile(song, TestContext.Current.CancellationToken)!.FormatName);
            Assert.Null(LibraryScanner.ProbeFile(Path.Combine(folder, "photo.jpg"), TestContext.Current.CancellationToken));
            Assert.Throws<ArgumentNullException>(() => new LibraryScanner(null!, _ => [], (_, _) => null, RexLog.InMemory()));
            Assert.Throws<ArgumentNullException>(() => new LibraryScanner(new MediaLibrary(RexStore.InMemory()), null!, (_, _) => null, RexLog.InMemory()));
            Assert.Throws<ArgumentNullException>(() => new LibraryScanner(new MediaLibrary(RexStore.InMemory()), _ => [], null!, RexLog.InMemory()));
            Assert.Throws<ArgumentNullException>(() => new LibraryScanner(new MediaLibrary(RexStore.InMemory()), _ => [], (_, _) => null, null!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
