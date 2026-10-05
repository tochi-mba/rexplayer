using System.Buffers.Binary;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

public sealed class PcmDecoderTests
{
    private static TrackInfo Track(CodecId codec, SampleFormat format, int channels = 1, bool bigEndian = false) => new()
    {
        Id = 0,
        Codec = codec,
        Audio = new AudioTrackInfo { SampleRate = 8000, Channels = channels, PcmFormat = format, BigEndian = bigEndian, Layout = ChannelLayouts.Default(channels) },
    };

    private static float[][] Decode(TrackInfo track, byte[] bytes)
    {
        using var decoder = new PcmDecoderFactory().CreateAudio(track);
        using var packet = Packet.Create(0, MediaBuffer.CopyOf(bytes), MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), MediaTime.Zero, isKeyframe: true);
        packet.Generation = 7;
        var frames = new List<AudioFrame>();
        decoder.Decode(packet, frames);
        var frame = Assert.Single(frames);
        Assert.Equal(MediaTime.FromSeconds(2), frame.Pts);
        Assert.Equal(7, frame.Generation);
        var result = Enumerable.Range(0, frame.Channels).Select(c => frame.Channel(c).ToArray()).ToArray();
        frame.Dispose();
        return result;
    }

    [Fact]
    [Capability("FMT-A01")]
    public void SixteenBitSamplesScaleByFullScale()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), 0);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 16_384);

        var samples = Decode(Track(CodecId.Pcm, SampleFormat.S16), bytes)[0];

        Assert.Equal([-1f, 0f, 0.5f], samples);
    }

    [Fact]
    public void InterleavedChannelsArriveInTheirOwnPlanes()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 8192);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), -8192);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 16_384);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), -16_384);

        var planes = Decode(Track(CodecId.Pcm, SampleFormat.S16, channels: 2), bytes);

        Assert.Equal([0.25f, 0.5f], planes[0]);
        Assert.Equal([-0.25f, -0.5f], planes[1]);
    }

    [Theory]
    [InlineData(SampleFormat.U8, false, new byte[] { 0x00, 0x80, 0xC0 }, new[] { -1f, 0f, 0.5f })]
    [InlineData(SampleFormat.S8, false, new byte[] { 0x80, 0x00, 0x40 }, new[] { -1f, 0f, 0.5f })]
    [InlineData(SampleFormat.S16, true, new byte[] { 0x80, 0x00, 0x40, 0x00 }, new[] { -1f, 0.5f })]
    [InlineData(SampleFormat.S24, false, new byte[] { 0x00, 0x00, 0x80, 0x00, 0x00, 0x40 }, new[] { -1f, 0.5f })]
    [InlineData(SampleFormat.S24, true, new byte[] { 0x80, 0x00, 0x00, 0x40, 0x00, 0x00 }, new[] { -1f, 0.5f })]
    [InlineData(SampleFormat.S32, false, new byte[] { 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x40 }, new[] { -1f, 0.5f })]
    [InlineData(SampleFormat.S32, true, new byte[] { 0x80, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00 }, new[] { -1f, 0.5f })]
    public void EveryIntegerFormatDecodes(SampleFormat format, bool bigEndian, byte[] bytes, float[] expected)
    {
        Assert.Equal(expected, Decode(Track(CodecId.Pcm, format, bigEndian: bigEndian), bytes)[0]);
    }

    [Fact]
    public void FloatFormatsDecodeInBothByteOrders()
    {
        var f32 = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(f32, 0.25f);
        var f32Big = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(f32Big, -0.75f);
        var f64 = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(f64, 0.125);
        var f64Big = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(f64Big, -0.5);

        Assert.Equal([0.25f], Decode(Track(CodecId.Pcm, SampleFormat.F32), f32)[0]);
        Assert.Equal([-0.75f], Decode(Track(CodecId.Pcm, SampleFormat.F32, bigEndian: true), f32Big)[0]);
        Assert.Equal([0.125f], Decode(Track(CodecId.Pcm, SampleFormat.F64), f64)[0]);
        Assert.Equal([-0.5f], Decode(Track(CodecId.Pcm, SampleFormat.F64, bigEndian: true), f64Big)[0]);
    }

    [Theory]
    [InlineData(0xD5, 8)]
    [InlineData(0x55, -8)]
    [InlineData(0xAA, 32_256)]
    [InlineData(0x2A, -32_256)]
    [InlineData(0xC5, 264)]
    public void ALawMatchesTheG711Table(byte code, int expected)
    {
        Assert.Equal(expected / 32768f, PcmDecoder.DecodeAlaw(code));
        Assert.Equal([expected / 32768f], Decode(Track(CodecId.Alaw, SampleFormat.Unknown), [code])[0]);
    }

    [Theory]
    [InlineData(0xFF, 0)]
    [InlineData(0x7F, 0)]
    [InlineData(0x80, 32_124)]
    [InlineData(0x00, -32_124)]
    [InlineData(0xFE, 8)]
    public void MuLawMatchesTheG711Table(byte code, int expected)
    {
        Assert.Equal(expected / 32768f, PcmDecoder.DecodeMulaw(code));
        Assert.Equal([expected / 32768f], Decode(Track(CodecId.Mulaw, SampleFormat.Unknown), [code])[0]);
    }

    [Fact]
    public void APacketShorterThanOneFrameProducesNothing()
    {
        using var decoder = new PcmDecoder(Track(CodecId.Pcm, SampleFormat.S16, channels: 2));
        using var packet = Packet.Create(0, MediaBuffer.CopyOf([1, 2, 3]), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        var frames = new List<AudioFrame>();

        decoder.Decode(packet, frames);
        decoder.Drain(frames);
        decoder.Flush();

        Assert.Empty(frames);
        Assert.Equal("rexplayer PCM", decoder.Name);
        Assert.Equal(DecoderSource.Own, decoder.Source);
    }

    [Fact]
    public void TheFactoryAcceptsOnlyKnownPcmAndCompandedAudio()
    {
        var factory = new PcmDecoderFactory();

        Assert.Equal("rexplayer PCM", factory.Name);
        Assert.Equal(DecoderSource.Own, factory.Source);
        Assert.Equal(100, factory.Rank);
        Assert.True(factory.CanDecode(Track(CodecId.Pcm, SampleFormat.S24)));
        Assert.True(factory.CanDecode(Track(CodecId.Alaw, SampleFormat.Unknown)));
        Assert.True(factory.CanDecode(Track(CodecId.Mulaw, SampleFormat.Unknown)));
        Assert.False(factory.CanDecode(Track(CodecId.Pcm, SampleFormat.Unknown)));
        Assert.False(factory.CanDecode(Track(CodecId.Mp3, SampleFormat.Unknown)));
        Assert.False(factory.CanDecode(new TrackInfo { Id = 0, Codec = CodecId.Pcm }));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(Track(CodecId.Mp3, SampleFormat.Unknown)));
        Assert.Throws<ArgumentNullException>(() => factory.CanDecode(null!));
        Assert.Throws<ArgumentNullException>(() => factory.CreateAudio(null!));
    }

    [Fact]
    public void TheDecoderChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new PcmDecoder(null!));
        Assert.Throws<NotSupportedException>(() => new PcmDecoder(new TrackInfo { Id = 0, Codec = CodecId.Pcm }));
        using var decoder = new PcmDecoder(Track(CodecId.Pcm, SampleFormat.S16));
        using var packet = Packet.Create(0, MediaBuffer.Rent(2), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, []));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(packet, null!));
    }

    [Fact]
    public void AFixtureFileDecodesToTheSineItWasMadeFrom()
    {
        var file = Pcm.SineWav(1000, 8000, 1, 0.1, amplitude: 0.5);
        using var demuxer = new Rex.Media.Containers.Riff.WavDemuxer(new Rex.Media.IO.MemoryByteSource(file), CancellationToken.None);
        using var decoder = new PcmDecoderFactory().CreateAudio(demuxer.Info.Tracks[0]);
        var samples = new List<float>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                var frames = new List<AudioFrame>();
                decoder.Decode(packet, frames);
                foreach (var frame in frames)
                {
                    samples.AddRange(frame.Channel(0).ToArray());
                    frame.Dispose();
                }
            }
        }

        Assert.Equal(800, samples.Count);
        Assert.InRange(Signals.Amplitude(samples.ToArray(), 8000, 1000), 0.49, 0.51);
        Assert.InRange(Signals.Amplitude(samples.ToArray(), 8000, 2000), 0, 0.01);
    }
}
