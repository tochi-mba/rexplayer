using Rex.Media.AppCore;
using Rex.Media.Containers;
using Rex.Media.Containers.Mp4;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Codecs;

namespace Rex.Media.Tests.Containers;

public sealed class Mp4DemuxerTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/mp4/{name}"));

    private static Mp4Demuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.mp4"), CancellationToken.None);

    private static List<Packet> ReadAll(IDemuxer demuxer)
    {
        var packets = new List<Packet>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add(packet);
        }

        return packets;
    }

    private static void Release(IEnumerable<Packet> packets)
    {
        foreach (var packet in packets)
        {
            packet.Dispose();
        }
    }

    /// <summary>Decodes the first audio track the way playback does: before time zero and past the duration cut.</summary>
    internal static float[][] DecodeAudio(IDemuxer demuxer, MediaTime? from = null)
    {
        var track = demuxer.Info.Tracks.First(t => t.Audio is not null);
        using var decoder = MediaRegistries.Decoders().CreateAudio(track).Decoder!;
        var rate = track.Audio!.SampleRate;
        var start = (from ?? MediaTime.Zero).ToSamples(rate);
        var end = track.Duration.ToSamples(rate);
        var planes = Enumerable.Range(0, track.Audio.Channels).Select(_ => new List<float>()).ToArray();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            var frames = new List<AudioFrame>();
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
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
                        if (first + i >= start && first + i < end)
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
    [InlineData("mp3-in-mp4.mp4", 6.2e-5)]
    [InlineData("flac-in-mp4.mp4", 1.2e-7)]
    [InlineData("pcm-s16le.mov", 1e-7)]
    [InlineData("pcm-s24be.mov", 1e-7)]
    [InlineData("pcm-f32le.mov", 1e-6)]
    [Capability("FMT-C05")]
    public void AudioInMp4AndQuickTimeDecodesLikeAnIndependentDecoder(string name, double tolerance)
    {
        var reference = Mp3FixtureTests.ReadReference(RepoPaths.Combine($"tests/fixtures/mp4/{Path.GetFileNameWithoutExtension(name)}.reference.wav"));
        using var demuxer = Open(Fixture(name));

        var decoded = DecodeAudio(demuxer);

        Assert.Equal(reference.Length, decoded.Length);
        for (var c = 0; c < reference.Length; c++)
        {
            Assert.Equal(reference[c].Length, decoded[c].Length);
            var firstBad = Enumerable.Range(0, reference[c].Length).FirstOrDefault(i => Math.Abs(decoded[c][i] - reference[c][i]) > tolerance, -1);
            Assert.True(firstBad < 0, $"{name} channel {c}: first difference at {firstBad}: {(firstBad < 0 ? 0 : decoded[c][firstBad])} vs {(firstBad < 0 ? 0 : reference[c][firstBad])}, peak {Mp3FixtureTests.Difference(decoded[c], reference[c]).Peak}");
        }
    }

    [Fact]
    [Capability("META-03")]
    public void AVideoFileDescribesItsTracksTagsAndChapters()
    {
        using var demuxer = Open(Fixture("h264-aac.mp4"));
        var info = demuxer.Info;

        Assert.Equal("MP4", info.FormatName);
        Assert.Equal(MediaTime.FromSeconds(0.4), info.Duration);
        Assert.Equal("Basquiat", info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", info.Metadata[MetadataKeys.Artist]);
        Assert.Equal("Lungu Boy", info.Metadata[MetadataKeys.Album]);
        Assert.Equal("2024", info.Metadata[MetadataKeys.Date]);
        Assert.Equal(["Intro", "Verse"], info.Chapters.Select(c => c.Title));
        Assert.Equal(MediaTime.FromSeconds(0.2), info.Chapters[1].Start);

        var video = info.FirstTrack(MediaKind.Video)!;
        Assert.Equal(CodecId.H264, video.Codec);
        Assert.Equal((128, 72, 0), (video.Video!.Width, video.Video.Height, video.Video.Rotation));
        Assert.Equal(new Rational(25, 1), video.Video.FrameRate);
        Assert.NotEmpty(video.CodecPrivate);
        Assert.True(video.IsDefault);
        var audio = info.FirstTrack(MediaKind.Audio)!;
        Assert.Equal(CodecId.Aac, audio.Codec);
        Assert.Equal((48_000, 1), (audio.Audio!.SampleRate, audio.Audio.Channels));
        Assert.Equal(1024, audio.Audio.LeadingPadding);
        Assert.True(audio.Audio.TrailingPadding > 0);
        Assert.Equal(2, info.Tracks.Count);
    }

    [Fact]
    public void PacketsComeInFileOrderWithBFrameTimestampsAndKeyframesMarked()
    {
        using var demuxer = Open(Fixture("h264-aac.mp4"));

        var packets = ReadAll(demuxer);

        var video = packets.Where(p => p.TrackId == 1).ToList();
        Assert.Equal(10, video.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => MediaTime.FromSeconds(i * 0.04)), video.Select(p => p.Pts).Order());
        Assert.NotEqual(video.Select(p => p.Pts).Order(), video.Select(p => p.Pts));
        Assert.Equal([0, 5], video.Select((p, i) => (p, i)).Where(x => x.p.IsKeyframe).Select(x => x.i));
        Assert.All(video, p => Assert.Equal(MediaTime.FromSeconds(0.04), p.Duration));
        Assert.Equal(MediaTime.FromSamples(-1024, 48_000), packets.First(p => p.TrackId == 2).Pts);
        Release(packets);
    }

    [Fact]
    public void AFragmentedFileHasTheSameTracksAndEverySample()
    {
        using var plain = Open(Fixture("h264-aac.mp4"));
        using var fragmented = Open(Fixture("h264-aac-fragmented.mp4"));

        var plainPackets = ReadAll(plain);
        var fragmentedPackets = ReadAll(fragmented);

        Assert.Equal(plain.Info.Tracks.Select(t => t.Codec), fragmented.Info.Tracks.Select(t => t.Codec));
        Assert.Equal(["Intro", "Verse"], fragmented.Info.Chapters.Select(c => c.Title));
        Assert.Equal(plainPackets.Count(p => p.TrackId == 1), fragmentedPackets.Count(p => p.TrackId == 1));
        Assert.Equal(plainPackets.Where(p => p.TrackId == 1).Select(p => p.IsKeyframe), fragmentedPackets.Where(p => p.TrackId == 1).Select(p => p.IsKeyframe));
        Release(plainPackets);
        Release(fragmentedPackets);
    }

    [Fact]
    public void ADisplayMatrixBecomesAClockwiseRotation()
    {
        using var demuxer = Open(Fixture("h264-rotated.mov"));

        var video = demuxer.Info.Tracks[0].Video!;
        Assert.Equal("QuickTime", demuxer.Info.FormatName);
        Assert.Equal(270, video.Rotation);
        Assert.Equal((128, 72), (video.Width, video.Height));
    }

    [Fact]
    public void ACodecWithoutADecoderIsStillDescribed()
    {
        using var demuxer = Open(Fixture("alac.m4a"));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal(CodecId.Alac, track.Codec);
        Assert.Equal((44_100, 2), (track.Audio!.SampleRate, track.Audio.Channels));
        Assert.NotEmpty(track.CodecPrivate);
        Assert.Null(MediaRegistries.Decoders().CreateAudio(track).Decoder);
    }

    [Theory]
    [InlineData(0.25, 0.2)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.19, 0.0)]
    [InlineData(5.0, 0.2)]
    public void SeekingStartsAtTheKeyframeAtOrBeforeTheTarget(double target, double keyframe)
    {
        using var demuxer = Open(Fixture("h264-aac.mp4"));

        demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);

        var packets = ReadAll(demuxer);
        var firstVideo = packets.First(p => p.TrackId == 1);
        Assert.True(firstVideo.IsKeyframe);
        Assert.Equal(MediaTime.FromSeconds(keyframe), firstVideo.Pts);
        Assert.True(packets.First(p => p.TrackId == 2).Pts <= firstVideo.Pts);
        Release(packets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3000)]
    [InlineData(8819)]
    [Capability("PB-03")]
    public void SeekingAnAudioOnlyFileThenTrimmingMatchesTheWholeDecode(int target)
    {
        var file = Fixture("mp3-in-mp4.mp4");
        using var whole = Open(file);
        var expected = DecodeAudio(whole)[0][target..];
        using var demuxer = Open(file);

        demuxer.Seek(MediaTime.FromSamples(target, 44_100), CancellationToken.None);
        var decoded = DecodeAudio(demuxer, MediaTime.FromSamples(target, 44_100))[0];

        Assert.Equal(expected.Length, decoded.Length);
        Assert.True(Mp3FixtureTests.Difference(decoded, expected).Peak < 1e-4);
    }

    [Fact]
    public void TheFactoryKnowsIsoFilesByTheirFirstBox()
    {
        var factory = new Mp4DemuxerFactory();

        Assert.Equal("mp4", factory.Name);
        Assert.Equal(100, factory.Probe(Fixture("h264-aac.mp4"), ".mp4"));
        Assert.Equal(60, factory.Probe([0, 0, 0, 8, .. "moov"u8], ".mov"));
        Assert.Equal(60, factory.Probe([0, 0, 0, 8, .. "wide"u8], ".mov"));
        Assert.Equal(0, factory.Probe("RIFF\0\0\0\0WAVE"u8, ".mp4"));
        Assert.Equal(0, factory.Probe([0, 0, 0], ".mp4"));
        Assert.Same(factory, new DemuxerRegistry().Add(factory).Probe(new MemoryByteSource(Fixture("pcm-s16le.mov"), "x"), CancellationToken.None));
        using var demuxer = factory.Open(new MemoryByteSource(Fixture("alac.m4a")), CancellationToken.None);
        Assert.Equal("MP4", demuxer.Info.FormatName);
    }
}
