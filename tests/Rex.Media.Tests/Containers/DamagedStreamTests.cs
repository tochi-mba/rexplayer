using Rex.Media.AppCore;
using Rex.Media.Containers;
using Rex.Media.Containers.Mp4;
using Rex.Media.Containers.Mpeg;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;
using static Rex.Media.TestKit.Mp4Writer;

namespace Rex.Media.Tests.Containers;

/// <summary>
/// Streams damaged part-way: an MP3 with junk that looks like one frame header, MP4 tracks and
/// fragments whose boxes are cut short, and files with junk in front of their format's signature.
/// </summary>
public sealed class DamagedStreamTests
{
    private static List<byte[]> Packets(IDemuxer demuxer)
    {
        var packets = new List<byte[]>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                packets.Add(packet.Data.Span.ToArray());
            }
        }

        return packets;
    }

    private static long Bytes(IDemuxer demuxer) => Packets(demuxer).Sum(packet => (long)packet.Length);

    [Fact]
    public void AnMp3ResyncsOnlyOnAHeaderAnotherFollows()
    {
        var healthy = HealthyMedia.Mp3(2);
        var frames = healthy.Landmarks["frames"];
        var bytes = healthy.Bytes;

        // Before frame 10: junk, then a lone copy of a real header followed by silence; and junk before the last frame.
        byte[] lone = [.. bytes.AsSpan(frames[0].Start, 4), .. new byte[150]];
        var (tenth, last) = (frames[10].Start, frames[^1].Start);
        byte[] damaged = [.. bytes[..tenth], .. new byte[40], .. lone, .. bytes[tenth..last], .. new byte[40], .. bytes[last..]];

        using var demuxer = new MpegAudioDemuxer(new MemoryByteSource(damaged, "damaged.mp3"), CancellationToken.None);
        var packets = Packets(demuxer);

        Assert.Equal(frames.Count, packets.Count);
        Assert.Equal(bytes.AsSpan(frames[10].Start, frames[10].Length).ToArray(), packets[10]);
        Assert.Equal(bytes.AsSpan(last).ToArray(), packets[^1]);
    }

    private static Mp4TrackSpec Sound(int id = 1) => new()
    {
        Id = id,
        SampleEntry = AudioEntry("sowt", 1, 16, 8000),
        Samples = [.. Enumerable.Range(0, 8).Select(i => new byte[320])],
    };

    private static Mp4Demuxer Mp4(byte[] file) => new(new MemoryByteSource(file, "damaged.mp4"), CancellationToken.None);

    /// <summary>A gigabyte that is a box header followed by nothing but zeros, without the gigabyte.</summary>
    private sealed class VastSource(byte[] start) : IByteSource
    {
        public string Name => "vast.mp4";

        public long? Length => 1L << 30;

        public bool CanSeek => true;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
        {
            var count = (int)Math.Max(0, Math.Min(destination.Length, Length!.Value - position));
            destination[..count].Clear();
            if (position < start.Length)
            {
                start.AsSpan((int)position, (int)Math.Min(count, start.Length - position)).CopyTo(destination);
            }

            return count;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void AnMp4HeaderLargerThanAnyMovieNeedsIsRefusedBeforeItIsRead()
    {
        // The file is long enough to hold it, but no movie's header is 300 MB: reading it would only cost memory.
        using var source = new VastSource([.. Box("ftyp", Ascii("isom"), U32(0)), .. U32(300_000_000), .. Ascii("moov")]);

        Assert.Contains("larger than", Assert.Throws<MediaFormatException>(() => new Mp4Demuxer(source, CancellationToken.None)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnMp4TrackTooDamagedToReadCostsOnlyThatTrack()
    {
        var broken = Box("trak", Full("tkhd", 0, 3, U32(0), U32(0), U32(7)), Box("mdia", Box("mdhd", [0, 0, 0, 0]), Full("hdlr", 0, 0, U32(0), Ascii("soun"), new byte[12], [0])));

        using var demuxer = Mp4(File([Sound()], [broken]));

        Assert.Single(demuxer.Info.Tracks);
        Assert.Equal(8 * 320, Bytes(demuxer));
    }

    [Fact]
    public void AnMp4MovieHeaderCutShortLeavesTheTracksToSayHowLongTheMovieIs()
    {
        var file = File([Sound()]);
        foreach (var keep in new[] { 0, 12, 24 })
        {
            var mvhd = StructuralDamage.Mp4Box(file, ["moov", "mvhd"])!.Value;
            var cut = (byte[])file.Clone();
            if (keep == 24)
            {
                cut[mvhd.Start + 8] = 1;
            }

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(cut.AsSpan(mvhd.Start), (uint)(8 + keep));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(cut.AsSpan(mvhd.Start + 8 + keep), (uint)(mvhd.Size - 8 - keep));
            "free"u8.CopyTo(cut.AsSpan(mvhd.Start + 12 + keep));

            using var demuxer = Mp4(cut);

            Assert.Equal(MediaTime.FromMilliseconds(160), demuxer.Info.Duration);
        }
    }

    [Fact]
    public void AnMp4FragmentCutShortKeepsTheFragmentsAroundIt()
    {
        var track = new Mp4TrackSpec { Id = 1, SampleEntry = AudioEntry("sowt", 1, 16, 8000) };
        var fragment = new Mp4Fragment(1, [.. Enumerable.Range(0, 4).Select(_ => new byte[320])]);
        var file = FragmentedFile([track], [fragment, fragment, fragment]);
        var trun = StructuralDamage.Mp4Boxes(file, ["moof", "traf", "trun"]).Skip(1).First();

        // The second fragment's run claims more samples than it describes.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(trun.Start + 12), 400);

        using var demuxer = Mp4(file);

        // The first and last fragments whole, and the samples of the second its run does describe.
        Assert.Equal(12 * 320, Bytes(demuxer));
    }

    [Fact]
    public void AnMp4ChapterTitleCutOffByTheEndOfTheFileIsLeftOut()
    {
        byte[] Title(string text) => [0, (byte)text.Length, .. System.Text.Encoding.UTF8.GetBytes(text)];
        var chapters = new Mp4TrackSpec
        {
            Id = 2,
            Handler = "text",
            Timescale = 1000,
            Duration = 80,
            SampleEntry = Box("text", new byte[6], U16(1)),
            Samples = [Title("Intro"), Title("Sungba")],
            SamplesPerChunk = 1,
        };
        var sound = new Mp4TrackSpec { Id = 1, SampleEntry = AudioEntry("sowt", 1, 16, 8000), Samples = [.. Enumerable.Range(0, 8).Select(_ => new byte[320])], ChapterTrack = 2 };
        var file = File([sound, chapters]);

        using var demuxer = Mp4(file[..^3]);

        Assert.Equal("Intro", Assert.Single(demuxer.Info.Chapters).Title);
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("mp4")]
    [InlineData("mkv")]
    [InlineData("ogg")]
    public void JunkInFrontOfAFormatsSignatureIsPassedOver(string format)
    {
        var healthy = format switch
        {
            "wav" => HealthyMedia.Wav(1),
            "flac" => HealthyMedia.Flac(1),
            "mp4" => HealthyMedia.Mp4(1),
            "mkv" => HealthyMedia.Matroska(1),
            _ => HealthyMedia.Ogg(1),
        };
        var junk = new byte[3000];
        new Random(3).NextBytes(junk);
        junk[0] = 0x55;

        using var source = new MemoryByteSource((byte[])[.. junk, .. healthy.Bytes], "damaged.bin");
        using var demuxer = MediaRegistries.Demuxers().Open(source, CancellationToken.None);

        Assert.NotEmpty(Packets(demuxer));
    }

    [Fact]
    public void JunkWithNoSignatureAfterItIsStillRefusedInPlainWords()
    {
        var junk = Enumerable.Repeat((byte)0x55, 5000).ToArray();
        using var source = new MemoryByteSource(junk, "junk.bin");

        Assert.StartsWith("rexplayer does not recognise", Assert.Throws<MediaFormatException>(() => MediaRegistries.Demuxers().Open(source, CancellationToken.None)).Message, StringComparison.Ordinal);

        // A signature with nothing behind it: the format's own refusal says why.
        byte[] fake = [.. junk[..100], .. "OggS\0"u8, .. junk[..100]];
        using var fakeSource = new MemoryByteSource(fake, "fake.bin");
        Assert.Throws<MediaFormatException>(() => MediaRegistries.Demuxers().Open(fakeSource, CancellationToken.None));
    }
}
