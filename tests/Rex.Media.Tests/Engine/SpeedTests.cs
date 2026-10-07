using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

/// <summary>Playing faster or slower: the sound is stretched, and the clock counts media time at the new speed.</summary>
public sealed class SpeedTests
{
    /// <summary>A second of a mono 8 kHz tone.</summary>
    private static byte[] Second() => WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(Signals.Sine(440, 8000, 8000))).Build();

    [Fact]
    [Capability("PB-06")]
    public async Task AtDoubleSpeedASecondOfMediaTakesHalfASecond()
    {
        using var harness = new SessionHarness();

        await harness.Session.SetSpeedAsync(2);
        await harness.Session.OpenAsync(SessionHarness.Source(Second()));
        await harness.FinishAsync();

        // Half a second at the output's rate, short of the stretch's last frame.
        var rate = harness.Recording.Format!.SampleRate;
        Assert.InRange(harness.Recording.Channel(0).Length, (rate / 2) - (rate / 10), rate / 2);
        Assert.Equal(2, harness.Session.Speed);
        Assert.Equal(MediaTime.FromSeconds(1), harness.Session.Position);
    }

    [Fact]
    public async Task ASpeedChosenWhilePausedTakesEffectWhenPlayingStarts()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Second()));

        await harness.Session.SetSpeedAsync(0.5);
        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        var rate = harness.Recording.Format!.SampleRate;
        Assert.InRange(harness.Recording.Channel(0).Length, (rate * 2) - (rate / 5), rate * 2);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(5.0)]
    [InlineData(double.NaN)]
    public void SpeedsOutsideTheRangeAreRefused(double speed)
    {
        using var harness = new SessionHarness();

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = harness.Session.SetSpeedAsync(speed); });
    }

    [Fact]
    public void TheAudioClockCountsMediaTimeAtTheSpeed()
    {
        var sink = new RecordingAudioSink(channels: 1);
        var clock = new AudioClock(sink);

        clock.Rebase(MediaTime.FromSeconds(10), 1000, 2);

        Assert.Equal(MediaTime.FromSeconds(10), clock.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Rebase(MediaTime.Zero, 1000, 0));
    }
}
