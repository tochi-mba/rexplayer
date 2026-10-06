using Rex.Media.Containers.Matroska;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Engine;

/// <summary>Padding a container flags at the end of a block never reaches the output.</summary>
public sealed class EndPaddingTests
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
}
