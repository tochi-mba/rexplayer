using System.Diagnostics;
using Rex.Media.Audio;
using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests.Perf;

/// <summary>
/// How fast the engine's own parts run, against tests/perf-baselines.json (§9.8). Throughput must stay
/// at or above 0.7 of the baseline recorded for this machine's lane (shared runners are noisy, so the
/// gate is loose). Steady-state playback must allocate nothing, which needs no baseline. Run with
/// ./dev.ps1 perf; record a lane's baselines with REXPLAYER_WRITE_PERF=1.
/// </summary>
[Trait("Category", "Perf")]
public sealed class PerfTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{name}"));

    /// <summary>
    /// Runs <paramref name="pass"/> for a warm-up (long enough for the JIT to finish tiering the hot
    /// loops), then until the budget is spent; returns passes per second at the fastest pass. The
    /// fastest pass is the steadiest measure on a shared or power-managed machine: the noise of other
    /// work and of clock-speed changes only ever slows a pass down.
    /// </summary>
    private static double Rate(Action pass)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < Budget / 2)
        {
            pass();
        }

        clock.Restart();
        var fastest = TimeSpan.MaxValue;
        while (clock.Elapsed < Budget)
        {
            var start = clock.Elapsed;
            pass();
            var took = clock.Elapsed - start;
            fastest = took < fastest ? took : fastest;
        }

        return 1 / fastest.TotalSeconds;
    }

    /// <summary>Demuxes and decodes a whole file's first audio track; returns the audio's length in seconds.</summary>
    private static double DecodeAll(byte[] file)
    {
        using var source = new MemoryByteSource(file, "perf");
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        var track = demuxer.Info.FirstTrack(MediaKind.Audio)!;
        using var decoder = MediaRegistries.Decoders().CreateAudio(track).Decoder!;
        var frames = new List<AudioFrame>();
        return (double)DecodePass(demuxer, track.Id, decoder, frames).Samples / track.Audio!.SampleRate;
    }

    /// <summary>
    /// Reads the demuxer to its end, decoding one track when there is a decoder; returns the samples
    /// decoded and the packets read.
    /// </summary>
    private static (long Samples, int Packets) DecodePass(IDemuxer demuxer, int trackId, IAudioDecoder? decoder, List<AudioFrame> frames)
    {
        long samples = 0;
        var packets = 0;
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                packets++;
                if (decoder is not null && packet.TrackId == trackId)
                {
                    decoder.Decode(packet, frames);
                }
            }

            foreach (var frame in frames)
            {
                samples += frame.SampleCount;
                frame.Dispose();
            }

            frames.Clear();
        }

        return (samples, packets);
    }

    private static long ReadAll(byte[] file)
    {
        using var source = new MemoryByteSource(file, "perf");
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        long bytes = 0;
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            bytes += packet.Data.Length;
            packet.Dispose();
        }

        return bytes;
    }

    [Theory]
    [InlineData("flac-decode-x-realtime", "flac/stereo-16bit-44k-lpc.flac")]
    [InlineData("mp3-decode-x-realtime", "mp3/stereo-44k-128k-cbr.mp3")]
    public void OwnDecodersRunFarFasterThanRealTime(string measure, string fixture)
    {
        var file = Fixture(fixture);
        var seconds = DecodeAll(file);

        PerfBaselines.AtLeast(measure, Rate(() => DecodeAll(file)) * seconds);
    }

    [Theory]
    [InlineData("mp4-demux-mb-per-second", "mp4/h264-aac.mp4")]
    [InlineData("matroska-demux-mb-per-second", "mkv/h264-aac-subtitles.mkv")]
    public void DemuxersReadQuickly(string measure, string fixture)
    {
        var file = Fixture(fixture);

        PerfBaselines.AtLeast(measure, Rate(() => ReadAll(file)) * file.Length / (1024.0 * 1024));
    }

    [Fact]
    public void TheResamplerRunsFarFasterThanRealTime()
    {
        var resampler = new Resampler(44_100, 48_000, 2);
        using var frame = AudioFrame.Rent(44_100, 2, 4410);

        var rate = Rate(() => resampler.Process(frame)?.Dispose());

        PerfBaselines.AtLeast("resampler-x-realtime", rate * 0.1);
    }

    /// <summary>
    /// Playback must not feed the garbage collector (§7.2): once warm, demuxing and decoding allocate
    /// nothing. The fixtures are short, so one demuxer (and our own decoder, where the file's audio has
    /// one) plays the file several times over, seeking back to the start between passes; only the
    /// passes after the first are counted, and the seeks are not. Exact, so it needs no baseline.
    /// </summary>
    [Theory]
    [InlineData("flac/stereo-16bit-44k-lpc.flac", true)]
    [InlineData("mp3/stereo-44k-128k-cbr.mp3", true)]
    [InlineData("mp4/h264-aac.mp4", false)]
    [InlineData("mkv/h264-aac-subtitles.mkv", false)]
    public void PlaybackAllocatesNothingOnceWarm(string fixture, bool ownDecoder)
    {
        using var source = new MemoryByteSource(Fixture(fixture), "perf");
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        var track = demuxer.Info.FirstTrack(MediaKind.Audio)!;
        using var decoder = ownDecoder ? MediaRegistries.Decoders().CreateAudio(track).Decoder! : null;
        var frames = new List<AudioFrame>();

        long allocated = 0;
        var packets = 0;
        for (var pass = 0; pass < 6; pass++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var (_, read) = DecodePass(demuxer, track.Id, decoder, frames);
            if (pass > 0)
            {
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                packets += read;
            }

            demuxer.Seek(MediaTime.Zero, CancellationToken.None);
            decoder?.Flush();
        }

        Assert.True(packets > 50, $"Only {packets} packets were measured.");
        Assert.Equal(0, allocated);
    }
}
