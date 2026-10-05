using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

/// <summary>The paths through a session that only open when timing is held still or the media is unusual.</summary>
public sealed class MediaSessionEdgeTests
{
    private static readonly byte[] Ramp = Pcm.RampWav(8000, 8000);

    private static float RampValue(int sample) => (short)(sample & 0x7FFF) / 32768f;

    /// <summary>Opens paused, then starts a seek and holds the decoder so the session stays in Seeking.</summary>
    private static async Task<SessionHarness> HeldInSeekAsync(GatedDecoderFactory decoder, MediaTime target)
    {
        var harness = new SessionHarness(autoPlay: false, decoder: decoder);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        decoder.Gate.Reset();
        await harness.Session.SeekAsync(target);
        Assert.Equal(SessionState.Seeking, harness.Session.State);
        return harness;
    }

    [Fact]
    public async Task PlayAndPauseDuringASeekOnlyChangeWhatHappensWhenItCompletes()
    {
        using var decoder = new GatedDecoderFactory();
        using var harness = await HeldInSeekAsync(decoder, MediaTime.FromSeconds(0.5));

        await harness.Session.PlayAsync();
        Assert.Equal(SessionState.Seeking, harness.Session.State);
        await harness.Session.PauseAsync();
        Assert.Equal(SessionState.Seeking, harness.Session.State);
        decoder.Gate.Set();

        harness.WaitForState(SessionState.Paused);
        Assert.Equal(SessionState.Paused, harness.Session.State);
    }

    [Fact]
    public async Task ASecondSeekSupersedesTheFirst()
    {
        using var decoder = new GatedDecoderFactory();
        using var harness = await HeldInSeekAsync(decoder, MediaTime.FromSeconds(0.25));

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.75));
        await harness.Session.PlayAsync();
        decoder.Gate.Set();
        await harness.FinishAsync();

        Assert.Equal(2000, harness.Recording.SampleCount);
        Assert.Equal(RampValue(6000), harness.Recording.Channel(0)[0]);
        Assert.Single(harness.Events.OfType<StateChangedEvent>(), e => e.To == SessionState.Seeking);
    }

    [Fact]
    [Capability("PB-03")]
    public async Task APreciseSeekTrimsTheKeyframePacketToTheExactSample()
    {
        using var harness = new SessionHarness(autoPlay: false, demuxer: new CoarseDemuxerFactory());
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp, "clip.coarse"));

        await harness.Session.SeekAsync(MediaTime.FromSamples(4321, 8000));
        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        Assert.Equal(8000 - 4321, harness.Recording.SampleCount);
        Assert.Equal(RampValue(4321), harness.Recording.Channel(0)[0]);
    }

    [Fact]
    public async Task PacketsOfOtherTracksAreSkippedAndAnUnknownDurationIsNotAClamp()
    {
        using var harness = new SessionHarness(demuxer: new CoarseDemuxerFactory(hideDuration: true));

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp, "clip.coarse"));
        await harness.FinishAsync();
        Assert.Equal(8000, harness.Recording.SampleCount);
        Assert.Equal(40, (await harness.Session.GetStatsAsync()).PacketsRead);

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.5));
        harness.WaitFor<EndedEvent>(_ => harness.Events.OfType<EndedEvent>().Count() >= 2);

        Assert.Equal(12_000, harness.Recording.SampleCount);
        Assert.Equal(RampValue(4000), harness.Recording.Channel(0)[8000]);
        Assert.False(harness.Session.Duration.IsKnown);
    }

    [Fact]
    public async Task ResamplingToTheSinkReleasesTheTailAtTheEnd()
    {
        using var harness = new SessionHarness(sink: new RecordingAudioSink(channels: 1, sampleRate: 48_000));

        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();

        Assert.Equal(48_000, harness.Recording.SampleCount);
    }

    [Fact]
    public async Task ASeekDuringTheFinalDrainTakesOver()
    {
        var sink = new StuckDrainSink();
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        Assert.True(sink.Draining.Wait(5000, TestContext.Current.CancellationToken));

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.9));
        harness.WaitFor<SeekCompletedEvent>();

        Assert.DoesNotContain(harness.Events, e => e is EndedEvent);
    }

    [Fact]
    public async Task ASinkThatCannotOpenFailsTheOpenAndIsReleased()
    {
        var sink = new UnpluggedSink();
        using var harness = new SessionHarness(sink: sink);

        await Assert.ThrowsAsync<IOException>(() => harness.Session.OpenAsync(SessionHarness.Source(Ramp)));

        Assert.True(sink.Disposed);
        Assert.Equal(SessionState.Faulted, harness.Session.State);
    }

    [Fact]
    public async Task CommandsThatDoNotApplyToTheCurrentStateAreIgnored()
    {
        using var harness = new SessionHarness(decoder: new BrokenDecoderFactory(), maxCorrupt: 1);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));
        await harness.FinishAsync();
        Assert.Equal(SessionState.Faulted, harness.Session.State);

        await harness.Session.SeekAsync(MediaTime.FromSeconds(0.5));
        await harness.Session.PauseAsync();

        Assert.Equal(SessionState.Faulted, harness.Session.State);
    }

    [Fact]
    public async Task PlayWhilePlayingAndPauseAfterTheEndChangeNothing()
    {
        using var proceed = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => proceed.Wait(TestContext.Current.CancellationToken);
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp));

        await harness.Session.PlayAsync();
        Assert.Equal(SessionState.Playing, harness.Session.State);
        proceed.Set();
        await harness.FinishAsync();
        await harness.Session.PauseAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
    }
}
