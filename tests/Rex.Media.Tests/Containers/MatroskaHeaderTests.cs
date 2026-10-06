using System.Buffers.Binary;
using System.Text;
using Rex.Media.Containers.Matroska;
using Rex.Media.Primitives;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Containers;

/// <summary>Track entries, chapters, tags, attachments and the EBML reader underneath them.</summary>
public sealed class MatroskaHeaderTests
{
    private static MediaInfo Describe(params byte[][] segment)
    {
        using var demuxer = MatroskaDemuxerTests.Open(MatroskaCraftedTests.Mkv(segment));
        return demuxer.Info;
    }

    private static TrackInfo Track(params byte[][] children) => Describe(MatroskaCraftedTests.Tracks(Element(Id.TrackEntry, [UInt(Id.TrackNumber, 1), .. children]))).Tracks[0];

    private static TrackInfo AudioTrack(string codec, params byte[][] children) => Track([UInt(Id.TrackType, 2), Text(Id.CodecId, codec), .. children]);

    [Theory]
    [InlineData("A_AAC/MPEG4/LC", CodecId.Aac)]
    [InlineData("A_FLAC", CodecId.Flac)]
    [InlineData("A_MPEG/L3", CodecId.Mp3)]
    [InlineData("A_MPEG/L2", CodecId.Mp2)]
    [InlineData("A_MPEG/L1", CodecId.Mp1)]
    [InlineData("A_AC3/BSID9", CodecId.Ac3)]
    [InlineData("A_EAC3", CodecId.Eac3)]
    [InlineData("A_DTS", CodecId.Dts)]
    [InlineData("A_TRUEHD", CodecId.TrueHd)]
    [InlineData("A_VORBIS", CodecId.Vorbis)]
    [InlineData("A_OPUS", CodecId.Opus)]
    [InlineData("A_ALAC", CodecId.Alac)]
    [InlineData("A_PCM/FLOAT/IEEE", CodecId.Pcm)]
    [InlineData("A_MS/ACM", CodecId.Unknown)]
    [InlineData("V_MPEG4/ISO/AVC", CodecId.H264)]
    [InlineData("V_MPEGH/ISO/HEVC", CodecId.Hevc)]
    [InlineData("V_AV1", CodecId.Av1)]
    [InlineData("V_VP8", CodecId.Vp8)]
    [InlineData("V_VP9", CodecId.Vp9)]
    [InlineData("V_MPEG1", CodecId.Mpeg1Video)]
    [InlineData("V_MPEG2", CodecId.Mpeg2Video)]
    [InlineData("V_MPEG4/ISO/ASP", CodecId.Mpeg4Part2)]
    [InlineData("V_MJPEG", CodecId.Mjpeg)]
    [InlineData("V_THEORA", CodecId.Theora)]
    [InlineData("V_UNCOMPRESSED", CodecId.RawVideo)]
    [InlineData("V_MS/VFW/FOURCC", CodecId.Unknown)]
    [InlineData("S_TEXT/UTF8", CodecId.SubRip)]
    [InlineData("S_TEXT/ASS", CodecId.Ass)]
    [InlineData("S_TEXT/SSA", CodecId.Ssa)]
    [InlineData("S_TEXT/WEBVTT", CodecId.WebVtt)]
    [InlineData("S_VOBSUB", CodecId.VobSub)]
    [InlineData("S_HDMV/PGS", CodecId.Pgs)]
    [InlineData("S_DVBSUB", CodecId.DvbSubtitle)]
    [InlineData("X_SOMETHING", CodecId.Unknown)]
    public void CodecIdsNameTheirCodecs(string name, CodecId codec)
    {
        Assert.Equal(codec, Track(UInt(Id.TrackType, 17), Text(Id.CodecId, name)).Codec);
    }

    [Fact]
    public void ACompatibilityAudioTrackIsReadFromItsWaveFormat()
    {
        byte[] format = [0x55, 0, 2, 0, 0x44, 0xAC, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0];

        var track = AudioTrack("A_MS/ACM", Raw(Id.CodecPrivate, format));

        Assert.Equal(CodecId.Mp3, track.Codec);
        Assert.Equal(format, track.CodecPrivate);
    }

    [Theory]
    [InlineData("H264", CodecId.H264)]
    [InlineData("avc1", CodecId.H264)]
    [InlineData("HEVC", CodecId.Hevc)]
    [InlineData("XVID", CodecId.Mpeg4Part2)]
    [InlineData("MJPG", CodecId.Mjpeg)]
    [InlineData("WMV3", CodecId.Wmv3)]
    [InlineData("WVC1", CodecId.Vc1)]
    [InlineData("VP80", CodecId.Vp8)]
    [InlineData("VP90", CodecId.Vp9)]
    [InlineData("ABCD", CodecId.Unknown)]
    public void ACompatibilityVideoTrackIsReadFromItsFourCc(string fourCc, CodecId codec)
    {
        var header = new byte[40];
        Encoding.ASCII.GetBytes(fourCc).CopyTo(header, 16);

        Assert.Equal(codec, Track(UInt(Id.TrackType, 1), Text(Id.CodecId, "V_MS/VFW/FOURCC"), Raw(Id.CodecPrivate, header)).Codec);
    }

    private static byte[] StreamInfo(int bits)
    {
        var info = new byte[34];
        info[12] = (byte)((bits - 1) >> 4);
        info[13] = (byte)(((bits - 1) & 0xF) << 4);
        return info;
    }

    public static TheoryData<byte[], int> FlacPrivates => new()
    {
        { Concat("fLaC"u8.ToArray(), [4, 0, 0, 2, 9, 9], [0x80, 0, 0, 34], StreamInfo(24)), 24 },
        { Concat([0, 0, 0, 34], StreamInfo(16)), 16 },
        { Concat("fLaC"u8.ToArray(), [4, 0, 0, 2, 9, 9]), 0 },
        { Concat("fLaC"u8.ToArray(), [0x80, 0, 0, 34], new byte[10]), 0 },
    };

    [Theory]
    [MemberData(nameof(FlacPrivates))]
    public void FlacTracksHandTheirDecoderTheStreamInfo(byte[] codecPrivate, int bits)
    {
        var track = AudioTrack("A_FLAC", Raw(Id.CodecPrivate, codecPrivate));

        Assert.Equal(bits == 0 ? 0 : 34, track.CodecPrivate.Length);
        Assert.Equal(bits, track.Audio!.BitsPerSample);
    }

    [Theory]
    [InlineData("A_PCM/INT/LIT", 8, SampleFormat.U8, false)]
    [InlineData("A_PCM/INT/LIT", 16, SampleFormat.S16, false)]
    [InlineData("A_PCM/INT/LIT", 24, SampleFormat.S24, false)]
    [InlineData("A_PCM/INT/LIT", 32, SampleFormat.S32, false)]
    [InlineData("A_PCM/INT/BIG", 8, SampleFormat.U8, true)]
    [InlineData("A_PCM/INT/BIG", 16, SampleFormat.S16, true)]
    [InlineData("A_PCM/INT/BIG", 24, SampleFormat.S24, true)]
    [InlineData("A_PCM/INT/BIG", 32, SampleFormat.S32, true)]
    [InlineData("A_PCM/FLOAT/IEEE", 32, SampleFormat.F32, false)]
    [InlineData("A_PCM/FLOAT/IEEE", 64, SampleFormat.F64, false)]
    [InlineData("A_OPUS", 0, SampleFormat.Unknown, false)]
    public void PcmTracksSayHowTheirSamplesAreStored(string codec, int bits, SampleFormat format, bool bigEndian)
    {
        var audio = AudioTrack(codec, Element(Id.Audio, Float32(Id.SamplingFrequency, 22_050), UInt(Id.Channels, 2), UInt(Id.BitDepth, (ulong)bits))).Audio!;

        Assert.Equal((format, bigEndian), (audio.PcmFormat, audio.BigEndian));
        Assert.Equal((22_050, 2, bits), (audio.SampleRate, audio.Channels, audio.BitsPerSample));
        Assert.Equal(format == SampleFormat.Unknown ? 0 : new AudioFormat(22_050, 2, format).BlockAlign, audio.BlockAlign);
        Assert.Equal(format == SampleFormat.Unknown ? 0 : 1, audio.SamplesPerBlock);
    }

    [Fact]
    public void AudioTracksCarryTheirFlagsNamesLanguagesAndDelay()
    {
        var track = AudioTrack(
            "A_OPUS",
            UInt(Id.FlagEnabled, 0),
            UInt(Id.FlagDefault, 1),
            UInt(Id.FlagForced, 1),
            Text(Id.Name, "Commentary"),
            Text(Id.LanguageBcp47, "yo-NG"),
            UInt(Id.CodecDelay, 6_500_000),
            Element(Id.Audio, Float(Id.SamplingFrequency, 24_000), Float(Id.OutputSamplingFrequency, 48_000), UInt(0x7D7B, 0)));

        Assert.False(track.IsDefault);
        Assert.True(track.IsForced);
        Assert.Equal("Commentary", track.Title);
        Assert.Equal("yo-NG", track.Language);
        Assert.Equal(48_000, track.Audio!.SampleRate);
        Assert.Equal(312, track.Audio.LeadingPadding);
        Assert.Equal(1, track.Audio.Channels);
        Assert.Null(AudioTrack("A_OPUS", Text(Id.Language, "und")).Language);
        Assert.Null(AudioTrack("A_OPUS", Text(Id.Language, string.Empty)).Language);
        Assert.Equal("eng", AudioTrack("A_OPUS").Language);
    }

    [Fact]
    public void VideoTracksCarryTheirShapeRateAndColour()
    {
        var colour = Element(Id.Colour, UInt(Id.MatrixCoefficients, 9), UInt(Id.TransferCharacteristics, 16), UInt(Id.Primaries, 9), UInt(Id.Range, 2), UInt(0x55B2, 10));
        var video = Track(
            UInt(Id.TrackType, 1),
            Text(Id.CodecId, "V_VP9"),
            UInt(Id.DefaultDuration, 40_000_000),
            Element(Id.Video, UInt(Id.PixelWidth, 64), UInt(Id.PixelHeight, 32), UInt(Id.DisplayWidth, 128), UInt(Id.DisplayHeight, 32), UInt(0x9A, 1), colour)).Video!;

        Assert.Equal((64, 32), (video.Width, video.Height));
        Assert.Equal(new Rational(2, 1), video.PixelAspect);
        Assert.Equal(new Rational(25, 1), video.FrameRate);
        Assert.Equal((ColorPrimaries.Bt2020, ColorTransfer.Pq, ColorMatrix.Bt2020NonConstant, true), (video.Color.Primaries, video.Color.Transfer, video.Color.Matrix, video.Color.FullRange));

        var plain = Track(UInt(Id.TrackType, 1), Text(Id.CodecId, "V_VP9"), Element(Id.Video, UInt(Id.PixelWidth, 64), UInt(Id.PixelHeight, 32), Element(Id.Colour))).Video!;
        Assert.Equal(new Rational(1, 1), plain.PixelAspect);
        Assert.Null(plain.FrameRate);
        Assert.False(plain.Color.FullRange);
        Assert.Null(Track(UInt(Id.TrackType, 1), Text(Id.CodecId, "V_VP9"), Element(Id.Video, UInt(Id.PixelWidth, 64))).Video);
    }

    private static byte[] Atom(long startNs, params byte[][] children) => Element(Id.ChapterAtom, [UInt(Id.ChapterTimeStart, (ulong)startNs), .. children]);

    private static byte[] Display(string title) => Element(Id.ChapterDisplay, Text(Id.ChapString, title));

    [Fact]
    public void ChaptersComeFromTheDefaultEditionInTimeOrder()
    {
        var chapters = Element(
            Id.Chapters,
            UInt(0xEC, 0),
            Element(Id.EditionEntry, UInt(Id.EditionFlagDefault, 0), Atom(0, Display("Elsewhere"))),
            Element(
                Id.EditionEntry,
                UInt(Id.EditionFlagDefault, 1),
                UInt(0x45BC, 7),
                Atom(200_000_000, Display("Verse")),
                Atom(0, Element(Id.ChapterDisplay, Text(0x437C, "eng")), Display("Intro"), Display("Ignored"), Atom(100_000_000, Display("Hook"))),
                Atom(300_000_000, UInt(Id.ChapterFlagHidden, 1), Display("Hidden"), Atom(350_000_000, Display("Inside hidden"))),
                Atom(400_000_000, UInt(Id.ChapterFlagHidden, 0))));

        var info = Describe(chapters, MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()));

        Assert.Equal(["Intro", "Hook", "Verse", string.Empty], info.Chapters.Select(c => c.Title));
        Assert.Equal([0, 0.1, 0.2, 0.4], info.Chapters.Select(c => c.Start.TotalSeconds));
    }

    [Fact]
    public void WithoutADefaultEditionTheFirstIsUsed()
    {
        var chapters = Element(
            Id.Chapters,
            Element(Id.EditionEntry, UInt(Id.EditionFlagDefault, 0), Atom(0, Display("First"))),
            Element(Id.EditionEntry, Atom(0, Display("Second"))));

        Assert.Equal(["First"], Describe(chapters, MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack())).Chapters.Select(c => c.Title));
        Assert.Empty(Describe(Element(Id.Chapters, UInt(0xEC, 0)), MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack())).Chapters);
    }

    private static byte[] SimpleTag(string name, string? value) => value is null
        ? Element(Id.SimpleTag, Text(Id.TagName, name))
        : Element(Id.SimpleTag, Text(Id.TagName, name), Text(Id.TagString, value));

    [Fact]
    public void TagsForTheWholeFileBecomeMetadata()
    {
        var tags = Element(
            Id.Tags,
            UInt(0xEC, 0),
            Element(Id.Tag, Element(Id.Targets, UInt(Id.TargetTrackUid, 5)), SimpleTag("TITLE", "Track title")),
            Element(Id.Tag, Element(Id.Targets, UInt(0x68CA, 50)), SimpleTag("TITLE", "Lonely At The Top"), SimpleTag("ALBUM", "Lungu Boy"), SimpleTag("GENRE", string.Empty), SimpleTag("COMMENT", null), UInt(0xEC, 0)),
            Element(Id.Tag, SimpleTag("ARTIST", "Asake")));

        var metadata = Describe(tags, MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack())).Metadata;

        Assert.Equal("Lonely At The Top", metadata[MetadataKeys.Title]);
        Assert.Equal("Lungu Boy", metadata[MetadataKeys.Album]);
        Assert.Equal("Asake", metadata[MetadataKeys.Artist]);
        Assert.False(metadata.ContainsKey(MetadataKeys.Genre));
        Assert.False(metadata.ContainsKey(MetadataKeys.Comment));
    }

    private static byte[] Attached(string name, string type, byte[]? data) => data is null
        ? Element(Id.AttachedFile, Text(Id.FileName, name), Text(Id.FileMediaType, type))
        : Element(Id.AttachedFile, Text(Id.FileName, name), Text(Id.FileMediaType, type), Raw(Id.FileData, data));

    public static TheoryData<byte[][], byte[]?> Attachments => new()
    {
        { [Attached("notes.txt", "text/plain", [1]), Attached("back.png", "image/png", [2]), Attached("Cover.jpg", "image/jpeg", [3])], [3] },
        { [UInt(0xEC, 0), Attached("empty.png", "image/png", null), Attached("back.png", "image/png", [2]), Attached("front.png", "image/png", [4])], [2] },
        { [Attached("font.ttf", "font/ttf", [5]), Element(Id.AttachedFile, Raw(Id.FileData, 6))], null },
    };

    [Theory]
    [MemberData(nameof(Attachments))]
    public void TheCoverIsTheImageNamedLikeOneOrElseTheFirstImage(byte[][] files, byte[]? cover)
    {
        Assert.Equal(cover, Describe(Element(Id.Attachments, files), MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack())).CoverArt);
    }

    [Theory]
    [InlineData(0x80, 1)]
    [InlineData(0x40, 2)]
    [InlineData(0x10, 4)]
    [InlineData(0x01, 8)]
    [InlineData(0x00, 0)]
    public void AVariableIntegersLengthIsInItsFirstByte(byte first, int length)
    {
        Assert.Equal(length, Ebml.Length(first));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x08, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x1A, 0x45 })]
    public void IdsThatAreMissingTooLongOrCutShortAreNotRead(byte[] data)
    {
        var offset = 0;

        Assert.False(Ebml.TryReadId(data, ref offset, out _));
        Assert.Equal(0, offset);
    }

    [Theory]
    [InlineData(new byte[0], false, 0L)]
    [InlineData(new byte[] { 0x00 }, false, 0L)]
    [InlineData(new byte[] { 0x40 }, false, 0L)]
    [InlineData(new byte[] { 0x81 }, true, 1L)]
    [InlineData(new byte[] { 0x40, 0x02 }, true, 2L)]
    [InlineData(new byte[] { 0xFF }, true, -1L)]
    [InlineData(new byte[] { 0x7F, 0xFF }, true, -1L)]
    [InlineData(new byte[] { 0x7F, 0xFE }, true, 0x3FFEL)]
    public void SizesAreReadWithTheAllOnesValueMeaningUnknown(byte[] data, bool read, long size)
    {
        var offset = 0;

        Assert.Equal(read, Ebml.TryReadSize(data, ref offset, out var value));
        Assert.Equal(size, value);
        Assert.Equal(read ? data.Length : 0, offset);
    }

    [Fact]
    public void ChildrenAreCutAtTheirParentsEnd()
    {
        Assert.Equal([new EbmlElement(0xEC, 2, 3), new EbmlElement(0xEC, 5, 7)], Ebml.Children([0xEC, 0x81, 1, 0xEC, 0x88, 1, 2]));
        Assert.Equal([new EbmlElement(0xEC, 2, 4)], Ebml.Children([0xEC, 0xFF, 7, 8]));
        Assert.Equal([new EbmlElement(0xEC, 2, 3)], Ebml.Children([0xEC, 0x81, 1, 0x00, 0x81]));
        Assert.Null(Ebml.Find([0xEC, 0x81, 1], 0xA3));
    }

    [Fact]
    public void NumbersFloatsAndTextReadAsEbmlStoresThem()
    {
        var eight = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(eight, 0.25);
        var four = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(four, 1.5f);

        Assert.Equal(0x0102030405060708UL, Ebml.UInt([1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.Equal(0UL, Ebml.UInt([]));
        Assert.Equal(-1L, Ebml.Int([0xFF]));
        Assert.Equal(128L, Ebml.Int([0x00, 0x80]));
        Assert.Equal(0L, Ebml.Int([]));
        Assert.Equal(0.25, Ebml.Float(eight));
        Assert.Equal(1.5, Ebml.Float(four));
        Assert.Equal(0.0, Ebml.Float([1, 2]));
        Assert.Equal("abc", Ebml.Text("abc\0def"u8));
        Assert.Equal("Sungba", Ebml.Text("Sungba"u8));
    }
}
