using System.Buffers.Binary;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>
/// Files still being downloaded into a watched folder (LIB-05): told from whole ones by their
/// name, the markers beside them, a declared size not yet reached, or growth between looks; the
/// library keeps that state up to date through scans and rechecks, and reads a file again once it
/// is whole.
/// </summary>
public sealed class DownloadsTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string Videos = Path.Combine("D:" + Path.DirectorySeparatorChar, "Videos");

    private static string In(string name) => Path.Combine(Videos, name);

    private static LibraryFile File(string path, long size, int day = 0) => new(path, size, Monday.AddDays(day));

    /// <summary>An aria2 control file declaring <paramref name="total"/> bytes, with an info hash of <paramref name="hashLength"/> bytes.</summary>
    private static byte[] Aria2(long total, int hashLength = 20, ushort version = 1)
    {
        var control = new byte[10 + hashLength + 4 + 8 + 8 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(control, version);
        BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(6), (uint)hashLength);
        BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(10 + hashLength), 1 << 20);
        BinaryPrimitives.WriteUInt64BigEndian(control.AsSpan(10 + hashLength + 4), (ulong)total);
        return control;
    }

    [Fact]
    [Capability("LIB-05")]
    public void ADownloadersNameIsTakenOffToFindTheMediaFile()
    {
        Assert.Equal(In("Film.mp4"), Downloads.WholeNameOf(In("Film.mp4.part")));
        Assert.Equal(In("Film.mp4"), Downloads.WholeNameOf(In("Film.mp4.!qB")));
        Assert.Equal(In("Film.mp4"), Downloads.WholeNameOf(In("Film.mp4.CRDOWNLOAD")));
        Assert.Null(Downloads.WholeNameOf(In("Film.mp4")));
        Assert.Null(Downloads.WholeNameOf(In("Film")));
        Assert.Equal(In("Film.mp4.aria2"), Downloads.MarkersOf(In("Film.mp4")).First());
        Assert.Contains(In("Film.mp4.part"), Downloads.MarkersOf(In("Film.mp4")));

        // The library and the player see the video the file will be, under its own title.
        Assert.Equal(LibraryKind.Video, MediaFiles.LibraryKindOf(In("Film.mp4.part")));
        Assert.Equal(LibraryKind.Music, MediaFiles.LibraryKindOf(In("Song.flac.crdownload")));
        Assert.Null(MediaFiles.LibraryKindOf(In("Unconfirmed 123.crdownload")));
        Assert.Equal("Film", MediaLibrary.TitleOf(In("Film.mp4.part")));

        Assert.Throws<ArgumentNullException>(() => Downloads.WholeNameOf(null!));
        Assert.Throws<ArgumentNullException>(() => Downloads.MarkersOf(null!).ToList());
    }

    [Fact]
    [Capability("LIB-05")]
    public void MarkersBesideAFileAreGatheredAndAria2SaysHowBigItWillBe()
    {
        var film = In("Film.mp4");
        var marked = Downloads.Gather(film, path => path == film + ".aria2", path => Aria2(3_400_000_000));
        Assert.Equal(new DownloadEvidence(true, 3_400_000_000), marked);
        Assert.Equal(new DownloadEvidence(true, null), Downloads.Gather(film, path => path == film + ".part", _ => null));
        Assert.Equal(new DownloadEvidence(true, null), Downloads.Gather(film, path => path == film + ".aria2", _ => null));
        Assert.Equal(new DownloadEvidence(true, null), Downloads.Gather(film + ".part", _ => false, _ => null));
        Assert.Equal(new DownloadEvidence(false, null), Downloads.Gather(film, _ => false, _ => throw new InvalidOperationException("never read")));

        Assert.Equal(1_234_567_890_123, Downloads.Aria2TotalLength(Aria2(1_234_567_890_123, hashLength: 0)));
        Assert.Null(Downloads.Aria2TotalLength(Aria2(10, version: 2)));
        Assert.Null(Downloads.Aria2TotalLength(Aria2(10).AsSpan(0, 20)));
        Assert.Null(Downloads.Aria2TotalLength([0, 1]));
        Assert.Null(Downloads.Aria2TotalLength(Aria2(10, hashLength: 2000)));
        var absurd = Aria2(0);
        absurd.AsSpan(10 + 20 + 4, 8).Fill(0xFF);
        Assert.Null(Downloads.Aria2TotalLength(absurd));

        Assert.Throws<ArgumentNullException>(() => Downloads.Gather(null!, _ => false, _ => null));
        Assert.Throws<ArgumentNullException>(() => Downloads.Gather(film, null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => Downloads.Gather(film, _ => false, null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnAria2MarkerWithoutAReaderStillMarksTheDownload()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Videos);
        var film = In("Film.mp4");

        // A caller may omit the optional reader even when a marker file was listed.
        // The fallback reader returns no bytes; the marker must still be noticed.
        var scan = library.Scan(Videos, [File(film, 400), File(film + ".aria2", 60)], MediaFiles.LibraryKindOf);

        Assert.Equal(new ScanResult(1, 0, 0), scan);
        Assert.True(library.Entry(film)!.Downloading);
        Assert.Null(library.Entry(film)!.FinalSize);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFileIsDownloadingWhileMarkedShortOfItsDeclaredSizeOrGrowing()
    {
        var plain = new DownloadEvidence(false, null);
        Assert.Equal((false, (long?)null), Downloads.Judge(plain, null, 100, null));
        Assert.Equal((false, (long?)null), Downloads.Judge(plain, 100, 100, null));
        Assert.Equal((true, (long?)null), Downloads.Judge(plain, 100, 150, null));
        Assert.Equal((false, (long?)null), Downloads.Judge(plain, 150, 100, null));
        Assert.Equal((true, (long?)null), Downloads.Judge(new DownloadEvidence(true, null), 100, 100, null));
        Assert.Equal((true, (long?)400), Downloads.Judge(new DownloadEvidence(true, 400), 100, 100, null));
        Assert.Equal((true, (long?)400), Downloads.Judge(plain, 100, 100, 400));
        Assert.Equal((true, (long?)500), Downloads.Judge(new DownloadEvidence(false, 500), 100, 100, 400));
        // Reached its declared size: whole, however it grew to get there, and the size is no longer a goal.
        Assert.Equal((false, (long?)null), Downloads.Judge(plain, 400, 400, 400));
        Assert.Equal((false, (long?)null), Downloads.Judge(plain, 100, 400, 400));
        Assert.Equal((true, (long?)null), Downloads.Judge(new DownloadEvidence(true, 400), 400, 400, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheWordsSayHowFarADownloadIs()
    {
        Assert.Equal("Downloading · 1.2 GB of 3.4 GB", Downloads.Detail(1_288_490_189, 3_650_722_201));
        Assert.Equal("Downloading · 812 MB of 1.0 GB", Downloads.Detail(851_443_712, 1_073_741_824));
        Assert.Equal("Downloading · 6 KB of 11 KB", Downloads.Detail(6000, 11125));
        Assert.Equal("Downloading · 20 B of 11 KB", Downloads.Detail(20, 11125));
        Assert.Equal("Downloading", Downloads.Detail(6000, null));
        Assert.Equal("Downloading", Downloads.Detail(6000, 0));
        Assert.Equal(0.5, Downloads.Fraction(50, 100));
        Assert.Equal(1, Downloads.Fraction(150, 100));
        Assert.Null(Downloads.Fraction(50, null));
        Assert.Equal("Still downloading: 20% so far", Downloads.NotYetNotice(20, 100));
        Assert.Equal("Still downloading: 99% so far", Downloads.NotYetNotice(999, 1000));
        Assert.Equal("Still downloading", Downloads.NotYetNotice(20, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AScanMarksPartialFilesAndClearsThemOnceWhole()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(Monday));
        var library = new MediaLibrary(RexStore.InMemory(), time);
        library.AddFolder(Videos);
        var film = In("Film.mp4");
        var show = In("Show.mkv");
        var song = In("Song.flac");
        byte[]? Marker(string path) => path == film + ".aria2" ? Aria2(1000) : null;

        // A browser's part file beside its placeholder stands for one video; aria2's control file says the size.
        var first = library.Scan(Videos, [File(film, 400), File(film + ".aria2", 60), File(show + ".part", 200), File(show, 0), File(song, 500)], MediaFiles.LibraryKindOf, Marker);
        Assert.Equal(new ScanResult(3, 0, 0), first);
        Assert.Equal((true, (long?)1000), (library.Entry(film)!.Downloading, library.Entry(film)!.FinalSize));
        Assert.Equal((true, (long?)null), (library.Entry(show)!.Downloading, library.Entry(show)!.FinalSize));
        Assert.Null(library.Entry(show + ".part"));
        Assert.False(library.Entry(song)!.Downloading);

        // Read once while partial, then left alone as it grows; a whole file that grows is downloading too.
        library.SetDetails(film, new MediaInfo { FormatName = "MP4", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }], Duration = MediaTime.FromSeconds(9) });
        library.SetDetails(song, new MediaInfo { FormatName = "FLAC", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.Pcm }] });
        var second = library.Scan(Videos, [File(film, 700, 1), File(film + ".aria2", 60), File(show + ".part", 300, 1), File(show, 0), File(song, 900, 1)], MediaFiles.LibraryKindOf, Marker);
        Assert.Equal(new ScanResult(0, 2, 0), second);
        var grown = library.Entry(film)!;
        Assert.Equal((700L, true, (long?)1000, true), (grown.Size, grown.Downloading, grown.FinalSize, grown.Probed));
        Assert.Equal((true, (long?)null, false), (library.Entry(song)!.Downloading, library.Entry(song)!.FinalSize, library.Entry(song)!.Probed));

        // Whole: the size reached, the growth over; each is read again. The show's part file was
        // renamed into place, which is growth from nothing until the next look finds it still.
        var third = library.Scan(Videos, [File(film, 1000, 2), File(show, 300, 2), File(song, 900, 1)], MediaFiles.LibraryKindOf, Marker);
        Assert.Equal(new ScanResult(0, 3, 0), third);
        Assert.All(new[] { film, song }, path => Assert.Equal((false, (long?)null, false), (library.Entry(path)!.Downloading, library.Entry(path)!.FinalSize, library.Entry(path)!.Probed)));
        Assert.True(library.Entry(show)!.Downloading);
        Assert.Equal(new ScanResult(0, 1, 0), library.Scan(Videos, [File(film, 1000, 2), File(show, 300, 2), File(song, 900, 1)], MediaFiles.LibraryKindOf));
        Assert.False(library.Entry(show)!.Downloading);
        Assert.Equal([film, show, song], library.Unprobed.Select(entry => entry.Path).Order(StringComparer.OrdinalIgnoreCase));

        // Unchanged, nothing happens.
        Assert.False(library.Scan(Videos, [File(film, 1000, 2), File(show, 300, 2), File(song, 900, 1)], MediaFiles.LibraryKindOf).Any);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ReadingACutShortContainerMarksTheFileAsDownloading()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Videos);
        var film = In("Film.mp4");
        library.Scan(Videos, [File(film, 6000)], MediaFiles.LibraryKindOf);
        library.SetDetails(film, new MediaInfo { FormatName = "MP4", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }], DeclaredLength = 11125 });
        var partial = library.Entry(film)!;
        Assert.Equal((true, (long?)11125, true), (partial.Downloading, partial.FinalSize, partial.Probed));

        // A container that reaches exactly as far as the file says nothing of the kind.
        library.SetDetails(film, new MediaInfo { FormatName = "MP4", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }], DeclaredLength = 6000 });
        Assert.True(library.Entry(film)!.Downloading);
        library.Scan(Videos, [File(film, 11125, 1)], MediaFiles.LibraryKindOf);
        var whole = library.Entry(film)!;
        Assert.Equal((false, (long?)null, false), (whole.Downloading, whole.FinalSize, whole.Probed));
        library.SetDetails(film, new MediaInfo { FormatName = "MP4", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }], DeclaredLength = 11125 });
        Assert.False(library.Entry(film)!.Downloading);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ARecheckSeesADownloadThatStoppedGrowingAsWhole()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Videos);
        var film = In("Film.mp4");
        var show = In("Show.mkv");
        var gone = In("Gone.mkv");
        library.Scan(Videos, [File(film, 100), File(show, 100), File(gone, 100)], MediaFiles.LibraryKindOf);
        library.Scan(Videos, [File(film, 200, 1), File(show, 200, 1), File(gone, 200, 1)], MediaFiles.LibraryKindOf);
        Assert.All(library.Entries, entry => Assert.True(entry.Downloading));
        var changes = 0;
        library.Changed += (_, _) => changes++;

        // Film grew again; Show stopped at the same size; Gone is no longer there, so it is left as it was.
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { [film] = 300, [show] = 200 };
        var markerChecks = new List<string>();
        library.RecheckDownloads(path => sizes.TryGetValue(path, out var size) ? File(path, size, 2) : null, path => { markerChecks.Add(path); return false; }, _ => null);
        Assert.Equal((true, 300L), (library.Entry(film)!.Downloading, library.Entry(film)!.Size));
        Assert.Equal((false, false), (library.Entry(show)!.Downloading, library.Entry(show)!.Probed));
        Assert.True(library.Entry(gone)!.Downloading);
        Assert.DoesNotContain(markerChecks, path => path.StartsWith(gone, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, changes);

        // A marker beside a file keeps it downloading however still it is.
        sizes[film] = 300;
        library.RecheckDownloads(path => sizes.TryGetValue(path, out var size) ? File(path, size, 2) : null, path => path == film + ".part", _ => null);
        Assert.True(library.Entry(film)!.Downloading);
        sizes[show] = 200;
        library.RecheckDownloads(path => sizes.TryGetValue(path, out var size) ? File(path, size, 2) : null, _ => false, _ => null);
        Assert.False(library.Entry(film)!.Downloading);
        // The look that found nothing new wrote nothing.
        Assert.Equal(2, changes);

        Assert.Throws<ArgumentNullException>(() => library.RecheckDownloads(null!, _ => false, _ => null));
        Assert.Throws<ArgumentNullException>(() => library.RecheckDownloads(_ => null, null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => library.RecheckDownloads(_ => null, _ => false, null!));
    }

    [Fact]
    public void TheHeadOfAMarkerFileIsReadFromTheDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var marker = Path.Combine(folder, "Film.mp4.aria2");
            System.IO.File.WriteAllBytes(marker, Aria2(4096));
            Assert.Equal(4096, Downloads.Aria2TotalLength(LibraryScanner.ReadMarker(marker)));
            System.IO.File.WriteAllBytes(marker, new byte[5000]);
            Assert.Equal(2048, LibraryScanner.ReadMarker(marker)!.Length);
            Assert.Null(LibraryScanner.ReadMarker(Path.Combine(folder, "missing.aria2")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
