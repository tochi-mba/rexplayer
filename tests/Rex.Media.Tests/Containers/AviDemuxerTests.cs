using System.Buffers.Binary;
using System.Text;
using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Containers;

public sealed class AviDemuxerTests
{
    [Fact]
    public void FactoryRecognisesAviByItsRiffType()
    {
        var factory = new AviDemuxerFactory();
        var avi = Build(indexed: false);

        Assert.Equal("avi", factory.Name);
        Assert.Equal(100, factory.Probe(avi, ".bin"));
        Assert.Equal(0, factory.Probe(avi.AsSpan(0, 8), ".avi"));
        var wave = (byte[])avi.Clone();
        "WAVE"u8.CopyTo(wave.AsSpan(8));
        Assert.Equal(0, factory.Probe(wave, ".avi"));
        using var opened = factory.Open(new MemoryByteSource(avi), CancellationToken.None);
        Assert.IsType<AviDemuxer>(opened);
    }

    [Fact]
    public void ScannedMovieChunksDescribeAndDeliverVideoAndAudio()
    {
        using var avi = Open(Build(indexed: false));

        Assert.Equal("AVI", avi.Info.FormatName);
        Assert.False(avi.Info.IsSeekable);
        Assert.Equal(MediaTime.FromMilliseconds(80), avi.Info.Duration);
        var video = Assert.Single(avi.Info.Tracks, track => track.Kind == MediaKind.Video);
        Assert.Equal((CodecId.Mpeg4Part2, 320, 240, new Rational(25, 1)),
            (video.Codec, video.Video!.Width, video.Video.Height, video.Video.FrameRate));
        var audio = Assert.Single(avi.Info.Tracks, track => track.Kind == MediaKind.Audio);
        Assert.Equal((CodecId.Pcm, 8000, 1), (audio.Codec, audio.Audio!.SampleRate, audio.Audio.Channels));

        using var first = avi.ReadPacket(CancellationToken.None)!;
        using var second = avi.ReadPacket(CancellationToken.None)!;
        using var third = avi.ReadPacket(CancellationToken.None)!;
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Data.Span.ToArray());
        Assert.Equal((0, MediaTime.Zero, MediaTime.FromMilliseconds(40), false), (first.TrackId, first.Pts, first.Duration, first.IsKeyframe));
        Assert.Equal((1, MediaTime.Zero), (second.TrackId, second.Pts));
        Assert.Equal((0, MediaTime.FromMilliseconds(40)), (third.TrackId, third.Pts));
        Assert.Null(avi.ReadPacket(CancellationToken.None));
    }

    [Fact]
    public void NestedRecordListsStillDeliverTheirMediaPackets()
    {
        // Some AVI writers wrap frame chunks in LIST 'rec ' instead of placing them
        // directly in movi. An unindexed file must descend into those lists.
        var header = List("hdrl", StreamList("vids", "XVID", 1, 25, 1, 0, VideoFormat()));
        var movie = List("movi", List("rec ", Chunk("00dc", [9, 8, 7])));
        using var avi = Open(Riff("AVI ", header, movie));

        using var packet = avi.ReadPacket(CancellationToken.None)!;
        Assert.Equal(0, packet.TrackId);
        Assert.Equal(new byte[] { 9, 8, 7 }, packet.Data.Span.ToArray());
        Assert.Null(avi.ReadPacket(CancellationToken.None));
    }

    [Fact]
    public void LegacyIndexMarksKeyframesAndSeekingStartsAtTheEarlierOne()
    {
        using var avi = Open(Build(indexed: true));
        Assert.True(avi.Info.IsSeekable);

        avi.Seek(MediaTime.FromMilliseconds(70), CancellationToken.None);
        using var packet = avi.ReadPacket(CancellationToken.None)!;

        Assert.Equal(0, packet.TrackId);
        Assert.True(packet.IsKeyframe);
        Assert.Equal(new byte[] { 7, 8 }, packet.Data.Span.ToArray());
        Assert.Equal(MediaTime.FromMilliseconds(40), packet.Pts);
    }

    [Fact]
    public void TrailingMetadataAfterTheLastRiffSegmentIsIgnored()
    {
        var bytes = Build(indexed: false).Concat("private metadata"u8.ToArray()).ToArray();

        using var avi = Open(bytes);
        using var packet = avi.ReadPacket(CancellationToken.None)!;

        Assert.Equal(new byte[] { 1, 2, 3 }, packet.Data.Span.ToArray());
    }

    [Fact]
    public void InvalidTopLevelAndStreamHeadersAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AviDemuxer(null!, CancellationToken.None));
        Assert.Throws<MediaFormatException>(() => Open([]));
        Assert.Throws<MediaFormatException>(() => Open(new byte[12]));

        var notAvi = Build(indexed: false);
        "WAVE"u8.CopyTo(notAvi.AsSpan(8));
        Assert.Throws<MediaFormatException>(() => Open(notAvi));

        var noMovie = Riff("AVI ", List("hdrl", StreamList("vids", "XVID", 1, 25, 1, 0, VideoFormat())));
        Assert.Throws<MediaFormatException>(() => Open(noMovie));

        var badTimebase = Riff("AVI ", List("hdrl", StreamList("vids", "XVID", 0, 25, 1, 0, VideoFormat())), List("movi", Chunk("00dc", [1])));
        Assert.Throws<MediaFormatException>(() => Open(badTimebase));
    }

    private static AviDemuxer Open(byte[] bytes) => new(new MemoryByteSource(bytes, "test.avi"), CancellationToken.None);

    private static byte[] Build(bool indexed)
    {
        var header = List("hdrl",
            Chunk("JUNK", [0]),
            StreamList("vids", "XVID", 1, 25, 2, 0, VideoFormat()),
            StreamList("auds", "\0\0\0\0", 2, 16_000, 2, 2, AudioFormat()));
        var movie = List("movi", Chunk("00dc", [1, 2, 3]), Chunk("01wb", [4, 5, 6, 7]), Chunk("00dc", [7, 8]));
        if (!indexed)
        {
            return Riff("AVI ", header, movie);
        }

        // idx1 offsets are from the four-byte movi list type to each chunk header.
        var entries = new List<byte>();
        AddIndex(entries, "00dc", 0x10, 0, 3);
        AddIndex(entries, "01wb", 0x10, 12, 4);
        AddIndex(entries, "00dc", 0x10, 24, 2);
        return Riff("AVI ", header, movie, Chunk("idx1", [.. entries]));
    }

    private static byte[] StreamList(string kind, string handler, uint scale, uint rate, uint length, uint sampleSize, byte[] format)
    {
        var header = new byte[56];
        Encoding.ASCII.GetBytes(kind).CopyTo(header, 0);
        Encoding.ASCII.GetBytes(handler).CopyTo(header, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), scale);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), rate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(32), length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), sampleSize);
        return List("strl", Chunk("strh", header), Chunk("strf", format));
    }

    private static byte[] VideoFormat()
    {
        var format = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(format, 40);
        BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), 320);
        BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), -240);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 24);
        "XVID"u8.CopyTo(format.AsSpan(16));
        return format;
    }

    private static byte[] AudioFormat()
    {
        var format = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(format, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), 8000);
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), 16_000);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);
        return format;
    }

    private static void AddIndex(List<byte> entries, string id, uint flags, uint offset, uint size)
    {
        entries.AddRange(Encoding.ASCII.GetBytes(id));
        Add(entries, flags);
        Add(entries, offset);
        Add(entries, size);
    }

    private static byte[] Riff(string type, params byte[][] children) => Sized("RIFF", [.. Encoding.ASCII.GetBytes(type), .. children.SelectMany(bytes => bytes)]);

    private static byte[] List(string type, params byte[][] children) => Sized("LIST", [.. Encoding.ASCII.GetBytes(type), .. children.SelectMany(bytes => bytes)]);

    private static byte[] Chunk(string id, byte[] body) => Sized(id, body);

    private static byte[] Sized(string id, byte[] body)
    {
        var bytes = new List<byte>(8 + body.Length + 1);
        bytes.AddRange(Encoding.ASCII.GetBytes(id));
        Add(bytes, (uint)body.Length);
        bytes.AddRange(body);
        if ((body.Length & 1) != 0) bytes.Add(0);
        return [.. bytes];
    }

    private static void Add(List<byte> bytes, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, value);
        bytes.AddRange(encoded);
    }
}
