using Rex.Media.AppCore;
using Rex.Media.Containers;
using Rex.Media.Containers.Matroska;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Codecs;

namespace Rex.Media.Tests.Containers;

public sealed class MatroskaDemuxerTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/mkv/{name}"));

    internal static MatroskaDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.mkv"), CancellationToken.None);

    internal static List<Packet> ReadAll(IDemuxer demuxer)
    {
        var packets = new List<Packet>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add(packet);
        }

        return packets;
    }

    internal static void Release(IEnumerable<Packet> packets)
    {
        foreach (var packet in packets)
        {
            packet.Dispose();
        }
    }

    /// <summary>Decodes one track the way playback does: before time zero and past the duration cut.</summary>
    internal static float[][] DecodeTrack(IDemuxer demuxer, TrackInfo track)
    {
        using var decoder = MediaRegistries.Decoders().CreateAudio(track).Decoder!;
        var rate = track.Audio!.SampleRate;
        var end = track.Duration.ToSamples(rate);
        var planes = Enumerable.Range(0, track.Audio.Channels).Select(_ => new List<float>()).ToArray();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            var frames = new List<AudioFrame>();
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
                if (packet.DiscardSamples > 0)
                {
                    frames[^1].SetSampleCount(frames[^1].SampleCount - packet.DiscardSamples);
                }
            }

            packet.Dispose();
            foreach (var frame in frames)
            {
                var first = frame.Pts.ToSamples(rate);
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
        }

        return [.. planes.Select(p => p.ToArray())];
    }

    [Theory]
    [InlineData(0, CodecId.Flac, 1.2e-7)]
    [InlineData(1, CodecId.Mp3, 6.2e-5)]
    [InlineData(2, CodecId.Pcm, 1e-7)]
    [Capability("FMT-C06")]
    public void EveryAudioTrackDecodesLikeAnIndependentDecoder(int index, CodecId codec, double tolerance)
    {
        var reference = Mp3FixtureTests.ReadReference(RepoPaths.Combine($"tests/fixtures/mkv/flac-mp3-pcm.audio{index}.reference.wav"));
        using var demuxer = Open(Fixture("flac-mp3-pcm.mkv"));
        var track = demuxer.Info.Tracks[index];

        var decoded = DecodeTrack(demuxer, track);

        Assert.Equal(codec, track.Codec);
        Assert.Equal(reference.Length, decoded.Length);
        for (var c = 0; c < reference.Length; c++)
        {
            Assert.Equal(reference[c].Length, decoded[c].Length);
            var firstBad = Enumerable.Range(0, reference[c].Length).FirstOrDefault(i => Math.Abs(decoded[c][i] - reference[c][i]) > tolerance, -1);
            Assert.True(firstBad < 0, $"track {index} channel {c}: first difference at {firstBad}, peak {Mp3FixtureTests.Difference(decoded[c], reference[c]).Peak}");
        }
    }

    [Fact]
    [Capability("META-04")]
    public void AVideoFileDescribesItsTracksTagsChaptersAndCover()
    {
        using var demuxer = Open(Fixture("h264-aac-subtitles.mkv"));
        var info = demuxer.Info;

        Assert.Equal("Matroska", info.FormatName);
        Assert.InRange(info.Duration.Ticks, MediaTime.FromSeconds(0.4).Ticks, MediaTime.FromSeconds(0.43).Ticks);
        Assert.Equal("Terminator", info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", info.Metadata[MetadataKeys.Artist]);
        Assert.Equal(["Intro", "Verse"], info.Chapters.Select(c => c.Title));
        Assert.Equal(MediaTime.FromSeconds(0.2), info.Chapters[1].Start);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], info.CoverArt![..4]);

        var video = info.FirstTrack(MediaKind.Video)!;
        Assert.Equal(CodecId.H264, video.Codec);
        Assert.Equal((128, 72), (video.Video!.Width, video.Video.Height));
        Assert.Equal(new Rational(1, 1), video.Video.PixelAspect);
        Assert.NotEmpty(video.CodecPrivate);
        var audio = info.FirstTrack(MediaKind.Audio)!;
        Assert.Equal(CodecId.Aac, audio.Codec);
        Assert.Equal("yor", audio.Language);
        Assert.Equal((48_000, 1), (audio.Audio!.SampleRate, audio.Audio.Channels));
        var subtitle = info.FirstTrack(MediaKind.Subtitle)!;
        Assert.Equal(CodecId.SubRip, subtitle.Codec);
        Assert.Equal("eng", subtitle.Language);
    }

    [Fact]
    public void BlocksBecomePacketsWithPresentationTimesAndKeyframes()
    {
        using var demuxer = Open(Fixture("h264-aac-subtitles.mkv"));
        var subtitleId = demuxer.Info.FirstTrack(MediaKind.Subtitle)!.Id;
        var videoId = demuxer.Info.FirstTrack(MediaKind.Video)!.Id;

        var packets = ReadAll(demuxer);

        var video = packets.Where(p => p.TrackId == videoId).ToList();
        Assert.Equal(10, video.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => MediaTime.FromSeconds(i * 0.04)), video.Select(p => p.Pts).Order());
        Assert.Equal([0, 5], video.Select((p, i) => (p, i)).Where(x => x.p.IsKeyframe).Select(x => x.i));
        var cues = packets.Where(p => p.TrackId == subtitleId).ToList();
        Assert.Equal(["Hello", "World"], cues.Select(p => System.Text.Encoding.UTF8.GetString(p.Data.Span)));
        Assert.Equal([MediaTime.Zero, MediaTime.FromSeconds(0.2)], cues.Select(p => p.Pts));
        Assert.Equal(MediaTime.FromSeconds(0.2), cues[0].Duration);
        Release(packets);
    }

    [Theory]
    [InlineData(0.25, 0.2)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.19, 0.0)]
    [InlineData(5.0, 0.2)]
    [Capability("PB-03")]
    public void SeekingByCuesStartsAtTheKeyframeAtOrBeforeTheTarget(double target, double keyframe)
    {
        using var demuxer = Open(Fixture("h264-aac-subtitles.mkv"));
        var videoId = demuxer.Info.FirstTrack(MediaKind.Video)!.Id;

        demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);

        var packets = ReadAll(demuxer);
        var firstVideo = packets.First(p => p.TrackId == videoId);
        Assert.True(firstVideo.IsKeyframe);
        Assert.Equal(MediaTime.FromSeconds(keyframe), firstVideo.Pts);
        Release(packets);
    }

    [Fact]
    public void WebMOpusStartsBeforeZeroByItsCodecDelay()
    {
        using var demuxer = Open(Fixture("vp9-opus.webm"));
        var info = demuxer.Info;
        var audio = info.FirstTrack(MediaKind.Audio)!;

        var packets = ReadAll(demuxer);

        Assert.Equal("WebM", info.FormatName);
        Assert.Equal(CodecId.Vp9, info.FirstTrack(MediaKind.Video)!.Codec);
        Assert.Equal(CodecId.Opus, audio.Codec);
        Assert.Equal(312, audio.Audio!.LeadingPadding);
        Assert.Equal(MediaTime.FromSamples(-312, 48_000), packets.First(p => p.TrackId == audio.Id).Pts);
        Release(packets);
    }

    [Fact]
    public void ALiveStreamWithAnUnsizedSegmentPlaysAndSeeksByItsClusters()
    {
        using var demuxer = Open(Fixture("live-opus.webm"));

        var packets = ReadAll(demuxer);
        demuxer.Seek(MediaTime.FromSeconds(0.3), CancellationToken.None);
        var again = ReadAll(demuxer);

        Assert.True(packets.Count >= 20);
        Assert.Equal(packets.Select(p => p.Pts).Order(), packets.Select(p => p.Pts));
        Assert.True(packets[^1].Pts > MediaTime.FromSeconds(0.35));
        Assert.InRange(again.Count, 1, packets.Count - 1);
        Assert.True(again[0].Pts <= MediaTime.FromSeconds(0.3 - 0.08), "the seek starts a pre-roll before the target");
        Assert.Equal(packets.TakeLast(again.Count).Select(p => p.Pts), again.Select(p => p.Pts));
        Release(packets);
        Release(again);
    }

    [Fact]
    public void TheFactoryKnowsTheEbmlHeader()
    {
        var factory = new MatroskaDemuxerFactory();

        Assert.Equal("matroska", factory.Name);
        Assert.Equal(100, factory.Probe(Fixture("vp9-opus.webm"), ".webm"));
        Assert.Equal(0, factory.Probe("RIFF\0\0\0\0WAVE"u8, ".mkv"));
        Assert.Same(factory, new DemuxerRegistry().Add(factory).Probe(new MemoryByteSource(Fixture("live-opus.webm"), "x"), CancellationToken.None));
        using var demuxer = factory.Open(new MemoryByteSource(Fixture("flac-mp3-pcm.mkv")), CancellationToken.None);
        Assert.Equal(3, demuxer.Info.Tracks.Count);
    }
}
