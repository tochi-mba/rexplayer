using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class AudioPipelineTests
{
    private static AudioFrame Tone(int rate, ChannelLayout layout, int count, MediaTime pts)
    {
        var channels = layout.ChannelCount();
        var frame = AudioFrame.Rent(rate, channels, count, layout);
        var tone = Signals.Sine(1000, rate, count, 0.5);
        for (var c = 0; c < channels; c++)
        {
            tone.CopyTo(frame.Channel(c));
        }

        frame.Pts = pts;
        return frame;
    }

    [Fact]
    public void AMatchingFormatIsCopiedThrough()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32));
        using var input = Tone(48_000, ChannelLayout.Stereo, 480, MediaTime.FromSeconds(1));

        using var output = pipeline.Process(input)!;

        Assert.NotSame(input, output);
        Assert.Equal(input.Channel(0).ToArray(), output.Channel(0).ToArray());
        Assert.Equal(MediaTime.FromSeconds(1), output.Pts);
        Assert.Null(pipeline.Drain());
        Assert.Equal(48_000, pipeline.Target.SampleRate);
    }

    [Fact]
    public void ADownmixIsDoneBeforeResampling()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32));
        using var input = Tone(44_100, ChannelLayout.Surround51, 4410, MediaTime.Zero);

        using var output = pipeline.Process(input)!;
        using var tail = pipeline.Drain()!;

        Assert.Equal(2, output.Channels);
        Assert.Equal(48_000, output.SampleRate);
        Assert.Equal(4800, output.SampleCount + tail.SampleCount);
        Assert.Equal(2, tail.Channels);
    }

    [Fact]
    public void AnUpmixIsDoneAfterResampling()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32));
        using var input = Tone(44_100, ChannelLayout.Mono, 4410, MediaTime.Zero);

        using var output = pipeline.Process(input)!;
        using var tail = pipeline.Drain()!;

        Assert.Equal(2, output.Channels);
        Assert.Equal(2, tail.Channels);
        Assert.Equal(4800, output.SampleCount + tail.SampleCount);
    }

    [Fact]
    public void AMixOnlyChangeNeedsNoResampler()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 1, SampleFormat.F32));
        using var input = Tone(48_000, ChannelLayout.Stereo, 48, MediaTime.Zero);

        using var output = pipeline.Process(input)!;

        Assert.Equal(1, output.Channels);
        Assert.Null(pipeline.Drain());
    }

    [Fact]
    public void ResampleOnlyKeepsTheLayout()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32), ResamplerQuality.Fast);
        using var tiny = Tone(44_100, ChannelLayout.Stereo, 2, MediaTime.Zero);

        Assert.Null(pipeline.Process(tiny));
        using var input = Tone(44_100, ChannelLayout.Stereo, 4410, MediaTime.Zero);
        using var output = pipeline.Process(input)!;
        Assert.Equal(2, output.Channels);
        Assert.Equal(48_000, output.SampleRate);
    }

    [Fact]
    public void ADrainWithNothingPendingReturnsNothing()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32));
        using (var input = Tone(44_100, ChannelLayout.Stereo, 441, MediaTime.Zero))
        {
            pipeline.Process(input)?.Dispose();
        }

        pipeline.Drain()?.Dispose();
        Assert.Null(pipeline.Drain());
    }

    [Fact]
    public void AFormatChangeMidStreamRebuildsTheStages()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32));
        using (var first = Tone(48_000, ChannelLayout.Stereo, 480, MediaTime.Zero))
        {
            pipeline.Process(first)!.Dispose();
        }

        using var second = Tone(44_100, ChannelLayout.Mono, 4410, MediaTime.FromSeconds(1));
        using var output = pipeline.Process(second)!;

        Assert.Equal(2, output.Channels);
        Assert.Equal(48_000, output.SampleRate);
        Assert.Equal(MediaTime.FromSeconds(1), output.Pts);
    }

    [Fact]
    public void ResetRestartsTheResamplerTimeline()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 1, SampleFormat.F32));
        using (var first = Tone(44_100, ChannelLayout.Mono, 4410, MediaTime.Zero))
        {
            pipeline.Process(first)?.Dispose();
        }

        pipeline.Reset(MediaTime.FromSeconds(20));
        using var second = Tone(44_100, ChannelLayout.Mono, 4410, MediaTime.FromSeconds(99));
        using var output = pipeline.Process(second)!;

        Assert.Equal(MediaTime.FromSeconds(20), output.Pts);
    }

    [Fact]
    public void VolumeAndClippingApplyToTheOutput()
    {
        var pipeline = new AudioPipeline(new AudioFormat(48_000, 1, SampleFormat.F32));
        pipeline.Volume.Volume = 2;
        using (var warm = Tone(48_000, ChannelLayout.Mono, 48, MediaTime.Zero))
        {
            pipeline.Process(warm)!.Dispose();
        }

        using var input = Tone(48_000, ChannelLayout.Mono, 480, MediaTime.Zero);
        using var output = pipeline.Process(input)!;

        Assert.InRange(Signals.Peak(output.Channel(0)), 0.95f, 1f);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new AudioPipeline(null!));
        Assert.Throws<ArgumentNullException>(() => new AudioPipeline(new AudioFormat(48_000, 2, SampleFormat.F32)).Process(null!));
    }
}
