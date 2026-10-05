using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class AiffDemuxerTests
{
    private static AiffDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.aiff"), CancellationToken.None);

    [Fact]
    [Capability("FMT-C02")]
    public void AnAiffFileDescribesItsBigEndianPcmTrack()
    {
        var file = new AiffBuilder()
            .Common(2, 100, 16, 44_100)
            .SoundData(new byte[400])
            .Text("NAME", "Terminator")
            .Text("AUTH", "Asake")
            .Text("ANNO", "note")
            .Text("(c) ", "2023")
            .Build();

        using var demuxer = Open(file);
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal("AIFF", demuxer.Info.FormatName);
        Assert.Equal(CodecId.Pcm, track.Codec);
        Assert.Equal(SampleFormat.S16, track.Audio!.PcmFormat);
        Assert.True(track.Audio.BigEndian);
        Assert.Equal(44_100, track.Audio.SampleRate);
        Assert.Equal(MediaTime.FromSamples(100, 44_100), demuxer.Info.Duration);
        Assert.Equal("Terminator", demuxer.Info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", demuxer.Info.Metadata[MetadataKeys.Artist]);
        Assert.Equal("note", demuxer.Info.Metadata[MetadataKeys.Comment]);
        Assert.Equal("2023", demuxer.Info.Metadata[MetadataKeys.Copyright]);
        Assert.Equal(44_100 * 4 * 8L, track.BitRate);
    }

    [Fact]
    public void PacketsAndSeeksWorkThroughTheSoundDataOffset()
    {
        var samples = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        var file = new AiffBuilder().Common(1, 100, 16, 8000).SoundData(samples, offset: 4).Build();
        using var demuxer = Open(file);

        using (var first = demuxer.ReadPacket(CancellationToken.None)!)
        {
            Assert.Equal(samples, first.Data.Span.ToArray());
        }

        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
        demuxer.Seek(MediaTime.FromSamples(50, 8000), CancellationToken.None);
        using var seeked = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(100, seeked.Data.Span[0]);
    }

    [Theory]
    [InlineData(8, null, SampleFormat.S8, true, CodecId.Pcm)]
    [InlineData(16, "NONE", SampleFormat.S16, true, CodecId.Pcm)]
    [InlineData(16, "twos", SampleFormat.S16, true, CodecId.Pcm)]
    [InlineData(16, "sowt", SampleFormat.S16, false, CodecId.Pcm)]
    [InlineData(24, "sowt", SampleFormat.S24, false, CodecId.Pcm)]
    [InlineData(32, "sowt", SampleFormat.S32, false, CodecId.Pcm)]
    [InlineData(8, "raw ", SampleFormat.U8, true, CodecId.Pcm)]
    [InlineData(24, "in24", SampleFormat.S24, true, CodecId.Pcm)]
    [InlineData(32, "in32", SampleFormat.S32, true, CodecId.Pcm)]
    [InlineData(32, "fl32", SampleFormat.F32, true, CodecId.Pcm)]
    [InlineData(32, "FL32", SampleFormat.F32, true, CodecId.Pcm)]
    [InlineData(64, "fl64", SampleFormat.F64, true, CodecId.Pcm)]
    [InlineData(64, "FL64", SampleFormat.F64, true, CodecId.Pcm)]
    [InlineData(16, "alaw", SampleFormat.Unknown, true, CodecId.Alaw)]
    [InlineData(16, "ALAW", SampleFormat.Unknown, true, CodecId.Alaw)]
    [InlineData(16, "ulaw", SampleFormat.Unknown, true, CodecId.Mulaw)]
    [InlineData(16, "ULAW", SampleFormat.Unknown, true, CodecId.Mulaw)]
    [InlineData(16, "ima4", SampleFormat.Unknown, true, CodecId.Unknown)]
    [InlineData(40, "NONE", SampleFormat.Unknown, true, CodecId.Pcm)]
    public void CompressionTypesMapToStorageFormats(int bits, string? compression, SampleFormat format, bool bigEndian, CodecId codec)
    {
        var file = new AiffBuilder(compressed: compression is not null).Common(1, 4, bits, 8000, compression).SoundData(new byte[64]).Build();

        using var demuxer = Open(file);
        var audio = demuxer.Info.Tracks[0].Audio!;

        Assert.Equal(codec, demuxer.Info.Tracks[0].Codec);
        Assert.Equal(format, audio.PcmFormat);
        Assert.Equal(bigEndian, audio.BigEndian);
        Assert.Equal(compression is null ? "AIFF" : "AIFF-C", demuxer.Info.FormatName);
    }

    [Fact]
    public void CompandedSamplesTakeOneByteEach()
    {
        var file = new AiffBuilder(compressed: true).Common(2, 10, 16, 8000, "ulaw").SoundData(new byte[20]).Build();

        using var demuxer = Open(file);

        Assert.Equal(2, demuxer.Info.Tracks[0].Audio!.BlockAlign);
        Assert.Equal(MediaTime.FromSamples(10, 8000), demuxer.Info.Duration);
    }

    [Fact]
    public void BrokenFilesAreFormatErrors()
    {
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().SoundData(new byte[4]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Common(1, 1, 16, 8000).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Chunk("COMM", new byte[10]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Common(0, 1, 16, 8000).SoundData(new byte[2]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Common(1, 1, 16, 0).SoundData(new byte[2]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Common(1, 1, 0, 8000).SoundData(new byte[2]).Build()));
        Assert.Throws<MediaFormatException>(() => Open(new AiffBuilder().Common(1, 1, 16, 1_000_000).SoundData(new byte[2]).Build()));
    }

    [Fact]
    public void ANonAiffFormIsRejected()
    {
        var file = new AiffBuilder().Common(1, 1, 16, 8000).SoundData(new byte[2]).Build();
        "8SVX"u8.CopyTo(file.AsSpan(8));
        var notForm = (byte[])file.Clone();
        "RIFF"u8.CopyTo(notForm);

        Assert.Throws<MediaFormatException>(() => Open(file));
        Assert.Throws<MediaFormatException>(() => Open(notForm));
        Assert.Throws<ArgumentNullException>(() => new AiffDemuxer(null!, CancellationToken.None));
    }

    [Fact]
    public void TheFactoryProbesBothForms()
    {
        var factory = new AiffDemuxerFactory();
        var aiff = new AiffBuilder().Common(1, 1, 16, 8000).SoundData(new byte[2]).Build();
        var aifc = new AiffBuilder(compressed: true).Common(1, 1, 16, 8000, "NONE").SoundData(new byte[2]).Build();

        Assert.Equal("aiff", factory.Name);
        Assert.Equal(100, factory.Probe(aiff, null));
        Assert.Equal(100, factory.Probe(aifc, null));
        Assert.Equal(0, factory.Probe(aiff.AsSpan(0, 8), null));
        Assert.Equal(0, factory.Probe("RIFF0000WAVE"u8, null));
        using var opened = factory.Open(new MemoryByteSource(aiff), CancellationToken.None);
        Assert.IsType<AiffDemuxer>(opened);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(8000)]
    [InlineData(0.5)]
    [InlineData(-22_050)]
    public void ExtendedFloatsRoundTrip(double value)
    {
        Assert.Equal(value, AiffDemuxer.ReadExtended(AiffBuilder.Extended(value)), precision: 9);
    }

    [Fact]
    public void ExtendedZeroAndInfinityAreRecognised()
    {
        Assert.Equal(0, AiffDemuxer.ReadExtended(new byte[10]));
        var infinite = new byte[10];
        infinite[0] = 0x7F;
        infinite[1] = 0xFF;
        Assert.True(double.IsNaN(AiffDemuxer.ReadExtended(infinite)));
    }
}
