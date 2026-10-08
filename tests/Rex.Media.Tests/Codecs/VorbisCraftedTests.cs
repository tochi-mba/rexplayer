using Rex.Media.Codecs.Vorbis;
using Rex.Media.Codecs.Software.Vorbis;
using Rex.Media.Primitives;
using Rex.Media.Codecs;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// Vorbis streams made bit by bit: every kind of floor, residue and coupling, and every way a header
/// or packet can be wrong, which files from real encoders never show.
/// </summary>
public sealed class VorbisCraftedTests
{
    // Block sizes 64 and 256: a short block has 32 spectral values.
    private const int ShortHalf = 32;

    /// <summary>A Vorbis float: a 21-bit mantissa and an exponent offset by 788 (section 9.2.2).</summary>
    private static long Pack(double value)
    {
        if (value == 0)
        {
            return 0;
        }

        var exponent = (int)Math.Floor(Math.Log2(Math.Abs(value)));
        var mantissa = (long)Math.Round(Math.Abs(value) / Math.Pow(2, exponent - 20));
        return (value < 0 ? 0x80000000L : 0) | ((long)(exponent - 20 + 788) << 21) | mantissa;
    }

    /// <summary>Book 0: one bit picks 0 or 1 (classifications, floor values). Book 1: two bits pick a pair from -1 to 2.</summary>
    private static void Books(VorbisWriter w)
    {
        w.Codebook(1, [1, 1]);
        w.Codebook(2, [2, 2, 2, 2], lookupType: 2).Bits(Pack(-1), 32).Bits(Pack(1), 32).Bits(1, 4).Flag(false);
        foreach (var value in new[] { 0, 1, 2, 3, 3, 2, 1, 0 })
        {
            w.Bits(value, 2);
        }
    }

    /// <summary>A floor 1 with one partition of one point, its value from book 0 through a one-bit subclass.</summary>
    private static void Floor(VorbisWriter w) =>
        w.Bits(1, 16).Bits(1, 5).Bits(0, 4).Bits(0, 3).Bits(1, 2).Bits(0, 8).Bits(0, 8).Bits(1, 8).Bits(0, 2).Bits(5, 4).Bits(9, 5);

    /// <summary>A residue of <paramref name="type"/> over all 32 values in partitions of 8, classified by book 0, decoded by book 1.</summary>
    private static void Residue(VorbisWriter w, int type) =>
        w.Bits(type, 16).Bits(0, 24).Bits(ShortHalf * 2, 24).Bits(7, 24).Bits(1, 6).Bits(0, 8).Bits(1, 3).Flag(false).Bits(1, 3).Flag(false).Bits(1, 8).Bits(1, 8);

    internal static byte[] Setup(int channels = 1, int residueType = 1, bool coupled = false, int modes = 1, Action<VorbisWriter>? floor = null, Action<VorbisWriter>? alter = null, Action<VorbisWriter>? residue = null, Action<VorbisWriter>? mapping = null)
    {
        var w = new VorbisWriter().Header(5);
        w.Bits(1, 8);
        Books(w);
        w.Bits(0, 6).Bits(0, 16);
        w.Bits(0, 6);
        (floor ?? Floor)(w);
        w.Bits(0, 6);
        if (residue is null)
        {
            Residue(w, residueType);
        }
        else
        {
            residue(w);
        }

        w.Bits(0, 6);
        if (mapping is null)
        {
            w.Bits(0, 16).Flag(false).Flag(coupled);
            if (coupled)
            {
                w.Bits(0, 8).Bits(0, VorbisCodebook.Ilog(channels - 1)).Bits(1, VorbisCodebook.Ilog(channels - 1));
            }

            w.Bits(0, 2).Bits(0, 8).Bits(0, 8).Bits(0, 8);
        }
        else
        {
            mapping(w);
        }

        w.Bits(modes - 1, 6);
        for (var i = 0; i < modes; i++)
        {
            w.Flag(i == 1).Bits(0, 16).Bits(0, 16).Bits(0, 8);
        }

        alter?.Invoke(w);
        return w.Flag(true).ToArray();
    }

    private static TrackInfo Track(byte[] setup, int channels = 1, byte[]? identification = null) => new()
    {
        Id = 1,
        Codec = CodecId.Vorbis,
        CodecPrivate = VorbisWriter.Laced(identification ?? VorbisWriter.Identification(channels), VorbisWriter.Comment(), setup),
        Audio = new AudioTrackInfo { SampleRate = 8000, Channels = channels },
    };

    private static List<AudioFrame> Decode(VorbisDecoder decoder, params byte[][] packets)
    {
        var frames = new List<AudioFrame>();
        foreach (var data in packets)
        {
            using var packet = Packet.Create(1, MediaBuffer.CopyOf(data), MediaTime.Zero, MediaTime.Unknown, MediaTime.Unknown, true);
            decoder.Decode(packet, frames);
        }

        return frames;
    }

    private static byte[][] RandomPackets(int count, int length, int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, count).Select(_ =>
        {
            var packet = new byte[length];
            random.NextBytes(packet);
            packet[0] &= 0xFE;
            return packet;
        })];
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, false)]
    [InlineData(2, 2, false)]
    [InlineData(1, 2, true)]
    [InlineData(2, 2, true)]
    public void EveryResidueTypeAndCouplingDecodes(int residueType, int channels, bool coupled)
    {
        using var decoder = new VorbisDecoder(Track(Setup(channels, residueType, coupled), channels));

        var frames = Decode(decoder, RandomPackets(12, 24, residueType * 10 + channels));

        Assert.Equal(11, frames.Count);
        Assert.All(frames, frame => Assert.Equal((channels, ShortHalf), (frame.Channels, frame.SampleCount)));
        Assert.Contains(frames, frame => frame.Channel(0).ToArray().Any(sample => sample != 0));
        Assert.Equal(0, decoder.CorruptPackets);
        frames.ForEach(frame => frame.Dispose());
    }

    [Fact]
    public void AnUnusedFloorIsSilenceAndEmptyOrHeaderPacketsAreSkipped()
    {
        using var decoder = new VorbisDecoder(Track(Setup()));

        // Packet type 0, then the floor's "in use" bit clear.
        var frames = Decode(decoder, [0], [0], [], [1, 2, 3]);

        var frame = Assert.Single(frames);
        Assert.All(frame.Channel(0).ToArray(), sample => Assert.Equal(0f, sample));
        frame.Dispose();
    }

    [Fact]
    public void PacketsCutShortAnywhereDecodeAsFarAsTheyGo()
    {
        using var decoder = new VorbisDecoder(Track(Setup(2, 2, coupled: true), 2));
        var whole = RandomPackets(1, 24, 3)[0];

        // Every length from one byte to the whole: each end-of-packet point in floors and residues.
        foreach (var length in Enumerable.Range(1, whole.Length))
        {
            Decode(decoder, whole[..length]).ForEach(frame => frame.Dispose());
        }

        Assert.Equal(0, decoder.CorruptPackets);
    }

    [Fact]
    public void LongBlocksAndAModeTheStreamDoesNotHaveAreHandled()
    {
        using var decoder = new VorbisDecoder(Track(Setup(modes: 3)));

        // Two mode bits: 1 is the long block (then its two window flags), 3 names no mode, 0 is short.
        var frames = Decode(decoder, [0b0000_0010], [0b0001_1010], [0b0000_0110], [0b0000_0000]);

        Assert.Equal(1, decoder.CorruptPackets);
        Assert.Equal([64 + 64, 64 + (ShortHalf / 2)], frames.Select(frame => frame.SampleCount));
        frames.ForEach(frame => frame.Dispose());
        decoder.Flush();
        Assert.Empty(Decode(decoder, [0b0000_0010]));
        decoder.Drain(frames);
    }

    [Fact]
    public void PacketsEndingInsideAFloorOrAType0ResidueStopThere()
    {
        // Floors whose values are seven bits each, so the first class or value read starts on the third byte.
        static void Mastered(VorbisWriter w) =>
            w.Bits(1, 16).Bits(1, 5).Bits(0, 4).Bits(0, 3).Bits(1, 2).Bits(0, 8).Bits(0, 8).Bits(1, 8).Bits(1, 2).Bits(5, 4).Bits(9, 5);
        static void Direct(VorbisWriter w) =>
            w.Bits(1, 16).Bits(1, 5).Bits(0, 4).Bits(0, 3).Bits(0, 2).Bits(1, 8).Bits(1, 2).Bits(5, 4).Bits(9, 5);
        using var mastered = new VorbisDecoder(Track(Setup(floor: Mastered)));
        using var direct = new VorbisDecoder(Track(Setup(floor: Direct)));
        using var type0 = new VorbisDecoder(Track(Setup(residueType: 0)));

        // A floor without partitions, whose two values run past a one-byte packet.
        static void Bare(VorbisWriter w) => w.Bits(1, 16).Bits(0, 5).Bits(0, 2).Bits(5, 4);
        using var bare = new VorbisDecoder(Track(Setup(floor: Bare)));

        Decode(mastered, [0b10, 0xFF], [0b10, 0xFF]).ForEach(frame => frame.Dispose());
        Decode(direct, [0b10, 0xFF], [0b10, 0xFF]).ForEach(frame => frame.Dispose());
        Decode(bare, [0b10], [0b10]).ForEach(frame => frame.Dispose());

        // The floor in use and done by bit 20; the first type 0 partition runs from bit 21 past the third byte.
        Decode(type0, [0b10, 0xAA, 0x55], [0b10, 0xAA, 0x55]).ForEach(frame => frame.Dispose());

        Assert.Equal((0, 0, 0, 0), (mastered.CorruptPackets, direct.CorruptPackets, type0.CorruptPackets, bare.CorruptPackets));
    }

    [Fact]
    public void ASubmapWithoutChannelsAndAResidueTooShortForAPartitionAreNothingToDecode()
    {
        // Two submaps, every channel in the first.
        static void TwoSubmaps(VorbisWriter w) => w.Bits(0, 16).Flag(true).Bits(1, 4).Flag(false).Bits(0, 2).Bits(0, 4).Bits(0, 8).Bits(0, 8).Bits(0, 8).Bits(0, 8).Bits(0, 8).Bits(0, 8);

        // A residue covering nothing: it ends where it begins.
        static void Empty(VorbisWriter w) => w.Bits(1, 16).Bits(0, 24).Bits(0, 24).Bits(7, 24).Bits(0, 6).Bits(0, 8).Bits(0, 3).Flag(false);
        using var submaps = new VorbisDecoder(Track(Setup(mapping: TwoSubmaps)));
        using var empty = new VorbisDecoder(Track(Setup(residue: Empty)));

        Assert.Single(Decode(submaps, RandomPackets(2, 24, 12)));
        Assert.Single(Decode(empty, RandomPackets(2, 24, 13)));
    }

    [Fact]
    public void LongCodewordsAreFoundByWalkingTheTree()
    {
        // Entry 0 is "0"; entries 1 and 2 are eleven bits, past the look-up table; "11..." names nothing.
        var book = new VorbisCodebook(new VorbisBits(new VorbisWriter().Codebook(1, [1, 11, 11]).ToArray()));

        Assert.Equal(1, book.DecodeScalar(new VorbisBits(new VorbisWriter().Bits(1, 1).Bits(0, 10).ToArray())));
        Assert.Equal(2, book.DecodeScalar(new VorbisBits(new VorbisWriter().Bits(1, 1).Bits(0, 9).Bits(1, 1).ToArray())));
        Assert.Equal(-1, book.DecodeScalar(new VorbisBits(new VorbisWriter().Bits(0b11, 2).Bits(0, 9).ToArray())));
        Assert.Equal(-1, book.DecodeScalar(new VorbisBits(new VorbisWriter().Bits(1, 1).ToArray())));
        Assert.True(book.DecodeVector(new VorbisBits([0])).IsEmpty);
    }

    [Fact]
    public void AFloorOfTheOldKindPlaysAsSilence()
    {
        static void Floor0(VorbisWriter w) => w.Bits(0, 16).Bits(8, 8).Bits(8000, 16).Bits(32, 16).Bits(6, 6).Bits(0, 8).Bits(0, 4).Bits(0, 8);
        using var decoder = new VorbisDecoder(Track(Setup(floor: Floor0)));

        var frames = Decode(decoder, [0x7E, 0xFF], [0x7E, 0xFF]);

        Assert.Equal(2, decoder.CorruptPackets);
        Assert.All(Assert.Single(frames).Channel(0).ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Theory]
    [InlineData(3, ChannelLayout.Surround)]
    [InlineData(5, ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.BackLeft | ChannelLayout.BackRight)]
    [InlineData(6, ChannelLayout.Surround51Back)]
    [InlineData(7, ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.LowFrequency | ChannelLayout.BackCenter | ChannelLayout.SideLeft | ChannelLayout.SideRight)]
    [InlineData(8, ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.LowFrequency | ChannelLayout.BackLeft | ChannelLayout.BackRight | ChannelLayout.SideLeft | ChannelLayout.SideRight)]
    public void ChannelsComeOutInTheOrderOfTheirLayout(int channels, ChannelLayout layout)
    {
        using var decoder = new VorbisDecoder(Track(Setup(channels), channels));

        var frames = Decode(decoder, RandomPackets(2, 24, channels));

        Assert.Equal((layout, channels), (frames[0].Layout, frames[0].Channels));
        Assert.Equal((8000, channels), (decoder.SampleRate, decoder.Channels));
    }

    [Fact]
    public void MoreChannelsThanTheSpecNamesKeepTheirOrder()
    {
        using var decoder = new VorbisDecoder(Track(Setup(9), 9));

        var frames = Decode(decoder, RandomPackets(2, 24, 9));

        Assert.Equal(9, frames[0].Channels);
    }

    [Fact]
    public void TheFactoryTakesOnlyVorbis()
    {
        var factory = new VorbisDecoderFactory();
        var track = Track(Setup());

        Assert.True(factory.CanDecode(track));
        Assert.False(factory.CanDecode(track with { Codec = CodecId.Opus }));
        Assert.False(factory.CanDecode(track with { Audio = null }));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(track with { Codec = CodecId.Opus }));
        using var decoder = (VorbisDecoder)factory.CreateAudio(track);
        Assert.Equal(("rexplayer Vorbis", DecoderSource.Own, 100), (decoder.Name, decoder.Source, factory.Rank));
        Assert.Equal(("rexplayer Vorbis", DecoderSource.Own), (factory.Name, factory.Source));
        Assert.Throws<ArgumentNullException>(() => factory.CanDecode(null!));
        Assert.Throws<ArgumentNullException>(() => factory.CreateAudio(null!));
        Assert.Throws<ArgumentNullException>(() => new VorbisDecoder(null!));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, []));
    }

    private static byte[] Replace(byte[] data, int index, byte value)
    {
        var copy = data.ToArray();
        copy[index] = value;
        return copy;
    }

    private static void ExpectBroken(byte[] setup, int channels = 1, byte[]? identification = null) =>
        Assert.Throws<MediaFormatException>(() => new VorbisDecoder(Track(setup, channels, identification)));

    [Fact]
    public void BrokenHeadersAreRefused()
    {
        // The identification header.
        ExpectBroken(Setup(), identification: VorbisWriter.Identification(version: 1));
        ExpectBroken(Setup(), identification: VorbisWriter.Identification(channels: 0));
        ExpectBroken(Setup(), identification: VorbisWriter.Identification(framing: false));
        ExpectBroken(Setup(), identification: VorbisWriter.Identification(shortExponent: 9, longExponent: 8));
        ExpectBroken(Setup(), identification: VorbisWriter.Identification()[..10]);
        ExpectBroken(Setup(), identification: Replace(VorbisWriter.Identification(), 0, 3));

        // The setup header: its type, a codebook's sync, and its framing bit.
        ExpectBroken(Replace(Setup(), 1, (byte)'x'));
        ExpectBroken(Replace(Setup(), 8, 0));
        var unframed = Setup();
        ExpectBroken(Replace(unframed, unframed.Length - 1, 0));
        ExpectBroken(Setup()[..12]);
    }

    private static byte[] SetupWith(Action<VorbisWriter> books, int bookCount)
    {
        var w = new VorbisWriter().Header(5).Bits(bookCount - 1, 8);
        books(w);
        w.Bits(0, 6).Bits(0, 16).Bits(0, 6);
        Floor(w);
        w.Bits(0, 6);
        Residue(w, 1);
        w.Bits(0, 6).Bits(0, 16).Flag(false).Flag(false).Bits(0, 2).Bits(0, 8).Bits(0, 8).Bits(0, 8);
        return w.Bits(0, 6).Flag(false).Bits(0, 16).Bits(0, 16).Bits(0, 8).Flag(true).ToArray();
    }

    [Fact]
    public void CodebooksOfEveryKindAreRead()
    {
        // Ordered lengths, a sparse book whose one used entry decodes without a choice, and a type 1
        // lookup with values carried forward.
        static void Ordered(VorbisWriter w) => w.Bits(0x564342, 24).Bits(1, 16).Bits(2, 24).Flag(true).Bits(0, 5).Bits(2, 2).Bits(0, 4);
        static void Lookup1(VorbisWriter w) =>
            w.Codebook(2, [2, 2, 2, 2], lookupType: 1).Bits(Pack(0.5), 32).Bits(Pack(1), 32).Bits(0, 4).Flag(true).Bits(0, 1).Bits(1, 1);
        static void Single(VorbisWriter w) => w.Codebook(2, [0, 3, 0, 0], lookupType: 2).Bits(Pack(1), 32).Bits(Pack(1), 32).Bits(0, 4).Flag(false).Bits(0, 8);

        using var ordered = new VorbisDecoder(Track(SetupWith(w => { Ordered(w); Lookup1(w); }, 2)));
        Decode(ordered, RandomPackets(4, 24, 5)).ForEach(frame => frame.Dispose());
        using var single = new VorbisDecoder(Track(SetupWith(w => { w.Codebook(1, [1, 1]); Single(w); }, 2)));
        Decode(single, RandomPackets(4, 24, 6)).ForEach(frame => frame.Dispose());
        Decode(single, [0, 0]).ForEach(frame => frame.Dispose());

        Assert.Equal(10, VorbisCodebook.Lookup1Values(1000, 3));
        Assert.Equal(4, VorbisCodebook.Lookup1Values(16, 2));
        Assert.Equal(0, VorbisCodebook.Ilog(0));
        Assert.Equal(3, VorbisCodebook.Ilog(7));
        Assert.Equal(-1.5f, VorbisCodebook.Float32Unpack((uint)Pack(-1.5)));
    }

    [Fact]
    public void BrokenCodebooksAreRefused()
    {
        // Lengths that run past the entries, lengths no prefix code can have, an undefined lookup, and a book cut short.
        ExpectBroken(SetupWith(w => w.Bits(0x564342, 24).Bits(1, 16).Bits(2, 24).Flag(true).Bits(0, 5).Bits(3, 2), 1));
        ExpectBroken(SetupWith(w => w.Codebook(1, [1, 1, 1]), 1));
        ExpectBroken(SetupWith(w => w.Codebook(1, [1, 1], lookupType: 3), 1));
        ExpectBroken(new VorbisWriter().Header(5).Bits(0, 8).Bits(0x564342, 24).Bits(1, 16).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => new VorbisMdct(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VorbisMdct(8));
    }

    [Fact]
    public void BrokenFloorsResiduesMappingsAndModesAreRefused()
    {
        static byte[] With(Action<VorbisWriter> rest)
        {
            var w = new VorbisWriter().Header(5).Bits(1, 8);
            Books(w);
            rest(w);
            return w.Flag(true).ToArray();
        }

        static void Floors(VorbisWriter w) => Floor(w.Bits(0, 6).Bits(0, 16).Bits(0, 6));

        static void Residues(VorbisWriter w)
        {
            Floors(w);
            Residue(w.Bits(0, 6), 1);
        }

        static void Mappings(VorbisWriter w, Action<VorbisWriter> mapping)
        {
            Residues(w);
            mapping(w.Bits(0, 6));
        }

        // A time transform, an undefined floor, floor points twice over, an undefined residue.
        ExpectBroken(With(w => w.Bits(0, 6).Bits(1, 16)));
        ExpectBroken(With(w => w.Bits(0, 6).Bits(0, 16).Bits(0, 6).Bits(2, 16)));
        ExpectBroken(With(w => w.Bits(0, 6).Bits(0, 16).Bits(0, 6).Bits(1, 16).Bits(1, 5).Bits(0, 4).Bits(0, 3).Bits(0, 2).Bits(1, 8).Bits(0, 2).Bits(5, 4).Bits(32, 5)));
        ExpectBroken(With(w => { Floors(w); w.Bits(0, 6).Bits(3, 16); }));

        // Mappings: an undefined type, a pair coupled to itself, the reserved field, a submap that does not exist, a floor that does not exist.
        ExpectBroken(With(w => Mappings(w, m => m.Bits(1, 16))));
        ExpectBroken(With(w => Mappings(w, m => m.Bits(0, 16).Flag(false).Flag(true).Bits(0, 8).Bits(0, 1).Bits(0, 1))), channels: 2);
        ExpectBroken(With(w => Mappings(w, m => m.Bits(0, 16).Flag(false).Flag(false).Bits(1, 2))));
        ExpectBroken(With(w => Mappings(w, m => m.Bits(0, 16).Flag(true).Bits(1, 4).Flag(false).Bits(0, 2).Bits(3, 4))));
        ExpectBroken(With(w => Mappings(w, m => m.Bits(0, 16).Flag(false).Flag(false).Bits(0, 2).Bits(0, 8).Bits(4, 8).Bits(0, 8))));

        // Modes: a window type Vorbis I does not have.
        ExpectBroken(With(w => Mappings(w, m => m.Bits(0, 16).Flag(false).Flag(false).Bits(0, 2).Bits(0, 8).Bits(0, 8).Bits(0, 8).Bits(0, 6).Flag(false).Bits(1, 16).Bits(0, 16).Bits(0, 8))));

        // A setup naming a codebook it does not have.
        ExpectBroken(With(w => w.Bits(0, 6).Bits(0, 16).Bits(0, 6).Bits(1, 16).Bits(1, 5).Bits(0, 4).Bits(0, 3).Bits(1, 2).Bits(9, 8)));
    }

    [Fact]
    public void HeadersMustBeThreeInXiphLacing()
    {
        var setup = Setup();
        var laced = VorbisWriter.Laced(VorbisWriter.Identification(), VorbisWriter.Comment(), setup);

        Assert.Equal(3, VorbisSetup.SplitXiph(laced).Count);
        Assert.Empty(VorbisSetup.SplitXiph([]));
        Assert.Empty(VorbisSetup.SplitXiph([2, 255]));
        Assert.Empty(VorbisSetup.SplitXiph([1, 200, 1, 2]));
        Assert.Throws<MediaFormatException>(() => new VorbisDecoder(Track(setup) with { CodecPrivate = VorbisWriter.Laced(VorbisWriter.Identification(), setup) }));
        Assert.Throws<MediaFormatException>(() => new VorbisDecoder(Track(setup) with { CodecPrivate = [] }));
    }
}
