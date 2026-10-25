using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class PitchSpeedTests
{
    private const int Rate = 48_000;

    private static float[] Render(double speed, bool preserve)
    {
        var tone = Signals.Sine(440, Rate, Rate * 2, amplitude: 0.5);
        var pipeline = new AudioPipeline(new AudioFormat(Rate, 1, SampleFormat.F32))
        {
            Speed = speed,
            PreservePitch = preserve,
        };
        var samples = new List<float>();
        for (var start = 0; start < tone.Length; start += 1024)
        {
            var length = Math.Min(1024, tone.Length - start);
            using var frame = AudioFrame.Rent(Rate, 1, length);
            frame.Pts = MediaTime.FromSamples(start, Rate);
            tone.AsSpan(start, length).CopyTo(frame.Channel(0));
            if (pipeline.Process(frame) is { } output)
            {
                samples.AddRange(output.Channel(0)[..output.SampleCount].ToArray());
                if (!ReferenceEquals(output, frame)) output.Dispose();
            }
        }

        return [.. samples];
    }

    [Theory]
    [InlineData(2.0, 880.0)]
    [InlineData(0.5, 220.0)]
    public void DeliberateSpeedShiftChangesBothPitchAndDuration(double speed, double pitch)
    {
        var output = Render(speed, preserve: false);
        Assert.InRange(output.Length, (int)(Rate * 2 / speed) - 100,
            (int)(Rate * 2 / speed) + 100);
        var middle = output.AsSpan(output.Length / 4, output.Length / 2);
        Assert.InRange(Signals.Amplitude(middle, Rate, pitch), 0.35, 0.65);
        Assert.True(Signals.Amplitude(middle, Rate, 440) < 0.08);
    }

    [Fact]
    public void RestartResetsTheRateShiftAndTheTimestamp()
    {
        var processor = new PitchSpeed(Rate, 1, 2);
        using var frame = AudioFrame.Rent(Rate, 1, 64);
        frame.Pts = MediaTime.FromSeconds(10);
        frame.Channel(0)[..frame.SampleCount].Fill(0.5f);
        using var output = processor.Process(frame)!;
        Assert.Equal(MediaTime.FromSeconds(10), output.Pts);
        Assert.True(processor.Fits(Rate, 1));
        Assert.False(processor.Fits(Rate, 2));

        processor.Reset(MediaTime.FromSeconds(30));
        using var after = processor.Process(frame)!;
        Assert.Equal(MediaTime.FromSeconds(30), after.Pts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(5)]
    public void InvalidSpeedsAreRefused(double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PitchSpeed(Rate, 1, speed));
}
