using Rex.Media.Containers.Matroska;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Engine;

/// <summary>Where decoded audio sits on the timeline: end padding cut, rounded timestamps joined up.</summary>
public sealed class AudioTimelineTests
{
    private static float[] Ramp(int first, int samples) => [.. Enumerable.Range(first, samples).Select(v => v / 32768f)];

    /// <summary>A Matroska PCM file of two 100-sample blocks, the second flagging its last <paramref name="paddingNs"/> as padding.</summary>
    private static byte[] Padded(long paddingNs) => MatroskaCraftedTests.Mkv(
        MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()),
        MatroskaCraftedTests.Cluster(
            0,
            Element(Id.BlockGroup, Element(Id.Block, Block(1, 0, 0, 0, Pcm.Int16(Ramp(0, 100))))),
            Element(Id.BlockGroup, Element(Id.Block, Block(1, 12, 0, 0, Pcm.Int16(Ramp(100, 100)))), UInt(Id.DiscardPadding, (ulong)paddingNs))));

    [Theory]
    [InlineData(1_250_000, 190)]
    [InlineData(0, 200)]
    [Capability("PB-14")]
    public async Task TheFlaggedPaddingIsNotPlayed(long paddingNs, int played)
    {
        using var harness = new SessionHarness(demuxer: new MatroskaDemuxerFactory());

        await harness.Session.OpenAsync(SessionHarness.Source(Padded(paddingNs), "clip.mkv"));
        await harness.FinishAsync();

        Assert.Equal(Ramp(0, played), harness.Recording.Channel(0));
    }

    [Fact]
    public async Task FramesFollowOnAcrossRoundedAndMissingTimestamps()
    {
        // 100 samples of codec delay (12.5 ms) before a block the muxer stamped 12 ms, not 12.5:
        // it lands 4 samples before zero. Its second laced frame has no timestamp at all.
        var file = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(1, UInt(Id.CodecDelay, 12_500_000))),
            MatroskaCraftedTests.Cluster(
                0,
                Element(Id.SimpleBlock, Block(1, 0, 0x80, 0, Pcm.Int16(Ramp(-100, 100)))),
                Element(Id.SimpleBlock, Block(1, 12, 0x80, 1, Pcm.Int16(Ramp(0, 50)), Pcm.Int16(Ramp(50, 50))))));
        using var harness = new SessionHarness(demuxer: new MatroskaDemuxerFactory());

        await harness.Session.OpenAsync(SessionHarness.Source(file, "clip.mkv"));
        await harness.FinishAsync();

        Assert.Equal(Ramp(0, 100), harness.Recording.Channel(0));
    }
}
