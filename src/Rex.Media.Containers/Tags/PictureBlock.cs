// Spec: IETF RFC 9639 (FLAC), section 8.8 "Picture"; the same structure is base64-encoded in METADATA_BLOCK_PICTURE Vorbis comments.
using System.Buffers.Binary;

namespace Rex.Media.Containers.Tags;

/// <summary>An embedded picture: its role (3 is the front cover), its MIME type and the image bytes.</summary>
public sealed record PictureBlock(int Type, string MimeType, byte[] Data)
{
    public const int FrontCover = 3;

    /// <summary>Reads the block, or returns null if its lengths run past its end.</summary>
    public static PictureBlock? Parse(ReadOnlySpan<byte> block)
    {
        var offset = 0;
        if (!TryReadUInt32(block, ref offset, out var type)
            || !TryReadBytes(block, ref offset, out var mime)
            || !TryReadBytes(block, ref offset, out _)
            || block.Length - offset < 16)
        {
            return null;
        }

        offset += 16;
        if (!TryReadBytes(block, ref offset, out var data))
        {
            return null;
        }

        return new PictureBlock((int)type, System.Text.Encoding.ASCII.GetString(mime), data.ToArray());
    }

    /// <summary>The front cover if there is one, otherwise the first picture.</summary>
    public static PictureBlock? Cover(IEnumerable<PictureBlock> pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        PictureBlock? first = null;
        foreach (var picture in pictures)
        {
            if (picture.Type == FrontCover)
            {
                return picture;
            }

            first ??= picture;
        }

        return first;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> block, ref int offset, out uint value)
    {
        value = 0;
        if (block.Length - offset < 4)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(block[offset..]);
        offset += 4;
        return true;
    }

    private static bool TryReadBytes(ReadOnlySpan<byte> block, ref int offset, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        if (!TryReadUInt32(block, ref offset, out var length) || length > (uint)(block.Length - offset))
        {
            return false;
        }

        bytes = block.Slice(offset, (int)length);
        offset += (int)length;
        return true;
    }
}
