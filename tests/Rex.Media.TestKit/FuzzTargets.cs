using Rex.Media.AppCore;
using Rex.Media.Codecs.Aac;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>
/// The parsers fed untrusted bytes, as fuzzing targets: each takes any input and either succeeds or
/// throws <see cref="MediaFormatException"/>. Anything else (an index out of range, a hang, running out
/// of memory) is a bug. The nightly fuzzer drives these with libFuzzer, and the pure suite replays the
/// corpus it found through the same code, so a fixed crash stays fixed.
/// </summary>
public static class FuzzTargets
{
    private const int PacketLimit = 10_000;

    /// <summary>Every target by name, with the fixture folders whose files seed its corpus.</summary>
    public static IReadOnlyDictionary<string, (Action<byte[]> Run, string[] Seeds)> All { get; } = new Dictionary<string, (Action<byte[]>, string[])>(StringComparer.Ordinal)
    {
        ["demux"] = (Demux, ["smoke", "flac", "mp3", "mp4", "mkv", "video"]),
        ["aac-config"] = (bytes => _ = AacConfig.TryParse(bytes, out _), []),
        ["adts"] = (bytes => _ = AdtsHeader.TryParse(bytes, out _), []),
        ["avc-config"] = (bytes => AvcConfig.Parse(bytes).Sequence(), []),
        ["hevc-config"] = (bytes => HevcConfig.Parse(bytes).Sequence(), []),
        ["h264-sps"] = (bytes => _ = H264Sps.TryParse(bytes, out _), []),
        ["hevc-sps"] = (bytes => _ = HevcSps.TryParse(bytes, out _), []),
        ["annex-b"] = (bytes => _ = NalUnits.SplitAnnexB(bytes).Select(range => NalUnits.Unescape(bytes.AsSpan()[range])).Count(), []),
    };

    /// <summary>Runs one target, letting only a clean refusal through.</summary>
    public static void Run(string target, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            All[target].Run(input);
        }
        catch (MediaFormatException)
        {
            // A clean refusal is the right answer for bytes that are not media.
        }
    }

    /// <summary>Probes, opens, reads every packet, decodes the first audio track, seeks and reads again.</summary>
    private static void Demux(byte[] input)
    {
        using var source = new MemoryByteSource(input, "fuzz");
        var factory = MediaRegistries.Demuxers().Probe(source, CancellationToken.None);
        if (factory is null)
        {
            return;
        }

        using var demuxer = factory.Open(source, CancellationToken.None);
        var track = demuxer.Info.Tracks.FirstOrDefault(t => t.Audio is not null);
        using var decoder = track is null ? null : MediaRegistries.Decoders().CreateAudio(track).Decoder;
        var frames = new List<AudioFrame>();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var read = 0; read < PacketLimit && demuxer.ReadPacket(CancellationToken.None) is { } packet; read++)
            {
                using (packet)
                {
                    if (packet.TrackId == track?.Id)
                    {
                        decoder?.Decode(packet, frames);
                    }
                }

                frames.ForEach(frame => frame.Dispose());
                frames.Clear();
            }

            decoder?.Flush();
            demuxer.Seek(demuxer.Info.Duration.IsKnown ? new MediaTime(demuxer.Info.Duration.Ticks / 2) : MediaTime.Zero, CancellationToken.None);
        }
    }
}
