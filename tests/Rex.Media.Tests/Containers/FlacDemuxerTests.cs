using Rex.Media.Codecs.Flac;
using Rex.Media.Containers;
using Rex.Media.Containers.Flac;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class FlacDemuxerTests
{
    private static readonly byte[] Cover = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private sealed class LiveSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public string Name => "live.flac";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    private static FlacDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.flac"), CancellationToken.None);

    private static int[][] Stereo(int samples) =>
    [
        FlacBuilder.Tone(samples, 16, 440, 44_100),
        FlacBuilder.Tone(samples, 16, 550, 44_100, seed: 2),
    ];

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

    /// <summary>The frames the builder wrote, as byte arrays, to compare packets against.</summary>
    private static List<byte[]> Frames(FlacBuilder builder, byte[] file, int trailing = 0)
    {
        var ends = builder.FrameOffsets.Skip(1).Append(file.Length - trailing - builder.FirstFrameOffset).ToList();
        return builder.FrameOffsets.Select((start, i) => file[(int)(builder.FirstFrameOffset + start)..(int)(builder.FirstFrameOffset + ends[i])]).ToList();
    }

    [Fact]
    [Capability("FMT-C04")]
    public void TheStreamInfoBecomesTheTrackAndEveryFrameAPacket()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(10_000));
        using var demuxer = Open(file);

        var track = Assert.Single(demuxer.Info.Tracks);
        Assert.Equal("FLAC", demuxer.Info.FormatName);
        Assert.Equal(CodecId.Flac, track.Codec);
        Assert.Equal(44_100, track.Audio!.SampleRate);
        Assert.Equal(2, track.Audio.Channels);
        Assert.Equal(16, track.Audio.BitsPerSample);
        Assert.Equal(ChannelLayout.Stereo, track.Audio.Layout);
        Assert.Equal(34, track.CodecPrivate.Length);
        Assert.True(track.IsDefault);
        Assert.Equal(MediaTime.FromSamples(10_000, 44_100), demuxer.Info.Duration);
        Assert.True(demuxer.Info.IsSeekable);

        var packets = ReadAll(demuxer);
        Assert.Equal(Frames(builder, file).Select(f => Convert.ToHexString(f)), packets.Select(p => Convert.ToHexString(p.Data.Span)));
        Assert.Equal(MediaTime.FromSamples(1152 * 3, 44_100), packets[3].Pts);
        Assert.Equal(MediaTime.FromSamples(1152, 44_100), packets[3].Duration);
        Assert.Equal(MediaTime.FromSamples(10_000 - (8 * 1152), 44_100), packets[^1].Duration);
        Assert.All(packets, p => Assert.True(p.IsKeyframe));
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
        Release(packets);
    }

    [Fact]
    [Capability("META-02")]
    public void TagsPicturesAndCueSheetTracksAreRead()
    {
        var builder = new FlacBuilder
        {
            Comments = ["TITLE=Sungba", "ARTIST=Asake", "artist=Burna Boy", "ALBUM=Mr. Money With The Vibe", "TRACKNUMBER=3", "TRACKTOTAL=12", "REPLAYGAIN_TRACK_GAIN=-6.20 dB", "EMPTY=", "noequals"],
            Picture = (Cover, "image/png", 3),
            CueTracks = [(0, 1), (5000, 2)],
            SeekPointInterval = 2,
            SeekPlaceholders = 2,
            Padding = 100,
            Application = "rexp",
        };
        using var demuxer = Open(builder.Build(Stereo(10_000)));

        var metadata = demuxer.Info.Metadata;
        Assert.Equal("Sungba", metadata[MetadataKeys.Title]);
        Assert.Equal("Asake; Burna Boy", metadata[MetadataKeys.Artist]);
        Assert.Equal("3/12", metadata[MetadataKeys.Track]);
        Assert.Equal("-6.20 dB", metadata[MetadataKeys.ReplayGainTrackGain]);
        Assert.False(metadata.ContainsKey("empty"));
        Assert.Equal(Cover, demuxer.Info.CoverArt);
        Assert.Equal([new Chapter(MediaTime.Zero, "Track 01"), new Chapter(MediaTime.FromSamples(5000, 44_100), "Track 02")], demuxer.Info.Chapters);
    }

    [Fact]
    public void AnId3TagInFrontIsSkippedAndItsFieldsAreKeptUnderTheVorbisOnes()
    {
        var builder = new FlacBuilder { Comments = ["TITLE=From Vorbis"] };
        var flac = builder.Build(Stereo(3000));
        var id3 = Id3Builder.V24().Text("TIT2", "From ID3").Text("TPE1", "Asake").Picture(Cover).Build();
        using var demuxer = Open([.. id3, .. flac]);

        Assert.Equal("From Vorbis", demuxer.Info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Asake", demuxer.Info.Metadata[MetadataKeys.Artist]);
        Assert.Equal(Cover, demuxer.Info.CoverArt);
        var packets = ReadAll(demuxer);
        Assert.Equal(3, packets.Count);
        Release(packets);
    }

    [Fact]
    public void AnUnknownTotalLeavesTheDurationUnknownAndAStreamCannotSeek()
    {
        var builder = new FlacBuilder { WriteTotalSamples = false };
        var file = builder.Build(Stereo(3000));
        using var demuxer = new FlacDemuxer(new LiveSource(file), CancellationToken.None);

        Assert.False(demuxer.Info.Duration.IsKnown);
        Assert.False(demuxer.Info.IsSeekable);
        demuxer.Seek(MediaTime.FromSeconds(10), CancellationToken.None);
        var packets = ReadAll(demuxer);
        Assert.Equal(3, packets.Count);
        Release(packets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeekingLandsOnTheFrameHoldingTheTarget(int seekPointInterval)
    {
        var builder = new FlacBuilder { SeekPointInterval = seekPointInterval };
        using var demuxer = Open(builder.Build(Stereo(100_000)));

        var targets = new long[] { 0, 1, 1151, 1152, 50_000, 77_777, 99_999, 98_000, 3 }.Concat(Enumerable.Range(0, 300).Select(i => i * 333L));
        foreach (var sample in targets)
        {
            demuxer.Seek(MediaTime.FromSamples(sample, 44_100), CancellationToken.None);
            using var packet = demuxer.ReadPacket(CancellationToken.None)!;
            var first = packet.Pts.ToSamples(44_100);
            Assert.True(first <= sample && sample < first + 1152, $"Seeking to {sample} landed on the frame at {first}.");
        }
    }

    [Fact]
    public void SeekingPastTheEndLeavesOnlyTheLastFrame()
    {
        var builder = new FlacBuilder();
        using var demuxer = Open(builder.Build(Stereo(5000)));

        demuxer.Seek(MediaTime.FromSeconds(60), CancellationToken.None);

        var packets = ReadAll(demuxer);
        Assert.Equal(MediaTime.FromSamples(4 * 1152, 44_100), Assert.Single(packets).Pts);
        Release(packets);
    }

    [Fact]
    public void VariableBlockSizedStreamsSeekBySampleNumber()
    {
        var builder = new FlacBuilder { VariableBlockSizes = [1000, 4000, 700] };
        using var demuxer = Open(builder.Build(Stereo(40_000)));

        demuxer.Seek(MediaTime.FromSamples(23_000, 44_100), CancellationToken.None);

        using var packet = demuxer.ReadPacket(CancellationToken.None)!;
        var first = packet.Pts.ToSamples(44_100);
        Assert.True(first <= 23_000 && 23_000 < first + packet.Duration.ToSamples(44_100));
    }

    [Fact]
    public void ADamagedFrameKeepsItsBoundsSoEveryOtherFramePlays()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(10_000));
        var frames = Frames(builder, file);
        file[(int)(builder.FirstFrameOffset + builder.FrameOffsets[4] + 40)] ^= 0x55;
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Select(f => f.Length), packets.Select(p => p.Data.Length));
        Release(packets);
    }

    [Fact]
    public void AMissingFrameIsSteppedOverAtTheNextValidBoundary()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(10_000));
        var frames = Frames(builder, file);
        var cut = file.Take((int)(builder.FirstFrameOffset + builder.FrameOffsets[3])).Concat(file.Skip((int)(builder.FirstFrameOffset + builder.FrameOffsets[4]))).ToArray();
        using var demuxer = Open(cut);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Count - 1, packets.Count);
        Assert.Equal(MediaTime.FromSamples(4 * 1152, 44_100), packets[3].Pts);
        Release(packets);
    }

    [Fact]
    public void JunkBeforeTheFirstFrameAndATrailingTagAreSkipped()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(5000));
        var frames = Frames(builder, file);
        var head = file[..(int)builder.FirstFrameOffset];
        var body = file[(int)builder.FirstFrameOffset..];
        var trailer = new byte[128];
        "TAG"u8.CopyTo(trailer);
        using var demuxer = Open([.. head, 0, 0, 0xFF, 0x00, 7, .. body, .. trailer]);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Select(Convert.ToHexString), packets.Select(p => Convert.ToHexString(p.Data.Span)));
        Release(packets);
    }

    [Fact]
    public void AnApeTagAfterTheLastFrameIsNotPartOfIt()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(3000));
        var frames = Frames(builder, file);
        using var demuxer = Open([.. file, .. "APETAGEX"u8, .. new byte[24]]);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Select(f => f.Length), packets.Select(p => p.Data.Length));
        Release(packets);
    }

    [Fact]
    public void ALastFrameWithABrokenChecksumRunsToTheEnd()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(3000));
        file[^1] ^= 0xFF;
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal(Frames(builder, file).Select(f => f.Length), packets.Select(p => p.Data.Length));
        Release(packets);
    }

    [Fact]
    public void AFrameDamagedSoBadlyItsSuccessorIsUnrecognisableStillEndsAtTheNextHeader()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo(10_000));
        var frames = Frames(builder, file);
        var at = (int)(builder.FirstFrameOffset + builder.FrameOffsets[5]);
        file[at + 4] = 0x09; // the next frame's number now says 9, not 5
        file[at + 5] = (byte)Crc.Crc8Flac.Compute(file.AsSpan(at, 5));
        file[at - 3] ^= 0x01; // and the frame before it fails its checksum
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Select(f => f.Length), packets.Select(p => p.Data.Length));
        Release(packets);
    }

    [Fact]
    public void AFrameBoundaryInTheLastBytesOfTheReadBufferIsStillFound()
    {
        // Size a verbatim first frame so the next header starts within a header's length of the
        // 64 KiB the demuxer reads first; it must read on rather than judge a half-seen header.
        var verbatim = new FlacSubframePlan { Kind = FlacSubframeKind.Verbatim, UseWastedBits = false };
        foreach (var first in Enumerable.Range(32_700, 120))
        {
            var builder = new FlacBuilder { VariableBlockSizes = [first, 100], Plan = (_, _) => verbatim };
            var file = builder.Build([FlacBuilder.Tone(first + 100, 16, 440, 44_100)]);
            if (builder.FrameOffsets[1] is <= 65_536 - FlacFrameHeader.MaxLength or >= 65_535)
            {
                continue;
            }

            using var demuxer = Open(file);
            var packets = ReadAll(demuxer);
            Assert.Equal(Frames(builder, file).Select(f => f.Length), packets.Select(p => p.Data.Length));
            Release(packets);
            return;
        }

        Assert.Fail("No block size put the second frame at the edge of the buffer.");
    }

    [Fact]
    public void ADamagedFrameJustBeforeTheLastKeepsItsBounds()
    {
        var builder = new FlacBuilder();
        var file = builder.Build(Stereo((1152 * 2) + 50));
        var frames = Frames(builder, file);
        file[(int)(builder.FirstFrameOffset + builder.FrameOffsets[1] + 40)] ^= 0x55;
        using var demuxer = Open(file);

        var packets = ReadAll(demuxer);

        Assert.Equal(frames.Select(f => f.Length), packets.Select(p => p.Data.Length));
        Release(packets);
    }

    [Fact]
    public void ACueSheetTooShortToHoldTracksAddsNoChapters()
    {
        using var demuxer = Open(new FlacBuilder { RawBlocks = [(5, new byte[10])] }.Build(Stereo(100)));

        Assert.Empty(demuxer.Info.Chapters);
    }

    [Fact]
    public void TheFactoryKnowsFlacByItsMarkerEvenBehindAnId3Tag()
    {
        var factory = new FlacDemuxerFactory();
        var flac = new FlacBuilder().Build(Stereo(100));
        var tagged = (byte[])[.. Id3Builder.V24().Text("TIT2", "x").Padding(100_000).Build(), .. flac];

        Assert.Equal("flac", factory.Name);
        Assert.Equal(100, factory.Probe(flac, ".flac"));
        Assert.Equal(0, factory.Probe("RIFF"u8, ".flac"));
        var registry = new DemuxerRegistry().Add(factory);
        Assert.Same(factory, registry.Probe(new MemoryByteSource(tagged, "x.bin"), CancellationToken.None));
        using var demuxer = factory.Open(new MemoryByteSource(flac), CancellationToken.None);
        Assert.Equal("FLAC", demuxer.Info.FormatName);
    }

    [Fact]
    public void FilesThatAreNotFlacOrBreakTheMetadataRulesAreRejected()
    {
        var good = new FlacBuilder().Build(Stereo(100));
        var wrongFirst = (byte[])good.Clone();
        wrongFirst[4] = 0x04;
        var forbidden = new FlacBuilder { Padding = 10 }.Build(Stereo(100));
        forbidden[4] &= 0x7F;
        forbidden[4 + 4 + 34] = 0xFF;

        Assert.Throws<MediaFormatException>(() => Open("RIFF0000WAVE"u8.ToArray()));
        Assert.Throws<MediaFormatException>(() => Open(wrongFirst));
        Assert.Throws<MediaFormatException>(() => Open(forbidden));
        Assert.Throws<MediaFormatException>(() => Open(good[..20]));
        Assert.Throws<ArgumentNullException>(() => new FlacDemuxer(null!, CancellationToken.None));
    }
}
