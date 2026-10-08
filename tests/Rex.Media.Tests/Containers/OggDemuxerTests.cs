using System.Buffers.Binary;
using Rex.Media.Codecs.Opus;
using Rex.Media.Codecs.Vorbis;
using Rex.Media.Containers.Ogg;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Codecs;

namespace Rex.Media.Tests.Containers;

/// <summary>Ogg files of Vorbis, Opus and FLAC, read as FFmpeg reads them (FMT-C09).</summary>
public sealed class OggDemuxerTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/ogg/{name}"));

    private static OggDemuxer Open(byte[] file) => new(new MemoryByteSource(file, "test.ogg"), CancellationToken.None);

    /// <summary>One Ogg page: its packets laced, with a correct CRC; a packet longer than the page continues on the next.</summary>
    internal static byte[] Page(int serial, int flags, long granule, int sequence, params byte[][] packets)
    {
        var lacing = new List<byte>();
        foreach (var packet in packets)
        {
            var size = packet.Length;
            while (size >= 255)
            {
                lacing.Add(255);
                size -= 255;
            }

            lacing.Add((byte)size);
        }

        var body = packets.SelectMany(packet => packet).ToArray();
        var page = new byte[27 + lacing.Count + body.Length];
        "OggS"u8.CopyTo(page);
        page[5] = (byte)flags;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(14), serial);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)lacing.Count;
        lacing.ToArray().CopyTo(page, 27);
        body.CopyTo(page, 27 + lacing.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc.Crc32Ogg.Compute(page));
        return page;
    }

    private static byte[] HeadPacket(int channels = 2, int preSkip = 312) =>
    [
        .. "OpusHead"u8, 1, (byte)channels, (byte)preSkip, (byte)(preSkip >> 8), 0x80, 0xBB, 0, 0, 0, 0, 0,
    ];

    private static byte[] OpusTags() => [.. "OpusTags"u8, 0, 0, 0, 0, 1, 0, 0, 0, 12, 0, 0, 0, .. "TITLE=Sungba"u8];

    [Fact]
    public void OggIsKnownByItsCapturePattern()
    {
        var factory = new OggDemuxerFactory();

        Assert.Equal(100, factory.Probe("OggS\0\x02"u8, ".ogg"));
        Assert.Equal(0, factory.Probe("fLaC"u8, ".ogg"));
        Assert.Equal("ogg", factory.Name);
        using var demuxer = factory.Open(new MemoryByteSource(Fixture("opus.opus"), "a.opus"), CancellationToken.None);
        Assert.Equal("Ogg", demuxer.Info.FormatName);
    }

    [Fact]
    [Capability("FMT-C09")]
    public void VorbisInOggDecodesLikeAnIndependentDecoder()
    {
        var reference = Mp3FixtureTests.ReadReference(RepoPaths.Combine("tests/fixtures/ogg/vorbis.reference.wav"));
        using var demuxer = Open(Fixture("vorbis.ogg"));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal((CodecId.Vorbis, 44100, 2), (track.Codec, track.Audio!.SampleRate, track.Audio.Channels));
        Assert.Equal(("Sungba", "Asake"), (demuxer.Info.Metadata[MetadataKeys.Title], demuxer.Info.Metadata[MetadataKeys.Artist]));
        Assert.Equal(2.0, demuxer.Info.Duration.TotalSeconds, 2);

        // Granule positions time every sample: the decode lines up with FFmpeg's from the first sample to the last.
        var decoded = MatroskaDemuxerTests.DecodeTrack(demuxer, track);
        for (var c = 0; c < 2; c++)
        {
            Assert.Equal(reference[c].Length, decoded[c].Length);
            Assert.True(Mp3FixtureTests.Difference(decoded[c], reference[c]).Peak < 2e-4, $"channel {c}");
        }
    }

    [Fact]
    [Capability("FMT-C09")]
    public void FlacInOggDecodesExactly()
    {
        var reference = Mp3FixtureTests.ReadReference(RepoPaths.Combine("tests/fixtures/ogg/flac.reference.wav"));
        using var demuxer = Open(Fixture("flac.oga"));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal((CodecId.Flac, 48000, 1), (track.Codec, track.Audio!.SampleRate, track.Audio.Channels));
        Assert.Equal("Terminator", demuxer.Info.Metadata[MetadataKeys.Title]);
        var decoded = MatroskaDemuxerTests.DecodeTrack(demuxer, track);
        Assert.Equal(reference[0].Length, decoded[0].Length);
        Assert.True(Mp3FixtureTests.Difference(decoded[0], reference[0]).Peak < 1e-6);
    }

    [Fact]
    [Capability("FMT-C09")]
    public void OpusPacketsAreTimedFromThePagesWithThePreSkipBeforeZero()
    {
        using var demuxer = Open(Fixture("opus.opus"));
        var track = Assert.Single(demuxer.Info.Tracks);
        var packets = new List<(long Pts, long Duration)>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packets.Add((packet.Pts.ToSamples(48000), packet.Duration.ToSamples(48000)));
            packet.Dispose();
        }

        Assert.Equal((CodecId.Opus, 48000, 2), (track.Codec, track.Audio!.SampleRate, track.Audio.Channels));
        Assert.Equal(312, OpusHead.Parse(track.CodecPrivate).PreSkip);
        // FFmpeg counts the pre-skip in its length; the sound itself is the second it was made from.
        Assert.Equal(1.0, demuxer.Info.Duration.TotalSeconds, 4);
        Assert.Equal(51, packets.Count);
        Assert.Equal(-312, packets[0].Pts);
        Assert.All(packets, p => Assert.Equal(960, p.Duration));
        Assert.Equal(Enumerable.Range(0, 51).Select(i => -312L + (i * 960)), packets.Select(p => p.Pts));
    }

    [Fact]
    [Capability("FMT-C09")]
    public void SeekingLandsAPacketOrSoBeforeTheTarget()
    {
        using var demuxer = Open(Fixture("vorbis.ogg"));
        demuxer.ReadPacket(CancellationToken.None)!.Dispose();

        demuxer.Seek(MediaTime.FromSeconds(1.5), CancellationToken.None);
        using var first = demuxer.ReadPacket(CancellationToken.None)!;

        // Ogg pages hold many packets: playing starts at a page before the target, and the engine trims to it.
        Assert.InRange(first.Pts.TotalSeconds, 0.5, 1.5);
        demuxer.Seek(MediaTime.Zero, CancellationToken.None);
        using var start = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(MediaTime.Zero, start.Pts);
    }

    [Fact]
    public void ADamagedPageIsSkippedAndTheRestPlays()
    {
        byte[] packet = [16 << 3, 1, 2, 3];
        var pages = new List<byte[]>
        {
            Page(7, 2, 0, 0, HeadPacket()),
            Page(7, 0, 0, 1, OpusTags()),
            Page(7, 0, 600, 2, packet, packet, packet, packet, packet),
            Page(7, 0, 1200, 3, packet, packet, packet, packet, packet),
            Page(7, 0, 1800, 4, packet, packet, packet, packet, packet),
            Page(7, 4, 2400, 5, packet, packet, packet, packet, packet),
        };
        pages[3][30] ^= 0xFF;
        using var demuxer = Open([.. pages.SelectMany(page => page)]);
        var times = new List<long>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } read)
        {
            times.Add(read.Pts.ToSamples(48000));
            read.Dispose();
        }

        // Fifteen packets; those after the lost page timed by their own page, not counted on from the one before it.
        Assert.Equal(15, times.Count);
        Assert.Equal(1800 - 312 - 600, times[5]);
    }

    /// <summary>A page with lacing and body given as they are, for packets laid across pages.</summary>
    private static byte[] RawPage(int serial, int flags, long granule, int sequence, byte[] lacing, byte[] body)
    {
        var page = new byte[27 + lacing.Length + body.Length];
        "OggS"u8.CopyTo(page);
        page[5] = (byte)flags;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(14), serial);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)lacing.Length;
        lacing.CopyTo(page, 27);
        body.CopyTo(page, 27 + lacing.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc.Crc32Ogg.Compute(page));
        return page;
    }

    [Fact]
    public void PacketsCrossPagesAndTheLastPageTrimsTheEnd()
    {
        // 20 ms Opus packets: the first, 300 bytes, starts on one page and ends on the next; the last page ends the stream 100 samples early.
        var packet = new byte[300];
        packet[0] = (byte)((16 + 3) << 3);
        var file = new List<byte>();
        file.AddRange(Page(7, 2, 0, 0, HeadPacket()));
        file.AddRange(Page(7, 0, 0, 1, OpusTags()));
        file.AddRange(RawPage(7, 0, -1, 2, [255], packet[..255]));
        file.AddRange(RawPage(7, 1, 960, 3, [45], packet[255..]));
        file.AddRange(Page(7, 4, (960 * 2) - 100, 4, packet));
        using var demuxer = Open([.. file]);

        using var first = demuxer.ReadPacket(CancellationToken.None)!;
        using var second = demuxer.ReadPacket(CancellationToken.None)!;

        Assert.Equal(300, first.Data.Length);
        Assert.Equal((0, 100), (first.DiscardSamples, second.DiscardSamples));
        Assert.Equal("Sungba", demuxer.Info.Metadata[MetadataKeys.Title]);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));

        // Seeking past the first page drops the packet it began, which cannot be completed.
        demuxer.Seek(MediaTime.FromSeconds(0.02), CancellationToken.None);
        using var after = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(300, after.Data.Length);
    }

    [Fact]
    public void StreamsOfOtherCodecsAreLeftAloneAndAFileOfNothingElseIsRefused()
    {
        var speex = Page(1, 2, 0, 0, [.. "Speex   "u8, .. new byte[72]]);
        Assert.Throws<MediaFormatException>(() => Open(speex));
        Assert.Throws<MediaFormatException>(() => Open([.. "OggS"u8, 1, 2, 3]));
        Assert.Throws<MediaFormatException>(() => Open([]));

        // A Speex stream beside an Opus one: only the Opus is a track.
        var file = new List<byte>();
        file.AddRange(Page(1, 2, 0, 0, [.. "Speex   "u8, .. new byte[72]]));
        file.AddRange(Page(7, 2, 0, 0, HeadPacket(1)));
        file.AddRange(Page(7, 0, 0, 1, OpusTags()));
        file.AddRange(Page(1, 0, 0, 1, [1, 2, 3]));
        file.AddRange(Page(7, 4, 960, 2, [16 << 3]));
        using var demuxer = Open([.. file]);
        Assert.Single(demuxer.Info.Tracks);
        Assert.Equal(1, demuxer.ReadPacket(CancellationToken.None)!.Data.Length);
    }

    [Fact]
    [Capability("FMT-C09")]
    public void ALongFileIsSoughtByBisectionToThePageBeforeTheTarget()
    {
        // Ten seconds of 20 ms Opus packets, one to a page, with another stream's pages among them.
        var packet = new byte[300];
        packet[0] = (byte)((16 + 3) << 3);
        var file = new List<byte>();
        file.AddRange(Page(9, 2, 0, 0, [.. "Speex   "u8, .. new byte[72]]));
        file.AddRange(Page(7, 2, 0, 0, HeadPacket()));
        file.AddRange(Page(7, 0, 0, 1, OpusTags()));
        for (var i = 0; i < 500; i++)
        {
            file.AddRange(Page(7, i == 499 ? 4 : 0, 312 + ((i + 1) * 960), i + 2, packet));
            if (i % 10 == 0)
            {
                file.AddRange(Page(9, 0, i, i + 1, [1, 2, 3]));
            }
        }

        // The other stream runs on after the sound ends, for longer than the bisection's last step.
        for (var i = 0; i < 150; i++)
        {
            file.AddRange(Page(9, 0, 1000 + i, 100 + i, new byte[600]));
        }

        using var demuxer = Open([.. file]);
        Assert.Equal(10.0, demuxer.Info.Duration.TotalSeconds, 3);

        demuxer.Seek(MediaTime.FromSeconds(6), CancellationToken.None);
        using var first = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.InRange(first.Pts.TotalSeconds, 5.9, 6.0);

        // Past the end there is nothing more to read.
        demuxer.Seek(MediaTime.FromSeconds(60), CancellationToken.None);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
    }

    /// <summary>A FLAC PICTURE block: a front cover, PNG, of <paramref name="data"/>.</summary>
    private static byte[] Picture(byte[] data)
    {
        var block = new byte[32 + 9 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(block, 3);
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(4), 9);
        "image/png"u8.CopyTo(block.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(37), (uint)data.Length);
        data.CopyTo(block, 41);
        return block;
    }

    [Fact]
    public void FlacInOggCarriesItsCoverAndSeeksWithoutPreRoll()
    {
        // The first packet: the mapping's marker, two more header packets, and STREAMINFO (48 kHz, mono, 16 bits).
        var first = new byte[17 + 34];
        first[0] = 0x7F;
        "FLAC"u8.CopyTo(first.AsSpan(1));
        (first[5], first[6], first[8]) = (1, 0, 2);
        "fLaC"u8.CopyTo(first.AsSpan(9));
        (first[13], first[16]) = (0, 34);
        BinaryPrimitives.WriteUInt16BigEndian(first.AsSpan(17), 4096);
        BinaryPrimitives.WriteUInt16BigEndian(first.AsSpan(19), 4096);
        BinaryPrimitives.WriteUInt64BigEndian(first.AsSpan(27), (48000UL << 44) | (15UL << 36));
        var cover = Picture([137, 80, 78, 71]);
        byte[] pictureBlock = [6, 0, (byte)(cover.Length >> 8), (byte)cover.Length, .. cover];
        byte[] comments = [0x84, 0, 0, 20, 0, 0, 0, 0, 1, 0, 0, 0, 12, 0, 0, 0, .. "TITLE=Sungba"u8];
        var file = new List<byte>();
        file.AddRange(Page(5, 2, 0, 0, first));
        file.AddRange(Page(5, 0, 0, 1, pictureBlock, comments));
        file.AddRange(Page(5, 4, 4096, 2, [0xFF, 0xF8, 1, 2]));
        using var demuxer = Open([.. file]);

        Assert.Equal([137, 80, 78, 71], demuxer.Info.CoverArt);
        Assert.Equal("Sungba", demuxer.Info.Metadata[MetadataKeys.Title]);
        demuxer.Seek(MediaTime.Zero, CancellationToken.None);
        using var packet = demuxer.ReadPacket(CancellationToken.None)!;
        // Not a real FLAC frame, so it takes no time: it sits at its page's position.
        Assert.Equal((MediaTime.FromSamples(4096, 48000), 4), (packet.Pts, packet.Data.Length));
    }

    [Fact]
    public void VorbisHeadersOfAnyLengthAreLacedForTheDecoderAndEmptyPacketsTakeNoTime()
    {
        var comment = new VorbisWriter().Header(3).Bits(300, 32);
        for (var i = 0; i < 300; i++)
        {
            comment.Bits('v', 8);
        }

        comment.Bits(0, 32).Flag(true);
        var file = new List<byte>();
        file.AddRange(Page(3, 2, 0, 0, VorbisWriter.Identification()));
        file.AddRange(Page(3, 0, 0, 1, comment.ToArray(), VorbisCraftedTests.Setup()));

        // An empty packet and a header in the middle of the sound take no time; short blocks give 32 samples each after the first.
        file.AddRange(Page(3, 4, 64, 2, [0], [], [1], [0], [0]));
        using var demuxer = Open([.. file]);

        Assert.Equal(3, VorbisSetup.SplitXiph(demuxer.Info.Tracks[0].CodecPrivate).Count);
        var times = new List<(long Pts, long Duration)>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            times.Add((packet.Pts.ToSamples(8000), packet.Duration.ToSamples(8000)));
            packet.Dispose();
        }

        Assert.Equal([(0L, 0L), (0, 0), (0, 0), (0, 32), (32, 32)], times);
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(3, 2880)]
    [InlineData(12, 480)]
    [InlineData(13, 960)]
    [InlineData(16, 120)]
    [InlineData(31, 960)]
    public void AnOpusPacketsLengthIsInItsFirstByte(int config, int samples)
    {
        Assert.Equal(samples, OpusPacket.Samples([(byte)(config << 3)]));
        Assert.Equal(samples * 2, OpusPacket.Samples([(byte)((config << 3) | 1)]));
        Assert.Equal(samples * 2, OpusPacket.Samples([(byte)((config << 3) | 2)]));
        Assert.Equal(Math.Min(samples * 3, 5760) == samples * 3 ? samples * 3 : 0, OpusPacket.Samples([(byte)((config << 3) | 3), 3]));
        Assert.Equal(0, OpusPacket.Samples([(byte)((config << 3) | 3)]));
        Assert.Equal(0, OpusPacket.Samples([]));
    }

    [Fact]
    public void TheOpusHeaderIsReadAndCheckedAsRfc7845Says()
    {
        var stereo = OpusHead.Parse(HeadPacket());
        Assert.Equal((2, 312, 48000, 0d, 0, 1, 1), (stereo.Channels, stereo.PreSkip, stereo.InputSampleRate, stereo.OutputGainDb, stereo.MappingFamily, stereo.Streams, stereo.CoupledStreams));
        Assert.Equal([0, 1], stereo.Mapping);
        Assert.Equal([0], OpusHead.Parse(HeadPacket(1)).Mapping);

        byte[] surround = [.. "OpusHead"u8, 1, 6, 0, 0, 0x80, 0xBB, 0, 0, 0x00, 0x01, 1, 4, 2, 0, 4, 1, 2, 3, 5];
        var six = OpusHead.Parse(surround);
        Assert.Equal((6, 1, 4, 2, 1.0), (six.Channels, six.MappingFamily, six.Streams, six.CoupledStreams, six.OutputGainDb));

        Assert.Throws<MediaFormatException>(() => OpusHead.Parse("OpusTags"u8));
        Assert.Throws<MediaFormatException>(() => OpusHead.Parse([.. HeadPacket()[..8], 0x10, .. HeadPacket()[9..]]));
        Assert.Throws<MediaFormatException>(() => OpusHead.Parse(HeadPacket(0)));
        Assert.Throws<MediaFormatException>(() => OpusHead.Parse(HeadPacket(3)));
        Assert.Throws<MediaFormatException>(() => OpusHead.Parse(surround.AsSpan(0, 22)));
    }
}
