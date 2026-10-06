using Rex.Media.Containers.Matroska;
using Rex.Media.IO;
using Rex.Media.Primitives;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Containers;

/// <summary>Matroska files written element by element, for the cases no muxer writes on request.</summary>
public sealed class MatroskaCraftedTests
{
    private const int Audio = 1;
    private const int Video = 2;

    private sealed class LiveSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public string Name => "live.mkv";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    internal static byte[] PcmTrack(int number = Audio, params byte[][] extra) => Element(Id.TrackEntry,
    [
        UInt(Id.TrackNumber, (ulong)number), UInt(Id.TrackType, 2), Text(Id.CodecId, "A_PCM/INT/LIT"),
        Element(Id.Audio, Float(Id.SamplingFrequency, 8000), UInt(Id.Channels, 1), UInt(Id.BitDepth, 16)),
        .. extra,
    ]);

    private static byte[] VideoTrack(params byte[][] extra) => Element(Id.TrackEntry,
    [
        UInt(Id.TrackNumber, Video), UInt(Id.TrackType, 1), Text(Id.CodecId, "V_VP9"),
        Element(Id.Video, UInt(Id.PixelWidth, 64), UInt(Id.PixelHeight, 32)),
        .. extra,
    ]);

    internal static byte[] Tracks(params byte[][] entries) => Element(Id.Tracks, entries);

    internal static byte[] Cluster(ulong time, params byte[][] children) => Element(Id.Cluster, [UInt(Id.Timestamp, time), .. children]);

    internal static byte[] Simple(int track, short relative, bool key, params byte[] payload) => Element(Id.SimpleBlock, Block(track, relative, key ? (byte)0x80 : (byte)0, 0, payload));

    internal static byte[] Mkv(params byte[][] segment) => File("matroska", segment);

    private static MatroskaDemuxer Open(byte[] file) => MatroskaDemuxerTests.Open(file);

    private static List<Packet> ReadAll(MatroskaDemuxer demuxer) => MatroskaDemuxerTests.ReadAll(demuxer);

    private static byte[] Filled(int length, int value) => Enumerable.Repeat((byte)value, length).ToArray();

    [Theory]
    [InlineData(1, new[] { 300, 2, 6 })]
    [InlineData(1, new[] { 255, 0 })]
    [InlineData(2, new[] { 4, 4, 4 })]
    [InlineData(3, new[] { 10, 300, 7, 9 })]
    [InlineData(3, new[] { 5, 6 })]
    public void LacedBlocksBecomeOnePacketPerFrame(int lacing, int[] sizes)
    {
        var frames = sizes.Select((size, i) => Filled(size, i + 1)).ToArray();
        var file = Mkv(Tracks(PcmTrack(Audio, UInt(Id.DefaultDuration, 1_000_000))), Cluster(0, Element(Id.SimpleBlock, Block(Audio, 0, 0x80, lacing, frames))));
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames, packets.Select(p => p.Data.Span.ToArray()));
        Assert.Equal(Enumerable.Range(0, frames.Length).Select(i => MediaTime.FromSeconds(i * 0.001)), packets.Select(p => p.Pts));
        Assert.All(packets, p => Assert.True(p.IsKeyframe));
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void FramesStillQueuedFromALacedBlockAreReleasedBySeekingOrClosing()
    {
        var file = Mkv(Tracks(PcmTrack()), Cluster(0, Element(Id.SimpleBlock, Block(Audio, 0, 0x80, 2, [1], [2], [3]))));
        using var demuxer = Open(file);

        demuxer.ReadPacket(CancellationToken.None)!.Dispose();
        demuxer.Seek(MediaTime.Zero, CancellationToken.None);
        using var first = demuxer.ReadPacket(CancellationToken.None)!;
        demuxer.Dispose();

        Assert.Equal(1, first.Data.Span[0]);
    }

    [Fact]
    public void BlockGroupsCarryKeyframesDurationsAndEndPadding()
    {
        var file = Mkv(
            Tracks(PcmTrack()),
            Cluster(
                10,
                Element(Id.BlockGroup, Element(Id.Block, Block(Audio, 0, 0, 1, [1, 2], [3, 4])), UInt(Id.BlockDuration, 4)),
                Element(Id.BlockGroup, Element(Id.Block, Block(Audio, 4, 0, 0, [5, 6])), UInt(Id.ReferenceBlock, 1), Raw(Id.DiscardPadding, 0x0F, 0x42, 0x40)),
                Element(Id.BlockGroup, Element(Id.Block, Block(Audio, 6, 0, 0, [7, 8])), Raw(Id.DiscardPadding, 0xFF)),
                Element(Id.BlockGroup, UInt(Id.BlockDuration, 1)),
                Element(Id.SimpleBlock, Block(Audio, 8, 0, 1, [9], [10]))));
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal([10.0, 12, 14, 16, 18], packets.Take(5).Select(p => p.Pts.Ticks / 10_000.0));
        Assert.Equal(MediaTime.FromSeconds(0.002), packets[0].Duration);
        Assert.Equal([true, true, false, true, false], packets.Take(5).Select(p => p.IsKeyframe));
        Assert.Equal([0, 0, 8, 0, 0], packets.Take(5).Select(p => p.DiscardSamples));
        Assert.Equal(MediaTime.Unknown, packets[^1].Pts);
        Assert.Equal(6, packets.Count);
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void StrippedHeadersArePutBackAndUndecodableEncodingsHideTheirTrack()
    {
        static byte[] Encoding(params byte[][] compression) => Element(Id.ContentEncodings, Element(Id.ContentEncoding, Element(Id.ContentCompression, compression)));
        var file = Mkv(
            Tracks(
                PcmTrack(1, Encoding(UInt(Id.ContentCompAlgo, 3), Raw(Id.ContentCompSettings, 0xFF, 0xFB))),
                PcmTrack(2, Encoding(UInt(Id.ContentCompAlgo, 3))),
                PcmTrack(3, Encoding(UInt(Id.ContentCompAlgo, 0))),
                PcmTrack(4, Encoding()),
                PcmTrack(5, Element(Id.ContentEncodings, Element(Id.ContentEncoding, UInt(0x5031, 0)))),
                PcmTrack(6, Element(Id.ContentEncodings, UInt(0xEC, 0)))),
            Cluster(0, [.. Enumerable.Range(1, 6).Select(t => Simple(t, 0, true, 0x90, (byte)t))]));
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal([1, 2], packets.Select(p => p.TrackId));
        Assert.Equal([0xFF, 0xFB, 0x90, 1], packets[0].Data.Span.ToArray());
        Assert.Equal([0x90, 2], packets[1].Data.Span.ToArray());
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void UnsizedClustersEndAtTheNextTopLevelElement()
    {
        var file = Mkv(
            Tracks(PcmTrack()),
            Unsized(Id.Cluster, UInt(Id.Timestamp, 0), Simple(Audio, 0, true, 1), UInt(0xEC, 0), Simple(Audio, 1, true, 2)),
            Element(Id.Cues),
            Unsized(Id.Cluster, UInt(Id.Timestamp, 5), Simple(Audio, 0, true, 3)));
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal([1, 2, 3], packets.Select(p => p.Data.Span[0]));
        Assert.Equal(MediaTime.FromSeconds(0.005), packets[2].Pts);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void ReadingStopsAtAnUnsizedElementThatIsNotACluster()
    {
        var file = Mkv(Tracks(PcmTrack()), Cluster(0, Simple(Audio, 0, true, 1)), Unsized(Id.Tags), Cluster(5, Simple(Audio, 0, true, 2)));
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal([1], packets.Select(p => p.Data.Span[0]));
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void BytesThatAreNoElementEndAClusterOrTheFile()
    {
        var file = Mkv(
            UInt(0xEC, 0),
            Tracks(PcmTrack(), VideoTrack()),
            Element(Id.Cluster, UInt(Id.Timestamp, 0), Simple(Audio, 0, true, 1), Simple(9, 0, true, 9), Raw(Id.SimpleBlock, 0x81, 0), [0x00, 0x00]),
            UInt(0xEC, 0),
            Cluster(5, Simple(Audio, 0, true, 2)),
            [0x00]);
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal([1, 2], packets.Select(p => p.Data.Span[0]));
        MatroskaDemuxerTests.Release(packets);
    }

    [Fact]
    public void AnUnsizedHeaderElementLeavesNothingToPlay()
    {
        using var demuxer = Open(Mkv(Tracks(PcmTrack()), Unsized(0xEC), Cluster(0, Simple(Audio, 0, true, 1))));

        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
        Assert.Single(demuxer.Info.Tracks);
    }

    public static TheoryData<byte[], string> Broken => new()
    {
        { [0x42, 0x86, 0x81, 0x01], "not EBML" },
        { Concat(Id(Id.Ebml), Size(5000), new byte[5000]), "impossible size" },
        { Concat(Id(Id.Ebml), [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]), "impossible size" },
        { Concat(Element(Id.Ebml), Element(Id.Cluster)), "no segment" },
        { Mkv(Element(Id.Info)), "no tracks" },
        { File("matroska", [Concat(Id(Id.Info), Size((64L << 20) + 1))], unsizedSegment: true), "larger than" },
        { [0x00], "malformed" },
        { [0x08, 0, 0, 0, 0], "malformed" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void BrokenHeadersAreRefused(byte[] file, string reason)
    {
        var error = Assert.Throws<MediaFormatException>(() => Open(file));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<byte[], string> BrokenBlocks => new()
    {
        { Unsized(Id.Cluster, Unsized(Id.SimpleBlock)), "unusable size" },
        { Unsized(Id.Cluster, Concat(Id(Id.SimpleBlock), Size((64L << 20) + 1))), "unusable size" },
        { Unsized(Id.Cluster, Concat(Id(Id.SimpleBlock), Size(100), [0x81, 0, 0])), "past the end" },
        { Cluster(0, Raw(Id.SimpleBlock, 0x81, 0, 0, 0x02)), "no frame count" },
        { Cluster(0, Raw(Id.SimpleBlock, 0x81, 0, 0, 0x02, 1, 255)), "inside its sizes" },
        { Cluster(0, Raw(Id.SimpleBlock, 0x81, 0, 0, 0x06, 1)), "inside its sizes" },
        { Cluster(0, Raw(Id.SimpleBlock, 0x81, 0, 0, 0x06, 2, 0x81)), "inside its sizes" },
        { Cluster(0, Raw(Id.SimpleBlock, 0x81, 0, 0, 0x02, 1, 200, 1, 2, 3)), "do not fit" },
    };

    [Theory]
    [MemberData(nameof(BrokenBlocks))]
    public void BrokenBlocksAreRefused(byte[] cluster, string reason)
    {
        using var demuxer = Open(File("matroska", [Tracks(PcmTrack()), cluster], unsizedSegment: true));

        var error = Assert.Throws<MediaFormatException>(() => demuxer.ReadPacket(CancellationToken.None));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Audio every 10 ms and video every 40 ms (a keyframe every 80), one cluster per 40 ms.</summary>
    private static byte[][] AudioAndVideoClusters() =>
    [
        .. Enumerable.Range(0, 4).Select(c => Cluster(
            (ulong)(c * 40),
            [
                Simple(Video, 0, c % 2 == 0, (byte)(100 + c)),
                .. Enumerable.Range(0, 4).Select(a => Simple(Audio, (short)(a * 10), true, (byte)((c * 4) + a))),
            ])),
    ];

    [Theory]
    [InlineData(0.1, 80, 8)]
    [InlineData(0.05, 80, 4)]
    [InlineData(0.0, 0, 0)]
    public void WithoutCuesSeeksWalkTheClustersAndWaitForAVideoKeyframe(double target, int videoMs, int firstAudio)
    {
        using var demuxer = Open(Mkv([Tracks(PcmTrack(), VideoTrack()), .. AudioAndVideoClusters()]));

        demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);
        var packets = ReadAll(demuxer);
        demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);
        var again = ReadAll(demuxer);

        var video = packets.First(p => p.TrackId == Video);
        Assert.True(video.IsKeyframe);
        Assert.Equal(MediaTime.FromSeconds(videoMs / 1000.0), video.Pts);
        Assert.Equal(firstAudio, packets.First(p => p.TrackId == Audio).Data.Span[0]);
        Assert.Equal(packets.Count, again.Count);
        MatroskaDemuxerTests.Release(packets);
        MatroskaDemuxerTests.Release(again);
    }

    [Fact]
    public void AClusterWithoutATimestampFirstIndexesAtZero()
    {
        var file = Mkv(Tracks(PcmTrack()), Element(Id.Cluster, Simple(Audio, 0, true, 1)), UInt(0xEC, 0), Cluster(50, Simple(Audio, 0, true, 2)));
        using var demuxer = Open(file);

        demuxer.Seek(MediaTime.FromSeconds(0.02), CancellationToken.None);

        Assert.Equal([1, 2], ReadAll(demuxer).Select(p => p.Data.Span[0]));
    }

    private static byte[] Cue(ulong time, int? track, long? cluster, long relative = 0)
    {
        List<byte[]> positions = [];
        if (track is { } t)
        {
            positions.Add(UInt(Id.CueTrack, (ulong)t));
        }

        if (cluster is { } c)
        {
            positions.Add(UInt(Id.CueClusterPosition, (ulong)c));
        }

        if (relative > 0)
        {
            positions.Add(UInt(Id.CueRelativePosition, (ulong)relative));
        }

        return Element(Id.CuePoint, UInt(Id.CueTime, time), Element(Id.CueTrackPositions, [.. positions]));
    }

    /// <summary>A file with its cues after the clusters, found through a seek head as muxers write them.</summary>
    private static byte[] WithCues(byte[] tracks, byte[][] clusters, Func<long[], byte[]> cues)
    {
        static byte[] Head(long at) => Element(Id.SeekHead, Element(Id.Seek, UInt(Id.SeekId, Id.Cues), UInt64(Id.SeekPosition, (ulong)at)));
        var offset = (long)Head(0).Length + tracks.Length;
        var positions = new long[clusters.Length];
        for (var i = 0; i < clusters.Length; i++)
        {
            positions[i] = offset;
            offset += clusters[i].Length;
        }

        return Mkv([Head(offset), tracks, .. clusters, cues(positions)]);
    }

    [Fact]
    public void CuesPointAtTheClusterAndTheBlockInside()
    {
        // Cluster 2 holds the 80 ms keyframe first; a relative position names its third block.
        var thirdBlock = UInt(Id.Timestamp, 80).Length + Simple(Video, 0, true, 0).Length + Simple(Audio, 0, true, 0).Length;
        var file = WithCues(Tracks(PcmTrack(), VideoTrack()), AudioAndVideoClusters(), positions => Element(
            Id.Cues,
            UInt(0xEC, 0),
            Cue(0, Video, positions[0]),
            Cue(80, Video, positions[2], thirdBlock),
            Element(Id.CuePoint, Element(Id.CueTrackPositions, UInt(Id.CueClusterPosition, 0)), UInt(Id.CueTime, 999)),
            Cue(40, Video, null),
            Cue(160, null, positions[3])));
        using var demuxer = Open(file);

        demuxer.Seek(MediaTime.FromSeconds(0.1), CancellationToken.None);
        var packets = ReadAll(demuxer);

        Assert.Equal([9, 10, 11], packets.Where(p => p.TrackId == Audio).Take(3).Select(p => (int)p.Data.Span[0]));
        Assert.DoesNotContain(packets, p => p.Data.Span[0] == 102);
        MatroskaDemuxerTests.Release(packets);
    }

    [Theory]
    [InlineData("A_MPEG/L3", 0L, 0.5, 0.2)]
    [InlineData("A_AAC", 0L, 0.5, 0.4)]
    [InlineData("A_OPUS", 80_000_000L, 0.5, 0.4)]
    [InlineData("A_PCM/INT/LIT", 0L, 0.5, 0.5)]
    [InlineData("A_PCM/INT/LIT", 0L, 0.0, 0.0)]
    public void AudioSeeksStartAPreRollBeforeTheTarget(string codec, long seekPreRoll, double target, double cluster)
    {
        var track = Element(Id.TrackEntry, UInt(Id.TrackNumber, Audio), UInt(Id.TrackType, 2), Text(Id.CodecId, codec), UInt(Id.SeekPreRoll, (ulong)seekPreRoll));
        var clusters = Enumerable.Range(0, 10).Select(i => Cluster((ulong)(i * 100), Simple(Audio, 0, true, (byte)i))).ToArray();

        // The cues name another track, so every cue counts.
        var file = WithCues(Tracks(track), clusters, positions => Element(Id.Cues, [.. positions.Select((at, i) => Cue((ulong)(i * 100), 7, at))]));
        using var demuxer = Open(file);

        demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);

        using var first = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(MediaTime.FromSeconds(cluster), first.Pts);
    }

    [Fact]
    public void ABlockPositionInACueThatMissesItsClusterIsIgnored()
    {
        var file = WithCues(Tracks(PcmTrack()), [Cluster(0, Simple(Audio, 0, true, 1), Simple(Audio, 1, true, 2))], _ => Element(Id.Cues, Cue(0, Audio, 0, 5)));
        using var demuxer = Open(file);

        demuxer.Seek(MediaTime.Zero, CancellationToken.None);

        Assert.Equal([1, 2], ReadAll(demuxer).Select(p => p.Data.Span[0]));
    }

    [Fact]
    public void HeadersAfterTheClustersAreFoundThroughTheSeekHead()
    {
        static byte[] Seek(uint id, long? position) => position is { } p
            ? Element(Id.Seek, UInt(Id.SeekId, id), UInt64(Id.SeekPosition, (ulong)p))
            : Element(Id.Seek, UInt(Id.SeekId, id));
        byte[] Head(long tags, long chapters, long tracks) => Element(
            Id.SeekHead,
            Seek(Id.Tags, tags),
            Seek(Id.Chapters, chapters),
            Seek(Id.Tracks, tracks),
            Seek(Id.Attachments, 1L << 40),
            Seek(Id.Cues, tracks),
            Seek(Id.Info, null),
            UInt(0xEC, 0));
        var tracks = Tracks(PcmTrack());
        var info = Element(Id.Info, Text(Id.Title, "Sungba"), Float(Id.Duration, 250), UInt(Id.TimestampScale, 2_000_000));
        var cluster = Cluster(0, Simple(Audio, 0, true, 1));
        var tags = Element(Id.Tags, Element(Id.Tag, Element(Id.SimpleTag, Text(Id.TagName, "ARTIST"), Text(Id.TagString, "Asake"))));
        var chapters = Unsized(Id.Chapters);
        var length = Head(0, 0, 0).Length;
        var head = Head(length + tracks.Length + info.Length + cluster.Length, length + tracks.Length + info.Length + cluster.Length + tags.Length, length);
        Assert.Equal(length, head.Length);
        var file = File("webm", [head, tracks, info, cluster, tags, chapters]);

        using var demuxer = Open(file);
        using var live = new MatroskaDemuxer(new LiveSource(file), CancellationToken.None);

        Assert.Equal("WebM", demuxer.Info.FormatName);
        Assert.Equal("Asake", demuxer.Info.Metadata["artist"]);
        Assert.Equal("Sungba", demuxer.Info.Metadata["title"]);
        Assert.Equal(MediaTime.FromSeconds(0.5), demuxer.Info.Duration);
        Assert.Equal(MediaTime.FromSeconds(0.5), demuxer.Info.Tracks[0].Duration);
        Assert.Empty(demuxer.Info.Chapters);
        Assert.True(demuxer.Info.IsSeekable);
        Assert.False(live.Info.IsSeekable);
        Assert.False(live.Info.Metadata.ContainsKey("artist"));
    }

    [Fact]
    public void AFileWithoutADocTypeOrDurationIsPlainMatroska()
    {
        var file = Concat(Element(Id.Ebml), Element(Id.Segment, Element(Id.Info, Text(Id.Title, "Lonely At The Top")), Tracks(PcmTrack())));

        using var demuxer = Open(file);

        Assert.Equal("Matroska", demuxer.Info.FormatName);
        Assert.Equal(MediaTime.Unknown, demuxer.Info.Duration);
        Assert.Equal("Lonely At The Top", demuxer.Info.Metadata["title"]);
    }
}
