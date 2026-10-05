// Spec: ISO/IEC 11172-3 clause 2.4.2.7 "huffmancodebits" and 2.4.3.4.4 (Huffman decoding of big values and count1 quadruples); Annex B table 3-B.7.
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Mpeg;

/// <summary>A binary decode tree built from a table of (code, length) entries.</summary>
internal sealed class HuffmanTree
{
    // Node n's children are at 2n and 2n + 1. A child above zero is the next node; below zero, a
    // leaf holding the symbol as -(symbol + 1). Every Layer III code table is complete (a test
    // proves it), so every path through the tree ends at a leaf.
    private readonly int[] _children;

    public HuffmanTree(uint[] entries)
    {
        var children = new List<int> { 0, 0 };
        for (var symbol = 0; symbol < entries.Length; symbol++)
        {
            var length = (int)(entries[symbol] >> 24);
            var code = entries[symbol] & 0xFFFFFF;
            var node = 0;
            for (var bit = length - 1; bit >= 0; bit--)
            {
                var slot = (2 * node) + (int)((code >> bit) & 1);
                if (bit == 0)
                {
                    children[slot] = -(symbol + 1);
                    break;
                }

                if (children[slot] <= 0)
                {
                    children[slot] = children.Count / 2;
                    children.Add(0);
                    children.Add(0);
                }

                node = children[slot];
            }
        }

        _children = [.. children];
    }

    public int Decode(ref BitReader reader)
    {
        var node = 0;
        while (true)
        {
            var next = _children[(2 * node) + (reader.ReadBit() ? 1 : 0)];
            if (next < 0)
            {
                return -next - 1;
            }

            node = next;
        }
    }
}

/// <summary>Reads the quantized spectrum of one granule of one channel.</summary>
internal static class Layer3Huffman
{
    /// <summary>Extra magnitude bits for values of 15 in each table (table 0 to 31).</summary>
    private static readonly int[] Linbits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];

    private static readonly (HuffmanTree Tree, int Size)?[] Pairs = BuildPairs();

    private static readonly HuffmanTree QuadA = new(Layer3Tables.QuadsA);

    private static readonly HuffmanTree QuadB = new(Layer3Tables.QuadsB);

    /// <summary>
    /// Fills <paramref name="values"/> with the granule's 576 quantized lines, reading no further
    /// than bit <paramref name="end"/> for the count1 region. Returns one past the last nonzero line.
    /// </summary>
    public static int ReadSpectrum(ref BitReader reader, GranuleInfo granule, long end, int[] values)
    {
        var bigEnd = granule.BigValues * 2;
        var index = 0;
        for (var region = 0; region < 3; region++)
        {
            var regionEnd = Math.Min(region switch
            {
                0 => granule.Region1Start,
                1 => granule.Region2Start,
                _ => 576,
            }, bigEnd);
            var table = granule.TableSelect[region];
            if (table == 0)
            {
                Array.Clear(values, index, Math.Max(0, regionEnd - index));
                index = Math.Max(index, regionEnd);
                continue;
            }

            if (Pairs[table] is not { } pair)
            {
                throw new MediaFormatException("A Layer III granule selects a Huffman table that does not exist.");
            }

            var linbits = Linbits[table];
            while (index < regionEnd)
            {
                var symbol = pair.Tree.Decode(ref reader);
                values[index++] = ReadValue(ref reader, symbol / pair.Size, linbits);
                values[index++] = ReadValue(ref reader, symbol % pair.Size, linbits);
            }
        }

        var quads = granule.Count1TableB ? QuadB : QuadA;
        while (index + 4 <= 576 && reader.BitPosition < end)
        {
            var symbol = quads.Decode(ref reader);
            var v = ReadValue(ref reader, (symbol >> 3) & 1, 0);
            var w = ReadValue(ref reader, (symbol >> 2) & 1, 0);
            var x = ReadValue(ref reader, (symbol >> 1) & 1, 0);
            var y = ReadValue(ref reader, symbol & 1, 0);
            if (reader.BitPosition > end)
            {
                // The last quadruple ran past the granule's bits: the encoder's stuffing, not data.
                break;
            }

            values[index++] = v;
            values[index++] = w;
            values[index++] = x;
            values[index++] = y;
        }

        Array.Clear(values, index, 576 - index);
        while (index > 0 && values[index - 1] == 0)
        {
            index--;
        }

        return index;
    }

    private static int ReadValue(ref BitReader reader, int magnitude, int linbits)
    {
        if (magnitude == 15 && linbits > 0)
        {
            magnitude += (int)reader.ReadBits(linbits);
        }

        return magnitude != 0 && reader.ReadBit() ? -magnitude : magnitude;
    }

    private static (HuffmanTree, int)?[] BuildPairs()
    {
        var tables = new (uint[] Entries, int Size)?[]
        {
            null, (Layer3Tables.Pairs1, 2), (Layer3Tables.Pairs2, 3), (Layer3Tables.Pairs3, 3), null,
            (Layer3Tables.Pairs5, 4), (Layer3Tables.Pairs6, 4), (Layer3Tables.Pairs7, 6), (Layer3Tables.Pairs8, 6),
            (Layer3Tables.Pairs9, 6), (Layer3Tables.Pairs10, 8), (Layer3Tables.Pairs11, 8), (Layer3Tables.Pairs12, 8),
            (Layer3Tables.Pairs13, 16), null, (Layer3Tables.Pairs15, 16),
        };
        var sixteen = new HuffmanTree(Layer3Tables.Pairs16);
        var twentyFour = new HuffmanTree(Layer3Tables.Pairs24);
        var trees = new (HuffmanTree, int)?[32];
        for (var table = 1; table < 32; table++)
        {
            trees[table] = table switch
            {
                >= 24 => (twentyFour, 16),
                >= 16 => (sixteen, 16),
                _ => tables[table] is { } t ? (new HuffmanTree(t.Entries), t.Size) : null,
            };
        }

        return trees;
    }
}
