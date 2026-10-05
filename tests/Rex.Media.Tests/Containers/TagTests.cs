using System.Text;
using Rex.Media.Containers.Tags;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class Id3v2Tests
{
    private static readonly byte[] Art = [0xFF, 0xD8, 0xFF, 0x00, 0xFF, 0xE0, 1, 2];

    private static Id3v2Tag Read(byte[] tag) => Id3v2.Read(tag) ?? throw new InvalidOperationException("The tag did not read.");

    [Fact]
    [Capability("META-01")]
    public void Version24TextInEveryEncodingAndMultipleValuesRead()
    {
        var tag = Read(Id3Builder.V24()
            .Text("TIT2", "Lonely At The Top", 0)
            .Text("TPE1", "Asake\0Olamide", 3)
            .Text("TALB", "Work of Art", 1)
            .Text("TPE2", "Asake", 2)
            .Text("TDRC", "2023")
            .Text("TRCK", "1/14")
            .Text("TPOS", "1/1")
            .Text("TCOM", "Ahmed Ololade")
            .Text("TCOP", "YBNL")
            .Text("TSSE", "LAME 3.100")
            .Text("TCON", "17")
            .Text("TXYZ", "unknown text frames are ignored")
            .Text("TBPM", string.Empty)
            .Build());

        Assert.Equal(4, tag.Version);
        Assert.Equal("Lonely At The Top", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake; Olamide", tag.Metadata[MetadataKeys.Artist]);
        Assert.Equal("Work of Art", tag.Metadata[MetadataKeys.Album]);
        Assert.Equal("Asake", tag.Metadata[MetadataKeys.AlbumArtist]);
        Assert.Equal("2023", tag.Metadata[MetadataKeys.Date]);
        Assert.Equal("1/14", tag.Metadata[MetadataKeys.Track]);
        Assert.Equal("1/1", tag.Metadata[MetadataKeys.Disc]);
        Assert.Equal("Ahmed Ololade", tag.Metadata[MetadataKeys.Composer]);
        Assert.Equal("YBNL", tag.Metadata[MetadataKeys.Copyright]);
        Assert.Equal("LAME 3.100", tag.Metadata[MetadataKeys.Encoder]);
        Assert.Equal("Rock", tag.Metadata[MetadataKeys.Genre]);
        Assert.Equal(11, tag.Metadata.Count);
    }

    [Fact]
    public void UserTextCommentsLyricsPicturesAndChaptersRead()
    {
        var tag = Read(Id3Builder.V24()
            .UserText("REPLAYGAIN_TRACK_GAIN", "-7.10 dB")
            .UserText("EMPTY", string.Empty)
            .Comment("iTunNORM", " 0000044E 00000000")
            .Comment(string.Empty, "Typed by a person", 1)
            .Comment("iTunSMPB", "ignored after a typed comment")
            .Lyrics("Sungba, sungba")
            .Picture([1, 2, 3], "image/png", 4)
            .Picture(Art)
            .Chapter("ch0", 0, "Intro")
            .Chapter("ch1", 61_500, null)
            .Build());

        Assert.Equal("-7.10 dB", tag.Metadata[MetadataKeys.ReplayGainTrackGain]);
        Assert.False(tag.Metadata.ContainsKey("empty"));
        Assert.Equal("Typed by a person", tag.Metadata[MetadataKeys.Comment]);
        Assert.Equal("Sungba, sungba", tag.Metadata[MetadataKeys.Lyrics]);
        Assert.Equal(2, tag.Pictures.Count);
        Assert.Equal(Art, PictureBlock.Cover(tag.Pictures)!.Data);
        Assert.Equal("image/png", tag.Pictures[0].MimeType);
        Assert.Equal([new Chapter(MediaTime.Zero, "Intro"), new Chapter(MediaTime.FromSeconds(61.5), "ch1")], tag.Chapters);
    }

    [Fact]
    public void AToolsPrivateCommentIsUsedOnlyWhenNothingElseIs()
    {
        var onlyPrivate = Read(Id3Builder.V24().Comment("iTunNORM", "garbage").Build());
        var describedByAPerson = Read(Id3Builder.V24().Comment("Note", "A described comment").Build());

        Assert.False(onlyPrivate.Metadata.ContainsKey(MetadataKeys.Comment));
        Assert.Equal("A described comment", describedByAPerson.Metadata[MetadataKeys.Comment]);
    }

    [Fact]
    public void CompressedGroupedAndUnsynchronisedFramesAreDecodedAndEncryptedOnesSkipped()
    {
        var v24 = Read(Id3Builder.V24()
            .Compressed("TIT2", "Terminator")
            .Grouped("TPE1", "Asake")
            .UnsynchronisedFrame("TALB", [0, 0xFF, 0xE0, .. "Mr. Money"u8])
            .Encrypted("TCON")
            .BrokenCompression("TCOM")
            .ExtendedHeader()
            .Footer()
            .Build());
        var v23 = Read(Id3Builder.V23()
            .Compressed("TIT2", "Joha")
            .Grouped("TPE1", "Asake")
            .Encrypted("TALB")
            .Text("TCON", "(17)Rock")
            .Picture(Art)
            .Unsynchronised()
            .ExtendedHeader()
            .Build());

        Assert.Equal("Terminator", v24.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", v24.Metadata[MetadataKeys.Artist]);
        Assert.Equal((char)0xFF + string.Empty + (char)0xE0 + "Mr. Money", v24.Metadata[MetadataKeys.Album]);
        Assert.False(v24.Metadata.ContainsKey(MetadataKeys.Genre));
        Assert.False(v24.Metadata.ContainsKey(MetadataKeys.Composer));
        Assert.Equal("Joha", v23.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", v23.Metadata[MetadataKeys.Artist]);
        Assert.False(v23.Metadata.ContainsKey(MetadataKeys.Album));
        Assert.Equal("Rock", v23.Metadata[MetadataKeys.Genre]);
        Assert.Equal(Art, Assert.Single(v23.Pictures).Data);
    }

    [Fact]
    public void Version22ThreeLetterFramesAndPicturesRead()
    {
        var tag = Read(Id3Builder.V22()
            .Text("TT2", "Basquiat")
            .Text("TP1", "Asake")
            .Text("TCO", "(13)")
            .UserText("REPLAYGAIN_ALBUM_GAIN", "-5 dB")
            .Comment(string.Empty, "Two")
            .Lyrics("Words")
            .Picture(Art, "image/png")
            .Picture(Art, "image/jpeg")
            .Picture(Art, "image/gif")
            .Build());

        Assert.Equal(2, tag.Version);
        Assert.Equal("Basquiat", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("Pop", tag.Metadata[MetadataKeys.Genre]);
        Assert.Equal("-5 dB", tag.Metadata[MetadataKeys.ReplayGainAlbumGain]);
        Assert.Equal("Two", tag.Metadata[MetadataKeys.Comment]);
        Assert.Equal("Words", tag.Metadata[MetadataKeys.Lyrics]);
        Assert.Equal(["image/png", "image/jpeg", "image/gif"], tag.Pictures.Select(p => p.MimeType));
    }

    [Fact]
    public void Utf16TextWithEitherByteOrderMarkReads()
    {
        var bigEndian = (byte[])[1, 0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Amapiano")];
        var noBom = (byte[])[1, .. Encoding.Unicode.GetBytes("Mogbe")];
        var tag = Read(Id3Builder.V24().Frame("TIT2", bigEndian).Frame("TPE1", noBom).Build());

        Assert.Equal("Amapiano", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("Mogbe", tag.Metadata[MetadataKeys.Artist]);
    }

    [Fact]
    public void DamagedFramesAreSkippedAndTheRestStillRead()
    {
        var tag = Read(Id3Builder.V24()
            .Frame("TIT1", [])
            .Frame("APIC", [0, .. "image/png"u8])
            .Frame("APIC", [0, .. "image/png"u8, 0])
            .Frame("APIC", [1, .. "image/png"u8, 0, 3, 0x41])
            .Frame("CHAP", [.. "c"u8, 0, 1, 2])
            .Frame("CHAP", [.. "nul"u8])
            .Frame("COMM", [3, .. "en"u8])
            .Frame("TXXX", [3, .. "NO TERMINATOR"u8])
            .Frame("TPE1", [0, 0, 0, 1], 0x01)
            .Frame("TPE2", [0, 0], 0x01)
            .Text("TIT2", "Still read")
            .Build());

        Assert.Equal("Still read", tag.Metadata[MetadataKeys.Title]);
        Assert.Empty(tag.Pictures);
        Assert.Empty(tag.Chapters);
        Assert.False(tag.Metadata.ContainsKey("no terminator"));
    }

    [Fact]
    public void Version22PictureTooShortIsSkipped()
    {
        Assert.Empty(Read(Id3Builder.V22().Frame("PIC", [0, 1, 2]).Build()).Pictures);
    }

    [Fact]
    public void AFrameClaimingMoreThanTheTagHoldsEndsTheTag()
    {
        var tag = Id3Builder.V23().Text("TIT2", "Kept").Text("TPE1", "Lost").Build();
        tag[10 + 10 + 1 + 4 + 7] = 0x7F;

        var read = Read(tag);

        Assert.Equal("Kept", read.Metadata[MetadataKeys.Title]);
        Assert.False(read.Metadata.ContainsKey(MetadataKeys.Artist));
    }

    [Fact]
    public void TheTagLengthCountsHeaderBodyAndFooter()
    {
        var plain = Id3Builder.V24().Text("TIT2", "x").Padding(5).Build();
        var footed = Id3Builder.V24().Text("TIT2", "x").Footer().Build();

        Assert.Equal(plain.Length, Id3v2.TagLength(plain));
        Assert.Equal(footed.Length, Id3v2.TagLength(footed));
        Assert.Equal(0, Id3v2.TagLength("ID3"u8));
        Assert.Equal(0, Id3v2.TagLength([.. "ID3"u8, 5, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(0, Id3v2.TagLength([.. "ID3"u8, 4, 0xFF, 0, 0, 0, 0, 0]));
        Assert.Equal(0, Id3v2.TagLength([.. "ID3"u8, 4, 0, 0, 0x80, 0, 0, 0]));
        Assert.Equal(0, Id3v2.TagLength([.. "XYZ"u8, 4, 0, 0, 0, 0, 0, 0]));
        Assert.Null(Id3v2.Read(plain.AsSpan(0, plain.Length - 1)));
        Assert.Null(Id3v2.Read("RIFF"u8));
    }

    [Fact]
    public void StackedTagsAtTheStartAreMeasuredTogether()
    {
        var first = Id3Builder.V23().Text("TIT2", "a").Build();
        var second = Id3Builder.V24().Text("TIT2", "b").Build();

        var stacked = (byte[])[.. first, .. second, 1, 2, 3];

        Assert.Equal(first.Length + second.Length, Id3v2.LeadingTagsLength(new MemoryByteSource(stacked), CancellationToken.None));
        Assert.Equal(0, Id3v2.LeadingTagsLength(new MemoryByteSource(new byte[] { 1, 2, 3 }), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => Id3v2.LeadingTagsLength(null!, CancellationToken.None));
    }

    [Fact]
    public void AShortExtendedHeaderEndsTheTagQuietly()
    {
        var tag = (byte[])[.. "ID3"u8, 3, 0, 0x40, 0, 0, 0, 2, 0, 0];

        Assert.Empty(Read(tag).Metadata);
    }
}

public sealed class Id3v1Tests
{
    private static byte[] Tag(string title, byte track, byte genre)
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        Encoding.Latin1.GetBytes(title).CopyTo(tag, 3);
        "Asake"u8.CopyTo(tag.AsSpan(33));
        "2024"u8.CopyTo(tag.AsSpan(93));
        "comment\0junk"u8.CopyTo(tag.AsSpan(97));
        tag[126] = track;
        tag[127] = genre;
        return tag;
    }

    [Fact]
    public void Version11FieldsIncludingTheTrackRead()
    {
        var metadata = new Dictionary<string, string>();

        Id3v1.Read(Tag("Active", 5, 13), metadata);

        Assert.Equal("Active", metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", metadata[MetadataKeys.Artist]);
        Assert.Equal("2024", metadata[MetadataKeys.Date]);
        Assert.Equal("comment", metadata[MetadataKeys.Comment]);
        Assert.Equal("5", metadata[MetadataKeys.Track]);
        Assert.Equal("Pop", metadata[MetadataKeys.Genre]);
        Assert.False(metadata.ContainsKey(MetadataKeys.Album));
    }

    [Fact]
    public void RicherFieldsAreNotOverwrittenAndUnknownGenresAreLeftOut()
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Title] = "From ID3v2" };

        Id3v1.Read(Tag("Old", 0, 255), metadata);
        Id3v1.Read(new byte[128], metadata);

        Assert.Equal("From ID3v2", metadata[MetadataKeys.Title]);
        Assert.False(metadata.ContainsKey(MetadataKeys.Track));
        Assert.False(metadata.ContainsKey(MetadataKeys.Genre));
        Assert.True(Id3v1.IsTag(Tag("x", 0, 0)));
        Assert.False(Id3v1.IsTag(new byte[127]));
        Assert.Throws<ArgumentNullException>(() => Id3v1.Read(Tag("x", 0, 0), null!));
    }

    [Theory]
    [InlineData("(17)", "Rock")]
    [InlineData("(17)Rock and Roll", "Rock and Roll")]
    [InlineData("17", "Rock")]
    [InlineData("79", "Hard Rock")]
    [InlineData("80", "80")]
    [InlineData("(RX)", "(RX)")]
    [InlineData("Afrobeats", "Afrobeats")]
    [InlineData("()", "()")]
    public void NumberedGenresBecomeNames(string stored, string shown)
    {
        Assert.Equal(shown, Id3v1.ExpandGenre(stored));
    }
}

public sealed class VorbisCommentTests
{
    [Theory]
    [InlineData("TITLE", MetadataKeys.Title)]
    [InlineData("artist", MetadataKeys.Artist)]
    [InlineData("ALBUM", MetadataKeys.Album)]
    [InlineData("ALBUMARTIST", MetadataKeys.AlbumArtist)]
    [InlineData("ALBUM ARTIST", MetadataKeys.AlbumArtist)]
    [InlineData("ALBUM_ARTIST", MetadataKeys.AlbumArtist)]
    [InlineData("GENRE", MetadataKeys.Genre)]
    [InlineData("DATE", MetadataKeys.Date)]
    [InlineData("YEAR", MetadataKeys.Date)]
    [InlineData("TRACKNUMBER", MetadataKeys.Track)]
    [InlineData("DISCNUMBER", MetadataKeys.Disc)]
    [InlineData("COMMENT", MetadataKeys.Comment)]
    [InlineData("DESCRIPTION", MetadataKeys.Comment)]
    [InlineData("COMPOSER", MetadataKeys.Composer)]
    [InlineData("COPYRIGHT", MetadataKeys.Copyright)]
    [InlineData("ENCODER", MetadataKeys.Encoder)]
    [InlineData("ENCODED-BY", MetadataKeys.Encoder)]
    [InlineData("LYRICS", MetadataKeys.Lyrics)]
    [InlineData("UNSYNCEDLYRICS", MetadataKeys.Lyrics)]
    [InlineData("MusicBrainz_TrackId", "musicbrainz_trackid")]
    public void FieldNamesMapToTheCanonicalKeys(string field, string key)
    {
        Assert.Equal(key, VorbisComments.CanonicalName(field));
    }

    [Fact]
    public void ATruncatedBlockKeepsTheFieldsThatWereWhole()
    {
        var block = FlacBuilder.VorbisComments("vendor", ["TITLE=Whole", "ARTIST=Cut off"]);
        var metadata = new Dictionary<string, string>();

        VorbisComments.Read(block.AsSpan(0, block.Length - 3), metadata);
        VorbisComments.Read(block.AsSpan(0, 2), metadata);
        VorbisComments.Read(block.AsSpan(0, 12), metadata);

        Assert.Equal("Whole", metadata[MetadataKeys.Title]);
        Assert.False(metadata.ContainsKey(MetadataKeys.Artist));
        Assert.Throws<ArgumentNullException>(() => VorbisComments.Read(block, null!));
        Assert.Throws<ArgumentNullException>(() => VorbisComments.CanonicalName(null!));
    }

    [Fact]
    public void AVendorStringLongerThanTheBlockReadsNothing()
    {
        var metadata = new Dictionary<string, string>();

        VorbisComments.Read([0xFF, 0, 0, 0, 1], metadata);

        Assert.Empty(metadata);
    }

    [Fact]
    public void ATrackNumberThatAlreadyHasItsTotalIsLeftAlone()
    {
        var metadata = new Dictionary<string, string>();

        VorbisComments.Read(FlacBuilder.VorbisComments("v", ["TRACKNUMBER=2/9", "TRACKTOTAL=12"]), metadata);

        Assert.Equal("2/9", metadata[MetadataKeys.Track]);
    }
}

public sealed class PictureBlockTests
{
    [Fact]
    public void TruncatedPictureBlocksAreNotPictures()
    {
        var whole = FlacPicture([9, 9, 9]);

        Assert.NotNull(PictureBlock.Parse(whole));
        foreach (var cut in new[] { 2, 6, 15, 20, 30, whole.Length - 1 })
        {
            Assert.Null(PictureBlock.Parse(whole.AsSpan(0, cut)));
        }
    }

    [Fact]
    public void TheFrontCoverIsPreferredThenTheFirstPicture()
    {
        var back = new PictureBlock(4, "image/png", [1]);
        var front = new PictureBlock(3, "image/png", [2]);

        Assert.Same(front, PictureBlock.Cover([back, front]));
        Assert.Same(back, PictureBlock.Cover([back]));
        Assert.Null(PictureBlock.Cover([]));
        Assert.Throws<ArgumentNullException>(() => PictureBlock.Cover(null!));
    }

    /// <summary>A FLAC PICTURE block body, written field by field.</summary>
    private static byte[] FlacPicture(byte[] data)
    {
        var mime = "image/png"u8.ToArray();
        var block = new List<byte>();
        void U32(int value) => block.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        U32(3);
        U32(mime.Length);
        block.AddRange(mime);
        U32(0);
        U32(1);
        U32(1);
        U32(24);
        U32(0);
        U32(data.Length);
        block.AddRange(data);
        return [.. block];
    }
}
