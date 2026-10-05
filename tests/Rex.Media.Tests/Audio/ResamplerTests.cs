using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class ResamplerTests
{
    private static AudioFrame Frame(float[] samples, int rate, MediaTime? pts = null)
    {
        var frame = AudioFrame.Rent(rate, 1, samples.Length);
        samples.CopyTo(frame.Channel(0));
        frame.Pts = pts ?? MediaTime.Zero;
        return frame;
    }

    /// <summary>Runs a whole signal through in chunks and drains, returning every output sample.</summary>
    private static float[] Run(Resampler resampler, float[] input, int chunk = 1000)
    {
        var output = new List<float>();
        for (var offset = 0; offset < input.Length; offset += chunk)
        {
            var piece = input.AsSpan(offset, Math.Min(chunk, input.Length - offset)).ToArray();
            using var frame = Frame(piece, resampler.InputRate);
            using var produced = resampler.Process(frame);
            if (produced is not null)
            {
                output.AddRange(produced.Channel(0).ToArray());
            }
        }

        using (var tail = resampler.Drain(ChannelLayout.Mono))
        {
            if (tail is not null)
            {
                output.AddRange(tail.Channel(0).ToArray());
            }
        }

        return [.. output];
    }

    [Theory]
    [InlineData(44_100, 48_000)]
    [InlineData(48_000, 44_100)]
    [InlineData(22_050, 48_000)]
    [InlineData(8000, 48_000)]
    [InlineData(96_000, 48_000)]
    public void ATonePassesAtItsAmplitudeAndFrequency(int inputRate, int outputRate)
    {
        var input = Signals.Sine(1000, inputRate, inputRate / 2, amplitude: 0.5);

        var output = Run(new Resampler(inputRate, outputRate, 1), input);

        var middle = output.AsSpan(output.Length / 4, output.Length / 2);
        Assert.InRange(Signals.Amplitude(middle, outputRate, 1000), 0.495, 0.505);
        Assert.InRange(Signals.Amplitude(middle, outputRate, 3000), 0, 0.001);
    }

    [Theory]
    [InlineData(44_100, 48_000, 44_100)]
    [InlineData(48_000, 44_100, 48_000)]
    [InlineData(44_100, 47_999, 12_345)]
    [InlineData(8000, 48_000, 999)]
    public void TheOutputHasExactlyTheProportionalNumberOfSamples(int inputRate, int outputRate, int inputSamples)
    {
        var output = Run(new Resampler(inputRate, outputRate, 1), new float[inputSamples], chunk: 777);

        var expected = (long)Math.Ceiling(inputSamples * (double)outputRate / inputRate);
        Assert.Equal(expected, output.Length);
    }

    [Fact]
    public void DownsamplingRemovesWhatWouldAlias()
    {
        var input = Signals.Sine(6000, 48_000, 24_000, amplitude: 0.5);

        var output = Run(new Resampler(48_000, 8000, 1), input);

        Assert.InRange(Signals.Rms(output.AsSpan(200, output.Length - 400)), 0, 0.005);
    }

    [Fact]
    public void RatesWithTooManyPhasesInterpolateBetweenThem()
    {
        var input = Signals.Sine(1000, 44_100, 22_050, amplitude: 0.5);

        var output = Run(new Resampler(44_100, 47_999, 1), input);

        var middle = output.AsSpan(output.Length / 4, output.Length / 2);
        Assert.InRange(Signals.Amplitude(middle, 47_999, 1000), 0.49, 0.51);
    }

    [Theory]
    [InlineData(ResamplerQuality.Fast)]
    [InlineData(ResamplerQuality.Normal)]
    [InlineData(ResamplerQuality.High)]
    public void EveryQualityKeepsTheTone(ResamplerQuality quality)
    {
        var input = Signals.Sine(440, 44_100, 22_050, amplitude: 0.5);
        var resampler = new Resampler(44_100, 48_000, 1, quality);

        var output = Run(resampler, input);

        Assert.True(resampler.Latency > 0);
        Assert.InRange(Signals.Amplitude(output.AsSpan(output.Length / 4, output.Length / 2), 48_000, 440), 0.48, 0.52);
    }

    [Fact]
    public void TimestampsFollowTheFirstInputAndTheOutputCount()
    {
        var resampler = new Resampler(44_100, 48_000, 1);
        using var first = Frame(new float[4410], 44_100, MediaTime.FromSeconds(10));
        first.Generation = 3;

        using var produced = resampler.Process(first)!;

        Assert.Equal(MediaTime.FromSeconds(10), produced.Pts);
        Assert.Equal(3, produced.Generation);
        using var second = Frame(new float[4410], 44_100, MediaTime.FromSeconds(10.1));
        using var next = resampler.Process(second)!;
        Assert.Equal(MediaTime.FromSeconds(10) + MediaTime.FromSamples(produced.SampleCount, 48_000), next.Pts);
    }

    [Fact]
    public void ResetStartsANewTimelineWithEmptyHistory()
    {
        var resampler = new Resampler(44_100, 48_000, 1);
        using (var frame = Frame(Signals.Sine(1000, 44_100, 4410), 44_100))
        {
            resampler.Process(frame)?.Dispose();
        }

        resampler.Reset(MediaTime.FromSeconds(30));
        using var silence = Frame(new float[4410], 44_100, MediaTime.FromSeconds(99));
        using var produced = resampler.Process(silence)!;

        Assert.Equal(MediaTime.FromSeconds(30), produced.Pts);
        Assert.Equal(0, Signals.Peak(produced.Channel(0)));
    }

    [Fact]
    public void UnknownTimestampsStayUnknownUntilOneArrives()
    {
        var resampler = new Resampler(8000, 16_000, 1);
        using var frame = Frame(new float[800], 8000);
        frame.Pts = MediaTime.Unknown;

        using var produced = resampler.Process(frame)!;

        Assert.False(produced.Pts.IsKnown);
    }

    [Fact]
    public void TooLittleInputProducesNothingYet()
    {
        var resampler = new Resampler(44_100, 48_000, 1);
        using var frame = Frame(new float[3], 44_100);

        Assert.Null(resampler.Process(frame));
    }

    [Fact]
    public void DrainingTwiceProducesNothingTheSecondTime()
    {
        var resampler = new Resampler(44_100, 48_000, 1);
        Run(resampler, new float[1000]);

        Assert.Null(resampler.Drain(ChannelLayout.Mono));
    }

    [Fact]
    public void ALargeFrameGrowsTheHistory()
    {
        var input = Signals.Sine(1000, 44_100, 44_100, amplitude: 0.5);

        var output = Run(new Resampler(44_100, 48_000, 1), input, chunk: 44_100);

        Assert.Equal(48_000, output.Length);
    }

    [Fact]
    public void StereoChannelsStaySeparate()
    {
        var resampler = new Resampler(44_100, 48_000, 2);
        using var frame = AudioFrame.Rent(44_100, 2, 4410);
        Signals.Sine(1000, 44_100, 4410, 0.5).CopyTo(frame.Channel(0));
        frame.Channel(1).Clear();

        using var produced = resampler.Process(frame)!;

        Assert.True(Signals.Peak(produced.Channel(0)) > 0.4);
        Assert.Equal(0, Signals.Peak(produced.Channel(1)));
    }

    [Fact]
    public void MismatchedFramesAndBadSettingsAreRejected()
    {
        var resampler = new Resampler(44_100, 48_000, 1);
        using var wrongRate = Frame(new float[10], 48_000);
        using var wrongChannels = AudioFrame.Rent(44_100, 2, 10);

        Assert.Throws<InvalidOperationException>(() => resampler.Process(wrongRate));
        Assert.Throws<InvalidOperationException>(() => resampler.Process(wrongChannels));
        Assert.Throws<ArgumentNullException>(() => resampler.Process(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resampler(0, 48_000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resampler(44_100, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Resampler(44_100, 48_000, 0));
    }

    [Fact]
    public void EveryPhaseOfTheFilterHasUnityGainAtDc()
    {
        var table = Resampler.BuildTable(160, 24, 0.465, 8.6);

        for (var phase = 0; phase <= 160; phase++)
        {
            var sum = table.AsSpan(phase * 48, 48).ToArray().Sum();
            Assert.InRange(sum, 0.9999f, 1.0001f);
        }
    }
}
