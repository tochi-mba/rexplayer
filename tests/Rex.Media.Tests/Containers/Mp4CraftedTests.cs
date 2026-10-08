using System.Text;
using Rex.Media.Containers.Mp4;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using static Rex.Media.TestKit.Mp4Writer;

namespace Rex.Media.Tests.Containers;

/// <summary>MP4 and QuickTime structures encoders rarely write, built box by box.</summary>
public sealed class Mp4CraftedTests
{
    private static Mp4Demuxer Open(byte[] file) => new(new MemoryByteSource(file, "crafted.mp4"), CancellationToken.None);

    private static IReadOnlyList<byte[]> Samples(params int[] sizes) => [.. sizes.Select((size, i) => Enumerable.Repeat((byte)(i + 1), size).ToArray())];

    private static Mp4TrackSpec Audio(byte[] entry, IReadOnlyList<byte[]>? samples = null, int id = 1) =>
        new() { Id = id, SampleEntry = entry, Samples = samples ?? Samples(4, 4, 4, 4) };

    private static Mp4TrackSpec Video(byte[] entry, IReadOnlyList<byte[]>? samples = null, int id = 1) =>
        new() { Id = id, Handler = "vide", Timescale = 25, Duration = 1, SampleEntry = entry, Samples = samples ?? Samples(5, 5, 5) };

    private static TrackInfo Track(byte[] file) => Assert.Single(Open(file).Info.Tracks);

    private static List<Packet> ReadAll(Mp4Demuxer demuxer)
    {
        var packets = new List<Packet>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add(packet);
        }

        return packets;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void LongHeadersWideOffsetsCompactSizesAndAnEditWithAPause(int sizeBits)
    {
        var elst = Full("elst", 1, 0, U32(2), U64(500), U64(-1), U32(0x10000), U64(1000), U64(960), U32(0x10000));
        var track = new Mp4TrackSpec
        {
            Timescale = 48_000,
            Duration = 960,
            SampleEntry = AudioEntry("Opus", 2, 16, 48_000, Box("dOps", [0, 2, 1, 56, 0, 0, 0xBB, 0x80, 0, 0, 0])),
            Samples = Samples(3, 7, 15, 9, 1, 12),
            Version1Headers = true,
            WideOffsets = true,
            CompactSizeBits = sizeBits,
            EditList = elst,
            HandlerName = "Sound",
        };
        using var demuxer = Open(File([track], movieHeaderV1: true));

        var info = Assert.Single(demuxer.Info.Tracks);
        var packets = ReadAll(demuxer);

        Assert.Equal(CodecId.Opus, info.Codec);
        Assert.Equal(11, info.CodecPrivate.Length);
        Assert.Equal(("eng", "Sound"), (info.Language, info.Title));
        Assert.Equal(MediaTime.FromSeconds(1), info.Duration);
        Assert.Equal([3, 7, 15, 9, 1, 12], packets.Select(p => p.Data.Length));
        Assert.Equal(MediaTime.FromSamples(-960, 48_000), packets[0].Pts);
        Assert.Equal(Enumerable.Range(1, 6).Select(i => (byte)i), packets.Select(p => p.Data.Span[0]));
        packets.ForEach(p => p.Dispose());
    }

    public static TheoryData<string, CodecId, byte[]> VideoFormats => new()
    {
        { "hvc1", CodecId.Hevc, Box("hvcC", [1, 2, 3]) },
        { "av01", CodecId.Av1, Box("av1C", [0x81, 0, 0, 0]) },
        { "vp08", CodecId.Vp8, [] },
        { "vp09", CodecId.Vp9, Box("vpcC", [1, 0, 0, 0]) },
        { "jpeg", CodecId.Mjpeg, [] },
        { "s263", CodecId.H263, [] },
        { "vc-1", CodecId.Vc1, Box("dvc1", [1]) },
        { "mp4v", CodecId.Mpeg4Part2, Esds(0x20, [0, 0, 1, 0xB0]) },
        { "mp4v", CodecId.Mpeg2Video, Esds(0x61) },
        { "mp4v", CodecId.Mpeg1Video, Esds(0x6A) },
        { "mp4v", CodecId.Mjpeg, Esds(0x6C) },
        { "zzzz", CodecId.Unknown, [] },
    };

    [Theory]
    [MemberData(nameof(VideoFormats))]
    [Capability("FMT-C05")]
    public void VideoSampleEntriesNameTheirCodecs(string format, CodecId codec, byte[] child)
    {
        Assert.Equal(codec, Track(File([Video(VideoEntry(format, 64, 48, child))])).Codec);
    }

    [Fact]
    public void PixelAspectColourAndTheNominalRateAreRead()
    {
        var entry = VideoEntry("avc1", 720, 576, Box("avcC", [1]), Box("pasp", U32(16), U32(15)), Box("colr", Ascii("nclx"), U16(9), U16(16), U16(9), [0x80]), Box("colr", Ascii("prof"), new byte[8]));
        var video = Track(File([Video(entry)])).Video!;

        Assert.Equal(new Rational(16, 15), video.PixelAspect);
        Assert.Equal(new ColorInfo(ColorMatrix.Bt2020NonConstant, ColorTransfer.Pq, ColorPrimaries.Bt2020, true), video.Color);
        Assert.Equal(new Rational(25, 1), video.FrameRate);
        Assert.Null(Track(File([Video(VideoEntry("avc1", 64, 48), Samples(5))])).Video!.FrameRate);
        Assert.Equal(new Rational(1, 1), Track(File([Video(VideoEntry("avc1", 64, 48, Box("pasp", U32(0), U32(1))))])).Video!.PixelAspect);
    }

    [Theory]
    [InlineData(0x10000, 0, 0)]
    [InlineData(0, 0x10000, 90)]
    [InlineData(-0x10000, 0, 180)]
    [InlineData(0, -0x10000, 270)]
    public void TheDisplayMatrixGivesAClockwiseRotation(int a, int b, int rotation)
    {
        var track = Video(VideoEntry("avc1", 64, 48));
        var rotated = new Mp4TrackSpec { Handler = "vide", Timescale = 25, Duration = 1, SampleEntry = track.SampleEntry, Samples = track.Samples, Matrix = (a, b) };

        Assert.Equal(rotation, Track(File([rotated])).Video!.Rotation);
    }

    [Fact]
    public void ReorderedFramesSignedOffsetsAndSyncSamplesAreHonoured()
    {
        var track = new Mp4TrackSpec
        {
            Handler = "vide",
            Timescale = 25,
            Duration = 1,
            SampleEntry = VideoEntry("avc1", 64, 48, Box("avcC", [1])),
            Samples = Samples(9, 3, 4, 8, 2),
            CompositionOffsets = [1, 3, -1, 1, -1],
            SignedCompositionOffsets = true,
            SyncSamples = [1, 4],
            Enabled = false,
        };
        using var demuxer = Open(File([track]));

        Assert.False(demuxer.Info.Tracks[0].IsDefault);
        var packets = ReadAll(demuxer);
        Assert.Equal([1, 4, 1, 4, 3], packets.Select(p => (int)(p.Pts.TotalSeconds * 25 + 0.5)));
        Assert.Equal([true, false, false, true, false], packets.Select(p => p.IsKeyframe));
        packets.ForEach(p => p.Dispose());

        demuxer.Seek(MediaTime.FromSeconds(0.17), CancellationToken.None);
        using var first = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(8, first.Data.Length);
    }

    public static TheoryData<byte[], CodecId, SampleFormat, bool> AudioFormats => new()
    {
        { AudioEntry("twos", 1, 16, 8000), CodecId.Pcm, SampleFormat.S16, true },
        { AudioEntry("twos", 1, 8, 8000), CodecId.Pcm, SampleFormat.S8, false },
        { AudioEntry("raw ", 1, 8, 8000), CodecId.Pcm, SampleFormat.U8, false },
        { AudioEntry("in32", 1, 32, 8000), CodecId.Pcm, SampleFormat.S32, true },
        { AudioEntry("fl64", 1, 64, 8000), CodecId.Pcm, SampleFormat.F64, true },
        { AudioEntry("in24", 1, 24, 8000, Box("wave", Box("frma", Ascii("in24")), Box("enda", U16(1)))), CodecId.Pcm, SampleFormat.S24, false },
        { QuickTimeAudioEntry("sowt", 1, 1, 16, 8000), CodecId.Pcm, SampleFormat.S16, false },
        { QuickTimeAudioEntry("lpcm", 2, 1, 32, 8000, 1 | 2), CodecId.Pcm, SampleFormat.F32, true },
        { QuickTimeAudioEntry("lpcm", 2, 1, 64, 8000, 1), CodecId.Pcm, SampleFormat.F64, false },
        { QuickTimeAudioEntry("lpcm", 2, 1, 24, 8000, 0), CodecId.Pcm, SampleFormat.S24, false },
        { AudioEntry("ipcm", 1, 24, 8000, Full("pcmC", 0, 0, [1, 24])), CodecId.Pcm, SampleFormat.S24, false },
        { AudioEntry("fpcm", 1, 64, 8000, Full("pcmC", 0, 0, [0, 64])), CodecId.Pcm, SampleFormat.F64, true },
        { AudioEntry("fpcm", 1, 32, 8000, Full("pcmC", 0, 0, [0, 32])), CodecId.Pcm, SampleFormat.F32, true },
        { AudioEntry("ulaw", 1, 8, 8000), CodecId.Mulaw, SampleFormat.Unknown, false },
        { AudioEntry("alaw", 1, 8, 8000), CodecId.Alaw, SampleFormat.Unknown, false },
        { AudioEntry(".mp3", 2, 16, 44_100), CodecId.Mp3, SampleFormat.Unknown, false },
        { AudioEntry("ac-3", 2, 16, 48_000, Box("dac3", [0x10, 0x3D, 0xC0])), CodecId.Ac3, SampleFormat.Unknown, false },
        { AudioEntry("ec-3", 2, 16, 48_000, Box("dec3", [0, 0x20, 0x0F])), CodecId.Eac3, SampleFormat.Unknown, false },
        { AudioEntry("alac", 2, 16, 44_100, Full("alac", 0, 0, new byte[24])), CodecId.Alac, SampleFormat.Unknown, false },
        { AudioEntry("samr", 1, 16, 8000), CodecId.AmrNb, SampleFormat.Unknown, false },
        { AudioEntry("sawb", 1, 16, 16_000), CodecId.AmrWb, SampleFormat.Unknown, false },
        { AudioEntry("dtsc", 2, 16, 48_000), CodecId.Dts, SampleFormat.Unknown, false },
        { AudioEntry("zzzz", 2, 16, 48_000), CodecId.Unknown, SampleFormat.Unknown, false },
    };

    [Theory]
    [MemberData(nameof(AudioFormats))]
    public void AudioSampleEntriesNameTheirCodecAndStorage(byte[] entry, CodecId codec, SampleFormat format, bool bigEndian)
    {
        var track = Track(File([Audio(entry, Samples(8, 8))]));

        Assert.Equal(codec, track.Codec);
        Assert.Equal(format, track.Audio!.PcmFormat);
        Assert.Equal(bigEndian, track.Audio.BigEndian);
    }

    [Fact]
    public void QuickTimeVersionTwoTakesItsRateAndChannelsFromTheWideFields()
    {
        var audio = Track(File([Audio(QuickTimeAudioEntry("lpcm", 2, 6, 24, 96_000, 0), Samples(18, 18))])).Audio!;

        Assert.Equal((96_000, 6, 24), (audio.SampleRate, audio.Channels, audio.BitsPerSample));
        Assert.Equal(18, audio.BlockAlign);
    }

    [Fact]
    public void OldQuickTimeByteSizedSamplesAreTakenAsWholeFrames()
    {
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("sowt", 2, 16, 8000), Samples = Samples(1, 1, 1, 1), FixedSize = true, Duration = 1 };
        var file = File([track]);
        using var demuxer = Open([.. file, .. new byte[12]]);

        using var packet = demuxer.ReadPacket(CancellationToken.None)!;

        Assert.Equal(8, packet.Data.Length);
    }

    public static TheoryData<byte[], CodecId, int, int> ElementaryStreams => new()
    {
        { Esds(0x40, [0x12, 0x10]), CodecId.Aac, 44_100, 2 },
        { Esds(0x40, [0x12, 0x10], esFlags: 0xE0), CodecId.Aac, 44_100, 2 },
        { Esds(0x66, [0x13, 0x88]), CodecId.Aac, 22_050, 1 },
        { Esds(0x40, [0x17, 0x80, 0x2B, 0x11, 0x08]), CodecId.Aac, 22_050, 1 },
        { Esds(0x40, [0xF8, 0x26, 0xE0]), CodecId.Aac, 48_000, 8 },
        { Esds(0x40, [0x12]), CodecId.Aac, 48_000, 6 },
        { Esds(0x40, [0x17, 0x80]), CodecId.Aac, 48_000, 6 },
        { Esds(0x40, [0x12, 0x00]), CodecId.Aac, 44_100, 6 },
        { Esds(0x40, [0x12, 0x00 | (15 << 3)]), CodecId.Aac, 44_100, 6 },
        { Esds(0x69), CodecId.Mp3, 48_000, 6 },
        { Esds(0x6B), CodecId.Mp3, 48_000, 6 },
        { Esds(0xA5), CodecId.Ac3, 48_000, 6 },
        { Esds(0xA6), CodecId.Eac3, 48_000, 6 },
        { Esds(0xA9), CodecId.Dts, 48_000, 6 },
        { Esds(0xDD), CodecId.Vorbis, 48_000, 6 },
        { Esds(0x01), CodecId.Unknown, 48_000, 6 },
        { Full("esds", 0, 0, [0x09, 0x01, 0x00]), CodecId.Aac, 48_000, 6 },
        { Full("esds", 0, 0, Descriptor(0x03, [0, 1, 0, 0x09, 0])), CodecId.Aac, 48_000, 6 },
    };

    [Theory]
    [MemberData(nameof(ElementaryStreams))]
    public void TheElementaryStreamDescriptorNamesTheCodecAndConfiguresAac(byte[] esds, CodecId codec, int rate, int channels)
    {
        var audio = Track(File([Audio(AudioEntry("mp4a", 6, 16, 48_000, esds))]));

        Assert.Equal(codec, audio.Codec);
        Assert.Equal((rate, channels), (audio.Audio!.SampleRate, audio.Audio.Channels));
    }

    [Theory]
    [InlineData("text", "tx3g", CodecId.MovText)]
    [InlineData("sbtl", "text", CodecId.MovText)]
    [InlineData("subt", "wvtt", CodecId.WebVtt)]
    [InlineData("clcp", "c608", CodecId.Cea608)]
    [InlineData("subt", "stpp", CodecId.Unknown)]
    public void TextTracksNameTheirFormats(string handler, string format, CodecId codec)
    {
        var track = new Mp4TrackSpec { Handler = handler, Timescale = 1000, Duration = 500, SampleEntry = Box(format, new byte[8]), Samples = Samples(4) };

        var info = Track(File([track]));

        Assert.Equal(codec, info.Codec);
        Assert.Null(info.Audio);
        Assert.Null(info.Video);
    }

    [Fact]
    public void TracksMissingTheirHeadersOrTablesAreLeftOut()
    {
        var good = Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2), id: 3);
        var noHeader = Audio(AudioEntry("sowt", 1, 16, 8000), id: 1);
        var tracks = new[]
        {
            new Mp4TrackSpec { Id = 1, SampleEntry = noHeader.SampleEntry, OmitMediaHeader = true },
            new Mp4TrackSpec { Id = 2, SampleEntry = noHeader.SampleEntry, OmitSampleTable = true },
            good,
        };

        Assert.Equal([3], Open(File(tracks)).Info.Tracks.Select(t => t.Id));
    }

    [Fact]
    public void EntriesTooShortToHoldTheirFieldsDescribeNothing()
    {
        Assert.Null(Track(File([Audio(Box("sowt", new byte[12]))])).Audio);
        Assert.Null(Track(File([Video(Box("avc1", new byte[12]))])).Video);
        var empty = new Mp4TrackSpec { SampleEntry = [], Samples = Samples(2) };
        Assert.Equal(CodecId.Unknown, Track(File([empty])).Codec);
    }

    [Fact]
    public void ADecodeTimeTableShorterThanTheSamplesHoldsItsLastTime()
    {
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 48_000), Samples = Samples(3, 3, 3), Durations = [960], Timescale = 48_000 };
        using var demuxer = Open(File([track]));

        var packets = ReadAll(demuxer);

        Assert.Equal([MediaTime.Zero, MediaTime.Zero, MediaTime.Zero], packets.Select(p => p.Pts));
        packets.ForEach(p => p.Dispose());
    }

    [Fact]
    public void TagsOfEveryKindAndAnItunesGaplessTagAreRead()
    {
        static byte[] Item(string type, int dataType, byte[] value) =>
            Box(type, Box("data", U32(dataType), U32(0), value));

        var mark = Encoding.Latin1.GetString([0xA9]);
        var ilst = Box("ilst",
            Item(mark + "nam", 1, Ascii("Mogbe")),
            Item(mark + "cmt", 1, Ascii("  ")),
            Item("trkn", 0, [0, 0, 0, 3, 0, 12, 0, 0]),
            Item("disk", 0, [0, 0, 0, 1, 0, 0]),
            Item("gnre", 0, U16(18)),
            Item("covr", 13, [0xFF, 0xD8]),
            Box("----", Box("mean", U32(0), Ascii("com.apple.iTunes")), Box("name", U32(0), Ascii("iTunSMPB")), Box("data", U32(1), U32(0), Ascii(" 00000000 00000840 000001CA 0000000000001F40"))),
            Box("----", Box("mean", U32(0), Ascii("com.apple.iTunes")), Box("data", U32(1), U32(0), Ascii("nameless"))),
            Box("aART"),
            Item("zzzz", 1, Ascii("ignored")));
        var udta = Box("udta", Full("meta", 0, 0, Full("hdlr", 0, 0, U32(0), Ascii("mdir"), new byte[12], [0]), ilst));
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("sowt", 1, 16, 44_100), Samples = Samples(2, 2), Timescale = 44_100, Duration = 1 };
        using var demuxer = Open(File([track], [udta]));
        var metadata = demuxer.Info.Metadata;

        Assert.Equal("Mogbe", metadata[MetadataKeys.Title]);
        Assert.False(metadata.ContainsKey(MetadataKeys.Comment));
        Assert.Equal("3/12", metadata[MetadataKeys.Track]);
        Assert.Equal("1", metadata[MetadataKeys.Disc]);
        Assert.Equal("Rock", metadata[MetadataKeys.Genre]);
        Assert.Equal([0xFF, 0xD8], demuxer.Info.CoverArt);
        Assert.StartsWith("00000000 00000840", metadata["itunsmpb"], StringComparison.Ordinal);
        var audio = demuxer.Info.Tracks[0];
        Assert.Equal(0x840, audio.Audio!.LeadingPadding);
        Assert.Equal(MediaTime.FromSamples(0x1F40, 44_100), audio.Duration);
    }

    [Theory]
    [InlineData(" 00000000 00000840")]
    [InlineData(" 00000000 ZZ 000001CA 0000000000001F40")]
    [InlineData(" 00000000 00000840 ZZ 0000000000001F40")]
    [InlineData(" 00000000 00000840 000001CA ZZ")]
    public void AnUnreadableGaplessTagIsIgnored(string value)
    {
        Assert.Null(Mp4Metadata.Gapless(new Dictionary<string, string> { ["itunsmpb"] = value }));
        Assert.Null(Mp4Metadata.Gapless(new Dictionary<string, string>()));
    }

    [Fact]
    public void GenresOutsideTheNumberedListAreLeftOut()
    {
        var ilst = Box("ilst", Box("gnre", Box("data", U32(0), U32(0), U16(0))), Box("gnre", Box("data", U32(0), U32(0), U16(200))), Box("trkn", Box("data", U32(0), U32(0), [0, 0, 0, 0, 0, 0])));
        var metadata = new Dictionary<string, string>();

        Mp4Metadata.ReadItems(ilst.AsSpan(8), metadata);

        Assert.Empty(metadata);
    }

    [Fact]
    public void QuickTimeMetaWithoutAFullBoxHeaderIsReadToo()
    {
        var mark = Encoding.Latin1.GetString([0xA9]);
        var meta = Box("meta", Full("hdlr", 0, 0, U32(0), Ascii("mdta"), new byte[12], [0]), Box("ilst", Box(mark + "ART", Box("data", U32(1), U32(0), Ascii("Asake")))));
        var track = Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2));

        Assert.Equal("Asake", Open(File([track], [meta])).Info.Metadata[MetadataKeys.Artist]);
    }

    [Fact]
    public void NeroChapterListsInBothVersionsAreRead()
    {
        static byte[] Entry(long start, string title) => Concat(U64(start), U8(title.Length), Ascii(title));
        var version0 = Box("udta", Full("chpl", 0, 0, U8(2), Entry(0, "One"), Entry(10_000_000, "Two")));
        var version1 = Box("udta", Full("chpl", 1, 0, U32(0), U8(1), Entry(5_000_000, "Half")));
        var track = Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2));

        Assert.Equal(["One", "Two"], Open(File([track], [version0])).Info.Chapters.Select(c => c.Title));
        var half = Assert.Single(Open(File([track], [version1])).Info.Chapters);
        Assert.Equal(MediaTime.FromSeconds(0.5), half.Start);
        Assert.Empty(Mp4Metadata.ReadChapterList([1, 0, 0, 0]));
        Assert.Empty(Mp4Metadata.ReadChapterList([1, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Empty(Mp4Metadata.ReadChapterList(Concat([0, 0, 0, 0], U8(1), U64(0), U8(10), Ascii("cut"))));
    }

    [Fact]
    public void AChapterTextTrackGivesTheChaptersAndIsNotATrack()
    {
        static byte[] Title(string text) => Concat(U16(text.Length), Ascii(text), Box("encd", U32(0x100)));
        var video = Video(VideoEntry("avc1", 64, 48), Samples(5, 5, 5, 5));
        var withChapters = new Mp4TrackSpec { Id = 1, Handler = "vide", Timescale = 25, Duration = 1, SampleEntry = video.SampleEntry, Samples = video.Samples, ChapterTrack = 2 };
        var chapters = new Mp4TrackSpec { Id = 2, Handler = "text", Timescale = 25, Duration = 2, SampleEntry = Box("text", new byte[8]), Samples = [Title("Opening"), [1], Title("Close")], SamplesPerChunk = 1 };

        using var demuxer = Open(File([withChapters, chapters]));

        Assert.Single(demuxer.Info.Tracks);
        Assert.Equal([new Chapter(MediaTime.Zero, "Opening"), new Chapter(MediaTime.FromSeconds(0.16), "Close")], demuxer.Info.Chapters);
    }

    [Fact]
    public void AMovieLengthBeyondAnyClockGivesWayToTheTracks()
    {
        var file = File([Audio(AudioEntry("sowt", 1, 16, 8000), Samples(4, 4))], movieHeaderV1: true, movieTimescale: 1);
        using var normal = Open(file);
        // The movie header's 64-bit duration, after its version, the creation and change times and the timescale.
        U64(long.MaxValue / 2).CopyTo(file, Encoding.Latin1.GetString(file).IndexOf("mvhd", StringComparison.Ordinal) + 4 + 4 + 8 + 8 + 4);

        using var demuxer = Open(file);

        Assert.True(normal.Info.Duration > MediaTime.Zero);
        Assert.Equal(normal.Info.Duration, demuxer.Info.Duration);
    }

    [Fact]
    public void ASampleTimeBeyondAnyClockRefusesTheFile()
    {
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 8000), Timescale = 1, Samples = [] };
        var fragments = new[] { new Mp4Fragment(1, Samples(4)) { BaseMediaDecodeTime = long.MaxValue / 2, Version1DecodeTime = true } };

        var error = Assert.Throws<MediaFormatException>(() =>
        {
            using var demuxer = Open(FragmentedFile([track], fragments));
            ReadAll(demuxer);
        });
        Assert.Contains("later than rexplayer can count to", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentsWithDefaultsBaseOffsetsAndSampleFlagsAreIndexed()
    {
        var trex = Full("trex", 0, 0, U32(1), U32(1), U32(160), U32(6), U32(0));
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 8000), Samples = [] };
        var video = new Mp4TrackSpec { Id = 2, Handler = "vide", Timescale = 25, SampleEntry = VideoEntry("avc1", 64, 48), Samples = [] };
        var fragments = new[]
        {
            // Durations and sizes from trex; the data at the stated base offset.
            new Mp4Fragment(1, Samples(6, 6)) { HeaderFlags = 0x1, RunFlags = 0 },
            // Defaults in the header, a first sample marked sync and the rest not, a v1 decode time.
            new Mp4Fragment(2, Samples(4, 4, 4), 1) { HeaderFlags = 0x8 | 0x10 | 0x20 | 0x20000, DefaultFlags = 0x10000, RunFlags = 0x1 | 0x4, FirstSampleFlags = 0, BaseMediaDecodeTime = 10, Version1DecodeTime = true },
            // Per-sample flags and signed composition offsets, then a second traf that follows on.
            new Mp4Fragment(2, Samples(3, 3), 1)
            {
                RunFlags = 0x1 | 0x100 | 0x200 | 0x400 | 0x800,
                SampleFlags = [0, 0x10000],
                Compositions = [1, -1],
                SignedCompositions = true,
                BaseMediaDecodeTime = 13,
                Next = new Mp4Fragment(1, Samples(2)) { HeaderFlags = 0, RunFlags = 0x1 | 0x100 | 0x200 },
            },
            // A traf for a track that does not exist and one with no header.
            new Mp4Fragment(9, Samples(1)) { Next = new Mp4Fragment(-1, Samples(1)) },
        };
        using var demuxer = Open(FragmentedFile([track, video], fragments, trex));

        var packets = ReadAll(demuxer);
        var audio = packets.Where(p => p.TrackId == 1).ToList();
        var frames = packets.Where(p => p.TrackId == 2).ToList();

        Assert.Equal([6, 6, 2], audio.Select(p => p.Data.Length));
        Assert.Equal([MediaTime.Zero, MediaTime.FromSamples(160, 8000), MediaTime.FromSamples(320, 8000)], audio.Select(p => p.Pts));
        Assert.Equal([4, 4, 4, 3, 3], frames.Select(p => p.Data.Length));
        Assert.Equal([true, false, false, true, false], frames.Select(p => p.IsKeyframe));
        Assert.Equal([10, 11, 12, 14, 13], frames.Select(p => (int)(p.Pts.TotalSeconds * 25 + 0.5)));
        packets.ForEach(p => p.Dispose());
    }

    [Fact]
    public void AFragmentClaimingTooManySamplesIsRejected()
    {
        var track = new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 8000), Samples = [] };
        var file = FragmentedFile([track], [new Mp4Fragment(1, Samples(1))]);
        var trun = Encoding.Latin1.GetString(file).LastIndexOf("trun", StringComparison.Ordinal);
        U32(60_000_000).CopyTo(file, trun + 8);

        Assert.Throws<MediaFormatException>(() => Open(file));
    }

    [Fact]
    public void BoxesWithLargeSizesSizeZeroAndUuidsAreWalked()
    {
        var track = Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2));
        var normal = File([track], [Box("uuid", new byte[16], [1, 2]), Concat(U32(1), Ascii("free"), U64(16)), Concat(U32(0), Ascii("skip"), [9])]);
        var mdat = Encoding.Latin1.GetString(normal).LastIndexOf("mdat", StringComparison.Ordinal);
        U32(0).CopyTo(normal, mdat - 4);
        var largeFree = Concat(U32(1), Ascii("free"), U64(24), new byte[8]);

        using var demuxer = Open([.. normal]);
        using var withLargeFree = Open([.. largeFree, .. normal[..(mdat - 4)]]);

        Assert.Equal(4, ReadAll(demuxer).Sum(p => p.Data.Length));
        Assert.Single(withLargeFree.Info.Tracks);
        Assert.Equal(60, new Mp4DemuxerFactory().Probe(Concat(U32(8), Ascii("skip")), null));
    }

    [Fact]
    public void BrokenFilesAreRejectedOrEndCleanly()
    {
        var track = Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2));
        var good = File([track]);
        var text = Encoding.Latin1.GetString(good);
        var stsz = text.IndexOf("stsz", StringComparison.Ordinal);

        var noMoov = Concat(Box("ftyp", Ascii("isom"), U32(0)), Box("mdat", [1, 2, 3]));
        var hugeMoov = Concat(U32(300_000_000), Ascii("moov"));
        var tooManyEntries = (byte[])good.Clone();
        U32(1000).CopyTo(tooManyEntries, stsz + 12);
        var tooManySamples = (byte[])good.Clone();
        U32(60_000_000).CopyTo(tooManySamples, stsz + 12);
        var hugeSample = File([new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 8000), Samples = Samples(2), FixedSize = true }]);
        U32(100_000_000).CopyTo(hugeSample, Encoding.Latin1.GetString(hugeSample).IndexOf("stsz", StringComparison.Ordinal) + 8);
        var compact = File([new Mp4TrackSpec { SampleEntry = AudioEntry("Opus", 1, 16, 8000), Samples = Samples(2), CompactSizeBits = 8 }]);
        compact[Encoding.Latin1.GetString(compact).IndexOf("stz2", StringComparison.Ordinal) + 11] = 3;
        var truncatedHeader = Concat(good, U32(1), Ascii("free"));
        var tinyBox = Concat(good, U32(4), Ascii("free"));

        Assert.Throws<MediaFormatException>(() => Open(noMoov));
        Assert.Throws<MediaFormatException>(() => Open(hugeMoov));
        Assert.Throws<MediaFormatException>(() => Open(tooManyEntries));
        Assert.Throws<MediaFormatException>(() => Open(tooManySamples));
        Assert.Throws<MediaFormatException>(() => Open(compact));
        Assert.Throws<MediaFormatException>(() => Open(hugeSample).ReadPacket(CancellationToken.None));
        Assert.Throws<MediaFormatException>(() => Open(good[..^3]).ReadPacket(CancellationToken.None));
        Assert.Single(Open(truncatedHeader).Info.Tracks);
        Assert.Single(Open(tinyBox).Info.Tracks);
        Assert.Throws<ArgumentNullException>(() => new Mp4Demuxer(null!, CancellationToken.None));
    }

    [Fact]
    public void AnEditListOfOnlyAPauseLeavesTheTimelineAlone()
    {
        var track = new Mp4TrackSpec
        {
            SampleEntry = AudioEntry("Opus", 1, 16, 8000),
            Samples = Samples(3, 3),
            EditList = Full("elst", 0, 0, U32(1), U32(500), U32(-1), U32(0x10000)),
        };
        using var demuxer = Open(File([track]));

        using var packet = demuxer.ReadPacket(CancellationToken.None)!;

        Assert.Equal(MediaTime.Zero, packet.Pts);
        Assert.Equal(MediaTime.FromSamples(320, 8000), demuxer.Info.Tracks[0].Duration);
    }

    [Fact]
    public void ASampleTableWithoutSizesHasNoSamples()
    {
        var file = File([Audio(AudioEntry("Opus", 1, 16, 8000), Samples(3, 3))]);
        Ascii("zzzz").CopyTo(file, Encoding.Latin1.GetString(file).IndexOf("stsz", StringComparison.Ordinal));
        using var demuxer = Open(file);

        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
    }

    [Fact]
    public void SeekingAFileWithoutSamplesOrWithEmptyTracksIsHarmless()
    {
        var empty = File([new Mp4TrackSpec { SampleEntry = AudioEntry("sowt", 1, 16, 8000) }]);
        var mixed = File([new Mp4TrackSpec { Id = 1, Handler = "vide", SampleEntry = VideoEntry("avc1", 64, 48) }, Audio(AudioEntry("sowt", 1, 16, 8000), Samples(2, 2), id: 2)]);
        using var nothing = Open(empty);
        using var audioOnly = Open(mixed);

        nothing.Seek(MediaTime.FromSeconds(1), CancellationToken.None);
        audioOnly.Seek(MediaTime.Zero, CancellationToken.None);

        Assert.Null(nothing.ReadPacket(CancellationToken.None));
        using var packet = audioOnly.ReadPacket(CancellationToken.None)!;
        Assert.Equal(2, packet.TrackId);
        Assert.False(Open(File([], movieHeaderV1: false)).Info.Duration > MediaTime.Zero);
    }
}
