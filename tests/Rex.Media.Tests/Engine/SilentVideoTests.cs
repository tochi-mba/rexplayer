using Rex.Media.Containers.Matroska;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Engine;

/// <summary>
/// Video without sound: it plays against silence, so the audio output keeps time for it as it does
/// for everything else.
/// </summary>
public sealed class SilentVideoTests
{
    private const int Video = 2;

    /// <summary><paramref name="pictures"/> pictures of 40 ms and no sound at all.</summary>
    private static byte[] Pictures(int pictures, ulong? durationMs = null)
    {
        var video = Element(
            Id.TrackEntry,
            UInt(Id.TrackNumber, Video),
            UInt(Id.TrackType, 1),
            Text(Id.CodecId, "V_MPEG4/ISO/AVC"),
            UInt(Id.DefaultDuration, 40_000_000),
            Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8)));
        var blocks = Enumerable.Range(0, pictures).Select(i => MatroskaCraftedTests.Simple(Video, (short)(i * 40), true, (byte)i)).ToArray();
        byte[][] segment = durationMs is { } ms
            ? [Element(Id.Info, UInt(Id.TimestampScale, 1_000_000), Float(Id.Duration, ms)), MatroskaCraftedTests.Tracks(video), MatroskaCraftedTests.Cluster(0, blocks)]
            : [MatroskaCraftedTests.Tracks(video), MatroskaCraftedTests.Cluster(0, blocks)];
        return MatroskaCraftedTests.Mkv(segment);
    }

    private static SessionHarness Harness(RecordingVideoPresenter? presenter) =>
        new(demuxer: new MatroskaDemuxerFactory(), videoDecoder: new FakeVideoDecoderFactory(), presenter: presenter);

    private static void WaitUntil(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "The condition was not met in time.");

    [Fact]
    [Capability("PB-01")]
    public async Task PicturesWithoutSoundPlayAgainstSilenceToTheEndOfTheLastPicture()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Pictures(5), "silent.mkv"));
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 5);

        Assert.Equal([0, 1, 2, 3, 4], presenter.Shown.Select(s => (int)s.First));
        var played = harness.Recording.Channel(0);
        Assert.Equal(harness.Recording.Format!.SampleRate / 5, played.Length);
        Assert.All(played, sample => Assert.Equal(0f, sample));
        Assert.Contains(harness.Events.OfType<TracksChangedEvent>(), e => e.AudioTrack == -1 && e.VideoTrack == Video);
        Assert.Single(harness.Events.OfType<EndedEvent>());
    }

    [Fact]
    public async Task TheSilenceLastsAsLongAsTheFileSaysItDoes()
    {
        using var harness = Harness(new RecordingVideoPresenter());

        await harness.Session.OpenAsync(SessionHarness.Source(Pictures(2, durationMs: 300), "silent.mkv"));
        await harness.FinishAsync();

        Assert.Equal(harness.Recording.Format!.SampleRate * 3 / 10, harness.Recording.Channel(0).Length);
    }

    [Fact]
    public async Task SeekingInSilentVideoStartsBothThePicturesAndTheSilenceAtTheTarget()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Pictures(5), "silent.mkv"), [], MediaTime.FromMilliseconds(120));
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 2);

        Assert.Equal([3, 4], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(harness.Recording.Format!.SampleRate * 8 / 100, harness.Recording.Channel(0).Length);
    }

    [Fact]
    public async Task SilentItemsFollowEachOtherEachFromItsOwnStart()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = Harness(presenter);

        await harness.Session.OpenAsync(SessionHarness.Source(Pictures(3), "a.mkv"), [SessionHarness.Source(Pictures(2), "b.mkv")]);
        await harness.FinishAsync();
        WaitUntil(() => presenter.Shown.Count == 5);

        Assert.Equal([0, 1, 2, 0, 1], presenter.Shown.Select(s => (int)s.First));
        Assert.Equal(harness.Recording.Format!.SampleRate / 5, harness.Recording.Channel(0).Length);
    }

    [Fact]
    public async Task WithoutSoundOrAWayToShowThePicturesThereIsNothingToPlay()
    {
        using var noPresenter = Harness(presenter: null);
        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => noPresenter.Session.OpenAsync(SessionHarness.Source(Pictures(2), "silent.mkv")));
        Assert.Equal("silent.mkv has no sound, and its pictures cannot be shown here.", refused.Message);

        var subtitlesOnly = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(Element(Id.TrackEntry, UInt(Id.TrackNumber, 3), UInt(Id.TrackType, 17), Text(Id.CodecId, "S_TEXT/UTF8"))),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(3, 0, true, (byte)'x')));
        using var harness = Harness(new RecordingVideoPresenter());
        var nothing = await Assert.ThrowsAsync<NotSupportedException>(() => harness.Session.OpenAsync(SessionHarness.Source(subtitlesOnly, "words.mkv")));
        Assert.Equal("words.mkv has no audio rexplayer can play yet.", nothing.Message);
    }

    [Fact]
    public void TheSilenceDecoderMakesZerosAsLongAsEachPacket()
    {
        using var decoder = new SilenceDecoder();
        using var packet = SilentAudio.Between(MediaTime.FromMilliseconds(20), MediaTime.FromMilliseconds(40));
        var frames = new List<AudioFrame>();

        decoder.Decode(packet, frames);
        decoder.Drain(frames);
        decoder.Flush();

        var frame = Assert.Single(frames);
        Assert.Equal((960, 2, MediaTime.FromMilliseconds(20)), (frame.SampleCount, frame.Channels, frame.Pts));
        Assert.All(frame.Channel(1).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.Equal(("silence", Rex.Media.Codecs.DecoderSource.Own), (decoder.Name, decoder.Source));
        frame.Dispose();
    }
}
