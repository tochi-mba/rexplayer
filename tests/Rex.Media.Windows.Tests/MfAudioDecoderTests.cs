using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Containers;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests;

/// <summary>Windows' own AAC decoder, reached through Media Foundation, against an independent decoder.</summary>
public sealed class MfAudioDecoderTests
{
    private static readonly MfDecoderFactory Factory = new();

    private static IDemuxer Open(string fixture)
    {
        var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture);
        return MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
    }

    private static TrackInfo AacTrack(IDemuxer demuxer)
    {
        var track = demuxer.Info.FirstTrack(MediaKind.Audio)!;
        if (!Factory.CanDecode(track))
        {
            Assert.Skip("This Windows has no Media Foundation AAC decoder (a Windows N edition without the Media Feature Pack, or a server without Media Foundation).");
        }

        return track;
    }

    /// <summary>Decodes a track as playback does: nothing before zero, nothing past an end the container gives.</summary>
    private static float[][] Decode(IDemuxer demuxer, TrackInfo track, IAudioDecoder decoder)
    {
        var rate = track.Audio!.SampleRate;
        var end = track.Audio.TrailingPadding > 0 ? track.Duration.ToSamples(rate) : long.MaxValue;
        var planes = Enumerable.Range(0, track.Audio.Channels).Select(_ => new List<float>()).ToArray();
        void Keep(List<AudioFrame> frames)
        {
            foreach (var frame in frames)
            {
                var first = frame.Pts.ToSamples(frame.SampleRate);
                for (var c = 0; c < planes.Length; c++)
                {
                    var samples = frame.Channel(c);
                    for (var i = 0; i < samples.Length; i++)
                    {
                        if (first + i >= 0 && first + i < end)
                        {
                            planes[c].Add(samples[i]);
                        }
                    }
                }

                frame.Dispose();
            }

            frames.Clear();
        }

        var frames = new List<AudioFrame>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
                if (packet.DiscardSamples > 0)
                {
                    frames[^1].SetSampleCount(frames[^1].SampleCount - packet.DiscardSamples);
                }
            }

            packet.Dispose();
            Keep(frames);
        }

        decoder.Drain(frames);
        Keep(frames);
        return [.. planes.Select(p => p.ToArray())];
    }

    [Theory]
    [InlineData("mp4/h264-aac.mp4", "mp4/h264-aac.aac.reference.wav")]
    [InlineData("mkv/h264-aac-subtitles.mkv", "mkv/h264-aac-subtitles.aac.reference.wav")]
    [Capability("FMT-A05")]
    public async Task AacPlaysLikeAnIndependentDecoder(string fixture, string reference)
    {
        using (var probe = Open(fixture))
        {
            AacTrack(probe);
        }

        var expected = ReferenceAudio.Read(RepoPaths.Combine($"tests/fixtures/{reference}"));
        var sink = new RecordingAudioSink(channels: expected.Length, sampleRate: 48_000);
        var options = new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(),
            Decoders = MediaRegistries.Decoders(Factory),
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

            // Two decoders of one stream differ most in its first frames, where the tone's attack
            // is coded in short windows: the whole stream must meet the standard's limited accuracy.
            // After the attack Windows' decoder is near full accuracy, by an amount that varies with
            // the Windows build (an RMS of 3.9e-6 on Windows 11, 9.6e-6 on Windows Server against a
            // limit of 8.8e-6), so it must stay within twice that limit.
            var whole = ReferenceAudio.Difference(played, expected[c]);
            var settled = ReferenceAudio.Difference(played[2048..], expected[c][2048..]);
            Assert.True(whole.Rms < ReferenceAudio.LimitedAccuracyRms, $"channel {c}: RMS difference {whole.Rms}, peak {whole.Peak}");
            Assert.True(settled.Rms < 2 * ReferenceAudio.FullAccuracyRms, $"channel {c}: RMS difference after the attack {settled.Rms}");
        }
    }

    [Theory]
    [InlineData("ogg/opus.opus", "ogg/opus.reference.wav")]
    [InlineData("mkv/vp9-opus.webm", "mkv/vp9-opus.audio0.reference.wav")]
    [Capability("FMT-A10")]
    public async Task OpusPlaysLikeAnIndependentDecoder(string fixture, string reference)
    {
        using (var probe = Open(fixture))
        {
            if (!Factory.CanDecode(probe.Info.FirstTrack(MediaKind.Audio)!))
            {
                Assert.Skip("This Windows has no Media Foundation Opus decoder.");
            }
        }

        var expected = ReferenceAudio.Read(RepoPaths.Combine($"tests/fixtures/{reference}"));
        var sink = new RecordingAudioSink(channels: expected.Length, sampleRate: 48_000);
        var options = new EngineOptions { Demuxers = MediaRegistries.Demuxers(), Decoders = MediaRegistries.Decoders(Factory), AudioSinkFactory = () => sink, AutoPlay = true };
        using var session = new MediaSession(options, _ => { });

        await session.OpenAsync(new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture));
        await session.WaitForFinishAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // The pre-skip is left out and the last page trims the end: the same samples as FFmpeg's decoder gives.
        for (var c = 0; c < expected.Length; c++)
        {
            var played = sink.Channel(c).ToArray();
            Assert.Equal(expected[c].Length, played.Length);
            var difference = ReferenceAudio.Difference(played, expected[c]);
            Assert.True(difference.Rms < ReferenceAudio.LimitedAccuracyRms, $"channel {c}: RMS difference {difference.Rms}, peak {difference.Peak}");
        }
    }

    [Fact]
    public void AFlushForgetsWhatTheDecoderHeld()
    {
        using var first = Open("mp4/h264-aac.mp4");
        var track = AacTrack(first);
        using var decoder = new MfAudioDecoder(track);
        var whole = Decode(first, track, decoder);

        decoder.Flush();
        using var second = Open("mp4/h264-aac.mp4");
        var again = Decode(second, track, decoder);

        Assert.Equal(whole[0], again[0]);
        Assert.False(string.IsNullOrEmpty(decoder.Name));
    }

    [Fact]
    public void TheFactoryOnlyTakesTheCodecsWindowsIsAskedFor()
    {
        var mp3 = new TrackInfo { Id = 1, Codec = CodecId.Mp3, Audio = new AudioTrackInfo { SampleRate = 44_100, Channels = 2 } };
        var video = new TrackInfo { Id = 1, Codec = CodecId.Aac };

        Assert.Equal(("Windows Media Foundation", DecoderSource.OsSoftware), (Factory.Name, Factory.Source));
        Assert.True(Factory.Rank < 100);
        Assert.False(Factory.CanDecode(mp3));
        Assert.False(Factory.CanDecode(video));
        Assert.Throws<MediaFormatException>(() => new MfAudioDecoder(mp3));
        Assert.Throws<MediaFormatException>(() => new MfAudioDecoder(video));
    }
}
