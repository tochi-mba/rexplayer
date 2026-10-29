using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Apple;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// AAC and Opus through the Mac's AudioToolbox (ADR-0021), played through a whole session and held
/// to the same independent references, with the same accuracy limits, as Windows' decoders (ADR-0020).
/// They run only on a Mac and skip elsewhere.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class AtAudioDecoderTests
{
    private static async Task<(float[][] Played, string Frames)> Play(string fixture, int channels)
    {
        var sink = new RecordingAudioSink(channels: channels, sampleRate: 48_000);
        var factory = new Recording(new AppleDecoderFactory());
        var options = new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(),
            Decoders = MediaRegistries.Decoders(factory),
            AudioSinkFactory = () => sink,
            AutoPlay = true,
        };
        using var session = new MediaSession(options, _ => { });
        await session.OpenAsync(new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture));
        await session.WaitForFinishAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        var log = factory.Frames;
        var frames = $"{log.Count} frames, first {string.Join(", ", log.Take(3))}, last {string.Join(", ", log.TakeLast(2))}";
        return ([.. Enumerable.Range(0, channels).Select(c => sink.Channel(c).ToArray())], frames);
    }

    /// <summary>The Mac's decoders, noting each audio frame's start (in samples) and length, to say why a decode differs.</summary>
    private sealed class Recording(IDecoderFactory inner) : IDecoderFactory
    {
        public List<string> Frames { get; } = [];

        public string Name => inner.Name;

        public DecoderSource Source => inner.Source;

        public int Rank => inner.Rank;

        public bool CanDecode(TrackInfo track) => inner.CanDecode(track);

        public IAudioDecoder CreateAudio(TrackInfo track) => new Noting(inner.CreateAudio(track), Frames);

        public IVideoDecoder CreateVideo(TrackInfo track) => inner.CreateVideo(track);
    }

    private sealed class Noting(IAudioDecoder inner, List<string> frames) : IAudioDecoder
    {
        private readonly List<AudioFrame> _decoded = [];

        public string Name => inner.Name;

        public DecoderSource Source => inner.Source;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            inner.Decode(packet, _decoded);
            Pass($"{packet.Pts.ToSamples(48_000)}/{packet.DiscardSamples}", output);
        }

        public void Drain(ICollection<AudioFrame> output)
        {
            inner.Drain(_decoded);
            Pass("drain", output);
        }

        public void Flush() => inner.Flush();

        public void Dispose() => inner.Dispose();

        private void Pass(string packet, ICollection<AudioFrame> output)
        {
            foreach (var frame in _decoded)
            {
                lock (frames)
                {
                    frames.Add($"packet {packet} -> {frame.Pts.ToSamples(48_000)}+{frame.SampleCount}");
                }

                output.Add(frame);
            }

            _decoded.Clear();
        }
    }

    [Theory]
    [InlineData("mp4/h264-aac.mp4", "mp4/h264-aac.aac.reference.wav")]
    [InlineData("mkv/h264-aac-subtitles.mkv", "mkv/h264-aac-subtitles.aac.reference.wav")]
    [Capability("FMT-A05", CapabilityPlatform.MacOS)]
    public async Task AacPlaysLikeTheSameIndependentDecoderWindowsIsHeldTo(string fixture, string reference)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "AudioToolbox exists only on a Mac.");
        var expected = ReferenceAudio.Read(RepoPaths.Combine($"tests/fixtures/{reference}"));
        var sink = new RecordingAudioSink(channels: expected.Length, sampleRate: 48_000);
        var options = new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(),
            Decoders = MediaRegistries.Decoders(new AppleDecoderFactory()),
            AudioSinkFactory = () => sink,
            AutoPlay = true,
        };
        using var session = new MediaSession(options, _ => { });

        await session.OpenAsync(new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture));
        await session.WaitForFinishAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        for (var c = 0; c < expected.Length; c++)
        {
            var played = sink.Channel(c).ToArray();
            Assert.Equal(expected[c].Length, played.Length);

            // The same two limits Windows' decoder meets: the standard's limited accuracy over the
            // whole stream, and twice its full accuracy once the attack's short windows are past.
            var whole = ReferenceAudio.Difference(played, expected[c]);
            var settled = ReferenceAudio.Difference(played[2048..], expected[c][2048..]);
            Assert.True(whole.Rms < ReferenceAudio.LimitedAccuracyRms, $"channel {c}: RMS difference {whole.Rms}, peak {whole.Peak}");
            Assert.True(settled.Rms < 2 * ReferenceAudio.FullAccuracyRms, $"channel {c}: RMS difference after the attack {settled.Rms}");
        }
    }

    [Theory]
    [InlineData("ogg/opus.opus", "ogg/opus.reference.wav")]
    [InlineData("mkv/vp9-opus.webm", "mkv/vp9-opus.audio0.reference.wav")]
    [Capability("FMT-A10", CapabilityPlatform.MacOS)]
    public async Task OpusPlaysLikeTheSameIndependentDecoderWindowsIsHeldTo(string fixture, string reference)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "AudioToolbox exists only on a Mac.");
        var expected = ReferenceAudio.Read(RepoPaths.Combine($"tests/fixtures/{reference}"));

        var (played, frames) = await Play(fixture, expected.Length);

        // The pre-skip is left out and the last page trims the end, by the demuxers, as on Windows.
        for (var c = 0; c < expected.Length; c++)
        {
            var why = $"channel {c}: {played[c].Length} samples for {expected[c].Length}, late by {ReferenceAudio.BestLag(played[c], expected[c], 1_000)}; {frames}";
            Assert.True(expected[c].Length == played[c].Length, why);
            var difference = ReferenceAudio.Difference(played[c], expected[c]);
            Assert.True(difference.Rms < ReferenceAudio.LimitedAccuracyRms, $"RMS difference {difference.Rms}, peak {difference.Peak}; {why}");
        }
    }

    [Fact]
    public void ATrackAudioToolboxIsNotGivenIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "AudioToolbox exists only on a Mac.");
        var vorbis = new TrackInfo { Id = 1, Codec = CodecId.Vorbis, CodecPrivate = [1], Audio = new AudioTrackInfo { SampleRate = 48_000, Channels = 2 } };

        Assert.Throws<MediaFormatException>(() => new AtAudioDecoder(vorbis));
        Assert.Throws<MediaFormatException>(() => new AtAudioDecoder(vorbis with { Codec = CodecId.Aac, CodecPrivate = [] }));
        Assert.Throws<MediaFormatException>(() => new AtAudioDecoder(vorbis with { Codec = CodecId.Opus, CodecPrivate = [] }));
    }
}
