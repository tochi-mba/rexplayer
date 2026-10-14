using Rex.Media.Containers;
using Rex.Media.Containers.Flac;
using Rex.Media.Containers.Matroska;
using Rex.Media.Containers.Ogg;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Containers;

/// <summary>
/// Headers damaged the way real files are damaged cost what they touch, not the whole file: the
/// sound is found past broken metadata, a lying stream description is put right from the frames,
/// optional elements cut short are read as far as they go, and positions that cannot be are not used.
/// </summary>
public sealed class DamagedHeaderTests
{
    private static int[][] Stereo(int samples) => [FlacBuilder.Tone(samples, 16, 440, 16000), FlacBuilder.Tone(samples, 16, 660, 16000)];

    private static int Count(IDemuxer demuxer)
    {
        var count = 0;
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            packet.Dispose();
            count++;
        }

        return count;
    }

    private static FlacDemuxer Flac(byte[] file) => new(new MemoryByteSource(file, "damaged.flac"), CancellationToken.None);

    private static (byte[] File, FlacBuilder Builder, int Frames) HealthyFlac(Func<FlacBuilder>? make = null)
    {
        var builder = make?.Invoke() ?? new FlacBuilder
        {
            SampleRate = 16000,
            Picture = (Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray(), "image/png", 3),
            Comments = ["TITLE=Lonely At The Top"],
            SeekPointInterval = 4,
            CueTracks = [(0, 1)],
            Application = "rexp",
        };
        var file = builder.Build(Stereo(16000 * 2));
        using var demuxer = Flac(file);
        return (file, builder, Count(demuxer));
    }

    /// <summary>The offset of the first metadata block of <paramref name="type"/> after STREAMINFO.</summary>
    private static int Block(byte[] file, int type)
    {
        for (var at = 4; ; at += 4 + ((file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3]))
        {
            if ((file[at] & 0x7F) == type)
            {
                return at;
            }
        }
    }

    private static void SetLength(byte[] file, int block, int length) =>
        (file[block + 1], file[block + 2], file[block + 3]) = ((byte)(length >> 16), (byte)(length >> 8), (byte)length);

    [Fact]
    public void AFlacPictureWithTheWrongLengthCostsOnlyTheMetadataAfterIt()
    {
        var (file, _, frames) = HealthyFlac();
        var picture = Block(file, 6);
        var length = (file[picture + 1] << 16) | (file[picture + 2] << 8) | file[picture + 3];
        SetLength(file, picture, length - 100);

        using var demuxer = Flac(file);

        Assert.Equal("Lonely At The Top", demuxer.Info.Metadata[MetadataKeys.Title]);
        Assert.Equal(frames, Count(demuxer));
    }

    [Theory]
    [InlineData(127)]
    [InlineData(0)]
    public void AFlacBlockThatCannotBeRightEndsTheMetadataNotTheFile(int type)
    {
        var (file, _, frames) = HealthyFlac();
        var comments = Block(file, 4);
        file[comments] = (byte)((file[comments] & 0x80) | type);

        using var demuxer = Flac(file);

        Assert.Equal(frames, Count(demuxer));
    }

    [Fact]
    public void AFlacFileCutInsideItsMetadataOpensWithNothingToPlay()
    {
        var (file, _, _) = HealthyFlac();
        var picture = Block(file, 6);

        using (var demuxer = Flac(file[..(picture + 40)]))
        {
            Assert.Equal(0, Count(demuxer));
        }

        // Cut inside a block's own header: the file ends where the next block was to be described.
        using (var demuxer = Flac(file[..(picture + 2)]))
        {
            Assert.Equal(0, Count(demuxer));
        }
    }

    [Fact]
    public void WhenTheFirstFramesAreLostTooTheFirstThatFitsIsWhereTheSoundStarts()
    {
        var (file, builder, frames) = HealthyFlac();
        SetLength(file, Block(file, 6), 7);
        var first = (int)builder.FirstFrameOffset;
        var second = first + (int)builder.FrameOffsets[1];

        using var demuxer = Flac([.. file[..first], .. file[second..]]);

        Assert.Equal(frames - 1, Count(demuxer));
    }

    [Fact]
    public void AStreamInformationThatNoFrameFitsIsPutRightFromTheFirstFrame()
    {
        var (file, _, frames) = HealthyFlac();
        var lying = (byte[])file.Clone();
        lying[8 + 12] = (byte)((lying[8 + 12] & 0xF1) | (5 << 1));

        using var demuxer = Flac(lying);

        var audio = demuxer.Info.Tracks[0].Audio!;
        Assert.Equal(2, audio.Channels);
        Assert.Equal(16000, audio.SampleRate);
        Assert.Equal(2, Rex.Media.Codecs.Flac.FlacStreamInfo.Parse(demuxer.Info.Tracks[0].CodecPrivate).Channels);
        Assert.Equal(frames, Count(demuxer));
    }

    [Fact]
    public void AVariableBlockStreamWhoseDescriptionLiesIsPutRightToo()
    {
        var (file, _, frames) = HealthyFlac(() => new FlacBuilder { SampleRate = 16000, VariableBlockSizes = [1152, 576, 2304], RateFromStreamInfo = true, BitsFromStreamInfo = true });
        file[8 + 12] = (byte)((file[8 + 12] & 0xF1) | (3 << 1));

        using var demuxer = Flac(file);

        Assert.Equal(2, demuxer.Info.Tracks[0].Audio!.Channels);
        Assert.Equal(frames, Count(demuxer));
    }

    private static MatroskaDemuxer Mkv(byte[] file) => MatroskaDemuxerTests.Open(file);

    private static byte[][] ClusterList(int count) => [.. Enumerable.Range(0, count).Select(c => MatroskaCraftedTests.Cluster((ulong)(c * 100), MatroskaCraftedTests.Simple(1, 0, true, 1, 2, 3, 4)))];

    private static byte[] Clusters(int count) => Concat(ClusterList(count));

    /// <summary>Where each cluster starts in the segment, when they follow <paramref name="before"/> bytes of other elements.</summary>
    private static long[] Positions(byte[][] clusters, long before)
    {
        var positions = new long[clusters.Length];
        for (var i = 0; i < clusters.Length; i++)
        {
            positions[i] = before;
            before += clusters[i].Length;
        }

        return positions;
    }

    [Fact]
    public void GarbageBetweenTheMatroskaHeadersAndTheClustersIsPassedOver()
    {
        var garbage = Enumerable.Repeat((byte)0x00, 300).ToArray();
        using var demuxer = Mkv(MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()), garbage, Clusters(3)));

        Assert.Equal(3, Count(demuxer));
    }

    [Fact]
    public void AHeaderElementLargerThanTheFileIsReadAsFarAsItGoes()
    {
        // Tags claiming far more than is left, in front of the clusters, and tags of unknown size.
        var tags = Concat(Id(Id.Tags), Size(50_000_000, 4), Element(Id.Tag, Element(Id.SimpleTag, Text(Id.TagName, "TITLE"), Text(Id.TagString, "Terminator"))));
        using (var demuxer = Mkv(MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()), tags, Clusters(2))))
        {
            Assert.Equal("Terminator", demuxer.Info.Metadata[MetadataKeys.Title]);
            Assert.Equal(2, Count(demuxer));
        }

        var unsized = Unsized(Id.Tags, Element(Id.Tag));
        using (var demuxer = Mkv(MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()), unsized, Clusters(2))))
        {
            Assert.Equal(2, Count(demuxer));
        }
    }

    [Fact]
    public void ElementsTheSeekHeadPointsToThatAreCutShortOrImpossiblyLargeAreLeftOut()
    {
        var tracks = MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack());
        var list = ClusterList(4);
        var clusters = Concat(list);
        byte[] Head(long cues, long tags) => Element(Id.SeekHead,
            Element(Id.Seek, UInt(Id.SeekId, Id.Cues), UInt64(Id.SeekPosition, (ulong)cues)),
            Element(Id.Seek, UInt(Id.SeekId, Id.Tags), UInt64(Id.SeekPosition, (ulong)tags)));
        var positions = Positions(list, Head(0, 0).Length + tracks.Length);
        var cuesAt = Head(0, 0).Length + tracks.Length + clusters.Length;
        var cues = Element(Id.Cues, [.. Enumerable.Range(0, 4).Select(c => Element(Id.CuePoint, UInt(Id.CueTime, (ulong)(c * 100)),
            Element(Id.CueTrackPositions, UInt(Id.CueTrack, 1), UInt64(Id.CueClusterPosition, (ulong)positions[c]))))]);
        var hugeTags = Concat(Id(Id.Tags), Size(100_000_000, 4), new byte[16]);
        var file = MatroskaCraftedTests.Mkv(Head(cuesAt, cuesAt + cues.Length), tracks, clusters, cues, hugeTags);

        // Cut inside the last cue: the cues before it still seek.
        using var cut = Mkv(file[..^(hugeTags.Length + 5)]);
        cut.Seek(MediaTime.FromMilliseconds(150), CancellationToken.None);
        var times = new List<MediaTime>();
        while (cut.ReadPacket(CancellationToken.None) is { } packet)
        {
            times.Add(packet.Pts);
            packet.Dispose();
        }

        Assert.True(times.Count == 3, string.Join(", ", times));

        using var whole = Mkv(file);
        Assert.Equal(4, Count(whole));
    }

    [Fact]
    public void CuesThatPointNowhereAreNotFollowed()
    {
        var tracks = MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack());
        var list = ClusterList(4);
        byte[] Cue(ulong time, long position) => Element(Id.CuePoint, UInt(Id.CueTime, time), Element(Id.CueTrackPositions, UInt(Id.CueTrack, 1), UInt64(Id.CueClusterPosition, (ulong)position)));
        byte[] Cues(long[] at) => Element(Id.Cues, Cue(0, at[0]), Cue(100, at[1] + 3), Cue(250, 1L << 40), Cue(300, at[3] + 3));

        // One cue far past the end, one into the middle of the second cluster, one into the last.
        var positions = Positions(list, tracks.Length + Cues(new long[4]).Length);
        using var demuxer = Mkv(MatroskaCraftedTests.Mkv(tracks, Cues(positions), Concat(list)));

        demuxer.Seek(MediaTime.FromMilliseconds(260), CancellationToken.None);
        Assert.Equal(2, Count(demuxer));
        demuxer.Seek(MediaTime.FromMilliseconds(320), CancellationToken.None);
        Assert.Equal(0, Count(demuxer));
    }

    private static OggDemuxer Ogg(byte[] file) => new(new MemoryByteSource(file, "damaged.ogg"), CancellationToken.None);

    /// <summary>The Vorbis fixture taken apart: its three header packets, its sound pages and the stream's serial.</summary>
    private static (byte[] Id, byte[] Comment, byte[] Setup, List<OggPageData> Sound, int Serial) VorbisParts()
    {
        var pages = OggPages.Split(HealthyMedia.Ogg(10).Bytes);
        var headerPages = pages.TakeWhile(page => page.Granule == 0).ToList();
        var packets = new List<byte[]>();
        var current = new List<byte>();
        foreach (var page in headerPages)
        {
            var at = 0;
            foreach (var lace in page.Lacing)
            {
                current.AddRange(page.Body.AsSpan(at, lace));
                at += lace;
                if (lace < 255)
                {
                    packets.Add([.. current]);
                    current.Clear();
                }
            }
        }

        return (packets[0], packets[1], packets[2], pages.Skip(headerPages.Count).ToList(), pages[0].Serial);
    }

    private static byte[] VorbisFile(int serial, IEnumerable<byte[]> headers, List<OggPageData> sound)
    {
        var parts = new List<byte[]>();
        var sequence = 0;
        foreach (var header in headers)
        {
            parts.Add(OggDemuxerTests.Page(serial, sequence == 0 ? 2 : 0, 0, sequence, header));
            sequence++;
        }

        parts.AddRange(sound.Select(page => OggPages.Write(page with { Sequence = sequence++ })));
        return [.. parts.SelectMany(part => part)];
    }

    [Fact]
    public void AVorbisStreamThatLostItsCommentsStillPlays()
    {
        var (id, comment, setup, sound, serial) = VorbisParts();
        using var healthy = Ogg(VorbisFile(serial, [id, comment, setup], sound));
        using var demuxer = Ogg(VorbisFile(serial, [id, setup], sound));

        Assert.Equal(Count(healthy), Count(demuxer));
        Assert.True(demuxer.Info.Tracks[0].CodecPrivate.Length > setup.Length);
    }

    [Fact]
    public void AVorbisStreamWithoutAUsableSetupIsRefusedAtOnce()
    {
        var (id, comment, setup, sound, serial) = VorbisParts();
        byte[] broken = [.. setup[..7], .. Enumerable.Repeat((byte)0xFF, 40)];

        Assert.Throws<MediaFormatException>(() => Ogg(VorbisFile(serial, [id, comment], sound)));
        Assert.Throws<MediaFormatException>(() => Ogg(VorbisFile(serial, [id, comment, broken], sound)));
    }

    [Fact]
    public void AHeaderPageWhoseCrcFailsIsStillTaken()
    {
        var (id, comment, setup, sound, serial) = VorbisParts();
        var file = VorbisFile(serial, [id, comment, setup], sound);
        var commentPage = OggDemuxerTests.Page(serial, 2, 0, 0, id).Length;
        file[commentPage + 27 + 1 + 12] ^= 0x20;

        using var demuxer = Ogg(file);

        Assert.True(Count(demuxer) > 0);
    }

    [Fact]
    public void AnOggPositionNoMediaCanReachIsNotBelieved()
    {
        var (id, comment, setup, sound, serial) = VorbisParts();
        var middle = sound.Count / 2;
        var misdated = sound.Select((page, i) => i == middle ? page with { Granule = long.MaxValue / 4 } : page).ToList();
        var endless = sound.Select((page, i) => i == sound.Count - 1 ? page with { Granule = long.MaxValue / 2 } : page).ToList();

        using (var demuxer = Ogg(VorbisFile(serial, [id, comment, setup], misdated)))
        {
            var last = MediaTime.Zero;
            while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
            {
                using (packet)
                {
                    Assert.True(packet.Pts < demuxer.Info.Duration + MediaTime.FromSeconds(1), $"{packet.Pts} after {last} in {demuxer.Info.Duration}");
                    last = packet.Pts;
                }
            }

            Assert.True(last > MediaTime.FromSeconds(1), $"last {last}");
        }

        using (var demuxer = Ogg(VorbisFile(serial, [id, comment, setup], endless)))
        {
            Assert.False(demuxer.Info.Duration.IsKnown);
            Assert.True(Count(demuxer) > 0, "endless");
        }
    }
}
