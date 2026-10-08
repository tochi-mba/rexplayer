// Spec: Vorbis I specification, section 3 (codebook format: entry lengths, codeword assignment, value lookup types 1 and 2, float32_unpack and lookup1_values) and 9.2.1-9.2.4 (ilog, float32_unpack, lookup1_values, low/high neighbor).
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Vorbis;

/// <summary>
/// One codebook of a Vorbis stream: a Huffman tree over its entries and, for vector quantisation,
/// the vector each entry stands for. Codewords are assigned as the spec requires: each entry, in
/// order, takes the lowest free codeword of its length.
/// </summary>
internal sealed class VorbisCodebook
{
    private const int TableBits = 10;

    // The tree: children per node (0 = none), and the entry a leaf decodes to (-1 for inner nodes).
    private readonly List<int> _zero = [0];
    private readonly List<int> _one = [0];
    private readonly List<int> _entryOf = [-1];
    private readonly List<bool> _full = [false];

    // Fast path: the next TableBits bits, as read, give the entry (and its length) of short codewords.
    private readonly int[] _tableEntry = new int[1 << TableBits];
    private readonly byte[] _tableLength = new byte[1 << TableBits];
    private readonly int _singleEntry = -1;
    private float[] _vectors = [];

    public VorbisCodebook(VorbisBits bits)
    {
        if (bits.Read(24) != 0x564342)
        {
            throw new MediaFormatException("A Vorbis codebook does not start with its sync pattern.");
        }

        Dimensions = bits.ReadInt(16);
        Entries = bits.ReadInt(24);
        var lengths = new int[Entries];
        if (bits.ReadFlag())
        {
            // Ordered: runs of entries of increasing length.
            var entry = 0;
            var length = bits.ReadInt(5) + 1;
            while (entry < Entries)
            {
                var count = bits.ReadInt(Ilog(Entries - entry));
                if (entry + count > Entries)
                {
                    throw new MediaFormatException("A Vorbis codebook lists more entries than it has.");
                }

                Array.Fill(lengths, length, entry, count);
                entry += count;
                length++;
            }
        }
        else
        {
            var sparse = bits.ReadFlag();
            for (var entry = 0; entry < Entries; entry++)
            {
                lengths[entry] = !sparse || bits.ReadFlag() ? bits.ReadInt(5) + 1 : 0;
            }
        }

        var used = lengths.Count(length => length > 0);
        if (used == 1)
        {
            // A codebook with one entry: it decodes without reading a codeword's worth of choice.
            _singleEntry = Array.FindIndex(lengths, length => length > 0);
        }
        else
        {
            Array.Fill(_tableLength, (byte)0);
            for (var entry = 0; entry < Entries; entry++)
            {
                if (lengths[entry] > 0)
                {
                    Assign(entry, lengths[entry]);
                }
            }
        }

        LookupType = bits.ReadInt(4);
        if (LookupType is 1 or 2)
        {
            var minimum = Float32Unpack(bits.Read(32));
            var delta = Float32Unpack(bits.Read(32));
            var valueBits = bits.ReadInt(4) + 1;
            var sequence = bits.ReadFlag();
            var lookupValues = LookupType == 1 ? Lookup1Values(Entries, Dimensions) : Entries * Dimensions;
            var multiplicands = new int[lookupValues];
            for (var i = 0; i < lookupValues; i++)
            {
                multiplicands[i] = bits.ReadInt(valueBits);
            }

            BuildVectors(minimum, delta, sequence, lookupValues, multiplicands);
        }
        else if (LookupType != 0)
        {
            throw new MediaFormatException($"Vorbis codebook lookup type {LookupType} is not defined.");
        }

        if (bits.EndOfPacket)
        {
            throw new MediaFormatException("The Vorbis setup header ends inside a codebook.");
        }
    }

    public int Dimensions { get; }

    public int Entries { get; }

    public int LookupType { get; }

    /// <summary>ilog: the position of the highest set bit (0 for 0).</summary>
    public static int Ilog(long value) => value <= 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)value);

    public static float Float32Unpack(uint value)
    {
        double mantissa = value & 0x1fffff;
        var exponent = (int)((value & 0x7fe00000) >> 21);
        if ((value & 0x80000000) != 0)
        {
            mantissa = -mantissa;
        }

        return (float)(mantissa * Math.Pow(2, exponent - 788));
    }

    /// <summary>The largest number whose <paramref name="dimensions"/>-th power is no more than <paramref name="entries"/>.</summary>
    public static int Lookup1Values(int entries, int dimensions)
    {
        // Math.Pow can land just below a whole root (1000 to the third gives 9.999...), never above one.
        var root = (int)Math.Floor(Math.Pow(entries, 1.0 / dimensions));
        while (Math.Pow(root + 1, dimensions) <= entries)
        {
            root++;
        }

        return root;
    }

    /// <summary>Reads one codeword and gives its entry, or -1 when the bits name no entry (or the packet ended).</summary>
    public int DecodeScalar(VorbisBits bits)
    {
        if (_singleEntry >= 0)
        {
            bits.Skip(1);
            return bits.EndOfPacket ? -1 : _singleEntry;
        }

        var peek = (int)bits.Peek(TableBits);
        var length = _tableLength[peek];
        if (length > 0)
        {
            bits.Skip(length);
            return bits.EndOfPacket ? -1 : _tableEntry[peek];
        }

        var node = 0;
        while (_entryOf[node] < 0)
        {
            node = bits.ReadFlag() ? _one[node] : _zero[node];
            if (node == 0 || bits.EndOfPacket)
            {
                return -1;
            }
        }

        return _entryOf[node];
    }

    /// <summary>The vector of <paramref name="entry"/>, <see cref="Dimensions"/> values long.</summary>
    public ReadOnlySpan<float> Vector(int entry) => _vectors.AsSpan(entry * Dimensions, Dimensions);

    /// <summary>Reads a codeword and gives its vector; empty when the bits name no entry.</summary>
    public ReadOnlySpan<float> DecodeVector(VorbisBits bits)
    {
        var entry = DecodeScalar(bits);
        return entry < 0 || _vectors.Length == 0 ? ReadOnlySpan<float>.Empty : Vector(entry);
    }

    private void Assign(int entry, int length)
    {
        if (!Insert(0, 0, length, entry, 0))
        {
            throw new MediaFormatException("A Vorbis codebook's lengths do not make a prefix code.");
        }
    }

    /// <summary>Puts <paramref name="entry"/> at the leftmost free node <paramref name="length"/> deep under <paramref name="node"/>.</summary>
    private bool Insert(int node, int depth, int length, int entry, uint code)
    {
        if (_entryOf[node] >= 0 || _full[node])
        {
            return false;
        }

        if (depth == length)
        {
            if (_zero[node] != 0 || _one[node] != 0)
            {
                return false;
            }

            _entryOf[node] = entry;
            _full[node] = true;
            AddToTable(entry, length, code);
            return true;
        }

        for (var side = 0; side < 2; side++)
        {
            var child = side == 0 ? _zero[node] : _one[node];
            if (child == 0)
            {
                child = _zero.Count;
                _zero.Add(0);
                _one.Add(0);
                _entryOf.Add(-1);
                _full.Add(false);
                if (side == 0)
                {
                    _zero[node] = child;
                }
                else
                {
                    _one[node] = child;
                }
            }

            if (Insert(child, depth + 1, length, entry, (code << 1) | (uint)side))
            {
                _full[node] = _zero[node] != 0 && _one[node] != 0 && _full[_zero[node]] && _full[_one[node]];
                return true;
            }
        }

        return false;
    }

    /// <summary>Short codewords decode with one table look-up: the bits as read are the codeword reversed.</summary>
    private void AddToTable(int entry, int length, uint code)
    {
        if (length > TableBits)
        {
            return;
        }

        uint reversed = 0;
        for (var i = 0; i < length; i++)
        {
            reversed |= ((code >> (length - 1 - i)) & 1) << i;
        }

        for (var high = 0; high < 1 << (TableBits - length); high++)
        {
            var index = reversed | ((uint)high << length);
            _tableEntry[index] = entry;
            _tableLength[index] = (byte)length;
        }
    }

    private void BuildVectors(float minimum, float delta, bool sequence, int lookupValues, int[] multiplicands)
    {
        _vectors = new float[Entries * Dimensions];
        for (var entry = 0; entry < Entries; entry++)
        {
            var last = 0f;
            var divisor = 1L;
            for (var i = 0; i < Dimensions; i++)
            {
                var offset = LookupType == 1 ? (int)(entry / divisor % lookupValues) : (entry * Dimensions) + i;
                var value = (multiplicands[offset] * delta) + minimum + last;
                if (sequence)
                {
                    last = value;
                }

                _vectors[(entry * Dimensions) + i] = value;
                // Past the number of entries every further quotient is zero, so the divisor stops growing.
                divisor = Math.Min(divisor * lookupValues, (long)Entries + 1);
            }
        }
    }
}
