using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Mpeg;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// Layer III paths that encoders rarely take, proved with oracles that need no second decoder: the
/// synthesis is linear and identical for both channels, so intensity stereo must leave the right
/// channel exactly k times the left, a mixed block whose long part is silent must equal the pure
/// short block, and decoding the sum of two spectra must give the sum of their outputs.
/// </summary>
public sealed class Mp3DecoderTests
{
    private static readonly byte[] Mpeg1Joint = Header(3, 14, modeExtension: 1);
    private static readonly byte[] LowRateJoint = Header(2, 14, modeExtension: 1);

    private static byte[] Header(int version, int bitrateIndex, int mode = 1, int modeExtension = 0, bool crc = false) =>
        MpegAudioHeaderTests.Header(version, 1, bitrateIndex, 0, crc: crc, mode: mode, modeExtension: modeExtension);

    private static TrackInfo Track(int rate, int channels) => new()
    {
        Id = 0,
        Codec = CodecId.Mp3,
        Audio = new AudioTrackInfo { SampleRate = rate, Channels = channels },
    };

    private static float[][] Decode(byte[] frame, int channels, int rate, Mp3Decoder? decoder = null)
    {
        var owned = decoder is null;
        decoder ??= new Mp3Decoder(Track(rate, channels));
        try
        {
            var output = new List<AudioFrame>();
            using var packet = Packet.Create(0, MediaBuffer.CopyOf(frame), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
            decoder.Decode(packet, output);
            Assert.Equal(0, decoder.CorruptFrames);
            var decoded = Assert.Single(output);
            var planes = Enumerable.Range(0, channels).Select(c => decoded.Channel(c).ToArray()).ToArray();
            decoded.Dispose();
            return planes;
        }
        finally
        {
            if (owned)
            {
                decoder.Dispose();
            }
        }
    }

    /// <summary>Granules for every channel, made fresh per granule so a test can vary them.</summary>
    private static Layer3Channel[][] Granules(int granules, int channels, Func<int, Layer3Channel> make) =>
        [.. Enumerable.Range(0, granules).Select(_ => Enumerable.Range(0, channels).Select(make).ToArray())];

    private static void Fill(Layer3Channel channel, int from, int to, int seed = 1)
    {
        var random = new Random(seed);
        for (var i = from; i < to; i++)
        {
            channel.Values[i] = random.Next(-9, 10);
        }
    }

    private static double Peak(float[] samples) => samples.Max(s => Math.Abs((double)s));

    private static void AssertScaled(float[] actual, float[] reference, double factor)
    {
        var peak = Peak(reference);
        Assert.True(peak > 1e-4, "The reference is silent, so the comparison proves nothing.");
        for (var i = 0; i < actual.Length; i++)
        {
            if (Math.Abs(actual[i] - (factor * reference[i])) > 2e-5 * Math.Max(peak, 1))
            {
                Assert.Fail($"Sample {i}: {actual[i]} is not {factor} x {reference[i]}.");
            }
        }
    }

    private static void AssertSilent(float[] samples) => Assert.True(Peak(samples) < 1e-7, $"Expected silence, got peak {Peak(samples)}.");

    /// <summary>Left carries lines [from, to); right is silent and its scalefactors hold the intensity position.</summary>
    private static Layer3Channel[][] Intensity(int granules, Func<Layer3Channel> make, int position, int from = 100, int to = 140, Action<Layer3Channel>? right = null)
    {
        return Granules(granules, 2, ch =>
        {
            var channel = make();
            if (ch == 0)
            {
                Fill(channel, from, to);
            }
            else
            {
                Array.Fill(channel.ScalefacLong, position);
                Array.Fill(channel.ScalefacShort, position);
                right?.Invoke(channel);
            }

            return channel;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [Capability("FMT-A02")]
    public void Mpeg1IntensityStereoSplitsTheSumByThePositionsRatio(int position)
    {
        var frame = Layer3FrameWriter.Frame(Mpeg1Joint, Intensity(2, () => new Layer3Channel { ScalefacCompress = 15 }, position));

        var (left, right) = Unpack(Decode(frame, 2, 44_100));

        var ratio = Math.Tan(position * Math.PI / 12);
        if (position == 0)
        {
            AssertSilent(left);
            Assert.True(Peak(right) > 1e-3);
        }
        else if (position == 6)
        {
            AssertSilent(right);
        }
        else
        {
            AssertScaled(right, left, 1 / ratio);
        }
    }

    [Fact]
    public void TheIllegalPositionLeavesTheBandToMidSideOrAlone()
    {
        var plain = Unpack(Decode(Layer3FrameWriter.Frame(Header(3, 14, modeExtension: 0), Intensity(2, () => new Layer3Channel { ScalefacCompress = 15 }, 7)), 2, 44_100));
        var intensityOnly = Unpack(Decode(Layer3FrameWriter.Frame(Mpeg1Joint, Intensity(2, () => new Layer3Channel { ScalefacCompress = 15 }, 7)), 2, 44_100));
        var withMidSide = Unpack(Decode(Layer3FrameWriter.Frame(Header(3, 14, modeExtension: 3), Intensity(2, () => new Layer3Channel { ScalefacCompress = 15 }, 7)), 2, 44_100));

        AssertScaled(intensityOnly.Left, plain.Left, 1);
        AssertSilent(intensityOnly.Right);
        AssertScaled(withMidSide.Left, plain.Left, Math.Sqrt(0.5));
        AssertScaled(withMidSide.Right, plain.Left, Math.Sqrt(0.5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BelowTheIntensityBoundMidSideApplyAndTheWholeIsTheSumOfItsParts(bool shortBlocks)
    {
        Layer3Channel Make() => shortBlocks ? Layer3Channel.Short() : new Layer3Channel { ScalefacCompress = 15 };
        Layer3Channel[][] Build(bool low, bool high) => Granules(2, 2, ch =>
        {
            var channel = Make();
            channel.ScalefacCompress = 15;
            if (low)
            {
                Fill(channel, 0, 30, seed: 10 + ch);
            }

            if (high && ch == 0)
            {
                Fill(channel, 300, 340, seed: 20);
            }

            if (ch == 1)
            {
                Array.Fill(channel.ScalefacLong, 2);
                Array.Fill(channel.ScalefacShort, 2);
            }

            return channel;
        });
        var header = Header(3, 14, modeExtension: 3);

        var lows = Decode(Layer3FrameWriter.Frame(header, Build(true, false)), 2, 44_100);
        var highs = Decode(Layer3FrameWriter.Frame(header, Build(false, true)), 2, 44_100);
        var both = Decode(Layer3FrameWriter.Frame(header, Build(true, true)), 2, 44_100);

        for (var c = 0; c < 2; c++)
        {
            var sum = lows[c].Zip(highs[c], (a, b) => a + b).ToArray();
            AssertScaled(both[c], sum, 1);
        }
    }

    [Fact]
    public void ShortBlockIntensityWorksWindowByWindow()
    {
        var frame = Layer3FrameWriter.Frame(Mpeg1Joint, Intensity(2, () => { var c = Layer3Channel.Short(); c.ScalefacCompress = 15; return c; }, 2, from: 120, to: 300));

        var (left, right) = Unpack(Decode(frame, 2, 44_100));

        AssertScaled(right, left, 1 / Math.Tan(2 * Math.PI / 12));
    }

    [Fact]
    public void ShortBlockIntensityStartsAboveTheLastWindowWithRightChannelAudio()
    {
        // The right channel has audio in band 1 of window 1 only: that window's intensity coding
        // starts above it, the other windows' at the bottom. Every band holds position 6, which
        // sends all of the sum to the left, so the right channel is exactly its own band.
        Layer3Channel[][] Build(bool withLeft) => Intensity(2, () => { var c = Layer3Channel.Short(); c.ScalefacCompress = 15; return c; }, 6, from: 150, to: withLeft ? 200 : 150, right: c =>
        {
            c.Values[(4 * 3) + 4 + 1] = 5;
        });

        var withLeft = Unpack(Decode(Layer3FrameWriter.Frame(Header(3, 14, modeExtension: 1), Build(true)), 2, 44_100));
        var rightOnly = Unpack(Decode(Layer3FrameWriter.Frame(Header(3, 14, modeExtension: 1), Build(false)), 2, 44_100));

        AssertScaled(withLeft.Right, rightOnly.Right, 1);
    }

    [Fact]
    public void MixedBlockIntensityCodesTheShortPartAndMidSidesTheLongPart()
    {
        var frame = Layer3FrameWriter.Frame(Header(3, 14, modeExtension: 3), Intensity(2, () => { var c = Layer3Channel.Short(mixed: true); c.ScalefacCompress = 15; return c; }, 1, from: 150, to: 260));

        var (left, right) = Unpack(Decode(frame, 2, 44_100));

        AssertScaled(right, left, 1 / Math.Tan(Math.PI / 12));
    }

    public static TheoryData<int, int> LowRateTables => new()
    {
        { 86, 3 },
        { 180 + 42, 4 },
        { 244 + 8, 5 },
    };

    [Theory]
    [MemberData(nameof(LowRateTables))]
    public void LowRateIntensityFollowsTheExponentialSteps(int compress, int table)
    {
        int[][] sizes = [[7, 7, 7, 0], [6, 6, 6, 3], [8, 8, 5, 0]];
        int[][] slens = [[2, 2, 2, 0], [2, 2, 2, 0], [2, 2, 0, 0]];
        foreach (var scale in new[] { 0, 1 })
        {
            foreach (var position in new[] { 0, 1, 2 })
            {
                var frame = Layer3FrameWriter.Frame(LowRateJoint, Intensity(1, () => new Layer3Channel(), position, from: 40, to: 100, right: c =>
                {
                    c.ScalefacCompress = (compress << 1) | scale;
                    c.LowRatePartitions = sizes[table - 3];
                    c.LowRateSlen = slens[table - 3];
                }));

                var (left, right) = Unpack(Decode(frame, 2, 22_050));

                var step = scale == 1 ? Math.Sqrt(0.5) : Math.Pow(2, -0.25);
                var (kl, kr) = position == 0 ? (1.0, 1.0) : position % 2 == 1 ? (Math.Pow(step, (position + 1) / 2), 1.0) : (1.0, Math.Pow(step, position / 2));
                AssertScaled(right, left, kr / kl);
            }
        }
    }

    [Fact]
    public void ALowRateIllegalPositionIsTheLargestItsFieldHolds()
    {
        var frame = Layer3FrameWriter.Frame(LowRateJoint, Intensity(1, () => new Layer3Channel(), 3, from: 40, to: 100, right: c =>
        {
            c.ScalefacCompress = 86 << 1;
            c.LowRatePartitions = [7, 7, 7, 0];
            c.LowRateSlen = [2, 2, 2, 0];
        }));

        var (_, right) = Unpack(Decode(frame, 2, 22_050));

        AssertSilent(right);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LowRateShortAndMixedIntensityMapScalefactorsToWindows(bool mixed)
    {
        var frame = Layer3FrameWriter.Frame(LowRateJoint, Intensity(1, () => Layer3Channel.Short(mixed), 1, from: 120, to: 260, right: c =>
        {
            c.ScalefacCompress = 86 << 1;
            c.LowRatePartitions = mixed ? [6, 15, 12, 0] : [12, 12, 12, 0];
            c.LowRateSlen = [2, 2, 2, 0];
        }));

        var (left, right) = Unpack(Decode(frame, 2, 22_050));

        AssertScaled(right, left, Math.Pow(2, 0.25));
    }

    public static TheoryData<int, int, int[], int[], int[]> MixedCases => new()
    {
        { 3, 15, [], [], [] },
        { 2, 0, [0, 0, 0, 0], [9, 9, 9, 9], [6, 9, 9, 9] },
        { 2, 400 + 50, [2, 2, 2, 0], [9, 9, 12, 6], [6, 9, 12, 6] },
        { 2, 500 + 5, [1, 2, 0, 0], [18, 18, 0, 0], [15, 18, 0, 0] },
    };

    [Theory]
    [MemberData(nameof(MixedCases))]
    public void AMixedBlockWithASilentLongPartEqualsThePureShortBlock(int version, int compress, int[] slen, int[] shortSizes, int[] mixedSizes)
    {
        var rate = version == 3 ? 44_100 : 22_050;
        Layer3Channel Make(bool mixed)
        {
            var channel = Layer3Channel.Short(mixed);
            channel.ScalefacCompress = compress;
            channel.LowRateSlen = slen.Length == 0 ? [0, 0, 0, 0] : slen;
            channel.LowRatePartitions = mixed ? mixedSizes : shortSizes;
            Fill(channel, 36, 400, seed: 4);

            // Short bands 3 and up carry scalefactors of 0 or 1, which every field width here can hold.
            for (var i = 9; i < 36; i++)
            {
                channel.ScalefacShort[i] = i % 2;
            }

            return channel;
        }

        var header = MpegAudioHeaderTests.Header(version, 1, 14, 0, mode: 3);
        var granules = version == 3 ? 2 : 1;

        var mixedOutput = Decode(Layer3FrameWriter.Frame(header, Granules(granules, 1, _ => Make(true))), 1, rate)[0];
        var shortOutput = Decode(Layer3FrameWriter.Frame(header, Granules(granules, 1, _ => Make(false))), 1, rate)[0];

        AssertScaled(mixedOutput, shortOutput, 1);
    }
    [Fact]
    public void SecondGranuleScalefactorsCanBeReusedFromTheFirst()
    {
        var mono = Header(3, 14, mode: 3);
        Layer3Channel Make()
        {
            var channel = new Layer3Channel { ScalefacCompress = 11 };
            Fill(channel, 0, 200);
            for (var sfb = 0; sfb < 21; sfb++)
            {
                channel.ScalefacLong[sfb] = sfb % 3;
            }

            return channel;
        }

        var reused = Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => Make()), scfsi: [[true, false, true, true], [false, false, false, false]]);
        var explicitly = Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => Make()));

        AssertScaled(Decode(reused, 1, 44_100)[0], Decode(explicitly, 1, 44_100)[0], 1);
    }

    [Fact]
    public void ACrcProtectedFrameDecodesLikeAnUnprotectedOne()
    {
        var granules = Granules(2, 1, _ =>
        {
            var channel = new Layer3Channel();
            Fill(channel, 0, 100);
            return channel;
        });

        var protectedFrame = Decode(Layer3FrameWriter.Frame(Header(3, 14, mode: 3, crc: true), granules), 1, 44_100)[0];
        var plain = Decode(Layer3FrameWriter.Frame(Header(3, 14, mode: 3), granules), 1, 44_100)[0];

        AssertScaled(protectedFrame, plain, 1);
    }

    [Fact]
    public void CountOneQuadruplesDecodeTheSameFromEitherTable()
    {
        Layer3Channel Make(bool tableB)
        {
            var channel = new Layer3Channel { Count1Start = 100, Count1TableB = tableB, Preflag = true, ScalefacScale = true };
            Fill(channel, 0, 100);
            for (var i = 100; i < 160; i++)
            {
                channel.Values[i] = (i % 5) - 2 is var v && Math.Abs(v) <= 1 ? v : 0;
            }

            return channel;
        }

        var mono = Header(3, 14, mode: 3);

        AssertScaled(
            Decode(Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => Make(true))), 1, 44_100)[0],
            Decode(Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => Make(false))), 1, 44_100)[0],
            1);
    }

    [Fact]
    public void FlushingForgetsTheReservoirAndTheFilterHistory()
    {
        var frame = Layer3FrameWriter.Frame(Header(3, 14, mode: 3), Granules(2, 1, _ =>
        {
            var channel = new Layer3Channel();
            Fill(channel, 0, 300);
            return channel;
        }));
        using var decoder = new Mp3Decoder(Track(44_100, 1));

        var first = Decode(frame, 1, 44_100, decoder)[0];
        var second = Decode(frame, 1, 44_100, decoder)[0];
        decoder.Flush();
        var afterFlush = Decode(frame, 1, 44_100, decoder)[0];

        Assert.NotEqual(first, second);
        Assert.Equal(first, afterFlush);
    }

    [Fact]
    public void AFrameWhoseDataStartsBeforeAnythingSeenIsSilentNotDamaged()
    {
        var frame = Layer3FrameWriter.Frame(Header(3, 14, mode: 3), Granules(2, 1, _ => new Layer3Channel()), mainDataBegin: 300);

        AssertSilent(Decode(frame, 1, 44_100)[0]);
    }

    public static TheoryData<string, byte[], int> Malformed()
    {
        var mono = Header(3, 14, mode: 3);
        return new TheoryData<string, byte[], int>
        {
            { "too many big values", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => new Layer3Channel { BigValuesOverride = 289 })), 1 },
            { "normal block with switching", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => new Layer3Channel { WindowSwitching = true, BlockType = 0 })), 1 },
            { "scalefactors overrun", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => new Layer3Channel { ScalefacCompress = 15, Part23Override = 10 })), 1 },
            { "missing table", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => { var c = new Layer3Channel { TableSelect = [4, 24, 24] }; c.Values[0] = 1; return c; })), 1 },
            { "shorter than its side information", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => new Layer3Channel()))[..10], 1 },
            { "mono frame in a stereo stream", Layer3FrameWriter.Frame(mono, Granules(2, 1, _ => new Layer3Channel())), 2 },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void DamagedFramesBecomeSilenceAndAreCounted(string why, byte[] frame, int channels)
    {
        using var decoder = new Mp3Decoder(Track(44_100, channels));
        var output = new List<AudioFrame>();

        using var packet = Packet.Create(0, MediaBuffer.CopyOf(frame), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
        decoder.Decode(packet, output);

        Assert.True(decoder.CorruptFrames == 1, why);
        var decoded = Assert.Single(output);
        AssertSilent(decoded.Channel(0).ToArray());
        decoded.Dispose();
    }

    [Fact]
    public void PacketsThatAreNotLayerThreeFramesAreDropped()
    {
        using var decoder = new Mp3Decoder(Track(44_100, 2));
        var output = new List<AudioFrame>();
        var layer2 = MpegAudioHeaderTests.Header(3, 2, 9, 0);

        foreach (var bytes in new[] { layer2, [1, 2, 3, 4] })
        {
            using var packet = Packet.Create(0, MediaBuffer.CopyOf(bytes), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
            decoder.Decode(packet, output);
        }

        Assert.Empty(output);
        Assert.Equal(2, decoder.CorruptFrames);
    }

    [Fact]
    public void TheFactoryOffersItselfForMp3TracksOnly()
    {
        var factory = new Mp3DecoderFactory();

        Assert.Equal(("rexplayer MP3", DecoderSource.Own, 100), (factory.Name, factory.Source, factory.Rank));
        Assert.True(factory.CanDecode(Track(44_100, 2)));
        Assert.False(factory.CanDecode(Track(44_100, 2) with { Codec = CodecId.Mp2 }));
        Assert.False(factory.CanDecode(Track(44_100, 2) with { Audio = null }));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(Track(44_100, 2) with { Codec = CodecId.Aac }));
        using var decoder = factory.CreateAudio(Track(44_100, 2));
        Assert.Equal(("rexplayer MP3", DecoderSource.Own), (decoder.Name, decoder.Source));
        decoder.Drain(new List<AudioFrame>());
        Assert.Throws<NotSupportedException>(() => new Mp3Decoder(Track(44_100, 3)));
        Assert.Throws<NotSupportedException>(() => new Mp3Decoder(Track(44_100, 2) with { Audio = null }));
        Assert.Throws<ArgumentNullException>(() => new Mp3Decoder(null!));
        Assert.Throws<ArgumentNullException>(() => factory.CanDecode(null!));
        Assert.Throws<ArgumentNullException>(() => factory.CreateAudio(null!));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, []));
    }

    private static (float[] Left, float[] Right) Unpack(float[][] planes) => (planes[0], planes[1]);
}
