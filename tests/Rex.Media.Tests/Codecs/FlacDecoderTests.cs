using Rex.Media.Codecs;
using Rex.Media.Codecs.Flac;
using Rex.Media.Codecs.Software.Flac;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

public sealed class FlacDecoderTests
{
    private static TrackInfo Track(int rate, int channels, int bits, byte[]? codecPrivate = null) => new()
    {
        Id = 0,
        Codec = CodecId.Flac,
        CodecPrivate = codecPrivate ?? [],
        Audio = new AudioTrackInfo { SampleRate = rate, Channels = channels, BitsPerSample = bits },
    };

    /// <summary>Encodes with the builder, decodes frame by frame, and returns the planes and the decoder.</summary>
    private static (float[][] Planes, List<AudioFrame> Frames, FlacDecoder Decoder) RoundTrip(FlacBuilder builder, int[][] channels, int? trackBits = null)
    {
        var file = builder.Build(channels);
        var decoder = new FlacDecoder(Track(builder.SampleRate, channels.Length, trackBits ?? builder.BitsPerSample));
        var frames = new List<AudioFrame>();
        var offsets = builder.FrameOffsets.Append(file.Length - builder.FirstFrameOffset).ToList();
        for (var i = 0; i < offsets.Count - 1; i++)
        {
            var bytes = file.AsSpan((int)(builder.FirstFrameOffset + offsets[i]), (int)(offsets[i + 1] - offsets[i]));
            using var packet = Packet.Create(0, MediaBuffer.CopyOf(bytes), MediaTime.FromSeconds(i), MediaTime.Unknown, MediaTime.Zero, isKeyframe: true);
            decoder.Decode(packet, frames);
        }

        var planes = new float[channels.Length][];
        for (var c = 0; c < channels.Length; c++)
        {
            planes[c] = frames.SelectMany(f => f.Channel(c).ToArray()).ToArray();
        }

        return (planes, frames, decoder);
    }

    private static void AssertExact(int[][] source, float[][] decoded, int bits)
    {
        var scale = 1.0 / (1L << (bits - 1));
        for (var c = 0; c < source.Length; c++)
        {
            Assert.Equal(source[c].Length, decoded[c].Length);
            for (var i = 0; i < source[c].Length; i++)
            {
                if ((float)(source[c][i] * scale) != decoded[c][i])
                {
                    Assert.Fail($"Channel {c} sample {i}: expected {source[c][i]}, decoded {decoded[c][i] / scale}.");
                }
            }
        }
    }

    private static int[][] Stereo(int samples, int bits, int seed = 1) =>
    [
        FlacBuilder.Tone(samples, bits, 440, 44_100, 0.6, seed),
        FlacBuilder.Tone(samples, bits, 660, 44_100, 0.5, seed + 1),
    ];

    private static void Release(List<AudioFrame> frames)
    {
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }

    public static TheoryData<int, FlacStereo> OrdersAndModes()
    {
        var data = new TheoryData<int, FlacStereo>();
        foreach (var order in new[] { 0, 1, 2, 3, 4 })
        {
            foreach (var stereo in Enum.GetValues<FlacStereo>())
            {
                data.Add(order, stereo);
            }
        }

        return data;
    }

    public static TheoryData<int, FlacStereo, FlacSubframeKind> SizesModesAndKinds()
    {
        var data = new TheoryData<int, FlacStereo, FlacSubframeKind>();
        foreach (var bits in new[] { 8, 12, 16, 20, 24, 32 })
        {
            foreach (var stereo in Enum.GetValues<FlacStereo>())
            {
                data.Add(bits, stereo, FlacSubframeKind.Verbatim);
                data.Add(bits, stereo, FlacSubframeKind.Fixed);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OrdersAndModes))]
    [Capability("FMT-A04")]
    public void FixedPredictorsOfEveryOrderDecodeExactlyInEveryStereoMode(int order, FlacStereo stereo)
    {
        var source = Stereo(5000, 16);
        var builder = new FlacBuilder { Stereo = stereo, BlockSize = 1152, Plan = (frame, _) => FlacSubframePlan.Fixed(order, frame % 4) };

        var (planes, frames, decoder) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Assert.Equal(0, decoder.CorruptFrames);
        Assert.Equal(MediaTime.FromSeconds(1), frames[1].Pts);
        Assert.Equal(ChannelLayout.Stereo, frames[0].Layout);
        Release(frames);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(32)]
    public void LinearPredictorsOfAnyOrderAndCoefficientsDecodeExactly(int order)
    {
        var random = new Random(order);
        var coefficients = Enumerable.Range(0, order).Select(_ => random.Next(-2048, 2048)).ToArray();
        var source = new[] { FlacBuilder.Tone(4096, 16, 1000, 44_100) };
        var builder = new FlacBuilder
        {
            Plan = (_, _) => new FlacSubframePlan { Kind = FlacSubframeKind.Lpc, Order = order, Coefficients = coefficients, Precision = 12, Shift = 10, PartitionOrder = 2 },
        };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Release(frames);
    }

    [Fact]
    public void ARealisticSecondOrderLinearPredictorDecodesExactly()
    {
        var source = Stereo(3000, 16);
        var builder = new FlacBuilder { Stereo = FlacStereo.MidSide, Plan = (_, _) => FlacSubframePlan.SecondOrderLpc() };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Release(frames);
    }

    [Theory]
    [MemberData(nameof(SizesModesAndKinds))]
    public void EverySampleSizeDecodesExactlyIncludingTheWideSideChannel(int bits, FlacStereo stereo, FlacSubframeKind kind)
    {
        var source = Stereo(2000, bits);
        var builder = new FlacBuilder
        {
            BitsPerSample = bits,
            Stereo = stereo,
            Plan = (_, _) => new FlacSubframePlan { Kind = kind, Order = 2, FiveBitParameters = bits > 16 },
        };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, bits);
        Release(frames);
    }

    [Fact]
    public void ConstantSubframesCarrySilenceAndSteadyValues()
    {
        var source = new[] { Enumerable.Repeat(0, 2304).ToArray(), Enumerable.Repeat(-1234, 2304).ToArray() };
        var builder = new FlacBuilder { Plan = (_, _) => new FlacSubframePlan { Kind = FlacSubframeKind.Constant } };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Release(frames);
    }

    [Fact]
    public void WastedLowBitsAreRestored()
    {
        var source = new[] { FlacBuilder.Tone(3000, 16, 300, 44_100).Select(v => v & ~0x1F).ToArray() };
        var builder = new FlacBuilder { Plan = (frame, _) => new FlacSubframePlan { Kind = frame % 2 == 0 ? FlacSubframeKind.Verbatim : FlacSubframeKind.Fixed, Order = 1 } };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Release(frames);
    }

    [Fact]
    public void EscapedPartitionsIncludingEmptyOnesDecodeExactly()
    {
        var ramp = Enumerable.Range(0, 4096).Select(i => i < 2048 ? 7 : i - 2048).ToArray();
        var builder = new FlacBuilder
        {
            Plan = (frame, _) => new FlacSubframePlan { Kind = FlacSubframeKind.Fixed, Order = 1, PartitionOrder = 3, Escape = true, FiveBitParameters = frame % 2 == 1 },
        };

        var (planes, frames, _) = RoundTrip(builder, [ramp]);

        AssertExact([ramp], planes, 16);
        Release(frames);
    }

    [Fact]
    public void LargeRiceParametersNeedFiveBitsAndDecodeExactly()
    {
        var random = new Random(3);
        var noise = Enumerable.Range(0, 2048).Select(_ => random.Next(int.MinValue, int.MaxValue)).ToArray();
        var builder = new FlacBuilder { BitsPerSample = 32, Plan = (_, _) => new FlacSubframePlan { Kind = FlacSubframeKind.Fixed, Order = 0, FiveBitParameters = true } };

        var (planes, frames, _) = RoundTrip(builder, [noise]);

        AssertExact([noise], planes, 32);
        Release(frames);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EveryChannelCountDecodesInItsSpeakerOrder(int count)
    {
        var source = Enumerable.Range(0, count).Select(c => FlacBuilder.Tone(1500, 16, 200 + (c * 100), 48_000, seed: c)).ToArray();
        var builder = new FlacBuilder { SampleRate = 48_000 };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Assert.Equal(FlacStreamInfo.Layout(count), frames[0].Layout);
        Release(frames);
    }

    [Fact]
    public void VariableBlockSizesAndFieldsLeftToStreamInfoDecode()
    {
        var source = Stereo(9000, 16);
        var builder = new FlacBuilder
        {
            VariableBlockSizes = [100, 4096, 17, 1000, 3000],
            RateFromStreamInfo = true,
            BitsFromStreamInfo = true,
        };

        var (planes, frames, _) = RoundTrip(builder, source);

        AssertExact(source, planes, 16);
        Assert.Equal([100, 4096, 17, 1000, 3000, 100, 687], frames.Select(f => f.SampleCount));
        Release(frames);
    }

    [Fact]
    public void BuffersGrowWhenAFrameIsLongerThanStreamInfoPromised()
    {
        var info = new FlacStreamInfo(16, 16, 0, 0, 44_100, 1, 16, 0, new byte[16]).ToBytes();
        using var decoder = new FlacDecoder(Track(44_100, 1, 16, info));
        var source = new[] { FlacBuilder.Tone(4096, 16, 440, 44_100) };
        var builder = new FlacBuilder { BlockSize = 4096 };
        var file = builder.Build(source);
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf(file.AsSpan((int)builder.FirstFrameOffset)), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        AssertExact(source, [output[0].Channel(0).ToArray()], 16);
        Release(output);
    }

    [Fact]
    public void ADamagedFrameBecomesSilenceOfItsLengthAndIsCounted()
    {
        var source = new[] { FlacBuilder.Tone(1152, 16, 440, 44_100) };
        var builder = new FlacBuilder();
        var frame = builder.Build(source)[(int)builder.FirstFrameOffset..];
        frame[frame.Length / 2] ^= 0x10;
        using var decoder = new FlacDecoder(Track(44_100, 1, 16));
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf(frame), MediaTime.FromSeconds(2), MediaTime.Unknown, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        var decoded = Assert.Single(output);
        Assert.Equal(1152, decoded.SampleCount);
        Assert.Equal(MediaTime.FromSeconds(2), decoded.Pts);
        Assert.All(decoded.Channel(0).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.Equal(1, decoder.CorruptFrames);
        Release(output);
    }

    [Fact]
    public void APacketWithNoHeaderIsDroppedAndCounted()
    {
        using var decoder = new FlacDecoder(Track(44_100, 1, 16));
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf([1, 2, 3, 4, 5, 6, 7, 8]), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        Assert.Empty(output);
        Assert.Equal(1, decoder.CorruptFrames);
    }

    private static readonly byte[] MonoHeader = FlacBuilder.FrameHeader(false, 16, 8000, 0, 8, 0);

    public static TheoryData<string, byte[], int> Malformed => new()
    {
        { "padding bit", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => w.Write(0b1_000001_0, 8)), 8 },
        { "reserved type", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => w.Write(0b0_000010_0, 8)), 8 },
        { "too many wasted bits", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_000000_1, 8); w.WriteUnary(7); }), 8 },
        { "forbidden precision", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_100000_0, 8); w.Write(0, 8); w.Write(15, 4); }), 8 },
        { "negative shift", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_100000_0, 8); w.Write(0, 8); w.Write(3, 4); w.WriteSigned(-1, 5); }), 8 },
        { "reserved residual method", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_001000_0, 8); w.Write(2, 2); }), 8 },
        { "partitions too small", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_001100_0, 8); w.Write(0, 32); w.Write(0, 2); w.Write(3, 4); }), 8 },
        { "partitions do not divide", FlacBuilder.Frame(FlacBuilder.FrameHeader(false, 17, 8000, 0, 8, 0), (ref BitWriter w) => { w.Write(0b0_001000_0, 8); w.Write(0, 2); w.Write(1, 4); }), 8 },
        { "order above block size", FlacBuilder.Frame(FlacBuilder.FrameHeader(false, 3, 8000, 0, 8, 0), (ref BitWriter w) => { w.Write(0b0_001100_0, 8); w.Write(0, 32); }), 8 },
        { "bitstream ends", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => w.Write(0b0_000001_0, 8)), 8 },
        { "no sample size anywhere", FlacBuilder.Frame(FlacBuilder.FrameHeader(false, 16, 8000, 0, 0, 0), (ref BitWriter w) => w.Write(0, 16)), 0 },
        { "wrong channel count", FlacBuilder.Frame(FlacBuilder.FrameHeader(false, 16, 8000, 1, 8, 0), (ref BitWriter w) => w.Write(0, 32)), 8 },
        { "bad checksum", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => w.Write(0, 16), breakCrc: true), 8 },
        { "checksum cut off", FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => w.Write(0, 16))[..^1], 8 },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedSubframesAreConcealedNotPlayed(string why, byte[] frame, int trackBits)
    {
        using var decoder = new FlacDecoder(Track(8000, 1, trackBits));
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf(frame), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        Assert.True(decoder.CorruptFrames == 1, why);
        Assert.All(Assert.Single(output).Channel(0).ToArray(), sample => Assert.Equal(0f, sample));
        Release(output);
    }

    [Fact]
    public void AWellFormedHandBuiltFrameDecodes()
    {
        var frame = FlacBuilder.Frame(MonoHeader, (ref BitWriter w) => { w.Write(0b0_000000_0, 8); w.WriteSigned(-64, 8); });
        using var decoder = new FlacDecoder(Track(8000, 1, 8));
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf(frame), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        Assert.Equal(0, decoder.CorruptFrames);
        Assert.All(Assert.Single(output).Channel(0).ToArray(), sample => Assert.Equal(-0.5f, sample));
        Release(output);
    }

    [Fact]
    public void TheFactoryOffersItselfForFlacTracksOnly()
    {
        var factory = new FlacDecoderFactory();

        Assert.Equal("rexplayer FLAC", factory.Name);
        Assert.Equal(DecoderSource.Own, factory.Source);
        Assert.Equal(100, factory.Rank);
        Assert.True(factory.CanDecode(Track(44_100, 2, 16)));
        Assert.False(factory.CanDecode(Track(44_100, 2, 16) with { Codec = CodecId.Mp3 }));
        Assert.False(factory.CanDecode(Track(44_100, 2, 16) with { Audio = null }));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(Track(44_100, 2, 16) with { Codec = CodecId.Mp3 }));
        using var decoder = factory.CreateAudio(Track(44_100, 2, 16));
        Assert.Equal("rexplayer FLAC", decoder.Name);
        Assert.Equal(DecoderSource.Own, decoder.Source);
        decoder.Flush();
        decoder.Drain(new List<AudioFrame>());
    }

    [Fact]
    public void ImpossibleTracksAreRefused()
    {
        Assert.Throws<NotSupportedException>(() => new FlacDecoder(Track(44_100, 2, 16) with { Audio = null }));
        Assert.Throws<NotSupportedException>(() => new FlacDecoder(Track(44_100, 0, 16)));
        Assert.Throws<NotSupportedException>(() => new FlacDecoder(Track(44_100, 9, 16)));
        Assert.Throws<NotSupportedException>(() => new FlacDecoder(Track(44_100, 2, 3)));
        Assert.Throws<NotSupportedException>(() => new FlacDecoder(Track(44_100, 2, 33)));
        Assert.Throws<ArgumentNullException>(() => new FlacDecoder(null!));
        using var decoder = new FlacDecoder(Track(44_100, 2, 16));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, []));
        Assert.Throws<ArgumentNullException>(() => new FlacDecoderFactory().CanDecode(null!));
        Assert.Throws<ArgumentNullException>(() => new FlacDecoderFactory().CreateAudio(null!));
    }
}
