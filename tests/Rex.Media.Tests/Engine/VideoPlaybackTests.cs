using Rex.Media.Containers.Matroska;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Engine;

/// <summary>
/// Pictures through the engine: a Matroska file of 8 kHz PCM and a picture every 40 ms, decoded by a
/// fake whose pictures carry their packet's first byte, shown by a presenter that records them.
/// </summary>
public sealed class VideoPlaybackTests
{
    private const int Audio = 1;
    private const int Video = 2;

    /// <summary><paramref name="pictures"/> pictures of 40 ms with 40 ms of silence beside each, interleaved.</summary>
    internal static byte[] Clip(int pictures)
    {
        var video = Element(
            Id.TrackEntry,
            UInt(Id.TrackNumber, Video),
            UInt(Id.TrackType, 1),
            Text(Id.CodecId, "V_MPEG4/ISO/AVC"),
            UInt(Id.DefaultDuration, 40_000_000),
            Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8)));
        var blocks = Enumerable.Range(0, pictures).SelectMany(i => new[]
        {
            MatroskaCraftedTests.Simple(Audio, (short)(i * 40), true, Pcm.Int16(new float[320])),
            MatroskaCraftedTests.Simple(Video, (short)(i * 40), true, (byte)i),
        });
        return MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(Audio), video), MatroskaCraftedTests.Cluster(0, [.. blocks]));
    }

    private static SessionHarness Harness(RecordingVideoPresenter? presenter, FakeVideoDecoderFactory? decoder = null, Rex.Media.Audio.IAudioSink? sink = null) =>
        new(sink: sink, demuxer: new MatroskaDemuxerFactory(), videoDecoder: decoder ?? new FakeVideoDecoderFactory(), presenter: presenter);

    private static void WaitUntil(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "The condition was not met in time.");

    [Fact]
    [Capability("VID-25")]
    public async Task IntoACaptureEveryPictureIsShownInOrder()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"));
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 5);

        Assert.Equal([0, 1, 2, 3, 4], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(Enumerable.Range(0, 5).Select(i => MediaTime.FromMilliseconds(i * 40)), presenter.Shown.Select(s => s.Pts));
        var stats = await harness.Session.GetStatsAsync();
        Assert.Equal((5L, 5L, 0L, "grey"), (stats.VideoFramesDecoded, stats.VideoFramesPresented, stats.VideoFramesDropped, stats.VideoDecoder));
        Assert.Contains(harness.Events.OfType<TracksChangedEvent>(), e => e.VideoTrack == Video);
        Assert.Equal(1600, harness.Recording.Channel(0).Length);
        harness.Session.Dispose();
        Assert.True(presenter.Disposed);
    }

    [Fact]
    public async Task StartingPartWayShowsNoPictureThatEndedBeforeTheStart()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"), [], MediaTime.FromMilliseconds(100));
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 3);

        Assert.Equal([2, 3, 4], presenter.Shown.Select(s => (int)s.First));
    }

    [Fact]
    public async Task WithoutAPresenterPicturesAreNotDecoded()
    {
        var decoder = new FakeVideoDecoderFactory();
        using var harness = Harness(null, decoder);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(3), "clip.mkv"));
        await harness.FinishAsync();

        Assert.Equal(0, decoder.Decoded);
        Assert.Null((await harness.Session.GetStatsAsync()).VideoDecoder);
    }

    [Fact]
    public async Task PicturesNothingCanDecodeAreReportedAndTheSoundPlays()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = new SessionHarness(demuxer: new MatroskaDemuxerFactory(), presenter: presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(3), "clip.mkv"));
        await harness.FinishAsync();

        Assert.Equal(Video, harness.WaitFor<TrackFailedEvent>().TrackId);
        Assert.Empty(presenter.Shown);
        Assert.True(presenter.Disposed);
        Assert.Equal(960, harness.Recording.Channel(0).Length);
    }

    [Fact]
    public async Task ABrokenPictureIsSkippedAndCounted()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter, new FakeVideoDecoderFactory { BrokenByte = 2 });

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"));
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 4);

        Assert.Equal([0, 1, 3, 4], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(1, (await harness.Session.GetStatsAsync()).CorruptPackets);
    }

    [Fact]
    public async Task PicturesADecoderHoldsAreShownAtTheEndAndAtEachHandOver()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter, new FakeVideoDecoderFactory { Hold = true });

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(2), "a.mkv"), [SessionHarness.Source(Clip(3), "b.mkv")]);
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 5);

        Assert.Equal([0, 1, 0, 1, 2], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(2, (await harness.Session.GetStatsAsync()).ItemsStarted);
    }

    [Fact]
    public async Task ARunCanEndOnMediaWithoutPictures()
    {
        var presenter = new RecordingVideoPresenter();
        var soundOnly = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(Audio)),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(Audio, 0, true, Pcm.Int16(new float[320]))));
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(2), "a.mkv"), [SessionHarness.Source(soundOnly, "b.mkv")]);
        await harness.FinishAsync();

        Assert.Equal([0, 1], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(960, harness.Recording.Channel(0).Length);
    }

    [Fact]
    public async Task ADecoderThatDrainsInStepsHasEveryPictureShown()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter, new FakeVideoDecoderFactory { Hold = true, DrainOneAtATime = true });

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(2), "a.mkv"), [SessionHarness.Source(Clip(3), "b.mkv")]);
        await harness.FinishAsync();

        Assert.Equal([0, 1, 0, 1, 2], presenter.Shown.Select(s => (int)s.First));
    }

    [Fact]
    [Capability("VID-02")]
    public async Task InRealTimeEachPictureWaitsForTheClockAndLatePicturesAreDropped()
    {
        var presenter = new RecordingVideoPresenter();
        var sink = new ManualClockSink();
        using var harness = Harness(presenter, sink: sink);

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"));
        WaitUntil(() => presenter.Shown.Count == 1);
        Thread.Sleep(50);
        Assert.Single(presenter.Shown);

        sink.PlayedSamples = 360;
        WaitUntil(() => presenter.Shown.Count == 2);
        sink.PlayedSamples = 8000;
        WaitUntil(() => harness.Session.GetStatsAsync().Result.VideoFramesDropped == 3);

        Assert.Equal([0, 1], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(2, (await harness.Session.GetStatsAsync()).VideoFramesPresented);
    }

    [Fact]
    [Capability("PB-19")]
    public async Task DelayingTheSoundShowsThePicturesThatMuchSooner()
    {
        var presenter = new RecordingVideoPresenter();
        var sink = new ManualClockSink();
        using var harness = Harness(presenter, sink: sink);
        harness.Session.AudioDelay = TimeSpan.FromMilliseconds(40);

        // The clock has not moved, yet the picture due at 40 ms is shown: the sound is 40 ms behind.
        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"));
        WaitUntil(() => presenter.Shown.Count == 2);
        Thread.Sleep(50);
        Assert.Equal(2, presenter.Shown.Count);

        harness.Session.AudioDelay = TimeSpan.Zero;
        sink.PlayedSamples = 360;
        Thread.Sleep(50);
        Assert.Equal(2, presenter.Shown.Count);
        harness.Session.AudioDelay = TimeSpan.FromSeconds(99);
        Assert.Equal(MediaSession.MaxAudioDelay, harness.Session.AudioDelay);
        harness.Session.AudioDelay = TimeSpan.FromSeconds(-99);
        Assert.Equal(-MediaSession.MaxAudioDelay, harness.Session.AudioDelay);
    }

    [Fact]
    public async Task PicturesThatOutlastTheSoundPlayOnAndTheEndWaitsForThem()
    {
        // Sound for the first two pictures only.
        var video = Element(
            Id.TrackEntry,
            UInt(Id.TrackNumber, Video),
            UInt(Id.TrackType, 1),
            Text(Id.CodecId, "V_MPEG4/ISO/AVC"),
            UInt(Id.DefaultDuration, 40_000_000),
            Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8)));
        var blocks = Enumerable.Range(0, 6).SelectMany(i => i < 2
            ? new[] { MatroskaCraftedTests.Simple(Audio, (short)(i * 40), true, Pcm.Int16(new float[320])), MatroskaCraftedTests.Simple(Video, (short)(i * 40), true, (byte)i) }
            : [MatroskaCraftedTests.Simple(Video, (short)(i * 40), true, (byte)i)]);
        var clip = MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(Audio), video), MatroskaCraftedTests.Cluster(0, [.. blocks]));
        var presenter = new RecordingVideoPresenter();
        var sink = new ManualClockSink();
        using var harness = Harness(presenter, sink: sink);

        await harness.Session.OpenAsync(SessionHarness.Source(clip, "clip.mkv"));
        WaitUntil(() => presenter.Shown.Count == 1);
        sink.PlayedSamples = 320;
        WaitUntil(() => presenter.Shown.Count == 2);
        sink.PlayedSamples = 640;
        await harness.FinishAsync();

        // After the sound, pictures follow real time: each is shown, or counted if a busy machine made it late.
        var stats = await harness.Session.GetStatsAsync();
        Assert.Equal([0, 1], presenter.Shown.Take(2).Select(s => (int)s.First));
        Assert.Equal(6, presenter.Shown.Count + stats.VideoFramesDropped);
    }

    [Fact]
    public async Task ASeekCutsAWaitingPictureShort()
    {
        var presenter = new RecordingVideoPresenter();
        var sink = new ManualClockSink();
        using var harness = Harness(presenter, sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Clip(5), "clip.mkv"));
        WaitUntil(() => presenter.Shown.Count == 1);

        await harness.Session.SeekAsync(MediaTime.Zero, SeekMode.Precise);
        WaitUntil(() => presenter.Shown.Count == 2);

        Assert.Equal([0, 0], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(2, presenter.Generations.Count);
        Assert.True(presenter.Generations[1] > presenter.Generations[0]);
    }

    [Fact]
    public async Task ClosingWhileAPictureWaitsForTheClockEndsTheWait()
    {
        // The clock never moves, so the second picture waits; the one-slot video queue fills behind
        // it while audio keeps flowing past, as the demuxer checks that audio is not starving.
        var presenter = new RecordingVideoPresenter();
        var harness = new SessionHarness(sink: new ManualClockSink(), demuxer: new MatroskaDemuxerFactory(), videoDecoder: new FakeVideoDecoderFactory(), presenter: presenter, videoQueueCapacity: 1);
        await harness.Session.OpenAsync(SessionHarness.Source(Clip(12), "clip.mkv"));
        WaitUntil(() => presenter.Shown.Count == 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        harness.Dispose();

        Assert.Single(presenter.Shown);
        Assert.True(presenter.Disposed);
    }

    [Fact]
    public async Task APresenterThatFailsEndsThePlaybackWithAReason()
    {
        // Held pictures arrive together at the end, so the failure leaves some of them unshown.
        using var harness = new SessionHarness(demuxer: new MatroskaDemuxerFactory(), videoDecoder: new FakeVideoDecoderFactory { Hold = true }, presenter: new FailingPresenter());

        await harness.Session.OpenAsync(SessionHarness.Source(Clip(3), "clip.mkv"));

        harness.WaitFor<StateChangedEvent>(e => e.To == SessionState.Faulted);
        Assert.StartsWith("The video output failed", harness.Session.FailureReason, StringComparison.Ordinal);
    }

    private sealed class FailingPresenter : Rex.Media.Video.IVideoPresenter
    {
        public string Name => "failing";

        public void Present(VideoFrame frame) => throw new InvalidOperationException("The window is gone.");

        public void Dispose()
        {
        }
    }
}
