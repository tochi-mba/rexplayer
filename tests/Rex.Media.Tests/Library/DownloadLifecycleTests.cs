using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>
/// A download's life in the library (LIB-05): it appears partial, is renamed into place, is moved in
/// from another folder, is cancelled, or fills in as a sparse torrent file; each keeps one entry,
/// shows as downloading exactly while it is, and is looked at no more than it needs.
/// </summary>
public sealed class DownloadLifecycleTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string Videos = Path.Combine("D:" + Path.DirectorySeparatorChar, "Videos");

    private static string In(string name) => Path.Combine(Videos, name);

    private static LibraryFile File(string path, long size, int minute = 0) => new(path, size, Monday.AddMinutes(minute));

    private static MediaLibrary Library()
    {
        var library = new MediaLibrary(RexStore.InMemory(), new ManualTimeProvider(new DateTimeOffset(Monday)));
        library.AddFolder(Videos);
        return library;
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABrowsersPartialFileRenamedIntoPlaceKeepsItsOneEntry()
    {
        var library = Library();
        var partial = In("Film.mp4.crdownload");
        var film = In("Film.mp4");
        Assert.Equal(new ScanResult(1, 0, 0), library.Scan(Videos, [File(partial, 400)], MediaFiles.LibraryKindOf));
        var arriving = library.Entry(partial)!;
        Assert.Equal(("Film", LibraryKind.Video, true), (arriving.Title, arriving.Kind, arriving.Downloading));
        library.Played(partial);

        // Renamed when done: the same entry under its own name, whole, its plays kept; the partial name is gone.
        Assert.Equal(new ScanResult(0, 1, 1), library.Scan(Videos, [File(film, 1000, 1)], MediaFiles.LibraryKindOf));
        var done = Assert.Single(library.Entries);
        Assert.Equal((film, false, 1, false), (done.Path, done.Downloading, done.Plays, done.Probed));
        Assert.Equal(arriving.Added, done.Added);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ACancelledDownloadLeavesTheLibrary()
    {
        var library = Library();
        var partial = In("Film.mp4.part");
        library.Scan(Videos, [File(partial, 400)], MediaFiles.LibraryKindOf);
        Assert.Equal(new ScanResult(0, 0, 1), library.Scan(Videos, [], MediaFiles.LibraryKindOf));
        Assert.Empty(library.Entries);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFileMovedInWholeIsWholeAndOneCopiedInIsDownloadingWhileItsWriterHoldsIt()
    {
        var library = Library();
        var moved = In("Moved.mkv");
        var copied = In("Copied.mkv");
        var writing = true;
        FileLook Inspect(LibraryFile file) => new(file.Path == copied && writing, null);

        // A move within the drive arrives whole at once; a copy from elsewhere has a writer. One look
        // at a writer is a suspicion (a virus scanner holds files for a moment too); two are a download.
        library.Scan(Videos, [File(moved, 1000), File(copied, 1000)], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.Equal((false, false), (library.Entry(moved)!.Downloading, library.Entry(moved)!.Suspect));
        Assert.Equal((false, true), (library.Entry(copied)!.Downloading, library.Entry(copied)!.Suspect));
        library.Scan(Videos, [File(moved, 1000), File(copied, 1000, 1)], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.True(library.Entry(copied)!.Downloading);

        writing = false;
        library.Scan(Videos, [File(moved, 1000), File(copied, 1000, 1)], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.False(library.Entry(copied)!.Downloading);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ASparseTorrentFileIsDownloadingUntilEveryByteIsOnTheDisk()
    {
        var library = Library();
        var film = In("Film.mp4");
        long onDisk = 8_061_200_000;
        const long size = 19_661_255_450;
        FileLook Inspect(LibraryFile file) => new(false, file.Sparse ? onDisk : null);

        // At full size from the start and never growing: only the bytes on the disk tell.
        library.Scan(Videos, [File(film, size) with { Sparse = true }], MediaFiles.LibraryKindOf, inspect: Inspect);
        var arriving = library.Entry(film)!;
        Assert.Equal((true, (long?)onDisk), (arriving.Downloading, arriving.Allocated));
        Assert.Equal("Downloading · 41%", Downloads.Detail(arriving.Size, arriving.FinalSize, arriving.Allocated));

        // Paused for days, unchanged: still downloading, since it is looked at while in question.
        library.Scan(Videos, [File(film, size) with { Sparse = true }], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.True(library.Entry(film)!.Downloading);

        onDisk = size;
        library.Scan(Videos, [File(film, size, 9) with { Sparse = true }], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.Equal((false, (long?)null, false), (library.Entry(film)!.Downloading, library.Entry(film)!.Allocated, library.Entry(film)!.Probed));
    }

    [Fact]
    [Capability("LIB-05")]
    public void OnlyFilesInQuestionAreLookedAtAndACloudPlaceholderNeverIs()
    {
        var library = Library();
        var whole = In("Whole.mp4");
        var cloud = In("Cloud.mp4");
        var marked = In("Marked.mp4.part");
        var looked = new List<string>();
        FileLook Inspect(LibraryFile file)
        {
            looked.Add(file.Path);
            return default;
        }

        // New files are looked at, except a placeholder (opening it would fetch it) and one a name settles.
        library.Scan(Videos, [File(whole, 10), File(cloud, 10) with { Placeholder = true }, File(marked, 10)], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.Equal([whole], looked);
        Assert.False(library.Entry(cloud)!.Downloading);

        // Unchanged and not in question: nothing is opened.
        looked.Clear();
        library.Scan(Videos, [File(whole, 10), File(cloud, 10) with { Placeholder = true }, File(marked, 10)], MediaFiles.LibraryKindOf, inspect: Inspect);
        Assert.Empty(looked);
    }
}
