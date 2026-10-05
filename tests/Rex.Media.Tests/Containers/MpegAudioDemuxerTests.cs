using System.Buffers.Binary;
using Rex.Media.Codecs.Mpeg;
using Rex.Media.Containers;
using Rex.Media.Containers.Mpeg;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Codecs;

namespace Rex.Media.Tests.Containers;

public sealed class MpegAudioDemuxerTests
{
    private const int Id3Length = 20;
    private const int TagFrameLength = 417;

    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/mp3/{name}.mp3"));

    private static MpegAudioDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.mp3"), CancellationToken.None);

    private sealed class LiveSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public string Name => "live.mp3";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    private static List<byte[]> Packets(IDemuxer demuxer)
    {
        var packets = new List<byte[]>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add(packet.Data.Span.ToArray());
            packet.Dispose();
        }

        return packets;
    }

    /// <summary>The CBR fixture's audio frames: everything after the ID3 tag and the tag frame.</summary>
    private static byte[] AudioFrames() => Fixture("stereo-44k-128k-cbr")[(Id3Length + TagFrameLength)..];

    [Fact]
    [Capability("FMT-C03")]
    public void TheLameTagGivesAnExactDurationAndTheGaplessTrims()
    {
        using var demuxer = Open(Fixture("stereo-44k-128k-cbr"));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal("MP3", demuxer.Info.FormatName);
        Assert.Equal(CodecId.Mp3, track.Codec);
        Assert.Equal((44_100, 2), (track.Audio!.SampleRate, track.Audio.Channels));
        Assert.Equal(576 + 529, track.Audio.LeadingPadding);
        Assert.Equal(1368 - 529, track.Audio.TrailingPadding);
        Assert.Equal(MediaTime.FromSamples(17_640, 44_100), demuxer.Info.Duration);
        Assert.Equal(128_000, track.BitRate);
        Assert.True(demuxer.Info.IsSeekable);

        var first = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(MediaTime.FromSamples(-1105, 44_100), first.Pts);
        Assert.Equal(MediaTime.FromSamples(1152, 44_100), first.Duration);
        first.Dispose();
        Assert.Equal(16, Packets(demuxer).Count);
    }

    [Fact]
    public void AVariableBitrateFileTakesItsBitrateFromTheTag()
    {
        using var demuxer = Open(Fixture("stereo-44k-vbr-bursts"));

        Assert.Equal(MediaTime.FromSamples(17_640, 44_100), demuxer.Info.Duration);
        Assert.InRange(demuxer.Info.Tracks[0].BitRate!.Value, 100_000, 200_000);
    }

    [Fact]
    public void WithoutATagTheDurationComesFromTheConstantBitrate()
    {
        using var demuxer = Open(AudioFrames());
        var track = demuxer.Info.Tracks[0];

        Assert.Equal(0, track.Audio!.LeadingPadding);
        Assert.Equal(0, track.Audio.TrailingPadding);
        Assert.Equal(MediaTime.FromSeconds(AudioFrames().Length * 8.0 / 128_000), demuxer.Info.Duration);
        Assert.Equal(MediaTime.Zero, demuxer.ReadPacket(CancellationToken.None)!.Pts);
    }

    [Fact]
    [Capability("META-01")]
    public void TagsInFrontAndBehindAreReadAndKeptOutOfTheAudio()
    {
        var id3 = Id3Builder.V24().Text("TIT2", "Terminator").Text("TPE1", "Asake").Picture([1, 2, 3]).Chapter("c", 0, "Start").Build();
        var v1 = new byte[128];
        "TAG"u8.CopyTo(v1);
        "Lonely At The Top"u8.CopyTo(v1.AsSpan(3));
        "Work of Art"u8.CopyTo(v1.AsSpan(63));
        var ape = new byte[64];
        "APETAGEX"u8.CopyTo(ape);
        BinaryPrimitives.WriteUInt32LittleEndian(ape.AsSpan(12), 64);
        var apeFooter = (byte[])ape.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(apeFooter.AsSpan(20), 0x80000000);
        var file = (byte[])[.. id3, .. AudioFrames(), .. ape, .. apeFooter[..32], .. v1];
        using var demuxer = Open(file);

        Assert.Equal("Terminator", demuxer.Info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Work of Art", demuxer.Info.Metadata[MetadataKeys.Album]);
        Assert.Equal([1, 2, 3], demuxer.Info.CoverArt);
        Assert.Equal("Start", Assert.Single(demuxer.Info.Chapters).Title);
        Assert.Equal(Packets(Open(AudioFrames())).Select(Convert.ToHexString), Packets(demuxer).Select(Convert.ToHexString));
    }

    [Fact]
    public void FreeFormatFramesAreMeasuredFromOneHeaderToTheNext()
    {
        var plain = AudioFrames();
        var free = (byte[])plain.Clone();
        for (var at = 0; at + 4 <= free.Length;)
        {
            Assert.True(MpegAudioHeader.TryParse(free.AsSpan(at), out var header));
            free[at + 2] &= 0x0F;
            at += header.FrameLength;
        }

        using var demuxer = Open(free);

        Assert.False(demuxer.Info.Duration.IsKnown);
        Assert.Equal(Packets(Open(plain)).Select(p => p.Length), Packets(demuxer).Select(p => p.Length));
    }

    [Fact]
    public void JunkBetweenFramesIsSkippedAndACutOffLastFrameIsDropped()
    {
        var frames = AudioFrames();
        var cut = Starts(frames)[3];
        var junk = (byte[])[.. frames[..cut], 0xFF, 0x00, 1, 2, 3, 0xFF, .. frames[cut..^100]];
        using var demuxer = Open(junk);

        var packets = Packets(demuxer);

        Assert.Equal(16, packets.Count);
        Assert.All(packets, p => Assert.True(MpegAudioHeader.TryParse(p, out _)));
    }

    [Fact]
    public void JunkBeforeTheFirstFrameAndAfterTheLastIsPassedOver()
    {
        var frames = AudioFrames();

        // A lone valid-looking header whose chain breaks, then the real stream, then filler.
        byte[] lead = [0xFF, 0xFB, 0x90, 0x64, 1, 2, 3, 0xFF, 0x00];
        using var demuxer = Open([.. lead, .. frames, .. Enumerable.Repeat((byte)0x11, 300)]);

        Assert.Equal(17, Packets(demuxer).Count);
    }

    [Fact]
    public void ResyncingStopsWhereATrailingTagBegins()
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        // More filler than one read takes, so the search stops at the tag rather than at the end of the file.
        using var demuxer = Open([.. AudioFrames(), .. Enumerable.Repeat((byte)0x11, 5000), .. tag]);

        Assert.Equal(17, Packets(demuxer).Count);
    }

    [Fact]
    public void AStreamOfUnknownLengthReadsToItsEndButCannotSeek()
    {
        using var demuxer = new MpegAudioDemuxer(new LiveSource(AudioFrames()), CancellationToken.None);

        Assert.False(demuxer.Info.IsSeekable);
        Assert.False(demuxer.Info.Duration.IsKnown);
        Assert.Equal(17, Packets(demuxer).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5_000)]
    [InlineData(12_345)]
    [InlineData(17_639)]
    [Capability("PB-03")]
    public void SeekingThenTrimmingToTheTargetMatchesTheWholeDecode(int target)
    {
        var file = Fixture("stereo-44k-128k-cbr");
        var (whole, _, _) = Mp3FixtureTests.Decode(file);
        using var demuxer = Open(file);
        var track = demuxer.Info.Tracks[0];
        using var decoder = new Rex.Media.Codecs.Software.Mpeg.Mp3Decoder(track);
        var end = track.Duration.ToSamples(44_100);

        demuxer.Seek(MediaTime.FromSamples(target, 44_100), CancellationToken.None);
        var left = new List<float>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            var frames = new List<AudioFrame>();
            decoder.Decode(packet, frames);
            packet.Dispose();
            foreach (var frame in frames)
            {
                var first = frame.Pts.ToSamples(44_100);
                var samples = frame.Channel(0);
                for (var i = 0; i < samples.Length; i++)
                {
                    if (first + i >= target && first + i < end)
                    {
                        left.Add(samples[i]);
                    }
                }

                frame.Dispose();
            }
        }

        Assert.Equal(whole[0][target..].Length, left.Count);
        Assert.True(Mp3FixtureTests.Difference([.. left], whole[0][target..]).Peak < 1e-4);
    }

    [Fact]
    public void SeekingPastTheEndLandsOnTheLastFrames()
    {
        using var demuxer = Open(Fixture("stereo-44k-128k-cbr"));

        demuxer.Seek(MediaTime.FromSeconds(30), CancellationToken.None);

        Assert.InRange(Packets(demuxer).Count, 1, MpegAudioDemuxer.PrerollFrames + 1);
    }

    [Fact]
    public void TheFactoryScoresAChainOfHeaders()
    {
        var factory = new MpegAudioDemuxerFactory();
        var frames = AudioFrames();
        var registry = new DemuxerRegistry().Add(factory);

        Assert.Equal("mpeg-audio", factory.Name);
        Assert.Equal(100, factory.Probe(frames, ".mp3"));
        Assert.Equal(70, factory.Probe([0, 1, 2, .. frames], ".mp3"));
        var first = Starts(frames)[1];
        Assert.Equal(100, factory.Probe(frames.AsSpan(0, first + 10), ".mp3"));
        Assert.Equal(100, factory.Probe(frames.AsSpan(0, first), ".mp3"));
        Assert.Equal(0, factory.Probe(frames.AsSpan(0, 300), ".mp3"));
        Assert.Equal(0, factory.Probe("RIFF\0\0\0\0WAVE"u8, ".mp3"));
        Assert.Equal(0, factory.Probe([0xFF, 0xFB, 0x90, 0x64, .. new byte[413], 0, 0, 0, 0], ".mp3"));
        Assert.Same(factory, registry.Probe(new MemoryByteSource(Fixture("mono-48k-vbr"), "x"), CancellationToken.None));
        using var demuxer = factory.Open(new MemoryByteSource(frames), CancellationToken.None);
        Assert.Equal("MP3", demuxer.Info.FormatName);
    }

    [Fact]
    public void LayerOneAndTwoStreamsAreNamedForWhatTheyAre()
    {
        var layer2 = Repeat(MpegAudioHeaderTests.Header(3, 2, 9, 0), 3);
        var layer1 = Repeat(MpegAudioHeaderTests.Header(3, 3, 9, 0), 3);

        Assert.Equal("MPEG audio Layer II", Open(layer2).Info.FormatName);
        Assert.Equal(CodecId.Mp1, Open(layer1).Info.Tracks[0].Codec);
        Assert.Equal("MPEG audio Layer I", Open(layer1).Info.FormatName);
    }

    [Fact]
    public void AFileWithNoFramesIsRejected()
    {
        Assert.Throws<MediaFormatException>(() => Open(new byte[5000]));
        Assert.Throws<MediaFormatException>(() => Open([0xFF, 0xFB, 0x00, 0x64, 0, 0, 0]));
        Assert.Throws<ArgumentNullException>(() => new MpegAudioDemuxer(null!, CancellationToken.None));
    }

    /// <summary>Where each frame starts, found by walking the headers.</summary>
    private static List<int> Starts(byte[] frames)
    {
        var starts = new List<int>();
        for (var at = 0; at + 4 <= frames.Length && MpegAudioHeader.TryParse(frames.AsSpan(at), out var header); at += header.FrameLength)
        {
            starts.Add(at);
        }

        return starts;
    }

    /// <summary>A file of <paramref name="count"/> silent frames with the given header.</summary>
    private static byte[] Repeat(byte[] header, int count)
    {
        Assert.True(MpegAudioHeader.TryParse(header, out var parsed));
        var frame = new byte[parsed.FrameLength];
        header.CopyTo(frame, 0);
        return [.. Enumerable.Repeat(frame, count).SelectMany(f => f)];
    }
}

public sealed class MpegInfoTagTests
{
    private static readonly byte[] Cbr = File.ReadAllBytes(RepoPaths.Combine("tests/fixtures/mp3/stereo-44k-128k-cbr.mp3"));

    private static byte[] TagFrame() => Cbr[20..(20 + 417)];

    private static MpegAudioHeader HeaderOf(byte[] frame)
    {
        Assert.True(MpegAudioHeader.TryParse(frame, out var header));
        return header;
    }

    [Fact]
    public void AnInfoTagWithLameFieldsGivesFramesDelayAndPadding()
    {
        var frame = TagFrame();

        var tag = MpegInfoTag.Parse(frame, HeaderOf(frame))!;

        Assert.Equal("Info", tag.Kind);
        Assert.Equal(17, tag.Frames);
        Assert.Equal((576, 1368), (tag.EncoderDelay, tag.EncoderPadding));
        Assert.Equal("Lavf lame", tag.Encoder);
        Assert.Equal(1105, tag.LeadingSamples);
        Assert.Equal(839, tag.TrailingSamples);
    }

    [Fact]
    public void ATagWithoutCountsOrAnEncoderNameHasNoGaplessData()
    {
        var frame = TagFrame();
        var at = 4 + 32;
        frame[at + 7] = 0;
        frame[at + 8] = (byte)'1';

        var tag = MpegInfoTag.Parse(frame, HeaderOf(frame))!;

        Assert.Null(tag.Frames);
        Assert.Null(tag.Bytes);
        Assert.Null(tag.Encoder);
        Assert.Equal((0, 0), (tag.LeadingSamples, tag.TrailingSamples));
        Assert.Null(MpegInfoTag.Parse(frame.AsSpan(0, 40), HeaderOf(frame)));
    }

    [Fact]
    public void SmallPaddingNeverMakesTheTrailingTrimNegative()
    {
        Assert.Equal(0, new MpegInfoTag("Info", 1, 1, 0, 100, "LAME").TrailingSamples);
    }

    [Fact]
    public void AVbriHeaderGivesTheFrameCount()
    {
        var frame = TagFrame();
        frame.AsSpan(4, 413).Clear();
        "VBRI"u8.CopyTo(frame.AsSpan(36));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(36 + 10), 7000);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(36 + 14), 17);

        var tag = MpegInfoTag.Parse(frame, HeaderOf(frame))!;

        Assert.Equal(("VBRI", 17L, 7000L), (tag.Kind, tag.Frames, tag.Bytes));
        Assert.Null(tag.Encoder);
    }

    [Fact]
    public void AnAudioFrameHasNoTag()
    {
        var frame = Cbr[(20 + 417)..(20 + 417 + 417)];

        Assert.Null(MpegInfoTag.Parse(frame, HeaderOf(frame)));
    }
}
