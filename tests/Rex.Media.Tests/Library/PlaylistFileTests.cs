using System.Text;
using Rex.Media.Library.Playlists;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>Playlist files of every format, read into entries and written back (PLF-01 to PLF-06).</summary>
public sealed class PlaylistFileTests
{
    private const string Music = @"C:\Music\Album";

    private static IReadOnlyList<PlaylistEntry> Read(string name, string text) =>
        PlaylistFiles.Read(Music + @"\" + name, Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("01 Sungba.mp3", @"C:\Music\Album\01 Sungba.mp3")]
    [InlineData(@"..\Other\a.mp3", @"C:\Music\Other\a.mp3")]
    [InlineData("./sub/../b.flac", @"C:\Music\Album\b.flac")]
    [InlineData(@"\Shared\c.mp3", @"C:\Shared\c.mp3")]
    [InlineData(@"D:\Elsewhere\d.mp3", @"D:\Elsewhere\d.mp3")]
    [InlineData(@"\\server\share\e.mp3", @"\\server\share\e.mp3")]
    [InlineData("\"quoted.mp3\"", @"C:\Music\Album\quoted.mp3")]
    [InlineData("file:///C:/Music/Lonely%20At%20The%20Top.mp3", @"C:\Music\Lonely At The Top.mp3")]
    [InlineData("file://server/share/f.mp3", @"\\server\share\f.mp3")]
    [InlineData("https://example.com/live.mp3", "https://example.com/live.mp3")]
    public void LocationsResolveAgainstThePlaylistsFolder(string reference, string location) =>
        Assert.Equal(location, PlaylistPaths.Resolve(reference, Music));

    [Fact]
    public void LocationsInPlaylistsKeptOnOtherSystemsResolveTheSameWay()
    {
        Assert.Equal("/media/music/albums/a.mp3", PlaylistPaths.Resolve("a.mp3", "/media/music/albums"));
        Assert.Equal("/media/music/a.mp3", PlaylistPaths.Resolve("../a.mp3", "/media/music/albums/"));
        Assert.Equal("/srv/a.mp3", PlaylistPaths.Resolve("/srv/a.mp3", "/media"));
        Assert.Equal("/srv/a b.mp3", PlaylistPaths.Resolve("file:///srv/a%20b.mp3", "/media"));
        Assert.Equal(@"sub\a.mp3", PlaylistPaths.Resolve(@"sub\a.mp3", ""));
        Assert.Equal("/a.mp3", PlaylistPaths.Resolve("a.mp3", "/"));
        Assert.Null(PlaylistPaths.Resolve("  ", Music));
        Assert.Null(PlaylistPaths.Resolve(null, Music));
        Assert.Throws<ArgumentNullException>(() => PlaylistPaths.Resolve("a", null!));
    }

    [Fact]
    public void MediaBesideAPlaylistIsWrittenRelativeToIt()
    {
        Assert.Equal(@"Disc 1\a.mp3", PlaylistPaths.Relative(@"C:\Music\Album\Disc 1\a.mp3", Music));
        Assert.Equal(@"C:\Music\Albums\a.mp3", PlaylistPaths.Relative(@"C:\Music\Albums\a.mp3", Music));
        Assert.Equal("https://example.com/a.mp3", PlaylistPaths.Relative("https://example.com/a.mp3", "https:"));
        Assert.Equal(@"C:\a.mp3", PlaylistPaths.Relative(@"C:\a.mp3", ""));
        Assert.True(PlaylistPaths.IsUrl("rtsp://camera/stream"));
        Assert.False(PlaylistPaths.IsUrl("file:///C:/a.mp3"));
        Assert.False(PlaylistPaths.IsUrl(@"C:\a.mp3"));
        Assert.True(PlaylistPaths.HasDriveOrShare("c:/a.mp3"));
        Assert.False(PlaylistPaths.HasDriveOrShare("ab"));
        Assert.Throws<ArgumentNullException>(() => PlaylistPaths.Relative(null!, ""));
        Assert.Throws<ArgumentNullException>(() => PlaylistPaths.Relative("", null!));
    }

    [Fact]
    [Capability("PLF-01")]
    public void ExtendedM3uGivesTitlesArtistsAndLengths()
    {
        var entries = Read("Asake.m3u8", """
            #EXTM3U
            #EXTINF:182,Asake - Lonely At The Top
            01 Lonely At The Top.mp3

            # a comment
            #EXTINF:-1 tvg-logo="x.png",Terminator
            02 Terminator.mp3
            #EXTINF:bad
            03 Sungba.mp3
            #EXTINF:4.5,
            04 Intro.mp3
            https://example.com/radio
            """);

        Assert.Equal(5, entries.Count);
        Assert.Equal(new PlaylistEntry(@"C:\Music\Album\01 Lonely At The Top.mp3") { Title = "Lonely At The Top", Artist = "Asake", Duration = TimeSpan.FromSeconds(182), Extensions = entries[0].Extensions }, entries[0]);
        Assert.Equal(("Terminator", null, (TimeSpan?)null), (entries[1].Title, entries[1].Artist, entries[1].Duration));
        Assert.Equal((null, (TimeSpan?)null), (entries[2].Title, entries[2].Duration));
        Assert.Equal((null, TimeSpan.FromSeconds(4.5)), (entries[3].Title, entries[3].Duration));
        Assert.Equal("https://example.com/radio", entries[4].Location);
        Assert.Null(entries[4].Title);
    }

    [Fact]
    [Capability("PLF-01")]
    public void PlainM3uIsOneLocationALineInTheOldCodePage()
    {
        var bytes = Encoding.ASCII.GetBytes("Caf\0.mp3\r\nb.mp3\r\n");
        bytes[3] = 0xE9;

        var entries = PlaylistFiles.Read(@"C:\Music\Album\old.m3u", bytes);

        Assert.Equal([@"C:\Music\Album\Caf" + (char)0xE9 + ".mp3", @"C:\Music\Album\b.mp3"], entries.Select(e => e.Location));
    }

    [Fact]
    public void AStreamDescriptionIsNotAPlaylistOfFiles()
    {
        var error = Assert.Throws<FormatException>(() => Read("live.m3u8", """
            #EXTM3U
            #EXT-X-TARGETDURATION:6
            seg1.ts
            """));
        Assert.Contains("stream", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("PLF-01")]
    public void M3uIsWrittenWithDetailsAndRelativeLocations()
    {
        PlaylistEntry[] entries =
        [
            new(@"C:\Music\Album\01 Sungba.mp3") { Title = "Sungba", Artist = "Asake", Duration = TimeSpan.FromSeconds(171.6) },
            new(@"C:\Music\Album\02.mp3") { Title = "Two" },
            new(@"D:\Other\03.mp3"),
        ];

        var text = PlaylistFiles.Write(entries, PlaylistFormat.M3u, Music);

        Assert.Equal(
            "#EXTM3U\n#EXTINF:172,Asake - Sungba\n01 Sungba.mp3\n#EXTINF:-1,Two\n02.mp3\nD:\\Other\\03.mp3\n",
            text);
        var again = PlaylistFiles.Parse(text, PlaylistFormat.M3u, Music);
        Assert.Equal(entries.Select(e => (e.Location, e.Title, e.Artist)), again.Select(e => (e.Location, e.Title, e.Artist)));
    }

    [Fact]
    [Capability("PLF-02")]
    public void PlsIsReadByNumberAndWrittenBack()
    {
        var entries = Read("list.pls", """
            [other]
            File9=ignored.mp3
            [Playlist]
            NumberOfEntries=3
            File2=b.mp3
            Title2=Bee
            Length2=-1
            File1=a.mp3
            Length1=60
            FileX=bad.mp3
            not a setting
            Title3=No file
            Version=2
            File10=https://example.com/stream
            """);

        Assert.Equal([@"C:\Music\Album\a.mp3", @"C:\Music\Album\b.mp3", "https://example.com/stream"], entries.Select(e => e.Location));
        Assert.Equal((null, TimeSpan.FromMinutes(1)), (entries[0].Title, entries[0].Duration));
        Assert.Equal(("Bee", (TimeSpan?)null), (entries[1].Title, entries[1].Duration));

        var text = PlaylistFiles.Write([entries[1], entries[0] with { Title = "A", Artist = "Asake" }], PlaylistFormat.Pls, Music);
        Assert.Equal("[playlist]\nFile1=b.mp3\nTitle1=Bee\nLength1=-1\nFile2=a.mp3\nTitle2=Asake - A\nLength2=60\nNumberOfEntries=2\nVersion=2\n", text);
    }

    [Fact]
    [Capability("PLF-03")]
    public void XspfKeepsTracksDetailsAndOtherProgramsData()
    {
        var entries = Read("list.xspf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/" xmlns:x="urn:example">
              <title>Asake</title>
              <trackList>
                <track>
                  <location>01%20Sungba.mp3</location>
                  <title>Sungba</title>
                  <creator>Asake</creator>
                  <duration>171000</duration>
                  <extension application="urn:example"><x:rating>5</x:rating></extension>
                </track>
                <track><title>No location</title></track>
                <track><location>file:///D:/Other/a.mp3</location><duration>soon</duration></track>
              </trackList>
            </playlist>
            """);

        Assert.Equal(2, entries.Count);
        Assert.Equal((@"C:\Music\Album\01 Sungba.mp3", "Sungba", "Asake", (TimeSpan?)TimeSpan.FromSeconds(171)), (entries[0].Location, entries[0].Title, entries[0].Artist, entries[0].Duration));
        Assert.Contains("5</x:rating>", entries[0].Extensions["urn:example"], StringComparison.Ordinal);
        Assert.Equal((@"D:\Other\a.mp3", (TimeSpan?)null), (entries[1].Location, entries[1].Duration));
        Assert.Throws<FormatException>(() => Read("bad.xspf", "<playlist xmlns=\"http://xspf.org/ns/0/\"><trackList>"));
    }

    [Fact]
    [Capability("PLF-03")]
    public void XspfIsWrittenAsUrisAndReadsBackTheSame()
    {
        PlaylistEntry[] entries =
        [
            new(@"C:\Music\Album\01 Sungba.mp3") { Title = "Sungba", Artist = "Asake", Duration = TimeSpan.FromSeconds(171), Extensions = new Dictionary<string, string> { ["urn:example"] = "<r xmlns=\"urn:example\">5</r>" } },
            new(@"D:\Other\a b.mp3"),
            new(@"\\server\share\c.mp3"),
            new("/srv/music/d.mp3"),
            new("https://example.com/e.mp3"),
        ];

        var text = PlaylistFiles.Write(entries, PlaylistFormat.Xspf, Music, "Mix");

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text, StringComparison.Ordinal);
        Assert.Contains("<location>01%20Sungba.mp3</location>", text, StringComparison.Ordinal);
        Assert.Contains("<location>file:///D:/Other/a%20b.mp3</location>", text, StringComparison.Ordinal);
        Assert.Contains("<location>file://server/share/c.mp3</location>", text, StringComparison.Ordinal);
        Assert.Contains("<location>file:///srv/music/d.mp3</location>", text, StringComparison.Ordinal);
        Assert.Contains("<title>Mix</title>", text, StringComparison.Ordinal);
        var again = PlaylistFiles.Parse(text, PlaylistFormat.Xspf, Music);
        Assert.Equal(entries.Select(e => e.Location), again.Select(e => e.Location));
        Assert.Equal((entries[0].Title, entries[0].Artist, entries[0].Duration), (again[0].Title, again[0].Artist, again[0].Duration));
        Assert.Contains(">5</r>", again[0].Extensions["urn:example"], StringComparison.Ordinal);
        Assert.DoesNotContain("<title>", PlaylistFiles.Write([entries[1]], PlaylistFormat.Xspf, Music), StringComparison.Ordinal);
    }

    [Fact]
    [Capability("PLF-04")]
    public void ACueSheetCutsAnAlbumIntoTracks()
    {
        var entries = Read("album.cue", """
            REM GENRE Afrobeats
            PERFORMER "Asake"
            TITLE "Work of Art"
            TRACK 00 AUDIO
            FILE "Work of Art.flac" WAVE
              TRACK 01 AUDIO
                TITLE "Olorun"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Amapiano"
                PERFORMER "Asake & Olamide"
                INDEX 00 02:20:00
                INDEX 01 02:21:37
              TRACK 03 DATA
                INDEX 01 05:00:00
              TRACK 04 AUDIO
                INDEX 01 07:00:74
              TRACK 05 AUDIO
                INDEX 01 bad
            FILE "Bonus.wav" WAVE
              TRACK XX AUDIO
                TITLE "Bonus"
                INDEX 01 00:00:00
            """);

        Assert.Equal(4, entries.Count);
        var album = @"C:\Music\Album\Work of Art.flac";
        Assert.Equal((album, "Olorun", "Asake", TimeSpan.Zero, (TimeSpan?)TimeSpan.FromSeconds(141.49333)), (entries[0].Location, entries[0].Title, entries[0].Artist, entries[0].Start, Round(entries[0].End)));
        Assert.Equal(("Amapiano", "Asake & Olamide"), (entries[1].Title, entries[1].Artist));
        Assert.Equal(entries[0].End, entries[1].Start);
        Assert.Equal(entries[2].Start - entries[1].Start, entries[1].Duration);
        Assert.Equal(("Track 04", TimeSpan.FromSeconds(420) + TimeSpan.FromTicks(74 * TimeSpan.TicksPerSecond / 75), (TimeSpan?)null), (entries[2].Title, entries[2].Start, entries[2].End));
        Assert.Equal((@"C:\Music\Album\Bonus.wav", "Bonus", (TimeSpan?)null), (entries[3].Location, entries[3].Title, entries[3].Duration));
    }

    private static TimeSpan? Round(TimeSpan? time) => time is { } value ? TimeSpan.FromSeconds(Math.Round(value.TotalSeconds, 5)) : null;

    [Fact]
    [Capability("PLF-05")]
    public void AsxIsReadLenientlyInAnyCase()
    {
        var entries = Read("radio.asx", """
            <ASX version="3.0">
              <Title>Stations</Title>
              <Entry>
                <Title>Lonely &amp; At The Top</Title>
                <Author>Asake</Author>
                <Ref HREF="mms://example.com/one" />
                <ref href="ignored-second-ref" />
              </Entry>
              <ENTRY><TITLE> </TITLE></ENTRY>
              <entry><ref href='two.wma'/></entry>
              <EntryRef href="more.asx" />
            </ASX>
            """);

        Assert.Equal(["mms://example.com/one", @"C:\Music\Album\two.wma", @"C:\Music\Album\more.asx"], entries.Select(e => e.Location));
        Assert.Equal(("Lonely & At The Top", "Asake"), (entries[0].Title, entries[0].Artist));
        Assert.Equal((null, null), (entries[1].Title, entries[1].Artist));
        Assert.Throws<FormatException>(() => PlaylistFiles.Parse("<entry/>", PlaylistFormat.Asx, Music));
    }

    [Fact]
    [Capability("PLF-06")]
    public void WplListsItsMedia()
    {
        var entries = Read("list.wpl", """
            <?wpl version="1.0"?>
            <smil>
              <head><title>Mix</title></head>
              <body><seq>
                <media src="..\Other\a.mp3"/>
                <MEDIA SRC="b.wma" tid="{1}"/>
                <media/>
              </seq></body>
            </smil>
            """);

        Assert.Equal([@"C:\Music\Other\a.mp3", @"C:\Music\Album\b.wma"], entries.Select(e => e.Location));
        Assert.Throws<FormatException>(() => Read("bad.wpl", "<?wpl version=\"1.0\"?><smil><body>"));
    }

    [Theory]
    [InlineData("a.txt", "#EXTM3U\nx.mp3", PlaylistFormat.M3u)]
    [InlineData("a.txt", "  [PLAYLIST]\nFile1=x", PlaylistFormat.Pls)]
    [InlineData("a.xml", "<?xml version=\"1.0\"?><playlist xmlns=\"http://xspf.org/ns/0/\"/>", PlaylistFormat.Xspf)]
    [InlineData("a.xml", "<Asx version=\"3\">", PlaylistFormat.Asx)]
    [InlineData("a.xml", "<?wpl version=\"1.0\"?><smil/>", PlaylistFormat.Wpl)]
    [InlineData("a.zpl", "<?zpl version=\"2.0\"?><smil/>", PlaylistFormat.Wpl)]
    [InlineData("a.xml", "<smil/>", PlaylistFormat.Wpl)]
    [InlineData("a.txt", "FILE \"a.flac\" WAVE\n  TRACK 01 AUDIO", PlaylistFormat.Cue)]
    [InlineData("a.M3U", "a.mp3", PlaylistFormat.M3u)]
    [InlineData("a.m3u8", "a.mp3", PlaylistFormat.M3u)]
    [InlineData("a.pls", "", PlaylistFormat.Pls)]
    public void AFormatIsKnownByItsContentsThenItsName(string path, string text, PlaylistFormat format) =>
        Assert.Equal(format, PlaylistFiles.FormatOf(path, text));

    [Fact]
    public void FilesThatAreNoPlaylistAreRefused()
    {
        Assert.Null(PlaylistFiles.FormatOf("a.txt", "hello"));
        Assert.Null(PlaylistFiles.FormatOf("a.xml", "<html/>"));
        Assert.Null(PlaylistFiles.FormatOf("a.cue", "FILE only"));
        var error = Assert.Throws<FormatException>(() => PlaylistFiles.Read(@"C:\notes.txt", "hello"u8));
        Assert.Contains("notes.txt", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaylistFiles.Parse("", (PlaylistFormat)42, ""));
        Assert.Throws<NotSupportedException>(() => PlaylistFiles.Write([], PlaylistFormat.Cue, ""));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.FormatOf(null!, ""));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.FormatOf("", null!));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Read(null!, []));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Parse(null!, PlaylistFormat.M3u, ""));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Parse("", PlaylistFormat.M3u, null!));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Write(null!, PlaylistFormat.M3u, ""));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Write([], PlaylistFormat.M3u, null!));
        Assert.Throws<ArgumentNullException>(() => PlaylistFiles.Folder(null!));
    }

    [Fact]
    public void NamesSayWhatIsAPlaylistAndHowToSaveOne()
    {
        Assert.True(PlaylistFiles.IsPlaylist(@"C:\a.CUE"));
        Assert.False(PlaylistFiles.IsPlaylist(@"C:\a.flac"));
        Assert.Equal(PlaylistFormat.Pls, PlaylistFiles.FormatForSaving("a.PLS"));
        Assert.Equal(PlaylistFormat.Xspf, PlaylistFiles.FormatForSaving("a.xspf"));
        Assert.Equal(PlaylistFormat.M3u, PlaylistFiles.FormatForSaving("a.m3u8"));
        Assert.Equal(PlaylistFiles.Writable, [PlaylistFormat.M3u, PlaylistFormat.Pls, PlaylistFormat.Xspf]);
        Assert.Equal(@"C:\Music", PlaylistFiles.Folder(@"C:\Music\a.m3u"));
        Assert.Equal("/", PlaylistFiles.Folder("/a.m3u"));
        Assert.Equal("", PlaylistFiles.Folder("a.m3u"));
    }

    [Fact]
    public void PlaylistTextIsReadByItsMarkThenAsUtf8()
    {
        Assert.Equal("a", PlaylistText.Decode([0xEF, 0xBB, 0xBF, (byte)'a']));
        Assert.Equal("a", PlaylistText.Decode([0xFF, 0xFE, (byte)'a', 0]));
        Assert.Equal("a", PlaylistText.Decode([0xFE, 0xFF, 0, (byte)'a']));
        Assert.Equal("caf" + (char)0xE9, PlaylistText.Decode([(byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9]));
        Assert.Equal(["a", "b", "c", ""], PlaylistText.Lines("a\r\nb\rc\n"));
    }
}
