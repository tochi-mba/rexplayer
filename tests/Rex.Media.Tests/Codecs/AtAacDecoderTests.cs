using Rex.Media.AppCore;
using Rex.Media.Codecs.Apple;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// AAC through the Mac's AudioToolbox (ADR-0021), played through a whole session and held to the
/// same independent reference, with the same accuracy limits, as Windows' AAC decoder (ADR-0020).
/// They run only on a Mac and skip elsewhere.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class AtAacDecoderTests
{
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

    [Fact]
    public void ATrackThatIsNotAacWithItsConfigurationIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "AudioToolbox exists only on a Mac.");
        var opus = new TrackInfo { Id = 1, Codec = CodecId.Opus, CodecPrivate = [1], Audio = new AudioTrackInfo { SampleRate = 48_000, Channels = 2 } };

        Assert.Throws<MediaFormatException>(() => new AtAacDecoder(opus));
        Assert.Throws<MediaFormatException>(() => new AtAacDecoder(opus with { Codec = CodecId.Aac, CodecPrivate = [] }));
    }
}
