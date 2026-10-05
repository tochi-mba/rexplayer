using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class WavDemuxerTests
{
    private static WavDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.wav"), CancellationToken.None);

    private static List<Packet> ReadAll(WavDemuxer demuxer)
    {
        var packets = new List<Packet>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add(packet);
        }

        return packets;
    }

    [Fact]
    [Capability("FMT-C01")]
    public void AStereoSixteenBitFileDescribesItsTrackAndDuration()
    {
        using var demuxer = Open(Pcm.SineWav(440, 44_100, 2, 1.0));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal("WAVE", demuxer.Info.FormatName);
        Assert.Equal(CodecId.Pcm, track.Codec);
        Assert.Equal(MediaKind.Audio, track.Kind);
        Assert.Equal(44_100, track.Audio!.SampleRate);
        Assert.Equal(2, track.Audio.Channels);
        Assert.Equal(SampleFormat.S16, track.Audio.PcmFormat);
        Assert.Equal(16, track.Audio.BitsPerSample);
        Assert.Equal(4, track.Audio.BlockAlign);
        Assert.False(track.Audio.BigEndian);
        Assert.Equal(44_100 * 4 * 8L, track.BitRate);
        Assert.Equal(MediaTime.FromSeconds(1), demuxer.Info.Duration);
        Assert.True(demuxer.Info.IsSeekable);
    }

    [Fact]
    public void PacketsCoverTheDataExactlyWithContinuousTimestamps()
    {
        var data = Enumerable.Range(0, 44_100 * 4).Select(i => (byte)i).ToArray();
        using var demuxer = Open(WavBuilder.Pcm(44_100, 2, 16, data).Build());

        var packets = ReadAll(demuxer);

        Assert.Equal(data, packets.SelectMany(p => p.Data.Span.ToArray()));
        var expected = MediaTime.Zero;
        foreach (var packet in packets)
        {
            Assert.Equal(expected, packet.Pts);
            Assert.Equal(packet.Pts, packet.Dts);
            Assert.True(packet.IsKeyframe);
            Assert.Equal(0, packet.TrackId);
            expected += packet.Duration;
            packet.Dispose();
        }

        Assert.Equal(MediaTime.FromSeconds(1), expected);
    }

    [Fact]
    public void SeekingLandsOnTheSampleAskedFor()
    {
        using var demuxer = Open(Pcm.RampWav(8000, 8000));

        demuxer.Seek(MediaTime.FromMilliseconds(500), CancellationToken.None);
        using var packet = demuxer.ReadPacket(CancellationToken.None)!;

        Assert.Equal(MediaTime.FromMilliseconds(500), packet.Pts);
        Assert.Equal(4000, BitConverter.ToInt16(packet.Data.Span[..2]));
    }

    [Fact]
    public void SeekingPastTheEndEndsTheStreamAndBeforeTheStartRewinds()
    {
        using var demuxer = Open(Pcm.RampWav(8000, 800));

        demuxer.Seek(MediaTime.FromSeconds(5), CancellationToken.None);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));

        demuxer.Seek(MediaTime.FromSeconds(-1), CancellationToken.None);
        using var packet = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(MediaTime.Zero, packet.Pts);
    }

    [Fact]
    public void BigEndianRifxFilesAreMarkedSo()
    {
        var file = new WavBuilder().Riff("RIFX").Format(1, 1, 8000, 2, 16).Data([0x12, 0x34]).Build();

        using var demuxer = Open(file);

        Assert.True(demuxer.Info.Tracks[0].Audio!.BigEndian);
    }

    [Fact]
    public void Rf64FilesTakeTheirDataSizeFromTheDs64Chunk()
    {
        var file = WavBuilder.Pcm(8000, 1, 16, new byte[1600]).Riff("RF64").Build();

        using var demuxer = Open(file);

        Assert.Equal("RF64", demuxer.Info.FormatName);
        Assert.Equal(MediaTime.FromMilliseconds(100), demuxer.Info.Duration);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    public void AnUnfinishedRecordingPlaysToTheEndOfTheFile(uint sizeField)
    {
        var file = WavBuilder.Pcm(8000, 1, 16, new byte[800]).DataSizeField(sizeField).Build();

        using var demuxer = Open(file);

        Assert.Equal(MediaTime.FromMilliseconds(50), demuxer.Info.Duration);
        Assert.Equal(800, ReadAll(demuxer).Sum(p => p.Data.Length));
    }

    [Fact]
    public void ADataSizeLargerThanTheFileIsCutToTheFile()
    {
        var file = WavBuilder.Pcm(8000, 1, 16, new byte[800]).DataSizeField(1_000_000).Build();

        using var demuxer = Open(file);

        Assert.Equal(MediaTime.FromMilliseconds(50), demuxer.Info.Duration);
    }

    [Fact]
    public void InfoTagsBecomeMetadataAndUnknownChunksAreSkipped()
    {
        var file = new WavBuilder()
            .Chunk("junk", [1, 2, 3])
            .Info(("INAM", "Lonely At The Top"), ("IART", "Asake"), ("IPRD", "Work of Art"), ("ICMT", "c"), ("ICRD", "2023"),
                ("IGNR", "Afrobeats"), ("ITRK", "1"), ("ICOP", "(c)"), ("ISFT", "rexplayer"), ("IXYZ", "ignored"), ("IKEY", ""))
            .Format(1, 1, 8000, 2, 16)
            .Data(new byte[16])
            .Build();

        using var demuxer = Open(file);
        var metadata = demuxer.Info.Metadata;

        Assert.Equal("Lonely At The Top", metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", metadata[MetadataKeys.Artist]);
        Assert.Equal("Work of Art", metadata[MetadataKeys.Album]);
        Assert.Equal("c", metadata[MetadataKeys.Comment]);
        Assert.Equal("2023", metadata[MetadataKeys.Date]);
        Assert.Equal("Afrobeats", metadata[MetadataKeys.Genre]);
        Assert.Equal("1", metadata[MetadataKeys.Track]);
        Assert.Equal("(c)", metadata[MetadataKeys.Copyright]);
        Assert.Equal("rexplayer", metadata[MetadataKeys.Encoder]);
        Assert.Equal(9, metadata.Count);
    }

    [Fact]
    public void ListChunksThatAreNotInfoOrAreTooBigAreIgnored()
    {
        var file = new WavBuilder()
            .Chunk("LIST", "adtlxxxx"u8.ToArray())
            .Chunk("LIST", [1, 2])
            .Format(1, 1, 8000, 2, 16)
            .Data(new byte[16])
            .Build();

        using var demuxer = Open(file);

        Assert.Empty(demuxer.Info.Metadata);
    }

    [Fact]
    public void DataBeforeTheFormatChunkIsFound()
    {
        var file = new WavBuilder().Data(new byte[16]).Format(1, 1, 8000, 2, 16).Build();

        using var demuxer = Open(file);

        Assert.Equal(16, ReadAll(demuxer).Sum(p => p.Data.Length));
    }

    [Fact]
    public void MissingChunksAreFormatErrors()
    {
        Assert.Throws<MediaFormatException>(() => Open(new WavBuilder().Data(new byte[4]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new WavBuilder().Format(1, 1, 8000, 2, 16).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new WavBuilder().Data(new byte[4]).DataSizeField(0).Build()));
    }

    [Fact]
    public void ARiffFileThatIsNotWaveIsRejected()
    {
        var file = new WavBuilder().Format(1, 1, 8000, 2, 16).Data(new byte[2]).Build();
        "AVI "u8.CopyTo(file.AsSpan(8));

        Assert.Throws<MediaFormatException>(() => Open(file));
    }

    [Fact]
    public void TheFactoryRecognisesEveryRiffFlavour()
    {
        var factory = new WavDemuxerFactory();
        var wave = WavBuilder.Pcm(8000, 1, 16, new byte[2]).Build();

        Assert.Equal("wav", factory.Name);
        foreach (var riff in new[] { "RIFF", "RIFX", "RF64", "BW64" })
        {
            var copy = (byte[])wave.Clone();
            System.Text.Encoding.ASCII.GetBytes(riff).CopyTo(copy, 0);
            Assert.Equal(100, factory.Probe(copy, ".bin"));
        }

        var avi = (byte[])wave.Clone();
        "AVI "u8.CopyTo(avi.AsSpan(8));
        Assert.Equal(0, factory.Probe(avi, ".wav"));
        var form = (byte[])wave.Clone();
        "FORM"u8.CopyTo(form);
        Assert.Equal(0, factory.Probe(form, ".wav"));
        Assert.Equal(0, factory.Probe(wave.AsSpan(0, 8), ".wav"));
        using var opened = factory.Open(new MemoryByteSource(wave), CancellationToken.None);
        Assert.IsType<WavDemuxer>(opened);
    }

    [Fact]
    public void TheDemuxerNeedsASource()
    {
        Assert.Throws<ArgumentNullException>(() => new WavDemuxer(null!, CancellationToken.None));
    }

    [Fact]
    public void ImaAdpcmBlocksAreOnePacketEachWithTheirSampleCount()
    {
        var extra = new byte[] { 0xF9, 0x01 };
        var file = new WavBuilder().Format(0x11, 1, 8000, 256, 4, extra).Data(new byte[512]).Build();

        using var demuxer = Open(file);
        var track = demuxer.Info.Tracks[0];

        Assert.Equal(CodecId.AdpcmIma, track.Codec);
        Assert.Equal(505, track.Audio!.SamplesPerBlock);
        Assert.Equal(MediaTime.FromSamples(1010, 8000), demuxer.Info.Duration);
    }
}
