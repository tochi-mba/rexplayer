using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class TimeStretchTests
{
    private const int Rate = 48_000;

    /// <summary>Two seconds of a stereo 440 Hz tone through a stretch, fed in 1024-sample frames.</summary>
    private static (float[] Left, float[] Right) Stretch(double speed)
    {
        var tone = Signals.Sine(440, Rate, Rate * 2, amplitude: 0.5);
        var stretch = new TimeStretch(Rate, 2, speed);
        var left = new List<float>();
        var right = new List<float>();
        for (var start = 0; start < tone.Length; start += 1024)
        {
            var count = Math.Min(1024, tone.Length - start);
            using var frame = AudioFrame.Rent(Rate, 2, count, ChannelLayout.Stereo);
            frame.Pts = MediaTime.FromSamples(start, Rate);
            tone.AsSpan(start, count).CopyTo(frame.Channel(0));
            tone.AsSpan(start, count).CopyTo(frame.Channel(1));
            if (stretch.Process(frame) is { } output)
            {
                left.AddRange(output.Channel(0)[..output.SampleCount].ToArray());
                right.AddRange(output.Channel(1)[..output.SampleCount].ToArray());
                if (!ReferenceEquals(output, frame))
                {
                    output.Dispose();
                }
            }
        }

        return ([.. left], [.. right]);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.5)]
    [InlineData(0.5)]
    [InlineData(0.25)]
    [InlineData(4.0)]
    [Capability("AU-15")]
    public void TheSpeedChangesHowLongItLastsButNotItsPitch(double speed)
    {
        var (left, right) = Stretch(speed);

        // Output follows the speed, short of the tail still waiting for the next frame: a frame and
        // the search either side of it, in input samples, which last longer the slower the speed.
        var waiting = ((Rate / 25) + (Rate / 100)) / speed;
        Assert.InRange(left.Length, (Rate * 2 / speed) - waiting - (Rate / 50), (Rate * 2 / speed) + 10);

        // The tone stays at 440 Hz: strong there, nothing an octave away, with both channels alike.
        var middle = left.AsSpan(left.Length / 4, left.Length / 2);
        Assert.InRange(Signals.Amplitude(middle, Rate, 440), 0.4, 0.6);
        Assert.True(Signals.Amplitude(middle, Rate, 880) < 0.05);
        Assert.True(Signals.Amplitude(middle, Rate, 220) < 0.05);
        Assert.Equal(left, right);
    }

    [Fact]
    public void AtNormalSpeedTheSoundPassesStraightThrough()
    {
        var stretch = new TimeStretch(Rate, 1, 1);
        using var frame = AudioFrame.Rent(Rate, 1, 8);

        Assert.Same(frame, stretch.Process(frame));
        Assert.True(stretch.Fits(Rate, 1));
        Assert.False(stretch.Fits(44_100, 1));
        Assert.False(stretch.Fits(Rate, 2));
    }

    [Fact]
    public void TimestampsFollowTheMediaAndAResetStartsAgain()
    {
        var stretch = new TimeStretch(Rate, 1, 2);
        using var first = AudioFrame.Rent(Rate, 1, Rate / 2);
        first.Channel(0)[..first.SampleCount].Clear();
        first.Pts = MediaTime.FromSeconds(10);
        using var output = stretch.Process(first)!;
        Assert.Equal(MediaTime.FromSeconds(10), output.Pts);

        stretch.Reset(MediaTime.FromSeconds(30));
        using var small = AudioFrame.Rent(Rate, 1, 100);
        small.Channel(0)[..100].Clear();
        Assert.Null(stretch.Process(small));
        using var more = AudioFrame.Rent(Rate, 1, Rate / 2);
        more.Channel(0)[..more.SampleCount].Clear();
        more.Pts = MediaTime.Unknown;
        using var after = stretch.Process(more)!;
        Assert.Equal(MediaTime.FromSeconds(30), after.Pts);
    }

    [Fact]
    public void FramesWithoutTimestampsOrWithFewerChannelsStillStretch()
    {
        var stretch = new TimeStretch(Rate, 2, 2);
        using var mono = AudioFrame.Rent(Rate, 1, Rate / 2);
        mono.Channel(0)[..mono.SampleCount].Fill(0.25f);
        mono.Pts = MediaTime.Unknown;

        using var output = stretch.Process(mono)!;

        Assert.False(output.Pts.IsKnown);
        Assert.Equal(2, output.Channels);
        Assert.All(output.Channel(1)[..output.SampleCount].ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(4.5)]
    [InlineData(double.NaN)]
    public void SpeedsOutsideTheRangeAreRefused(double speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeStretch(Rate, 1, speed));
    }

    [Fact]
    public void ImpossibleFormatsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeStretch(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeStretch(Rate, 0, 1));
        Assert.Throws<ArgumentNullException>(() => new TimeStretch(Rate, 1, 2).Process(null!));
    }

    [Fact]
    public void ThePipelineWaitsForEnoughSoundToStretchThenConvertsIt()
    {
        var pipeline = new AudioPipeline(new AudioFormat(Rate, 1, SampleFormat.F32)) { Speed = 2 };
        using var small = AudioFrame.Rent(Rate, 1, 100);
        small.Channel(0)[..100].Clear();
        Assert.Null(pipeline.Process(small));

        using var more = AudioFrame.Rent(Rate, 1, Rate / 2);
        more.Channel(0)[..more.SampleCount].Clear();
        using var output = pipeline.Process(more)!;
        Assert.True(output.SampleCount > 0);
        pipeline.Reset(MediaTime.Zero);
    }
}
