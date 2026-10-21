using System.Diagnostics;
using Rex.Media.AppCore.Library;
using Rex.Media.Interop.Storage;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// A real NTFS sparse file with a hole, made the way a torrent client lays out a download: the
/// file system says which bytes are on the disk, the library counts it as downloading with the
/// right share, and reading stops at the hole until it is written.
/// </summary>
public sealed class SparseFileTests : IDisposable
{
    private const int Mb = 1 << 20;
    private readonly string _path = Path.Combine(Path.GetTempPath(), "rexplayer-sparse-" + Guid.NewGuid().ToString("N") + ".mp4");

    public void Dispose() => File.Delete(_path);

    private static void Fsutil(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("fsutil", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "fsutil " + string.Join(' ', arguments) + ": " + process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    /// <summary>A 4 MB sparse file whose first megabyte and last megabyte are written, the two between a hole.</summary>
    private void MakeSparse()
    {
        using (var file = File.Create(_path))
        {
            file.SetLength(4 * Mb);
        }

        Fsutil("sparse", "setflag", _path);
        Fsutil("sparse", "setrange", _path, "0", (4 * Mb).ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var writer = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        writer.Write(Enumerable.Repeat((byte)7, Mb).ToArray());
        writer.Position = 3 * Mb;
        writer.Write(Enumerable.Repeat((byte)9, Mb).ToArray());
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheFileSystemSaysWhichBytesAreOnTheDiskAndTheLibraryShowsTheShare()
    {
        MakeSparse();
        using (var handle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Equal(2 * Mb, FileAllocation.Instance.Allocated(handle, 4 * Mb));
            Assert.Equal(Mb, FileAllocation.Instance.AllocatedEnd(handle, 0, 4 * Mb));
            Assert.Equal(2 * Mb, FileAllocation.Instance.AllocatedEnd(handle, 2 * Mb, Mb));
            Assert.Equal(4 * Mb, FileAllocation.Instance.AllocatedEnd(handle, 3 * Mb, Mb));
        }

        var listed = LibraryScanner.Listed(new FileInfo(_path));
        Assert.True(listed.Sparse);
        var look = LibraryScanner.Inspect(listed, FileAllocation.Instance);
        Assert.Equal(new FileLook(false, 2 * Mb), look);
        var state = Downloads.Judge(new DownloadEvidence(false, null), look, null, listed.Size, listed.Modified);
        Assert.True(state.Downloading);
        Assert.Equal("Downloading · 50%", Downloads.Detail(listed.Size, state.FinalSize, state.Allocated));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ReadingStopsAtTheHoleUntilItIsWritten()
    {
        MakeSparse();
        using var source = new FileByteSource(_path, FileAllocation.Instance);
        var buffer = new byte[2 * Mb];
        Assert.Equal(Mb, source.Read(0, buffer, CancellationToken.None));
        Assert.Equal(0, source.Read(Mb, buffer, CancellationToken.None));
        Assert.True(source.IsPending(Mb, Mb));

        var extent = source.Extent;
        using (var writer = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.Position = Mb;
            writer.Write(Enumerable.Repeat((byte)8, 2 * Mb).ToArray());
        }

        Assert.Equal(ByteSourceChange.Grew, source.WaitForChange(extent, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(2 * Mb, source.Read(Mb, buffer, CancellationToken.None));
        Assert.Equal(8, buffer[0]);
        Assert.False(source.IsPending(Mb, Mb));
    }
}
