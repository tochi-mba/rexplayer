// Spec: Vorbis I specification, sections 4.2.1-4.2.4 (common header, identification and setup headers), 6.2.1 (floor 1 header), 8.6.1 (residue header) and 4.2.4 (mappings, modes), and Matroska's A_VORBIS CodecPrivate (the three headers in Xiph lacing).
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Vorbis;

/// <summary>A floor type 1 configuration: partitions of classes, and the X positions of its points.</summary>
internal sealed class VorbisFloor1
{
    public required int[] PartitionClasses { get; init; }

    public required int[] ClassDimensions { get; init; }

    public required int[] ClassSubclasses { get; init; }

    public required int[] ClassMasterbooks { get; init; }

    public required int[][] SubclassBooks { get; init; }

    public required int Multiplier { get; init; }

    public required int[] X { get; init; }

    /// <summary>For each point from the third: the indices of its low and high neighbours (section 9.2.4).</summary>
    public required int[] Low { get; init; }

    public required int[] High { get; init; }

    /// <summary>The points in order of X, for drawing the curve.</summary>
    public required int[] Sorted { get; init; }
}

/// <summary>A residue configuration (types 0, 1 and 2 share it).</summary>
internal sealed class VorbisResidue
{
    public required int Type { get; init; }

    public required int Begin { get; init; }

    public required int End { get; init; }

    public required int PartitionSize { get; init; }

    public required int Classifications { get; init; }

    public required int Classbook { get; init; }

    /// <summary>The book for each classification and pass, or -1 for none.</summary>
    public required int[][] Books { get; init; }
}

internal sealed class VorbisMapping
{
    public required int[] Magnitude { get; init; }

    public required int[] Angle { get; init; }

    /// <summary>The submap of each channel.</summary>
    public required int[] Mux { get; init; }

    public required int[] SubmapFloor { get; init; }

    public required int[] SubmapResidue { get; init; }
}

internal readonly record struct VorbisMode(bool LongBlock, int Mapping);

/// <summary>
/// The decoding setup of a Vorbis stream, from its identification and setup headers: block sizes,
/// codebooks, floors, residues, mappings and modes.
/// </summary>
internal sealed class VorbisSetup
{
    private VorbisSetup()
    {
    }

    public int Channels { get; private set; }

    public int SampleRate { get; private set; }

    public int ShortBlock { get; private set; }

    public int LongBlock { get; private set; }

    public VorbisCodebook[] Codebooks { get; private set; } = [];

    /// <summary>The floors; null marks a floor of type 0, which rexplayer does not decode.</summary>
    public VorbisFloor1?[] Floors { get; private set; } = [];

    public VorbisResidue[] Residues { get; private set; } = [];

    public VorbisMapping[] Mappings { get; private set; } = [];

    public VorbisMode[] Modes { get; private set; } = [];

    /// <summary>The setup from Matroska's (and rexplayer's Ogg reader's) CodecPrivate: the three headers in Xiph lacing.</summary>
    public static VorbisSetup FromCodecPrivate(ReadOnlySpan<byte> codecPrivate)
    {
        var headers = SplitXiph(codecPrivate);
        if (headers.Count != 3)
        {
            throw new MediaFormatException("Vorbis needs its three headers, and the track does not carry them.");
        }

        return FromHeaders(headers[0], headers[2]);
    }

    /// <summary>The setup from the identification and setup headers, as an Ogg stream carries them.</summary>
    public static VorbisSetup FromHeaders(byte[] identification, byte[] setupHeader)
    {
        var setup = new VorbisSetup();
        setup.ReadIdentification(identification);
        setup.ReadSetup(setupHeader);
        return setup;
    }

    /// <summary>
    /// The block size an audio packet decodes at, from the mode its first bits name: the short or the
    /// long one. Zero for an empty packet, a header, or a mode the stream does not have.
    /// </summary>
    public int BlockSizeOf(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty || (packet[0] & 1) != 0)
        {
            return 0;
        }

        var bits = (packet[0] >> 1) | (packet.Length > 1 ? packet[1] << 7 : 0);
        var mode = bits & ((1 << VorbisCodebook.Ilog(Modes.Length - 1)) - 1);
        return mode >= Modes.Length ? 0 : Modes[mode].LongBlock ? LongBlock : ShortBlock;
    }

    /// <summary>Packets in Xiph lacing: a count less one, the sizes of all but the last (in runs of 255), then the packets.</summary>
    public static List<byte[]> SplitXiph(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return [];
        }

        var count = data[0] + 1;
        var at = 1;
        var sizes = new List<int>();
        for (var i = 0; i < count - 1; i++)
        {
            var size = 0;
            while (true)
            {
                if (at >= data.Length)
                {
                    return [];
                }

                var b = data[at++];
                size += b;
                if (b < 255)
                {
                    break;
                }
            }

            sizes.Add(size);
        }

        sizes.Add(data.Length - at - sizes.Sum());
        if (sizes[^1] < 0)
        {
            return [];
        }

        var packets = new List<byte[]>();
        foreach (var size in sizes)
        {
            packets.Add(data.Slice(at, size).ToArray());
            at += size;
        }

        return packets;
    }

    private static void ExpectHeader(VorbisBits bits, int type)
    {
        if (bits.ReadInt(8) != type || bits.Read(8) != 'v' || bits.Read(8) != 'o' || bits.Read(8) != 'r' || bits.Read(8) != 'b' || bits.Read(8) != 'i' || bits.Read(8) != 's')
        {
            throw new MediaFormatException($"A Vorbis header of type {type} was expected.");
        }
    }

    private void ReadIdentification(byte[] packet)
    {
        var bits = new VorbisBits(packet);
        ExpectHeader(bits, 1);
        if (bits.Read(32) != 0)
        {
            throw new MediaFormatException("Only Vorbis I streams can be decoded.");
        }

        Channels = bits.ReadInt(8);
        SampleRate = (int)bits.Read(32);
        bits.Read(32);
        bits.Read(32);
        bits.Read(32);
        ShortBlock = 1 << bits.ReadInt(4);
        LongBlock = 1 << bits.ReadInt(4);
        if (Channels == 0 || SampleRate == 0 || ShortBlock < 64 || LongBlock > 8192 || ShortBlock > LongBlock || !bits.ReadFlag())
        {
            throw new MediaFormatException("The Vorbis identification header is not valid.");
        }
    }

    private void ReadSetup(byte[] packet)
    {
        var bits = new VorbisBits(packet);
        ExpectHeader(bits, 5);
        Codebooks = new VorbisCodebook[bits.ReadInt(8) + 1];
        for (var i = 0; i < Codebooks.Length; i++)
        {
            Codebooks[i] = new VorbisCodebook(bits);
        }

        // Time domain transforms: placeholders, all zero in Vorbis I.
        var times = bits.ReadInt(6) + 1;
        for (var i = 0; i < times; i++)
        {
            if (bits.Read(16) != 0)
            {
                throw new MediaFormatException("The Vorbis setup has a time domain transform Vorbis I does not define.");
            }
        }

        Floors = new VorbisFloor1?[bits.ReadInt(6) + 1];
        for (var i = 0; i < Floors.Length; i++)
        {
            Floors[i] = bits.ReadInt(16) switch
            {
                0 => SkipFloor0(bits),
                1 => ReadFloor1(bits),
                var type => throw new MediaFormatException($"Vorbis floor type {type} is not defined."),
            };
        }

        Residues = new VorbisResidue[bits.ReadInt(6) + 1];
        for (var i = 0; i < Residues.Length; i++)
        {
            Residues[i] = ReadResidue(bits);
        }

        Mappings = new VorbisMapping[bits.ReadInt(6) + 1];
        for (var i = 0; i < Mappings.Length; i++)
        {
            Mappings[i] = ReadMapping(bits);
        }

        Modes = new VorbisMode[bits.ReadInt(6) + 1];
        for (var i = 0; i < Modes.Length; i++)
        {
            var longBlock = bits.ReadFlag();
            var window = bits.Read(16);
            var transform = bits.Read(16);
            var mapping = bits.ReadInt(8);
            if (window != 0 || transform != 0 || mapping >= Mappings.Length)
            {
                throw new MediaFormatException("A Vorbis mode is not valid.");
            }

            Modes[i] = new VorbisMode(longBlock, mapping);
        }

        if (!bits.ReadFlag() || bits.EndOfPacket)
        {
            throw new MediaFormatException("The Vorbis setup header does not end as it should.");
        }
    }

    /// <summary>Floor 0 (line spectral pairs) is read past, so the stream's setup parses; packets using it are not decoded.</summary>
    private static VorbisFloor1? SkipFloor0(VorbisBits bits)
    {
        bits.Read(8);
        bits.Read(16);
        bits.Read(16);
        bits.Read(6);
        bits.Read(8);
        var books = bits.ReadInt(4) + 1;
        for (var i = 0; i < books; i++)
        {
            bits.Read(8);
        }

        return null;
    }

    private VorbisFloor1 ReadFloor1(VorbisBits bits)
    {
        var partitions = bits.ReadInt(5);
        var partitionClasses = new int[partitions];
        var maximumClass = -1;
        for (var i = 0; i < partitions; i++)
        {
            partitionClasses[i] = bits.ReadInt(4);
            maximumClass = Math.Max(maximumClass, partitionClasses[i]);
        }

        var classes = maximumClass + 1;
        var dimensions = new int[classes];
        var subclasses = new int[classes];
        var masterbooks = new int[classes];
        var subclassBooks = new int[classes][];
        for (var c = 0; c < classes; c++)
        {
            dimensions[c] = bits.ReadInt(3) + 1;
            subclasses[c] = bits.ReadInt(2);
            masterbooks[c] = subclasses[c] > 0 ? Book(bits.ReadInt(8)) : -1;
            subclassBooks[c] = new int[1 << subclasses[c]];
            for (var j = 0; j < subclassBooks[c].Length; j++)
            {
                var book = bits.ReadInt(8) - 1;
                subclassBooks[c][j] = book < 0 ? -1 : Book(book);
            }
        }

        var multiplier = bits.ReadInt(2) + 1;
        var rangeBits = bits.ReadInt(4);
        var x = new List<int> { 0, 1 << rangeBits };
        foreach (var c in partitionClasses)
        {
            for (var j = 0; j < dimensions[c]; j++)
            {
                x.Add(bits.ReadInt(rangeBits));
            }
        }

        if (x.Count > 65 || x.Distinct().Count() != x.Count)
        {
            throw new MediaFormatException("A Vorbis floor's points are not valid.");
        }

        var xs = x.ToArray();
        var low = new int[xs.Length];
        var high = new int[xs.Length];
        for (var i = 2; i < xs.Length; i++)
        {
            var lowX = -1;
            var highX = int.MaxValue;
            for (var j = 0; j < i; j++)
            {
                if (xs[j] < xs[i] && xs[j] > lowX)
                {
                    lowX = xs[j];
                    low[i] = j;
                }

                if (xs[j] > xs[i] && xs[j] < highX)
                {
                    highX = xs[j];
                    high[i] = j;
                }
            }
        }

        return new VorbisFloor1
        {
            PartitionClasses = partitionClasses,
            ClassDimensions = dimensions,
            ClassSubclasses = subclasses,
            ClassMasterbooks = masterbooks,
            SubclassBooks = subclassBooks,
            Multiplier = multiplier,
            X = xs,
            Low = low,
            High = high,
            Sorted = [.. Enumerable.Range(0, xs.Length).OrderBy(i => xs[i])],
        };
    }

    private VorbisResidue ReadResidue(VorbisBits bits)
    {
        var type = bits.ReadInt(16);
        if (type > 2)
        {
            throw new MediaFormatException($"Vorbis residue type {type} is not defined.");
        }

        var begin = (int)bits.Read(24);
        var end = (int)bits.Read(24);
        var partitionSize = (int)bits.Read(24) + 1;
        var classifications = bits.ReadInt(6) + 1;
        var classbook = Book(bits.ReadInt(8));
        var cascades = new int[classifications];
        for (var i = 0; i < classifications; i++)
        {
            var lowBits = bits.ReadInt(3);
            var highBits = bits.ReadFlag() ? bits.ReadInt(5) : 0;
            cascades[i] = (highBits << 3) | lowBits;
        }

        var books = new int[classifications][];
        for (var i = 0; i < classifications; i++)
        {
            books[i] = new int[8];
            for (var pass = 0; pass < 8; pass++)
            {
                books[i][pass] = (cascades[i] & (1 << pass)) != 0 ? Book(bits.ReadInt(8)) : -1;
            }
        }

        return new VorbisResidue { Type = type, Begin = begin, End = end, PartitionSize = partitionSize, Classifications = classifications, Classbook = classbook, Books = books };
    }

    private VorbisMapping ReadMapping(VorbisBits bits)
    {
        if (bits.Read(16) != 0)
        {
            throw new MediaFormatException("Only Vorbis mapping type 0 is defined.");
        }

        var submaps = bits.ReadFlag() ? bits.ReadInt(4) + 1 : 1;
        var magnitude = Array.Empty<int>();
        var angle = Array.Empty<int>();
        if (bits.ReadFlag())
        {
            var steps = bits.ReadInt(8) + 1;
            magnitude = new int[steps];
            angle = new int[steps];
            var width = VorbisCodebook.Ilog(Channels - 1);
            for (var i = 0; i < steps; i++)
            {
                magnitude[i] = bits.ReadInt(width);
                angle[i] = bits.ReadInt(width);
                if (magnitude[i] == angle[i] || magnitude[i] >= Channels || angle[i] >= Channels)
                {
                    throw new MediaFormatException("A Vorbis channel coupling step is not valid.");
                }
            }
        }

        if (bits.Read(2) != 0)
        {
            throw new MediaFormatException("A Vorbis mapping's reserved field is not zero.");
        }

        var mux = new int[Channels];
        if (submaps > 1)
        {
            for (var channel = 0; channel < Channels; channel++)
            {
                mux[channel] = bits.ReadInt(4);
                if (mux[channel] >= submaps)
                {
                    throw new MediaFormatException("A Vorbis mapping names a submap it does not have.");
                }
            }
        }

        var floors = new int[submaps];
        var residues = new int[submaps];
        for (var i = 0; i < submaps; i++)
        {
            bits.Read(8);
            floors[i] = bits.ReadInt(8);
            residues[i] = bits.ReadInt(8);
            if (floors[i] >= Floors.Length || residues[i] >= Residues.Length)
            {
                throw new MediaFormatException("A Vorbis mapping names a floor or residue it does not have.");
            }
        }

        return new VorbisMapping { Magnitude = magnitude, Angle = angle, Mux = mux, SubmapFloor = floors, SubmapResidue = residues };
    }

    private int Book(int number) =>
        number < Codebooks.Length ? number : throw new MediaFormatException("The Vorbis setup names a codebook it does not have.");
}
