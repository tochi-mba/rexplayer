// Spec: IETF RFC 8794 (Extensible Binary Meta Language) sections 4 "Variable-Size Integer", 5 "Element ID", 6 "Element Data Size" (including the unknown size), 7 "EBML Element Types".
using System.Buffers.Binary;
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Matroska;

/// <summary>An element found inside a parent: its ID and where its data lies in the parent's bytes.</summary>
internal readonly record struct EbmlElement(uint Id, int Start, int End);

/// <summary>
/// Reads EBML, the binary format Matroska and WebM are written in: every element is an ID, a
/// size and data, with IDs and sizes stored as variable-length integers. Parsing is lenient where
/// a damaged file can still play and strict where a wrong length would read the wrong bytes.
/// </summary>
internal static class Ebml
{
    /// <summary>The size value meaning "unknown": the element runs until something that cannot be inside it.</summary>
    public const long UnknownSize = -1;

    /// <summary>The length of a variable-size integer from its first byte (1 to 8), or 0 if the byte cannot start one.</summary>
    public static int Length(byte first) => first == 0 ? 0 : System.Numerics.BitOperations.LeadingZeroCount((uint)first) - 23;

    /// <summary>Reads an element ID (marker bit kept, as IDs are written); false when there is no valid ID here.</summary>
    public static bool TryReadId(ReadOnlySpan<byte> data, ref int offset, out uint id)
    {
        id = 0;
        if (offset >= data.Length)
        {
            return false;
        }

        var length = Length(data[offset]);
        if (length is 0 or > 4 || offset + length > data.Length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            id = (id << 8) | data[offset + i];
        }

        offset += length;
        return true;
    }

    /// <summary>Reads a data size (marker bit removed); <see cref="UnknownSize"/> when every value bit is set.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, ref int offset, out long size)
    {
        size = 0;
        if (offset >= data.Length)
        {
            return false;
        }

        var length = Length(data[offset]);
        if (length == 0 || offset + length > data.Length)
        {
            return false;
        }

        size = ReadValue(data.Slice(offset, length), length);
        offset += length;
        return true;
    }

    /// <summary>The value of a variable-size integer whose bytes are <paramref name="bytes"/>.</summary>
    public static long ReadValue(ReadOnlySpan<byte> bytes, int length)
    {
        var value = (ulong)(bytes[0] & (0xFF >> length));
        var allOnes = value == (ulong)(0xFF >> length);
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | bytes[i];
            allOnes &= bytes[i] == 0xFF;
        }

        return allOnes ? UnknownSize : (long)value;
    }

    /// <summary>Reads a variable-size integer from a cursor; the ID form keeps the marker bit.</summary>
    public static (long Value, int Length) Read(ByteCursor cursor, bool keepMarker)
    {
        Span<byte> bytes = stackalloc byte[8];
        bytes[0] = cursor.ReadByte();
        var length = Length(bytes[0]);
        if (length == 0 || (keepMarker && length > 4))
        {
            throw new MediaFormatException("A Matroska element has a malformed ID or size.");
        }

        cursor.ReadExactly(bytes[1..length]);
        if (!keepMarker)
        {
            return (ReadValue(bytes, length), length);
        }

        long id = 0;
        for (var i = 0; i < length; i++)
        {
            id = (id << 8) | bytes[i];
        }

        return (id, length);
    }

    /// <summary>
    /// The elements directly inside <paramref name="data"/>. An element whose size runs past the
    /// end is cut there; an unknown size runs to the end; bytes that are no element end the list.
    /// </summary>
    public static List<EbmlElement> Children(ReadOnlySpan<byte> data)
    {
        var elements = new List<EbmlElement>();
        var offset = 0;
        while (TryReadId(data, ref offset, out var id) && TryReadSize(data, ref offset, out var size))
        {
            var end = size == UnknownSize || size > data.Length - offset ? data.Length : offset + (int)size;
            elements.Add(new EbmlElement(id, offset, end));
            offset = end;
        }

        return elements;
    }

    /// <summary>The first child with <paramref name="id"/>, walked like <see cref="Children"/> but without a list (it runs per block).</summary>
    public static EbmlElement? Find(ReadOnlySpan<byte> data, uint id)
    {
        var offset = 0;
        while (TryReadId(data, ref offset, out var found) && TryReadSize(data, ref offset, out var size))
        {
            var end = size == UnknownSize || size > data.Length - offset ? data.Length : offset + (int)size;
            if (found == id)
            {
                return new EbmlElement(found, offset, end);
            }

            offset = end;
        }

        return null;
    }

    /// <summary>An unsigned integer of up to eight bytes; an empty element is 0.</summary>
    public static ulong UInt(ReadOnlySpan<byte> body)
    {
        ulong value = 0;
        foreach (var b in body[..Math.Min(body.Length, 8)])
        {
            value = (value << 8) | b;
        }

        return value;
    }

    /// <summary>A signed integer of up to eight bytes; an empty element is 0.</summary>
    public static long Int(ReadOnlySpan<byte> body)
    {
        var length = Math.Min(body.Length, 8);
        var shift = 64 - (8 * length);
        return length == 0 ? 0 : (long)(UInt(body) << shift) >> shift;
    }

    /// <summary>A float of four or eight bytes; any other length reads as 0.</summary>
    public static double Float(ReadOnlySpan<byte> body) => body.Length switch
    {
        4 => BinaryPrimitives.ReadSingleBigEndian(body),
        8 => BinaryPrimitives.ReadDoubleBigEndian(body),
        _ => 0,
    };

    /// <summary>A UTF-8 string, ending at its first NUL.</summary>
    public static string Text(ReadOnlySpan<byte> body)
    {
        var nul = body.IndexOf((byte)0);
        return Encoding.UTF8.GetString(nul < 0 ? body : body[..nul]);
    }
}
