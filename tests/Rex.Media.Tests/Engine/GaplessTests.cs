using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

/// <summary>A run of media played one item after another into one output, with nothing between them.</summary>
public sealed class GaplessTests
{
    /// <summary>A mono 8 kHz WAV whose samples count up from <paramref name="first"/>, so every sample says where it came from.</summary>
    private static byte[] Count(int first, int samples)
    {
        var plane = Enumerable.Range(first, samples).Select(v => v / 32768f).ToArray();
        return WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(plane)).Build();
    }

    private static float[] Expected(params (int First, int Samples)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Range(run.First, run.Samples).Select(v => v / 32768f))];

    private sealed class TrackedSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data, "tracked.wav");

        public bool Disposed { get; private set; }

        public string Name => _inner.Name;

        public long? Length => _inner.Length;

        public bool CanSeek => true;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose()
        {
            Disposed = true;
            _inner.Dispose();
        }
    }

    [Fact]
    [Capability("PB-14")]
    public async Task QueuedItemsFollowEachOtherSampleForSample()
    {
        using var harness = new SessionHarness();

        await harness.Session.OpenAsync(SessionHarness.Source(Count(0, 800)), [SessionHarness.Source(Count(1000, 800)), SessionHarness.Source(Count(2000, 400))]);
        await harness.FinishAsync();

        // Events arrive in order on the event thread, so once Ended has, every item's opening has too.
        harness.WaitFor<EndedEvent>();
        Assert.Equal(Expected((0, 800), (1000, 800), (2000, 400)), harness.Recording.Channel(0));
        Assert.Equal(3, harness.Events.OfType<MediaOpenedEvent>().Count());
        Assert.Single(harness.Events.OfType<EndedEvent>());
        var stats = await harness.Session.GetStatsAsync();
        Assert.Equal(3, stats.ItemsStarted);
        Assert.Equal(MediaTime.FromSeconds(0.2), stats.EarlierItemsDuration);
        Assert.Equal(MediaTime.FromSamples(400, 8000), harness.Session.Duration);
    }

    [Fact]
    public async Task MediaQueuedAfterTheEndPlaysOn()
    {
        using var harness = new SessionHarness();
        await harness.Session.OpenAsync(SessionHarness.Source(Count(0, 400)));
        await harness.FinishAsync();

        await harness.Session.QueueNextAsync(SessionHarness.Source(Count(500, 400)));
        harness.WaitFor<StateChangedEvent>(e => e.From == SessionState.Ended && e.To == SessionState.Playing);
        await harness.FinishAsync();

        Assert.Equal(Expected((0, 400), (500, 400)), harness.Recording.Channel(0));
        Assert.Equal(SessionState.Ended, harness.Session.State);
    }

    [Fact]
    public async Task QueueingWithNothingOpenOpensIt()
    {
        using var harness = new SessionHarness();

        await harness.Session.QueueNextAsync(SessionHarness.Source(Count(0, 400)));
        await harness.FinishAsync();

        Assert.Equal(Expected((0, 400)), harness.Recording.Channel(0));
    }

    [Fact]
    public async Task AQueuedItemThatCannotPlayIsSkipped()
    {
        using var harness = new SessionHarness();

        await harness.Session.OpenAsync(SessionHarness.Source(Count(0, 400)), [SessionHarness.Source(new byte[100], "noise.bin"), SessionHarness.Source(Count(900, 400))]);
        await harness.FinishAsync();

        Assert.Equal(Expected((0, 400), (900, 400)), harness.Recording.Channel(0));
        var skipped = harness.WaitFor<ItemSkippedEvent>();
        Assert.Equal("noise.bin", skipped.Name);
        Assert.StartsWith("rexplayer does not recognise", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASeekAfterTheDemuxerMovedOnAppliesToTheNextItem()
    {
        using var release = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => release.Wait(TestContext.Current.CancellationToken);
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Count(0, 800)), [SessionHarness.Source(Count(10_000, 8000))]);

        // The output is held on the first item while the demuxer reads on into the second.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await harness.Session.GetStatsAsync()).PacketsRead < 3)
        {
            Assert.True(DateTime.UtcNow < deadline, "The demuxer never reached the second item.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await harness.Session.SeekAsync(MediaTime.FromSamples(400, 8000));
        release.Set();
        await harness.FinishAsync();

        var recorded = sink.Channel(0);
        Assert.Equal(Expected((10_400, 7600)), recorded[^7600..]);
        Assert.Equal(2, (await harness.Session.GetStatsAsync()).ItemsStarted);
    }

    [Fact]
    public async Task AHandOverReportedAfterAStopIsIgnored()
    {
        using var decoder = new JoinGateDecoderFactory();
        using var harness = new SessionHarness(decoder: decoder);
        await harness.Session.OpenAsync(SessionHarness.Source(Count(0, 400)), [SessionHarness.Source(Count(500, 400))]);
        Assert.True(decoder.AtJoin.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The stop is queued before the audio thread reports the hand-over, so the report finds
        // the session already closed and changes nothing.
        var stop = harness.Session.StopAsync();
        decoder.Release.Set();
        await stop;
        await harness.Session.GetStatsAsync();

        Assert.Single(harness.Events.OfType<MediaOpenedEvent>());
        Assert.Equal(SessionState.Idle, harness.Session.State);
    }

    [Fact]
    public async Task MediaStillQueuedWhenTheSessionStopsIsReleased()
    {
        // The output takes nothing until the session closes, so the demuxer fills the audio queue and
        // stops long before the end of a minute of audio: the queued media is still waiting to be opened.
        var queued = new TrackedSource(Count(0, 400));
        var minute = WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[480_000])).Build();
        using (var harness = new SessionHarness(sink: new ClosedSink()))
        {
            await harness.Session.OpenAsync(SessionHarness.Source(minute), [queued]);
            await harness.Session.QueueNextAsync(new TrackedSource(Count(0, 400)));
        }

        Assert.True(queued.Disposed);
    }

    /// <summary>An output whose writes wait until the session closes.</summary>
    private sealed class ClosedSink : IAudioSink
    {
        public string Name => "closed";

        public long PlayedSamples => 0;

        public long QueuedSamples => 0;

        public bool IsRealTime => true;

        public AudioFormat Open(AudioFormat preferred) => preferred;

        public void Write(AudioFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }

        public void Flush()
        {
        }

        public void Drain(CancellationToken cancellationToken)
        {
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task AFailedOpenReleasesTheMediaQueuedBehindIt()
    {
        var queued = new TrackedSource(Count(0, 400));
        using var harness = new SessionHarness();

        await Assert.ThrowsAsync<MediaFormatException>(() => harness.Session.OpenAsync(SessionHarness.Source(new byte[100], "noise.bin"), [queued]));

        Assert.True(queued.Disposed);
        Assert.Throws<ArgumentNullException>(() => { _ = harness.Session.OpenAsync(SessionHarness.Source(Count(0, 1)), (IReadOnlyList<IByteSource>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = harness.Session.QueueNextAsync(null!); });
    }
}
