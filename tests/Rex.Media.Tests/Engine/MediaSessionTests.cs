using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

public sealed class MediaSessionTests
{
    private static readonly byte[] Ramp = Pcm.RampWav(8000, 8000);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static float RampValue(int sample) => (short)(sample & 0x7FFF) / 32768f;

    [Fact]
    [Capability("PB-01")]
    public async Task AFilePlaysToTheEndSampleForSample()
    {
        using var harness = new SessionHarness();

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        var samples = harness.Recording.Channel(0);
        Assert.Equal(8000, samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(RampValue(i), samples[i]);
        }

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(MediaTime.FromSeconds(1), harness.Session.Position);
        Assert.Equal(MediaTime.FromSeconds(1), harness.Session.Duration);
        harness.WaitForState(SessionState.Ended);
        Assert.Equal([SessionState.Opening, SessionState.Playing, SessionState.Ended], harness.States);
        harness.WaitFor<MediaOpenedEvent>();
        Assert.Equal(0, harness.WaitFor<TracksChangedEvent>().AudioTrack);
        Assert.Contains("drain", harness.Recording.Calls);
        Assert.NotNull(harness.Session.Info);
    }

    [Fact]
    [Capability("PB-03")]
    public async Task APreciseSeekLandsOnTheExactSample()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        Assert.Equal(SessionState.Ready, harness.Session.State);

        await harness.Session.SeekAsync(MediaTime.FromSamples(4321, 8000));
        var completed = harness.WaitFor<SeekCompletedEvent>();
        harness.WaitForState(SessionState.Paused);
        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        var samples = harness.Recording.Channel(0);
        Assert.Equal(MediaTime.FromSamples(4321, 8000), completed.Position);
        Assert.Equal(8000 - 4321, samples.Length);
        Assert.Equal(RampValue(4321), samples[0]);
        Assert.Equal(RampValue(7999), samples[^1]);
    }

    [Fact]
    public async Task AKeyframeSeekStartsAtTheContainingPacket()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        await harness.Session.SeekAsync(MediaTime.FromSamples(4321, 8000), SeekMode.Keyframe);
        harness.WaitFor<SeekCompletedEvent>();
        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        Assert.Equal(RampValue(4321), harness.Recording.Channel(0)[0]);
    }

    [Fact]
    public async Task OpeningAtAStartTimeSkipsWhatComesBefore()
    {
        using var harness = new SessionHarness();

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp), MediaTime.FromMilliseconds(250));
        await harness.FinishAsync();

        var samples = harness.Recording.Channel(0);
        Assert.Equal(6000, samples.Length);
        Assert.Equal(RampValue(2000), samples[0]);
    }

    [Fact]
    [Capability("PB-02")]
    public async Task PauseHoldsTheAudioAndResumeContinuesIt()
    {
        using var firstWrite = new ManualResetEventSlim(false);
        using var proceed = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ =>
        {
            firstWrite.Set();
            proceed.Wait(Token);
        };
        using var harness = new SessionHarness(sink: sink);

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        Assert.True(firstWrite.Wait(5000, Token));
        await harness.Session.PauseAsync();
        Assert.Equal(SessionState.Paused, harness.Session.State);
        proceed.Set();
        await Task.Delay(100, Token);
        Assert.True(sink.SampleCount < 8000);

        await harness.Session.TogglePauseAsync();
        await harness.FinishAsync();

        Assert.Equal(8000, sink.SampleCount);
        Assert.Contains("pause", sink.Calls);
        Assert.Contains("resume", sink.Calls);
        harness.WaitForState(SessionState.Ended);
        Assert.Equal([SessionState.Opening, SessionState.Playing, SessionState.Paused, SessionState.Playing, SessionState.Ended], harness.States);
    }

    [Fact]
    public async Task TogglingWhilePlayingPauses()
    {
        using var proceed = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => proceed.Wait(Token);
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        await harness.Session.TogglePauseAsync();
        proceed.Set();

        Assert.Equal(SessionState.Paused, harness.Session.State);
    }

    [Fact]
    public async Task PlayingAfterTheEndStartsAgainFromTheBeginning()
    {
        using var harness = new SessionHarness();
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();
        harness.WaitFor<EndedEvent>();

        await harness.Session.PlayAsync();
        harness.WaitFor<SeekCompletedEvent>();
        harness.WaitFor<EndedEvent>(_ => harness.Events.OfType<EndedEvent>().Count() >= 2);

        Assert.Equal(16_000, harness.Recording.SampleCount);
        Assert.Equal(RampValue(0), harness.Recording.Channel(0)[8000]);
    }

    [Fact]
    public async Task ASeekToTheEndWhilePausedEndsTheMedia()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.5));
        await harness.Session.SeekAsync(MediaTime.FromSeconds(50));
        harness.WaitFor<SeekCompletedEvent>(e => e.Position == MediaTime.FromSeconds(1));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(0, harness.Recording.SampleCount);
    }

    [Fact]
    public async Task PlayDuringASeekResumesWhenTheSeekCompletes()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.5));

        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(4000, harness.Recording.SampleCount);
    }

    [Fact]
    public async Task PausingDuringASeekKeepsItPaused()
    {
        using var harness = new SessionHarness(autoPlay: false);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.5));
        await harness.Session.PauseAsync();
        harness.WaitForState(SessionState.Paused);
        harness.WaitFor<SeekCompletedEvent>();

        Assert.Equal(SessionState.Paused, harness.Session.State);
    }

    [Fact]
    public async Task StoppingReleasesEverythingAndReturnsToIdle()
    {
        using var proceed = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => proceed.Wait(Token);
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        var stop = harness.Session.StopAsync();
        proceed.Set();
        await stop;

        Assert.Equal(SessionState.Idle, harness.Session.State);
        Assert.True(sink.Disposed);
        Assert.Null(harness.Session.Info);
        Assert.Equal(MediaTime.Zero, harness.Session.Position);
        Assert.False(harness.Session.Duration.IsKnown);
        Assert.True(harness.Session.WaitForFinishAsync().IsCompleted);
    }

    [Fact]
    public async Task OpeningAgainReplacesWhatWasPlaying()
    {
        using var harness = new SessionHarness();
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        await harness.Session.OpenAsync(SessionHarness.Source(Pcm.RampWav(8000, 800)));
        await harness.FinishAsync();

        Assert.Equal(MediaTime.FromMilliseconds(100), harness.Session.Duration);
    }

    [Fact]
    public async Task AnUnknownFormatFaultsTheSessionWithAPlainMessage()
    {
        using var harness = new SessionHarness();

        await Assert.ThrowsAsync<MediaFormatException>(() => harness.Session.OpenAsync(SessionHarness.Source([1, 2, 3, 4], "noise.bin")));

        Assert.Equal(SessionState.Faulted, harness.Session.State);
        Assert.StartsWith("rexplayer does not recognise", harness.WaitFor<ErrorEvent>().Message, StringComparison.Ordinal);
        Assert.True(harness.Session.WaitForFinishAsync().IsCompleted);
    }

    [Fact]
    public async Task AFaultedSessionCanOpenSomethingElse()
    {
        using var harness = new SessionHarness();
        await Assert.ThrowsAsync<MediaFormatException>(() => harness.Session.OpenAsync(SessionHarness.Source([1, 2, 3, 4])));

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
    }

    [Fact]
    public async Task ATrackNoDecoderCanHandleIsReported()
    {
        using var harness = new SessionHarness();
        var mp3InWav = new WavBuilder().Format(0x55, 2, 44_100, 1, 0).Data(new byte[100]).Build();

        await Assert.ThrowsAsync<NotSupportedException>(() => harness.Session.OpenAsync(SessionHarness.Source(mp3InWav)));

        Assert.Equal("No decoder for MP3 is available.", harness.WaitFor<TrackFailedEvent>().Reason);
        Assert.Equal(SessionState.Faulted, harness.Session.State);
    }

    [Fact]
    public async Task MediaWithoutAudioIsNotPlayableYet()
    {
        using var harness = new SessionHarness(demuxer: new SilentDemuxerFactory());

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => harness.Session.OpenAsync(SessionHarness.Source(Ramp, "clip.silent")));

        Assert.Equal("clip.silent has no audio rexplayer can play yet.", error.Message);
    }

    [Fact]
    public async Task AnIoFailureWhileOpeningFaultsTheSession()
    {
        using var harness = new SessionHarness();

        await Assert.ThrowsAsync<IOException>(() => harness.Session.OpenAsync(new ThrowingSource()));

        Assert.Equal("IOException: the network dropped", harness.WaitFor<ErrorEvent>().Message);
    }

    [Fact]
    public async Task AStreamThatKeepsFailingToDecodeFaultsTheSession()
    {
        using var harness = new SessionHarness(decoder: new BrokenDecoderFactory(), maxCorrupt: 3);

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Faulted, harness.Session.State);
        Assert.StartsWith("The audio stream is too damaged", harness.WaitFor<TrackFailedEvent>().Reason, StringComparison.Ordinal);
        Assert.Equal("No playable streams remain.", harness.WaitFor<ErrorEvent>().Message);
        Assert.Equal(3, (await harness.Session.GetStatsAsync()).CorruptPackets);
    }

    [Fact]
    public async Task OccasionalCorruptPacketsAreSkipped()
    {
        using var harness = new SessionHarness(decoder: new BrokenDecoderFactory(), maxCorrupt: 1000);

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(20, (await harness.Session.GetStatsAsync()).CorruptPackets);
    }

    [Fact]
    public async Task ADamagedTailEndsPlaybackInsteadOfFailingIt()
    {
        using var harness = new SessionHarness(demuxer: new FailingDemuxerFactory(5, new MediaFormatException("truncated")));

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp, "clip.fail"));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(2000, harness.Recording.SampleCount);
    }

    [Fact]
    public async Task AReadFailureFaultsTheSession()
    {
        using var harness = new SessionHarness(demuxer: new FailingDemuxerFactory(2, new IOException("the disk went away")));

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp, "clip.fail"));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Faulted, harness.Session.State);
        Assert.Equal("Reading the media failed: IOException: the disk went away", harness.WaitFor<ErrorEvent>().Message);
    }

    [Fact]
    public async Task VolumeAppliesToWhatIsPlayedAndMuteIsRemembered()
    {
        using var harness = new SessionHarness();
        harness.Session.Volume = 0.5;
        harness.Session.Muted = true;
        harness.Session.Muted = false;

        await harness.Session.OpenAsync(SessionHarness.Source(Pcm.SineWav(1000, 8000, 1, 0.5, amplitude: 0.8)));
        harness.Session.Muted = false;
        await harness.FinishAsync();

        var samples = harness.Recording.Channel(0);
        Assert.InRange(Signals.Amplitude(samples.AsSpan(1000, 2000), 8000, 1000), 0.19, 0.21);
        Assert.Equal(0.5, harness.Session.Volume);
        Assert.False(harness.Session.Muted);
        harness.Session.Volume = double.NaN;
        Assert.Equal(1, harness.Session.Volume);
    }

    [Fact]
    public async Task StatsCountWhatWentThrough()
    {
        using var harness = new SessionHarness();
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        var stats = await harness.Session.GetStatsAsync();

        Assert.Equal(20, stats.PacketsRead);
        Assert.Equal(16_000, stats.BytesRead);
        Assert.Equal(20, stats.AudioFramesDecoded);
        Assert.Equal(8000, stats.AudioSamplesPlayed);
        Assert.Equal("rexplayer PCM", stats.AudioDecoder);
        Assert.NotEmpty(harness.Events.OfType<PositionEvent>());
    }

    [Fact]
    public async Task CommandsNeedSomethingOpen()
    {
        using var harness = new SessionHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Session.PlayAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Session.PauseAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Session.SeekAsync(MediaTime.Zero));
        await harness.Session.StopAsync();
        Assert.Equal(new SessionStats(), await harness.Session.GetStatsAsync());
        Assert.Throws<ArgumentNullException>(() => { _ = harness.Session.OpenAsync(null!); });
    }

    [Fact]
    public void ADisposedSessionRefusesCommandsAndDisposingTwiceIsHarmless()
    {
        var harness = new SessionHarness();
        harness.Session.Dispose();
        harness.Session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = harness.Session.PlayAsync(); });
        Assert.Throws<ArgumentNullException>(() => new MediaSession(null!));
    }

    [Fact]
    public async Task ASessionWithoutAListenerStillWorks()
    {
        using var session = new MediaSession(new EngineOptions
        {
            Demuxers = new DemuxerRegistry().Add(new WavDemuxerFactory()),
            Decoders = new DecoderRegistry().Add(new PcmDecoderFactory()),
            AudioSinkFactory = () => new RecordingAudioSink(channels: 1),
        });

        await session.OpenAsync(SessionHarness.Source(Ramp));

        await session.WaitForFinishAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(SessionState.Ended, session.State);
    }

    private sealed class SilentDemuxerFactory : IDemuxerFactory
    {
        public string Name => "silent";

        public int Probe(ReadOnlySpan<byte> head, string? extension) => extension == ".silent" ? 200 : 0;

        public IDemuxer Open(Rex.Media.IO.IByteSource source, CancellationToken cancellationToken) => new Demuxer();

        private sealed class Demuxer : IDemuxer
        {
            public MediaInfo Info { get; } = new() { FormatName = "silent", Tracks = [new TrackInfo { Id = 0, Codec = CodecId.H264 }] };

            public Packet? ReadPacket(CancellationToken cancellationToken) => null;

            public void Seek(MediaTime target, CancellationToken cancellationToken)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class ThrowingSource : Rex.Media.IO.IByteSource
    {
        public string Name => "stream";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => throw new IOException("the network dropped");

        public void Dispose()
        {
        }
    }
}
