using Rex.Media.AppCore.Performance;
using Rex.Media.Codecs;
using Rex.Media.Engine;

namespace Rex.Media.Tests.AppCore;

public sealed class PlayerMetricsTests
{
    private static readonly ProcessSample Process = new(1500, 200L << 20, 150L << 20, 40L << 20, 900L << 20, 30, 5, 1, 42, 8);

    [Fact]
    public void TheProcessAndTheWindowAreAlwaysReported()
    {
        var metrics = PlayerMetrics.Read(Process, null, new ResponsivenessStats(10, 1, 120, 12, 110), null);

        Assert.Equal(1500, metrics["cpu-ms"]);
        Assert.Equal(200, metrics["working-set-mb"]);
        Assert.Equal(150, metrics["private-mb"]);
        Assert.Equal(40, metrics["managed-heap-mb"]);
        Assert.Equal(900, metrics["allocated-mb"]);
        Assert.Equal((30.0, 5.0, 1.0), (metrics["gc0"], metrics["gc1"], metrics["gc2"]));
        Assert.Equal((42.0, 8.0), (metrics["threads"], metrics["processors"]));
        Assert.Equal((10.0, 1.0, 120.0, 12.0, 110.0), (metrics["ui-probes"], metrics["ui-hitches"], metrics["ui-blocked-ms"], metrics["ui-blocked-ms-per-second"], metrics["ui-worst-ms"]));
        Assert.False(metrics.ContainsKey("frames-presented"));
        Assert.Equal(0, metrics["performance-tier"]);
        Assert.False(metrics.ContainsKey("visual-draw-ms"));
    }

    [Theory]
    [InlineData(DecoderSource.OsHardware, 1)]
    [InlineData(DecoderSource.OsSoftware, 0)]
    public void PlaybackAndTheVisualisationAreReportedWhenThereAreAny(DecoderSource source, double onCard)
    {
        var stats = new SessionStats
        {
            VideoFramesDecoded = 100,
            VideoFramesPresented = 98,
            VideoFramesDropped = 2,
            AudioSamplesPlayed = 48_000,
            VideoDecoderSource = source,
            Cadence = new FrameCadenceStats(98, 97, 41.7, 1.5, 50, 3, 0.5, 6),
        };

        var metrics = PlayerMetrics.Read(Process, stats, default, new VisualSample(9.5, 0.85, 1280, 720), PerformanceTier.Fastest);
        Assert.Equal(2, metrics["performance-tier"]);

        Assert.Equal((100.0, 98.0, 2.0, 48_000.0), (metrics["frames-decoded"], metrics["frames-presented"], metrics["frames-dropped"], metrics["audio-samples-played"]));
        Assert.Equal(onCard, metrics["decoding-on-graphics-card"]);
        Assert.Equal((98.0, 41.7, 1.5, 50.0), (metrics["cadence-pictures"], metrics["frame-interval-mean-ms"], metrics["frame-interval-jitter-ms"], metrics["frame-interval-worst-ms"]));
        Assert.Equal((3.0, 0.5, 6.0), (metrics["frame-lateness-mean-ms"], metrics["frame-lateness-jitter-ms"], metrics["frame-lateness-worst-ms"]));
        Assert.Equal((9.5, 0.85, 1280.0, 720.0), (metrics["visual-draw-ms"], metrics["visual-quality"], metrics["visual-width"], metrics["visual-height"]));
    }

    [Fact]
    public void ResponsivenessCountsOnlyWaitsLongerThanAFrame()
    {
        var watch = new ThreadResponsiveness();
        watch.Answered(TimeSpan.FromMilliseconds(-3));
        watch.Answered(TimeSpan.FromMilliseconds(10));
        watch.Answered(TimeSpan.FromMilliseconds(40));
        watch.Answered(TimeSpan.FromMilliseconds(250));

        var stats = watch.Snapshot(TimeSpan.FromSeconds(2));

        Assert.Equal(new ResponsivenessStats(4, 1, 290, 145, 250), stats);
        Assert.Equal(0, watch.Snapshot(TimeSpan.Zero).BlockedMsPerSecond);

        watch.Clear();
        Assert.Equal(default, watch.Snapshot(TimeSpan.FromSeconds(1)));
    }
}
