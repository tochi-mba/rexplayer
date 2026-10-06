// Spec: ISO/IEC 14496-12 (ISO base media file format) clause 4.2 "Object structure" (box size, largesize, size zero, uuid, FullBox version and flags).
using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Mp4;

/// <summary>A box found inside a parent: its type and where its body lies in the parent's bytes.</summary>
internal readonly record struct Mp4Box(string Type, int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// Reads ISO base media boxes out of a block of memory. Boxes are lenient by design: one that
/// claims more bytes than its parent holds is cut to the parent's end, because files written by a
/// crashed recorder end mid-box and should still play as far as they go.
/// </summary>
internal static class Mp4Boxes
{
    /// <summary>The boxes directly inside <paramref name="data"/>, starting at <paramref name="offset"/>.</summary>
    public static List<Mp4Box> Children(ReadOnlySpan<byte> data, int offset = 0)
    {
        var boxes = new List<Mp4Box>();
        while (offset + 8 <= data.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var type = FourCC.ToString(data.Slice(offset + 4, 4));
            var header = 8;
            if (size == 1)
            {
                if (offset + 16 > data.Length)
                {
                    break;
                }

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]), long.MaxValue);
                header = 16;
            }
            else if (size == 0)
            {
                size = data.Length - offset;
            }

            if (type == "uuid")
            {
                header += 16;
            }

            if (size < header)
            {
                break;
            }

            var end = (int)Math.Min(offset + size, data.Length);
            boxes.Add(new Mp4Box(type, Math.Min(offset + header, end), end));
            offset = end;
        }

        return boxes;
    }

    /// <summary>The first child of the given type, or null.</summary>
    public static Mp4Box? Find(ReadOnlySpan<byte> data, string type, int offset = 0)
    {
        foreach (var box in Children(data, offset))
        {
            if (box.Type == type)
            {
                return box;
            }
        }

        return null;
    }

    /// <summary>Follows a path of box types (for example "mdia", "minf", "stbl") from <paramref name="parent"/>.</summary>
    public static Mp4Box? Path(ReadOnlySpan<byte> data, Mp4Box parent, params string[] path)
    {
        Mp4Box? current = parent;
        foreach (var type in path)
        {
            current = Find(data[..current.Value.End], type, current.Value.Start);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public static ReadOnlySpan<byte> Body(ReadOnlySpan<byte> data, Mp4Box box) => data[box.Start..box.End];

    public static uint U32(ReadOnlySpan<byte> data, int offset) => offset + 4 <= data.Length ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : throw Truncated();

    public static ulong U64(ReadOnlySpan<byte> data, int offset) => offset + 8 <= data.Length ? BinaryPrimitives.ReadUInt64BigEndian(data[offset..]) : throw Truncated();

    public static ushort U16(ReadOnlySpan<byte> data, int offset) => offset + 2 <= data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : throw Truncated();

    public static MediaFormatException Truncated() => new("An MP4 box ends before its fields do.");
}
