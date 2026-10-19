using System.Text;
using Rex.Media.Containers.Tags;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class TagEditTests
{
    [Fact]
    public void FieldsListOnlyTheChangedDetailsTrimmedInDialogOrder()
    {
        var edit = new TagEdit { Genre = "Afrobeats ", Title = " Sungba", AlbumArtist = string.Empty };

        Assert.Equal([(MetadataKeys.Title, "Sungba"), (MetadataKeys.AlbumArtist, string.Empty), (MetadataKeys.Genre, "Afrobeats")], edit.Fields);
        Assert.False(edit.IsEmpty);
        Assert.False(edit.ChangesCover);
        Assert.Null(edit.Value(MetadataKeys.Comment));
        Assert.True(new TagEdit().IsEmpty);
        Assert.False(new TagEdit { RemoveCover = true }.IsEmpty);
        Assert.False(new TagEdit { Cover = new PictureBlock(3, "image/png", [1]) }.IsEmpty);
    }

    [Fact]
    public void EveryEditableKeyMapsToItsProperty()
    {
        var edit = new TagEdit { Title = "1", Artist = "2", Album = "3", AlbumArtist = "4", Track = "5", Disc = "6", Date = "7", Genre = "8" };

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], TagEdit.EditableKeys.Select(edit.Value));
    }

    [Fact]
    public void SplitSeparatesValuesAndDropsEmptyOnes()
    {
        Assert.Equal(["Asake", "Olamide"], TagEdit.Split("Asake ;; Olamide;"));
        Assert.Empty(TagEdit.Split(";"));
    }

    [Fact]
    public void ValidateRefusesEmptyOrHugeCoversAndNulCharacters()
    {
        Assert.Throws<TagWriteException>(() => new TagEdit { Cover = new PictureBlock(3, "image/png", []) }.Validate());
        Assert.Throws<TagWriteException>(() => new TagEdit { Cover = new PictureBlock(3, string.Empty, [1]) }.Validate());
        Assert.Throws<TagWriteException>(() => new TagEdit { Cover = new PictureBlock(3, "image/png", new byte[TagEdit.MaxCoverBytes + 1]) }.Validate());
        Assert.Throws<TagWriteException>(() => new TagEdit { Title = "Lonely\0At The Top" }.Validate());
        new TagEdit { Title = "Terminator", Cover = new PictureBlock(3, "image/png", [1]) }.Validate();
    }

    [Fact]
    public void TheExceptionCarriesItsMessageAndCause()
    {
        Assert.NotNull(new TagWriteException().Message);
        var inner = new IOException("x");
        Assert.Same(inner, new TagWriteException("refused", inner).InnerException);
    }
}

public sealed class Id3v2WriterTests
{
    private static readonly byte[] Art = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];

    private static readonly TagEdit Full = new()
    {
        Title = "Lonely At The Top",
        Artist = "Asake; Olamide",
        Album = "Work of Art",
        AlbumArtist = "Asake",
        Track = "1/14",
        Disc = "1/1",
        Date = "2023-06-16",
        Genre = "Afrobeats",
        Cover = new PictureBlock(3, "image/png", Art),
    };

    private static Id3v2Tag Reread(TagRewrite rewrite) => Id3v2.Read(rewrite.Bytes) ?? throw new InvalidOperationException("The tag did not read back.");

    [Fact]
    public void ANewTagIsVersion24AndReadsBackEveryField()
    {
        var rewrite = Id3v2Writer.Rewrite([], Full);
        var tag = Reread(rewrite);

        Assert.Equal(4, tag.Version);
        Assert.Equal("Asake; Olamide", tag.Metadata[MetadataKeys.Artist]);
        Assert.Equal("2023-06-16", tag.Metadata[MetadataKeys.Date]);
        Assert.Equal(Art, PictureBlock.Cover(tag.Pictures)!.Data);
        foreach (var (key, value) in rewrite.Expected)
        {
            Assert.Equal(value, tag.Metadata[key]);
        }

        Assert.Equal(Id3v2Writer.Padding, rewrite.Bytes.Length - Id3v2.HeaderSize - FramesLength(rewrite.Bytes));
    }

    [Fact]
    public void UntouchedFramesAreCopiedAndTheTagKeepsItsLengthWhenTheEditFits()
    {
        var old = Id3Builder.V24().Text("TIT2", "Old").Text("TCOM", "Ahmed Ololade").Comment(string.Empty, "Typed").Lyrics("Sungba, sungba")
            .Compressed("TCOP", "YBNL").Picture([9, 9], "image/jpeg", 4).Padding(4096).Build();

        var rewrite = Id3v2Writer.Rewrite(old, new TagEdit { Title = "Sungba", Album = string.Empty });
        var tag = Reread(rewrite);

        Assert.Equal(old.Length, rewrite.Bytes.Length);
        Assert.Equal("Sungba", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("Ahmed Ololade", tag.Metadata[MetadataKeys.Composer]);
        Assert.Equal("YBNL", tag.Metadata[MetadataKeys.Copyright]);
        Assert.Equal("Typed", tag.Metadata[MetadataKeys.Comment]);
        Assert.Equal("Sungba, sungba", tag.Metadata[MetadataKeys.Lyrics]);
        Assert.Equal([9, 9], Assert.Single(tag.Pictures).Data);
        Assert.Null(rewrite.Expected[MetadataKeys.Album]);
    }

    [Fact]
    public void Version23KeepsItsVersionWritesUtf16AndStoresOnlyTheYear()
    {
        var old = Id3Builder.V23().Text("TIT2", "Old").Text("TYER", "2019").Text("TDRC", "2019").Unsynchronised().Build();

        var rewrite = Id3v2Writer.Rewrite(old, Full with { Title = "Ọlọrun Terminator" });
        var tag = Reread(rewrite);

        Assert.Equal(3, tag.Version);
        Assert.Equal("Ọlọrun Terminator", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("2023", tag.Metadata[MetadataKeys.Date]);
        Assert.Equal("2023", rewrite.Expected[MetadataKeys.Date]);
        Assert.Equal("Asake; Olamide", tag.Metadata[MetadataKeys.Artist]);
        Assert.Equal(0, rewrite.Bytes[5]);
    }

    [Fact]
    public void Version23RefusesADateWithoutAYear()
    {
        var old = Id3Builder.V23().Text("TIT2", "Old").Build();

        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(old, new TagEdit { Date = "June" }));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(old, new TagEdit { Date = "20x3" }));
    }

    [Fact]
    public void ReplacingTheCoverDropsOnlyThePlainFrontCoverAndWritesTheNewOneFirst()
    {
        var old = Id3Builder.V24().Picture([1], "image/jpeg", 3).Picture([2], "image/jpeg", 4)
            .Frame("APIC", [0, .. "image/png"u8, 0, 3, 0, 7], 0x40)
            .Frame("APIC", [0, .. "image/png"u8])
            .Frame("APIC", [0, .. "image/png"u8, 0])
            .Build();

        var tag = Reread(Id3v2Writer.Rewrite(old, new TagEdit { Cover = new PictureBlock(3, "image/png", Art) }));

        Assert.Equal(Art, PictureBlock.Cover(tag.Pictures)!.Data);
        Assert.Equal(2, tag.Pictures.Count);
        Assert.Contains(tag.Pictures, picture => picture.Data.SequenceEqual(new byte[] { 2 }));
    }

    [Fact]
    public void RemovingTheCoverDropsEveryPicture()
    {
        var old = Id3Builder.V24().Text("TIT2", "Sungba").Picture([1], "image/jpeg", 3).Picture([2], "image/jpeg", 4).Build();

        var tag = Reread(Id3v2Writer.Rewrite(old, new TagEdit { RemoveCover = true }));

        Assert.Empty(tag.Pictures);
        Assert.Equal("Sungba", tag.Metadata[MetadataKeys.Title]);
    }

    [Fact]
    public void ExtendedHeadersAndFootersAreDroppedAndPerFrameUnsynchronisationIsKept()
    {
        var old = Id3Builder.V24().UnsynchronisedFrame("TCOM", [3, 0xFF, 0xE0, .. "x"u8]).Text("TPE1", "Asake").ExtendedHeader().Footer().Build();

        var rewrite = Id3v2Writer.Rewrite(old, new TagEdit { Title = "Terminator" });
        var tag = Reread(rewrite);

        Assert.Equal("Terminator", tag.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", tag.Metadata[MetadataKeys.Artist]);
        Assert.True(tag.Metadata.ContainsKey(MetadataKeys.Composer));
        Assert.Equal(0, rewrite.Bytes[5]);
        var v3 = Reread(Id3v2Writer.Rewrite(Id3Builder.V23().Text("TPE1", "Asake").ExtendedHeader().Build(), new TagEdit { Title = "Terminator" }));
        Assert.Equal("Asake", v3.Metadata[MetadataKeys.Artist]);
    }

    [Fact]
    public void DamagedTagsAreRefusedNeverGuessedAt()
    {
        var good = Id3Builder.V24().Text("TIT2", "Sungba").Build();
        var edit = new TagEdit { Title = "Terminator" };

        // Truncated, stacked, 2.2, unknown flags.
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(good.AsSpan(0, good.Length - 2), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite([.. good, .. good], edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(Id3Builder.V22().Text("TT2", "x").Build(), edit));
        var flagged = (byte[])good.Clone();
        flagged[5] = 0x01;
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(flagged, edit));

        // An oversized frame, a frame cut off mid-header, a bad id, a size that is not sync-safe.
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [.. "TIT2"u8, 0, 0, 1, 0, 0, 0, 3, 65]), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [.. "TIT2"u8, 0, 0]), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [.. "tit2"u8, 0, 0, 0, 1, 0, 0, 65]), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [.. "TIT2"u8, 0, 0, 0, 0x81, 0, 0, 65]), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(3, [.. "TIT2"u8, 0x80, 0, 0, 0, 0, 0, 65]), edit));

        // Extended headers that do not fit.
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [0, 0, 1], 0x40), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [0, 0, 0x7F, 0, 1, 0], 0x40), edit));
        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(WithBody(4, [0, 0, 0, 2, 1, 0], 0x40), edit));
    }

    [Fact]
    public void ATagThatWouldPassTheReadersLimitIsRefused()
    {
        var old = Id3Builder.V24().Frame("PRIV", new byte[Id3v2Writer.MaxTagBytes - 30]).Build();

        Assert.Throws<TagWriteException>(() => Id3v2Writer.Rewrite(old, new TagEdit { Title = "Lonely At The Top, a title long enough to grow the tag" }));
    }

    [Fact]
    public void Id3v1TakesTheEditTruncatedToItsFields()
    {
        var old = new byte[Id3v1.Size];
        "TAG"u8.CopyTo(old);
        Encoding.Latin1.GetBytes("Old title").CopyTo(old, 3);
        Encoding.Latin1.GetBytes("A thirty character long comment").AsSpan(0, 30).CopyTo(old.AsSpan(97));

        var updated = Id3v1Writer.Update(old, new TagEdit
        {
            Title = "Lonely At The Top (a very long title indeed)",
            Artist = "Asake",
            Album = "Work of Art",
            Date = "2023-06-16",
            Track = "7/14",
            Genre = "hip-hop",
        });
        var metadata = new Dictionary<string, string>();
        Id3v1.Read(updated, metadata);

        Assert.Equal("Lonely At The Top (a very lon", metadata[MetadataKeys.Title][..29]);
        Assert.Equal("Asake", metadata[MetadataKeys.Artist]);
        Assert.Equal("Work of Art", metadata[MetadataKeys.Album]);
        Assert.Equal("2023", metadata[MetadataKeys.Date]);
        Assert.Equal("7", metadata[MetadataKeys.Track]);
        Assert.Equal("Hip-Hop", metadata[MetadataKeys.Genre]);

        var cleared = Id3v1Writer.Update(updated, new TagEdit { Title = string.Empty, Track = "999", Genre = "Afrobeats", Disc = "1" });
        metadata.Clear();
        Id3v1.Read(cleared, metadata);
        Assert.False(metadata.ContainsKey(MetadataKeys.Title));
        Assert.False(metadata.ContainsKey(MetadataKeys.Track));
        Assert.False(metadata.ContainsKey(MetadataKeys.Genre));
        Assert.Equal(0, Id3v1Writer.Update(updated, new TagEdit { Track = "1234" })[126]);
        Assert.Throws<TagWriteException>(() => Id3v1Writer.Update(new byte[Id3v1.Size], new TagEdit()));
    }

    private static byte[] WithBody(byte version, byte[] body, byte flags = 0)
    {
        var header = new byte[10];
        "ID3"u8.CopyTo(header);
        header[3] = version;
        header[5] = flags;
        header[9] = (byte)body.Length;
        return [.. header, .. body];
    }

    private static int FramesLength(byte[] tag)
    {
        var offset = Id3v2.HeaderSize;
        while (offset < tag.Length && tag[offset] != 0)
        {
            offset += 10 + Id3v2.SyncSafe(tag.AsSpan(offset + 4, 4));
        }

        return offset - Id3v2.HeaderSize;
    }
}

public sealed class FlacTagWriterTests
{
    private static readonly byte[] Art = [0x89, (byte)'P', (byte)'N', (byte)'G', 4, 5, 6];

    private static int[][] Stereo(int samples) =>
    [
        FlacBuilder.Tone(samples, 16, 440, 44_100),
        FlacBuilder.Tone(samples, 16, 550, 44_100, seed: 2),
    ];

    private static byte[] Metadata(FlacBuilder builder, out byte[] file)
    {
        file = builder.Build(Stereo(4000));
        return file[..(int)builder.FirstFrameOffset];
    }

    private static (Dictionary<string, string> Fields, List<PictureBlock> Pictures, List<int> Types) Read(byte[] metadata)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictures = new List<PictureBlock>();
        var types = new List<int>();
        var offset = 4;
        var last = false;
        while (!last)
        {
            var type = metadata[offset] & 0x7F;
            last = (metadata[offset] & 0x80) != 0;
            var length = (metadata[offset + 1] << 16) | (metadata[offset + 2] << 8) | metadata[offset + 3];
            var body = metadata.AsSpan(offset + 4, length);
            types.Add(type);
            if (type == 4)
            {
                VorbisComments.Read(body, fields);
            }
            else if (type == 6)
            {
                pictures.Add(PictureBlock.Parse(body)!);
            }

            offset += 4 + length;
        }

        Assert.Equal(metadata.Length, offset);
        return (fields, pictures, types);
    }

    [Fact]
    public void TheEditReplacesItsFieldsAndKeepsEveryOtherBlockAndField()
    {
        var builder = new FlacBuilder
        {
            Comments = ["TITLE=Old", "ARTIST=Someone", "TRACKNUMBER=3", "TRACKTOTAL=12", "ALBUM ARTIST=Old", "YEAR=2019", "REPLAYGAIN_TRACK_GAIN=-6.20 dB", "METADATA_BLOCK_PICTURE=AAAA", "noequals"],
            Picture = ([1, 2], "image/jpeg", 3),
            SeekPointInterval = 1,
            CueTracks = [(0, 1)],
            Application = "rext",
            Padding = 16384,
        };
        var old = Metadata(builder, out _);

        var rewrite = FlacTagWriter.Rewrite(old, new TagEdit
        {
            Title = "Sungba",
            Artist = "Asake; Burna Boy",
            Track = "3/12",
            AlbumArtist = "Asake",
            Date = "2023",
            Genre = ";",
            Album = string.Empty,
            Cover = new PictureBlock(3, "image/png", Art),
        });
        var (fields, pictures, types) = Read(rewrite.Bytes);

        Assert.Equal(old.Length, rewrite.Bytes.Length);
        Assert.Equal("Sungba", fields[MetadataKeys.Title]);
        Assert.Equal("Asake; Burna Boy", fields[MetadataKeys.Artist]);
        Assert.Equal("3/12", fields[MetadataKeys.Track]);
        Assert.Equal("Asake", fields[MetadataKeys.AlbumArtist]);
        Assert.Equal("2023", fields[MetadataKeys.Date]);
        Assert.Equal("-6.20 dB", fields[MetadataKeys.ReplayGainTrackGain]);
        Assert.False(fields.ContainsKey("metadata_block_picture"));
        Assert.Null(rewrite.Expected[MetadataKeys.Genre]);
        Assert.Null(rewrite.Expected[MetadataKeys.Album]);
        Assert.Equal(Art, Assert.Single(pictures).Data);
        Assert.Equal(0, types[0]);
        Assert.Contains(2, types);
        Assert.Contains(3, types);
        Assert.Contains(5, types);
        Assert.Equal(1, types[^1]);
    }

    [Fact]
    public void AFileWithoutCommentsGetsThemAndGrowsWithPadding()
    {
        var old = Metadata(new FlacBuilder { Picture = ([1, 2], "image/jpeg", 4) }, out _);

        var rewrite = FlacTagWriter.Rewrite(old, new TagEdit { Title = "Terminator", Cover = new PictureBlock(3, "image/png", Art) });
        var (fields, pictures, _) = Read(rewrite.Bytes);

        Assert.Equal("Terminator", fields[MetadataKeys.Title]);
        Assert.Equal(2, pictures.Count);
        Assert.Equal(Art, PictureBlock.Cover(pictures)!.Data);
        Assert.True(rewrite.Bytes.Length > old.Length + FlacTagWriter.Padding);
    }

    [Fact]
    public void RemovingTheCoverDropsEveryPicture()
    {
        var old = Metadata(new FlacBuilder { Comments = ["TITLE=Sungba"], Picture = ([1, 2], "image/jpeg", 3) }, out _);

        var (fields, pictures, _) = Read(FlacTagWriter.Rewrite(old, new TagEdit { RemoveCover = true }).Bytes);

        Assert.Empty(pictures);
        Assert.Equal("Sungba", fields[MetadataKeys.Title]);
    }

    [Fact]
    public void DamagedMetadataIsRefused()
    {
        var old = Metadata(new FlacBuilder { Comments = ["TITLE=Sungba"] }, out _);
        var edit = new TagEdit { Title = "Terminator" };

        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite(old.AsSpan(0, 6), edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. "fLaX"u8, .. old[4..]], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite(old.AsSpan(0, old.Length - 1), edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. old, 0], edit));

        var streamInfo = old[..(4 + 4 + 34)];
        streamInfo[4] = 0;
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite(streamInfo, edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x80 | 127, 0, 0, 0], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x80, 0, 0, 34, .. new byte[34]], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x86, 0, 0, 2, 0, 0], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 4, 0, 0, 0, 0x84, 0, 0, 0], edit));

        // Comment blocks whose vendor, count or field length does not fit.
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x84, 0, 0, 2, 0, 0], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x84, 0, 0, 4, 9, 0, 0, 0], edit));
        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite([.. streamInfo, 0x84, 0, 0, 12, 0, 0, 0, 0, 1, 0, 0, 0, 0xFF, 0, 0, 0], edit));
    }

    [Fact]
    public void AnEmptyCommentBlockTakesTheEdit()
    {
        var old = Metadata(new FlacBuilder(), out _);
        var streamInfo = old[..(4 + 4 + 34)];
        streamInfo[4] = 0;

        var (fields, _, _) = Read(FlacTagWriter.Rewrite([.. streamInfo, 0x84, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0], new TagEdit { Disc = "2" }).Bytes);

        Assert.Equal("2", fields[MetadataKeys.Disc]);
    }

    [Fact]
    public void ABlockThatWouldPassTheFormatsLimitIsRefused()
    {
        var old = Metadata(new FlacBuilder { Comments = [new string('x', 15 << 20)] }, out _);

        Assert.Throws<TagWriteException>(() => FlacTagWriter.Rewrite(old, new TagEdit { Title = new string('y', 2 << 20) }));
    }
}
