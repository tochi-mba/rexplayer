using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>The library's folders and files (LIB-05): scanned against the disk, read, counted and kept.</summary>
public sealed class MediaLibraryTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string Music = Path.Combine("D:" + Path.DirectorySeparatorChar, "Music");

    private static string In(params string[] parts) => Path.Combine([Music, .. parts]);

    private static LibraryKind? KindOf(string path) => Path.GetExtension(path) switch
    {
        ".mp3" or ".flac" => LibraryKind.Music,
        ".mp4" => LibraryKind.Video,
        _ => null,
    };

    private static LibraryFile File(string path, long size = 100) => new(path, size, Monday);

    private static MediaInfo Song(string title, string? artist = "Asake", string? album = "Work of Art", string track = "1/14", string date = "2023-06-16", bool video = false) => new()
    {
        FormatName = "MP3",
        Tracks = video ? [new TrackInfo { Id = 1, Codec = CodecId.H264 }, new TrackInfo { Id = 2, Codec = CodecId.Aac }] : [new TrackInfo { Id = 1, Codec = CodecId.Mp3 }],
        Duration = MediaTime.FromSeconds(180),
        Metadata = new Dictionary<string, string?>
        {
            [MetadataKeys.Title] = title,
            [MetadataKeys.Artist] = artist,
            [MetadataKeys.Album] = album,
            [MetadataKeys.Track] = track,
            [MetadataKeys.Date] = date,
            [MetadataKeys.Genre] = " Afrobeats ",
            [MetadataKeys.Disc] = "x",
        }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!),
    };

    [Fact]
    [Capability("LIB-05")]
    public void AScanAddsNewFilesReadsChangedOnesAgainAndForgetsGoneOnes()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(Monday));
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store, time);
        var changes = 0;
        library.Changed += (_, _) => changes++;
        Assert.True(library.AddFolder(Music + Path.DirectorySeparatorChar));

        var first = library.Scan(Music, [File(In("Sungba.mp3")), File(In("Live", "Terminator.flac")), File(In("cover.jpg")), File(In("Sungba.mp3"))], KindOf);

        Assert.Equal(new ScanResult(2, 0, 0), first);
        Assert.True(first.Any);
        var sungba = library.Entry(In("sungba.MP3"))!;
        Assert.Equal(("Sungba", LibraryKind.Music, false, Monday), (sungba.Title, sungba.Kind, sungba.Probed, sungba.Added));
        // Found at the same moment, they are read in the order of their paths.
        Assert.Equal([In("Live", "Terminator.flac"), In("Sungba.mp3")], library.Unprobed.Select(entry => entry.Path));

        library.SetDetails(In("Sungba.mp3"), Song("Sungba (Remix)"));
        library.SetDetails(In("Live", "Terminator.flac"), null);
        library.SetDetails(In("Gone.mp3"), Song("Never there"));
        Assert.Empty(library.Unprobed);

        // Unchanged, nothing happens; a changed file is read again and keeps its details meanwhile.
        time.Advance(TimeSpan.FromDays(1));
        Assert.False(library.Scan(Music, [File(In("Sungba.mp3")), File(In("Live", "Terminator.flac"))], KindOf).Any);
        Assert.Equal(new ScanResult(1, 1, 1), library.Scan(Music, [File(In("Sungba.mp3"), 200), File(In("Lonely At The Top.mp3"))], KindOf));
        var changed = library.Entry(In("Sungba.mp3"))!;
        Assert.Equal(("Sungba (Remix)", 200L, false, Monday), (changed.Title, changed.Size, changed.Probed, changed.Added));
        Assert.Equal(Monday.AddDays(1), library.Entry(In("Lonely At The Top.mp3"))!.Added);
        Assert.Null(library.Entry(In("Live", "Terminator.flac")));

        // Kept: another library on the same store knows the same.
        var again = new MediaLibrary(store);
        Assert.Equal([Music], again.Folders);
        Assert.Equal(library.Entries.OrderBy(entry => entry.Path), again.Entries.OrderBy(entry => entry.Path));
        // Added, scanned, two read, scanned with changes: the unknown file and the scan that found nothing raised nothing.
        Assert.Equal(5, changes);
    }

    [Fact]
    [Capability("LIB-05")]
    public void DetailsAreReadFromTheTags()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        library.Scan(Music, [File(In("a.mp3")), File(In("b.mp4")), File(In("c.mp4")), File(In("d.mp3"))], KindOf);

        library.SetDetails([
            (In("a.mp3"), Song("Sungba")),
            (In("b.mp4"), Song("Terminator", video: false)),
            (In("c.mp4"), Song("Video", video: true, track: "", date: "1")),
            (In("d.mp3"), new MediaInfo { FormatName = "MP3", Tracks = [], Metadata = new Dictionary<string, string> { [MetadataKeys.Title] = "  " } }),
        ]);

        var a = library.Entry(In("a.mp3"))!;
        Assert.Equal(("Sungba", "Asake", "Work of Art", "Afrobeats", 1, 2023, (int?)null), (a.Title, a.Artist, a.Album, a.Genre, a.Track, a.Year, a.Disc));
        Assert.Equal(TimeSpan.FromSeconds(180), a.Duration);
        Assert.Equal("Asake", a.FiledArtist);

        // An .mp4 of only sound is music; one with pictures stays a video.
        Assert.Equal(LibraryKind.Music, library.Entry(In("b.mp4"))!.Kind);
        var c = library.Entry(In("c.mp4"))!;
        Assert.Equal((LibraryKind.Video, (int?)null, (int?)null), (c.Kind, c.Track, c.Year));
        var d = library.Entry(In("d.mp3"))!;
        Assert.Equal(("d", (string?)null, (TimeSpan?)null), (d.Title, d.Artist, d.Duration));
        Assert.Equal(LibraryKind.Music, d.Kind);
        Assert.Equal("Various", (a with { AlbumArtist = "Various" }).FiledArtist);
    }

    [Fact]
    [Capability("LIB-05")]
    public void FoldersHoldingOthersTakeTheirPlaceAndRemovingOneForgetsItsFiles()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        var live = In("Live");
        var videos = Path.Combine("D:" + Path.DirectorySeparatorChar, "Videos");
        Assert.True(library.AddFolder(live));
        Assert.True(library.AddFolder(videos));
        Assert.False(library.AddFolder(live.ToUpperInvariant()));
        Assert.True(library.AddFolder(Music));
        Assert.False(library.AddFolder(In("Live", "2024")));
        Assert.Equal([videos, Music], library.Folders);

        library.Scan(Music, [File(In("a.mp3")), File(In("Live", "b.mp3"))], KindOf);
        library.Scan(videos, [File(Path.Combine(videos, "c.mp4"))], KindOf);
        Assert.False(library.RemoveFolder(In("Live")));
        Assert.True(library.RemoveFolder(Music + Path.DirectorySeparatorChar));

        Assert.Equal([videos], library.Folders);
        Assert.Equal([Path.Combine(videos, "c.mp4")], library.Entries.Select(entry => entry.Path));
        Assert.Throws<ArgumentException>(() => library.AddFolder(" "));
    }

    [Theory]
    [InlineData(@"D:\Music", @"D:\Music\a.mp3", true)]
    [InlineData(@"D:\Music", @"D:\music", true)]
    [InlineData(@"D:\Music", @"D:\Musicals\a.mp3", false)]
    [InlineData(@"D:\", @"D:\a.mp3", true)]
    [InlineData("/", "/srv/a.mp3", true)]
    [InlineData(@"\\server\share", @"\\server\share\a.mp3", true)]
    [InlineData(@"D:\Music", @"E:\Music\a.mp3", false)]
    public void AFolderHoldsWhatIsInsideIt(string folder, string path, bool holds) => Assert.Equal(holds, MediaLibrary.Holds(folder, path));

    [Fact]
    public void DrivesAndTheRootKeepTheirSeparator()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(@"E:\");
        library.AddFolder("/");
        library.AddFolder(@"\\server\share\");

        Assert.Equal([@"E:\", "/", @"\\server\share"], library.Folders);
        Assert.Throws<ArgumentNullException>(() => MediaLibrary.Holds(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => MediaLibrary.Holds("a", null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void PlaysAreCountedAndCleared()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(Monday));
        var library = new MediaLibrary(RexStore.InMemory(), time);
        library.AddFolder(Music);
        library.Scan(Music, [File(In("a.mp3")), File(In("b.mp3"))], KindOf);

        library.Played(In("a.mp3"));
        time.Advance(TimeSpan.FromHours(1));
        library.Played(In("A.MP3"));
        library.Played(In("elsewhere.mp3"));

        var a = library.Entry(In("a.mp3"))!;
        Assert.Equal((2, (DateTime?)Monday.AddHours(1)), (a.Plays, a.LastPlayed));
        library.ClearPlays();
        Assert.All(library.Entries, entry => Assert.Equal((0, (DateTime?)null), (entry.Plays, entry.LastPlayed)));
        var changes = 0;
        library.Changed += (_, _) => changes++;
        library.ClearPlays();
        library.SetDetails([]);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void WhatNoLongerReadsIsLeftOutAndArgumentsAreChecked()
    {
        var store = RexStore.InMemory();
        store.Set("folders", "{ not json");
        store.Set("entry/0", "[]");
        store.Set("entry/1", "{ not json");
        var library = new MediaLibrary(store);

        Assert.Empty(library.Folders);
        Assert.Empty(library.Entries);
        Assert.Equal("a.b", MediaLibrary.TitleOf("a.b.mp3"));
        Assert.Equal(".mp3", MediaLibrary.TitleOf(".mp3"));
        Assert.Throws<ArgumentNullException>(() => new MediaLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => library.Entry(null!));
        Assert.Throws<ArgumentNullException>(() => library.Played(null!));
        Assert.Throws<ArgumentNullException>(() => library.Scan(Music, null!, KindOf));
        Assert.Throws<ArgumentNullException>(() => library.Scan(Music, [], null!));
        Assert.Throws<ArgumentNullException>(() => library.SetDetails(null!));
    }
}
