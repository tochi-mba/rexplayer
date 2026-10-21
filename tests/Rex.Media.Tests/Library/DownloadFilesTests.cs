using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>
/// Downloads on a real disk (LIB-05): a file another program holds open for writing, a file laid
/// out at full size before its bytes arrive, a browser's partial file renamed into place, a file
/// moved in from another folder, and a cancelled download, as the window's scanner sees them.
/// </summary>
public sealed class DownloadFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-download-files-" + Guid.NewGuid().ToString("N"));

    public DownloadFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string In(string name) => Path.Combine(_folder, name);

    private static FileLook Inspect(LibraryFile file) => LibraryScanner.Inspect(file, null);

    private MediaLibrary Library()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(_folder);
        return library;
    }

    private ScanResult Scan(MediaLibrary library) =>
        library.Scan(_folder, LibraryScanner.ListFolder(_folder), MediaFiles.LibraryKindOf, LibraryScanner.ReadMarker, Inspect);

    [Fact]
    [Capability("LIB-05")]
    public void AWriterHoldingTheFileIsSeenAndRexplayersOwnReadingIsNot()
    {
        var film = In("Film.mkv");
        File.WriteAllBytes(film, new byte[4096]);
        var listed = LibraryScanner.Listed(new FileInfo(film));
        Assert.Equal((false, false), (listed.Sparse, listed.Placeholder));
        Assert.Equal(default, Inspect(listed));

        // Playing it opens it for reading, sharing writing: that is never a download.
        using (new FileByteSource(film))
        {
            Assert.False(Inspect(listed).WriterOpen);
        }

        // A downloader holds it for writing. Windows refuses the look that shuts writers out; other
        // systems lock only by agreement, so there it goes unseen and the other signs decide.
        using (new FileStream(film, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.Equal(OperatingSystem.IsWindows(), Inspect(listed).WriterOpen);
        }

        Assert.Equal(default, Inspect(listed with { Path = In("missing.mkv") }));
    }

    /// <summary>A sparse file with 1000 of its bytes on the disk, as the file system would say.</summary>
    private sealed class Thousand : IFileAllocation
    {
        public long Allocated(Microsoft.Win32.SafeHandles.SafeFileHandle file, long length) => Math.Min(1000, length);

        public long AllocatedEnd(Microsoft.Win32.SafeHandles.SafeFileHandle file, long position, long length) => Math.Clamp(1000, position, position + length);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ASparseFileSaysHowManyOfItsBytesAreOnTheDiskEvenWhileItsWriterGoesOn()
    {
        var film = In("Film.mp4");
        File.WriteAllBytes(film, new byte[4096]);
        var sparse = LibraryScanner.Listed(new FileInfo(film)) with { Sparse = true };
        Assert.Equal(new FileLook(false, 1000), LibraryScanner.Inspect(sparse, new Thousand()));
        Assert.Equal(new FileLook(false, null), LibraryScanner.Inspect(sparse, null));
        Assert.Equal(new FileLook(false, null), LibraryScanner.Inspect(sparse with { Sparse = false }, new Thousand()));
        using (new FileStream(film, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.Equal(new FileLook(OperatingSystem.IsWindows(), 1000), LibraryScanner.Inspect(sparse, new Thousand()));
        }

        Assert.Equal(default, LibraryScanner.Inspect(sparse with { Path = In("gone.mp4") }, new Thousand()));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFileLaidOutAtFullSizeIsDownloadingWhileItsWriterGoesOn()
    {
        var film = In("Film.mp4");
        using var writer = new FileStream(film, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        writer.SetLength(1 << 20);
        writer.Flush();
        var library = Library();

        Scan(library);
        Assert.Equal(OperatingSystem.IsWindows(), library.Entry(film)!.Suspect);
        writer.Position = 4096;
        writer.Write(new byte[4096]);
        writer.Flush(flushToDisk: true);
        File.SetLastWriteTimeUtc(film, DateTime.UtcNow.AddMinutes(1));
        Scan(library);
        // Rewritten in place twice over, with its writer seen both times where the system can see it.
        var entry = library.Entry(film)!;
        Assert.True(OperatingSystem.IsWindows() ? entry.Downloading : entry.Suspect, "two looks at a file being written to make it a download");
    }

    [Fact]
    [Capability("LIB-05")]
    public void APartialFileRenamedIntoPlaceThenCancelledLeavesOneEntryThenNone()
    {
        var library = Library();
        var partial = In("Film.mp4.crdownload");
        File.WriteAllBytes(partial, new byte[100]);
        Assert.Equal(new ScanResult(1, 0, 0), Scan(library));
        Assert.True(library.Entry(partial)!.Downloading);

        File.Move(partial, In("Film.mp4"));
        Assert.Equal(new ScanResult(0, 1, 1), Scan(library));
        Assert.False(Assert.Single(library.Entries).Downloading);

        // The next download is cancelled: the browser deletes its partial file, and the library forgets it.
        var next = In("Next.mkv.part");
        File.WriteAllBytes(next, new byte[100]);
        Scan(library);
        File.Delete(next);
        Assert.Equal(new ScanResult(0, 0, 1), Scan(library));
        Assert.Null(library.Entry(next));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFileMovedInFromAnIncompleteFolderArrivesWhole()
    {
        var incomplete = Path.Combine(_folder, "..", Path.GetFileName(_folder) + "-incomplete");
        Directory.CreateDirectory(incomplete);
        try
        {
            var library = Library();
            var staged = Path.Combine(incomplete, "Film.mkv");
            File.WriteAllBytes(staged, new byte[100]);
            File.Move(staged, In("Film.mkv"));
            Assert.Equal(new ScanResult(1, 0, 0), Scan(library));
            Assert.Equal((false, false), (library.Entry(In("Film.mkv"))!.Downloading, library.Entry(In("Film.mkv"))!.Suspect));
        }
        finally
        {
            Directory.Delete(incomplete, recursive: true);
        }
    }
}
