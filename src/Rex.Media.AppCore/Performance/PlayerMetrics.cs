using Rex.Media.Codecs;
using Rex.Media.Engine;

namespace Rex.Media.AppCore.Performance;

/// <summary>What the process costs at one moment, read by the window from the runtime.</summary>
public readonly record struct ProcessSample(
    double CpuMs,
    long WorkingSetBytes,
    long PrivateBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long ManagedHeapBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int Threads,
    int ProcessorCount);

/// <summary>The software visualisation as it is drawing now.</summary>
public readonly record struct VisualSample(double DrawMs, double Quality, int Width, int Height);

/// <summary>
/// The running player's figures for the automation pipe's <c>metrics</c> request, each a number
/// under a stable name that scripts/measure-startup.ps1 reads: what the process costs, how steadily
/// pictures are shown, how responsive the window is, and what the visualisation costs. A script
/// takes two readings and works out rates (processor use, allocations a second) from the change.
/// The performance tier is its number: 0 best quality, 1 balanced, 2 fastest.
/// </summary>
public static class PlayerMetrics
{
    private const double Megabyte = 1024 * 1024;

    public static IReadOnlyDictionary<string, double> Read(ProcessSample process, SessionStats? playback, ResponsivenessStats window, VisualSample? visual, PerformanceTier tier = PerformanceTier.BestQuality)
    {
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["cpu-ms"] = process.CpuMs,
            ["working-set-mb"] = process.WorkingSetBytes / Megabyte,
            ["private-mb"] = process.PrivateBytes / Megabyte,
            ["peak-working-set-mb"] = process.PeakWorkingSetBytes / Megabyte,
            ["peak-private-mb"] = process.PeakPrivateBytes / Megabyte,
            ["managed-heap-mb"] = process.ManagedHeapBytes / Megabyte,
            ["allocated-mb"] = process.AllocatedBytes / Megabyte,
            ["gc0"] = process.Gen0Collections,
            ["gc1"] = process.Gen1Collections,
            ["gc2"] = process.Gen2Collections,
            ["threads"] = process.Threads,
            ["processors"] = process.ProcessorCount,
            ["ui-probes"] = window.Probes,
            ["ui-hitches"] = window.Hitches,
            ["ui-blocked-ms"] = window.BlockedMs,
            ["ui-blocked-ms-per-second"] = window.BlockedMsPerSecond,
            ["ui-worst-ms"] = window.WorstMs,
            ["performance-tier"] = (int)tier,
        };

        if (playback is not null)
        {
            var cadence = playback.Cadence;
            metrics["frames-decoded"] = playback.VideoFramesDecoded;
            metrics["frames-presented"] = playback.VideoFramesPresented;
            metrics["frames-dropped"] = playback.VideoFramesDropped;
            metrics["audio-samples-played"] = playback.AudioSamplesPlayed;
            metrics["decoding-on-graphics-card"] = playback.VideoDecoderSource == DecoderSource.OsHardware ? 1 : 0;
            metrics["cadence-pictures"] = cadence.Pictures;
            metrics["frame-interval-mean-ms"] = cadence.IntervalMeanMs;
            metrics["frame-interval-jitter-ms"] = cadence.IntervalJitterMs;
            metrics["frame-interval-worst-ms"] = cadence.IntervalWorstMs;
            metrics["frame-lateness-mean-ms"] = cadence.LatenessMeanMs;
            metrics["frame-lateness-jitter-ms"] = cadence.LatenessJitterMs;
            metrics["frame-lateness-worst-ms"] = cadence.LatenessWorstMs;
        }

        if (visual is { } stage)
        {
            metrics["visual-draw-ms"] = stage.DrawMs;
            metrics["visual-quality"] = stage.Quality;
            metrics["visual-width"] = stage.Width;
            metrics["visual-height"] = stage.Height;
        }

        return metrics;
    }
}
