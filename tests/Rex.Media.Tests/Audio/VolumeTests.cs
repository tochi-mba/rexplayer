using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class VolumeTests
{
    private static AudioFrame Ones(int count = 4)
    {
        var frame = AudioFrame.Rent(48_000, 1, count);
        frame.Channel(0).Fill(1f);
        return frame;
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(0.5, 0.25f)]
    [InlineData(1, 1f)]
    [InlineData(2, 4f)]
    [InlineData(3, 4f)]
    [InlineData(-1, 0f)]
    public void TheSliderFollowsASquareCurve(double volume, float gain)
    {
        Assert.Equal(gain, VolumeProcessor.Gain(volume));
    }

    [Fact]
    public void DecibelsAreReportedForTheOsd()
    {
        Assert.Equal(double.NegativeInfinity, VolumeProcessor.Decibels(0));
        Assert.Equal(0, VolumeProcessor.Decibels(1), 6);
        Assert.Equal(-12.04, VolumeProcessor.Decibels(0.5), 2);
    }

    [Fact]
    [Capability("AU-05")]
    public void UnityLeavesSamplesUntouchedAndAChangeRampsAcrossOneFrame()
    {
        var volume = new VolumeProcessor();
        using (var frame = Ones())
        {
            volume.Process(frame);
            Assert.Equal([1f, 1f, 1f, 1f], frame.Channel(0).ToArray());
        }

        volume.Volume = 0.5;
        using (var ramp = Ones())
        {
            volume.Process(ramp);
            Assert.Equal([0.8125f, 0.625f, 0.4375f, 0.25f], ramp.Channel(0).ToArray());
        }

        using var steady = Ones();
        volume.Process(steady);
        Assert.Equal([0.25f, 0.25f, 0.25f, 0.25f], steady.Channel(0).ToArray());
    }

    [Fact]
    [Capability("AU-06")]
    public void MuteRampsToSilence()
    {
        var volume = new VolumeProcessor { Muted = true };
        using (var first = Ones())
        {
            volume.Process(first);
        }

        using var silent = Ones();
        volume.Process(silent);

        Assert.All(silent.Channel(0).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.True(volume.Muted);
    }

    [Fact]
    public void VolumeIsClampedAndNonsenseIsUnity()
    {
        var volume = new VolumeProcessor { Volume = 5 };
        Assert.Equal(VolumeProcessor.Maximum, volume.Volume);
        volume.Volume = -1;
        Assert.Equal(0, volume.Volume);
        volume.Volume = double.NaN;
        Assert.Equal(1, volume.Volume);
        Assert.Throws<ArgumentNullException>(() => volume.Process(null!));
    }

    [Fact]
    public void AnEmptyFrameRampsWithoutDividingByZero()
    {
        var volume = new VolumeProcessor { Volume = 0.5 };
        using var empty = AudioFrame.Rent(48_000, 1, 0);

        volume.Process(empty);

        Assert.Equal(0, empty.SampleCount);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.9f, 0.9f)]
    [InlineData(-0.9f, -0.9f)]
    public void TheClipperLeavesTheLinearRangeAlone(float input, float output)
    {
        Assert.Equal(output, SoftClipper.Apply(input));
    }

    [Fact]
    public void TheClipperBendsSmoothlyTowardsFullScale()
    {
        var one = SoftClipper.Apply(1f);
        var big = SoftClipper.Apply(4f);

        Assert.InRange(one, 0.97f, 0.98f);
        Assert.InRange(big, 0.999f, 1f);
        Assert.Equal(-one, SoftClipper.Apply(-1f));
        Assert.True(SoftClipper.Apply(0.95f) > 0.9f);
    }

    [Fact]
    public void TheClipperProcessesEveryChannel()
    {
        using var frame = AudioFrame.Rent(48_000, 2, 1);
        frame.Channel(0)[0] = 1.1f;
        frame.Channel(1)[0] = -1.1f;

        SoftClipper.Process(frame);

        Assert.True(frame.Channel(0)[0] < 1f);
        Assert.True(frame.Channel(1)[0] > -1f);
        Assert.Throws<ArgumentNullException>(() => SoftClipper.Process(null!));
    }
}
