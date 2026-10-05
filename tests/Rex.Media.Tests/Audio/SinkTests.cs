using Rex.Media.Audio;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class SinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rexplayer-sink-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static AudioFrame Frame(params float[] samples)
    {
        var frame = AudioFrame.Rent(8000, 1, samples.Length);
        samples.CopyTo(frame.Channel(0));
        return frame;
    }

    private static float[] ReadBack(string path)
    {
        using var demuxer = new WavDemuxer(new FileByteSource(path), CancellationToken.None);
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

        return [.. samples];
    }

    [Theory]
    [InlineData(SampleFormat.F32)]
    [InlineData(SampleFormat.S16)]
    [InlineData(SampleFormat.S24)]
    public void AWavCaptureReadsBackAsWhatWasWritten(SampleFormat storage)
    {
        var path = Path.Combine(_directory, "capture.wav");
        using (var sink = new WavFileSink(path, storage))
        {
            var format = sink.Open(new AudioFormat(8000, 1, SampleFormat.S16));
            Assert.Equal(SampleFormat.F32, format.SampleFormat);
            using var frame = Frame(0.5f, -0.25f, 0f);
            sink.Write(frame, CancellationToken.None);
            sink.Write(frame, CancellationToken.None);
            Assert.Equal(6, sink.PlayedSamples);
            Assert.Equal(6, sink.TotalSamples);
            Assert.Equal(0, sink.QueuedSamples);
            Assert.False(sink.IsRealTime);
            Assert.Equal("WAV file capture.wav", sink.Name);
            sink.Pause();
            sink.Resume();
            sink.Drain(CancellationToken.None);
            sink.Flush();
            Assert.Equal(0, sink.PlayedSamples);
        }

        Assert.Equal([0.5f, -0.25f, 0f, 0.5f, -0.25f, 0f], ReadBack(path));
    }

    [Fact]
    public void ReopeningACaptureStartsTheFileAgain()
    {
        var path = Path.Combine(_directory, "again.wav");
        using (var sink = new WavFileSink(path))
        {
            sink.Open(new AudioFormat(8000, 1, SampleFormat.F32));
            using (var first = Frame(1f))
            {
                sink.Write(first, CancellationToken.None);
            }

            sink.Open(new AudioFormat(8000, 1, SampleFormat.F32));
            using var second = Frame(0.5f);
            sink.Write(second, CancellationToken.None);
        }

        Assert.Equal([0.5f], ReadBack(path));
    }

    [Fact]
    public void ACaptureRefusesBadUse()
    {
        var path = Path.Combine(_directory, "bad.wav");
        using var sink = new WavFileSink(path);
        using var frame = Frame(0f);

        Assert.Throws<InvalidOperationException>(() => sink.Write(frame, CancellationToken.None));
        sink.Open(new AudioFormat(16_000, 1, SampleFormat.F32));
        Assert.Throws<InvalidOperationException>(() => sink.Write(frame, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => sink.Write(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => sink.Open(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WavFileSink(path, SampleFormat.U8));
        Assert.Throws<ArgumentException>(() => new WavFileSink(" "));
    }

    [Fact]
    public void DisposingANeverOpenedCaptureDoesNothing()
    {
        var sink = new WavFileSink(Path.Combine(_directory, "never.wav"));
        sink.Drain(CancellationToken.None);
        sink.Dispose();

        Assert.False(File.Exists(Path.Combine(_directory, "never.wav")));
    }

    [Fact]
    public void TheHeaderDescribesFloatAndIntegerStorage()
    {
        var floatHeader = WavFileSink.Header(new AudioFormat(48_000, 2, SampleFormat.F32), 100);
        var intHeader = WavFileSink.Header(new AudioFormat(48_000, 2, SampleFormat.S16), 100);

        Assert.Equal(3, BitConverter.ToUInt16(floatHeader, 20));
        Assert.Equal(1, BitConverter.ToUInt16(intHeader, 20));
        Assert.Equal(136u, BitConverter.ToUInt32(floatHeader, 4));
        Assert.Equal(100u, BitConverter.ToUInt32(floatHeader, 40));
    }

    [Theory]
    [InlineData(SampleFormat.S16, new byte[] { 0x00, 0x40 })]
    [InlineData(SampleFormat.S24, new byte[] { 0x00, 0x00, 0x40 })]
    [InlineData(SampleFormat.S32, new byte[] { 0x00, 0x00, 0x00, 0x40 })]
    [InlineData(SampleFormat.F32, new byte[] { 0x00, 0x00, 0x00, 0x3F })]
    public void SamplesInterleaveIntoEachStorageFormat(SampleFormat storage, byte[] expected)
    {
        using var frame = Frame(0.5f);
        var bytes = new byte[expected.Length];

        SampleConverter.Interleave(frame, storage, bytes);

        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void ConversionSaturatesInsteadOfWrapping()
    {
        Assert.Equal(short.MaxValue, SampleConverter.ToInt16(2f));
        Assert.Equal(short.MinValue, SampleConverter.ToInt16(-2f));
        Assert.Equal(8388607, SampleConverter.ToInt24(2f));
        Assert.Equal(-8388608, SampleConverter.ToInt24(-2f));
        Assert.Equal(int.MaxValue, SampleConverter.ToInt32(2f));
        Assert.Equal(int.MinValue, SampleConverter.ToInt32(-2f));
    }

    [Fact]
    public void InterleavingChecksItsArguments()
    {
        using var frame = Frame(0f, 0f);

        Assert.Throws<ArgumentOutOfRangeException>(() => SampleConverter.Interleave(frame, SampleFormat.U8, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleConverter.Interleave(frame, SampleFormat.S16, new byte[3]));
        Assert.Throws<ArgumentNullException>(() => SampleConverter.Interleave(null!, SampleFormat.S16, new byte[4]));
    }

    [Fact]
    public void TheNullSinkPlaysAtTheSpeedOfItsClock()
    {
        var clock = new ManualTimeProvider();
        using var sink = new NullAudioSink(clock, (_, _) => clock.Advance(TimeSpan.FromMilliseconds(5)));
        sink.Open(new AudioFormat(48_000, 2, SampleFormat.S16));
        using var frame = AudioFrame.Rent(48_000, 2, 4800);

        sink.Write(frame, CancellationToken.None);
        Assert.Equal(0, sink.PlayedSamples);
        Assert.Equal(4800, sink.QueuedSamples);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(2400, sink.PlayedSamples);

        sink.Pause();
        sink.Pause();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2400, sink.PlayedSamples);
        sink.Resume();
        sink.Resume();
        clock.Advance(TimeSpan.FromMilliseconds(25));
        Assert.Equal(3600, sink.PlayedSamples);

        sink.Drain(CancellationToken.None);
        Assert.Equal(4800, sink.PlayedSamples);
        Assert.True(sink.IsRealTime);
        Assert.Equal("No audio device", sink.Name);
    }

    [Fact]
    public void TheNullSinkBlocksWritesBeyondItsBuffer()
    {
        var clock = new ManualTimeProvider();
        var waits = 0;
        using var sink = new NullAudioSink(clock, (_, _) =>
        {
            waits++;
            clock.Advance(TimeSpan.FromMilliseconds(5));
        });
        sink.Open(new AudioFormat(48_000, 1, SampleFormat.F32));
        using var second = AudioFrame.Rent(48_000, 1, 48_000);

        sink.Write(second, CancellationToken.None);
        sink.Write(second, CancellationToken.None);

        Assert.True(waits >= 150);
        Assert.Equal(TimeSpan.FromMilliseconds(200), sink.Buffer);
    }

    [Fact]
    public void FlushingTheNullSinkRestartsTheCountEvenWhilePaused()
    {
        var clock = new ManualTimeProvider();
        using var sink = new NullAudioSink(clock);
        sink.Open(new AudioFormat(48_000, 1, SampleFormat.F32));
        using var frame = AudioFrame.Rent(48_000, 1, 480);
        sink.Write(frame, CancellationToken.None);
        sink.Pause();

        sink.Flush();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, sink.PlayedSamples);
        sink.Resume();
        sink.Write(frame, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(5));
        Assert.Equal(240, sink.PlayedSamples);
    }

    [Fact]
    public void TheNullSinkHonoursCancellationAndChecksItsState()
    {
        var clock = new ManualTimeProvider();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var sink = new NullAudioSink(clock, (_, _) => { });
        using var frame = AudioFrame.Rent(48_000, 1, 48_000);

        Assert.Equal(0, sink.PlayedSamples);
        Assert.Throws<InvalidOperationException>(() => sink.Write(frame, CancellationToken.None));
        sink.Open(new AudioFormat(48_000, 1, SampleFormat.F32));
        sink.Write(frame, CancellationToken.None);
        Assert.Throws<OperationCanceledException>(() => sink.Write(frame, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => sink.Drain(cancelled.Token));
        Assert.Throws<ArgumentNullException>(() => sink.Write(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => sink.Open(null!));
    }

    [Fact]
    public void TheNullSinkDefaultsToTheSystemClock()
    {
        using var sink = new NullAudioSink();
        sink.Open(new AudioFormat(48_000, 1, SampleFormat.F32));
        using var frame = AudioFrame.Rent(48_000, 1, 48);
        sink.Write(frame, CancellationToken.None);

        sink.Drain(CancellationToken.None);

        Assert.Equal(48, sink.PlayedSamples);
    }

    [Theory]
    [InlineData(32, true, SampleFormat.F32)]
    [InlineData(16, false, SampleFormat.S16)]
    [InlineData(24, false, SampleFormat.S24)]
    [InlineData(32, false, SampleFormat.S32)]
    public void DeviceFormatsMapToStorage(int bits, bool isFloat, SampleFormat storage)
    {
        var (frames, actual) = DeviceFormatPolicy.FromMixFormat(48_000, 2, bits, (uint)ChannelLayout.Stereo, isFloat);

        Assert.Equal(storage, actual);
        Assert.Equal(SampleFormat.F32, frames.SampleFormat);
        Assert.Equal(ChannelLayout.Stereo, frames.Layout);
    }

    [Fact]
    public void UnusualDeviceFormatsAreRefusedAndOddMasksIgnored()
    {
        Assert.Throws<NotSupportedException>(() => DeviceFormatPolicy.FromMixFormat(48_000, 2, 64, 3, isFloat: true));
        Assert.Throws<NotSupportedException>(() => DeviceFormatPolicy.FromMixFormat(48_000, 2, 8, 3, isFloat: false));
        Assert.Equal(ChannelLayout.Surround51, DeviceFormatPolicy.FromMixFormat(48_000, 6, 32, (uint)ChannelLayout.Stereo, true).Frames.Layout);
    }
}
