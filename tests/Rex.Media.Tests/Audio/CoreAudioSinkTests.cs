using Rex.Media.Audio.CoreAudio;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Audio;

/// <summary>
/// The Mac's audio output driven for real (ADR-0021): only on a Mac, and only where it has an
/// output device; elsewhere each test says why it did not run. Silence is played, so nothing is heard.
/// </summary>
public sealed class CoreAudioSinkTests
{
    private const int Rate = 48_000;

    private static CoreAudioSink OpenOrSkip(int channels, out AudioFormat format)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Mac's audio output exists only on a Mac.");
        var sink = new CoreAudioSink();
        try
        {
            format = sink.Open(new AudioFormat(Rate, channels, SampleFormat.F32));
            return sink;
        }
        catch (IOException ex)
        {
            sink.Dispose();
            Assert.Skip("This Mac has no audio output: " + ex.Message);
            throw;
        }
    }

    private static void Play(CoreAudioSink sink, AudioFormat format, int frames)
    {
        using var frame = AudioFrame.Rent(format.SampleRate, format.Channels, frames, format.Layout);
        sink.Write(frame, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheDeviceTakesFloatsAtTheAskedRateAndAtMostTwoChannels()
    {
        using var sink = OpenOrSkip(channels: 6, out var format);

        Assert.Equal(new AudioFormat(Rate, 2, SampleFormat.F32, ChannelLayout.Stereo), format);
        Assert.True(sink.IsRealTime);
        Assert.Equal("Mac default audio output", sink.Name);
    }

    [Fact]
    public void OnlyWhatTheDeviceHasPulledCountsAsPlayedAndDrainWaitsForAllOfIt()
    {
        using var sink = OpenOrSkip(channels: 2, out var format);

        Play(sink, format, Rate / 2);
        Assert.InRange(sink.PlayedSamples + sink.QueuedSamples, Rate / 2, Rate / 2);
        sink.Drain(TestContext.Current.CancellationToken);

        Assert.Equal(Rate / 2, sink.PlayedSamples);
        Assert.Equal(0, sink.QueuedSamples);
    }

    [Fact]
    public void PausingHoldsTheClockAndFlushingStartsItAgain()
    {
        using var sink = OpenOrSkip(channels: 1, out var format);
        sink.Pause();

        Play(sink, format, Rate / 10);
        var held = sink.PlayedSamples;
        Thread.Sleep(100);
        Assert.Equal(held, sink.PlayedSamples);

        sink.Flush();
        Assert.Equal((0L, 0L), (sink.PlayedSamples, sink.QueuedSamples));
        sink.Resume();
        Play(sink, format, Rate / 10);
        sink.Drain(TestContext.Current.CancellationToken);
        Assert.Equal(Rate / 10, sink.PlayedSamples);
    }
}
