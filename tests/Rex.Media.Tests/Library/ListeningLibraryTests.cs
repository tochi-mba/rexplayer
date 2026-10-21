using System.Text;
using Rex.Media.AppCore.Library;
using Rex.Media.Containers.Mp4;
using Rex.Media.Containers.Tags;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using static Rex.Media.TestKit.Mp4Writer;

namespace Rex.Media.Tests.Library;

/// <summary>
/// What the audio classification needs from the tags and the library: the podcast and audiobook
/// marks, chapters kept and read again for older music, moves that outlast written tags, (LIB-05).
/// </summary>
public sealed class ListeningLibraryTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "rexplayer-listening");

    [Fact]
    [Capability("LIB-05")]
    public void TheTagsMarkPodcastsAndAudiobooks()
    {
        Assert.Equal(MetadataKeys.Podcast, Id3v2.Read(Id3Builder.V23().Frame("PCST", [0, 0, 0, 0]).Build())!.Metadata[MetadataKeys.MediaType]);
        Assert.Equal(MetadataKeys.Podcast, Id3v2.Read(Id3Builder.V24().Frame("WFED", Encoding.ASCII.GetBytes("https://example.org/feed")).Build())!.Metadata[MetadataKeys.MediaType]);

        static Dictionary<string, string> Items(params byte[][] items)
        {
            var metadata = new Dictionary<string, string>();
            Mp4Metadata.ReadItems(Box("ilst", items).AsSpan(8), metadata);
            return metadata;
        }

        static byte[] Item(string type, params byte[] value) => Box(type, Box("data", U32(21), U32(0), value));
        Assert.Equal(MetadataKeys.Audiobook, Items(Item("stik", 2))[MetadataKeys.MediaType]);
        Assert.Equal(MetadataKeys.Podcast, Items(Item("stik", 21))[MetadataKeys.MediaType]);
        Assert.Equal(MetadataKeys.Podcast, Items(Item("pcst", 1))[MetadataKeys.MediaType]);
        Assert.Equal(MetadataKeys.Podcast, Items(Item("purl", Encoding.ASCII.GetBytes("https://example.org/feed")))[MetadataKeys.MediaType]);
        Assert.Equal(MetadataKeys.Podcast, Items(Item("egid", Encoding.ASCII.GetBytes("episode-1")))[MetadataKeys.MediaType]);

        // A music video's media kind, a cleared podcast flag and empty items mark nothing.
        Assert.Empty(Items(Item("stik", 6), Item("pcst", 0), Item("purl")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheLibraryKeepsChaptersAndTheMarkAndReadsOlderMusicAgainAfterNewFiles()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        var book = Path.Combine(Folder, "Book.m4b");
        var song = Path.Combine(Folder, "Sungba.mp3");
        var damaged = Path.Combine(Folder, "Damaged.mp3");
        var video = Path.Combine(Folder, "Film.mp4");
        library.Scan(Folder, [new LibraryFile(book, 1, DateTime.UnixEpoch), new LibraryFile(song, 1, DateTime.UnixEpoch), new LibraryFile(damaged, 1, DateTime.UnixEpoch), new LibraryFile(video, 1, DateTime.UnixEpoch)],
            path => path.EndsWith(".mp4", StringComparison.Ordinal) ? LibraryKind.Video : LibraryKind.Music);
        library.SetReadDetails([
            (book, new MediaInfo
            {
                FormatName = "MP4",
                Tracks = [new TrackInfo { Id = 1, Codec = CodecId.Aac }],
                Chapters = [new Chapter(MediaTime.Zero, "Opening"), new Chapter(MediaTime.FromSeconds(600), "Two")],
                Metadata = new Dictionary<string, string> { [MetadataKeys.MediaType] = MetadataKeys.Audiobook },
            }, null),
            (damaged, null, "Damaged"),
        ]);

        var read = library.Entry(book)!;
        Assert.Equal([new LibraryChapter(TimeSpan.Zero, "Opening"), new LibraryChapter(TimeSpan.FromMinutes(10), "Two")], read.Chapters);
        Assert.Equal(MetadataKeys.Audiobook, read.MediaType);
        Assert.Null(library.Entry(damaged)!.Chapters);

        // Music read before chapters were kept is read again, after every file never read at all;
        // a video never is, and music read by this reading is done.
        var store = library.Store;
        var older = library.Entry(book)! with { Chapters = null, TagReading = 1, Path = Path.Combine(Folder, "Older.mp3") };
        library.Restore([older, library.Entry(video)! with { Path = Path.Combine(Folder, "Old film.mp4"), Probed = true }]);
        Assert.Equal([video, song, older.Path], library.Unprobed.Select(entry => entry.Path));
        Assert.True(new MediaLibrary(store).Entry(book)!.Chapters!.Count == 2);

        // The store's text keeps titles to one line, and a line that is not a chapter is passed over.
        Assert.Equal([new LibraryChapter(TimeSpan.FromTicks(10), "Ten  more")], LibraryChapter.Decode(LibraryChapter.Encode([new LibraryChapter(TimeSpan.FromTicks(10), "Ten\t\nmore")]) + "\nx\tbad\nno tab"));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMoveOutlastsARescanAndTagsWrittenIntoTheFile()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        var song = Path.Combine(Folder, "Asake - Terminator.mp3");
        library.Scan(Folder, [new LibraryFile(song, 1, DateTime.UnixEpoch)], _ => LibraryKind.Music);
        library.SetOverride([song], new DetailOverride { Audio = AudioShelves.Recordings });
        library.SetOverride([song], new DetailOverride { Title = "Terminator" });
        Assert.Equal(new DetailOverride { Audio = AudioShelves.Recordings, Title = "Terminator" }, library.Entry(song)!.Override);

        // The details went into the file; where the person moved it cannot, so it stays.
        library.ClearOverride([song]);
        Assert.Equal(new DetailOverride { Audio = AudioShelves.Recordings }, library.Entry(song)!.Override);
        library.SetDetails(song, new MediaInfo { FormatName = "MP3", Tracks = [new TrackInfo { Id = 1, Codec = CodecId.Mp3 }] });
        Assert.Equal(AudioShelf.Recordings, AudioRules.Judge(library.Entry(song)!, MusicLookup.NotSearched)!.Value.Shelf);

        // An override of tags alone leaves nothing to keep.
        Assert.Null(new DetailOverride { Title = "x" }.Kept());
    }
}
