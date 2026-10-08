// Spec: JPEG (ITU-T T.81 Annex B: markers, segment lengths, SOFn), PNG (ISO/IEC 15948:2004: signature, IHDR), GIF89a (CompuServe 1990: header, logical screen descriptor), WebP container (RFC 9649: RIFF/WEBP, VP8, VP8L and VP8X headers), BMP (Microsoft BITMAPFILEHEADER, BITMAPCOREHEADER, BITMAPINFOHEADER), TIFF 6.0 (Adobe 1992: header, first IFD, ImageWidth and ImageLength), HEIF (ISO/IEC 23008-12: ftyp brands, meta/iprp/ipco/ispe) and AVIF (AV1 Image File Format 1.1: brands).
using System.Buffers.Binary;

namespace Rex.Media.Containers.Image;

/// <summary>The picture formats rexplayer shows.</summary>
public enum PictureFormat
{
    Jpeg,
    Png,
    Gif,
    WebP,
    Bmp,
    Tiff,

    /// <summary>HEIF (HEIC) and AVIF: pictures in an ISO base media file.</summary>
    Heif,
}

/// <summary>
/// Recognises a picture by its first bytes, whatever its file is called, and reads the size its
/// header gives. The pictures themselves are decoded elsewhere.
/// </summary>
public static class PictureFormats
{
    private static readonly HashSet<string> HeifBrands = new(StringComparer.Ordinal) { "heic", "heix", "heim", "heis", "hevc", "hevx", "mif1", "msf1", "avif", "avis" };

    /// <summary>The format the bytes start as, or null when they are no picture rexplayer knows.</summary>
    public static PictureFormat? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return PictureFormat.Jpeg;
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return PictureFormat.Png;
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return PictureFormat.Gif;
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return PictureFormat.WebP;
        }

        // "BM" alone is too common a start, so the size of the header after it must be one Windows writes.
        if (head.Length >= 18 && head.StartsWith("BM"u8) && BinaryPrimitives.ReadUInt32LittleEndian(head[14..]) is 12 or 40 or 52 or 56 or 64 or 108 or 124)
        {
            return PictureFormat.Bmp;
        }

        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8))
        {
            return PictureFormat.Tiff;
        }

        return IsHeif(head) ? PictureFormat.Heif : null;
    }

    /// <summary>The name shown in Media Information.</summary>
    public static string Name(PictureFormat format) => format switch
    {
        PictureFormat.Jpeg => "JPEG",
        PictureFormat.Png => "PNG",
        PictureFormat.Gif => "GIF",
        PictureFormat.WebP => "WebP",
        PictureFormat.Bmp => "BMP",
        PictureFormat.Tiff => "TIFF",
        _ => "HEIF",
    };

    /// <summary>The picture's width and height as its header gives them; (0, 0) when it does not say or is cut short.</summary>
    public static (int Width, int Height) SizeOf(PictureFormat format, ReadOnlySpan<byte> data)
    {
        var (width, height) = format switch
        {
            PictureFormat.Jpeg => JpegSize(data),
            PictureFormat.Png => data.Length >= 24 && data[12..16].SequenceEqual("IHDR"u8) ? (BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..])) : (0L, 0L),
            PictureFormat.Gif => data.Length >= 10 ? (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..])) : (0, 0),
            PictureFormat.WebP => WebPSize(data),
            PictureFormat.Bmp => BmpSize(data),
            PictureFormat.Tiff => TiffSize(data),
            _ => HeifSize(data),
        };
        return width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue ? ((int)width, (int)height) : (0, 0);
    }

    /// <summary>An ISO base media file whose brands name a HEIF or AVIF picture (an MP4 names none of them).</summary>
    public static bool IsHeif(ReadOnlySpan<byte> head)
    {
        if (head.Length < 16 || !head[4..8].SequenceEqual("ftyp"u8))
        {
            return false;
        }

        // The major brand, then the compatible ones after the minor version.
        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head), (uint)head.Length);
        for (var at = 8; at + 4 <= size; at += at == 8 ? 8 : 4)
        {
            if (HeifBrands.Contains(System.Text.Encoding.ASCII.GetString(head.Slice(at, 4))))
            {
                return true;
            }
        }

        return false;
    }

    private static (long, long) JpegSize(ReadOnlySpan<byte> data)
    {
        var at = 2;
        while (at + 4 <= data.Length && data[at] == 0xFF)
        {
            var marker = data[at + 1];

            // Fill bytes, then markers that stand alone.
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            var isFrameHeader = marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (isFrameHeader && at + 9 <= data.Length)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]));
            }

            if (length < 2)
            {
                break;
            }

            at += 2 + length;
        }

        return (0, 0);
    }

    private static (long, long) WebPSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
        {
            return (0, 0);
        }

        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && data[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        return chunk.SequenceEqual("VP8X"u8)
            ? ((data[24] | (data[25] << 8) | (data[26] << 16)) + 1, (data[27] | (data[28] << 8) | (data[29] << 16)) + 1)
            : (0, 0);
    }

    private static (long, long) BmpSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26)
        {
            return (0, 0);
        }

        // An old OS/2 header has 16-bit sizes; the rest 32-bit, the height negative for top-down rows.
        return BinaryPrimitives.ReadUInt32LittleEndian(data[14..]) == 12
            ? (BinaryPrimitives.ReadUInt16LittleEndian(data[18..]), BinaryPrimitives.ReadUInt16LittleEndian(data[20..]))
            : (BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
    }

    private static (long, long) TiffSize(ReadOnlySpan<byte> data)
    {
        var little = data[0] == (byte)'I';
        if (data.Length < 8 || U32(data, 4, little) is var directory && directory + 2L > data.Length)
        {
            return (0, 0);
        }

        long width = 0, height = 0;
        var entries = U16(data, (int)directory, little);
        for (var i = 0; i < entries; i++)
        {
            var at = (int)directory + 2 + (12 * i);
            if (at + 12 > data.Length)
            {
                break;
            }

            // A SHORT or LONG value sits in the entry itself.
            long value = U16(data, at + 2, little) switch
            {
                3 => U16(data, at + 8, little),
                4 => U32(data, at + 8, little),
                _ => 0,
            };
            switch (U16(data, at, little))
            {
                case 256:
                    width = value;
                    break;
                case 257:
                    height = value;
                    break;
            }
        }

        return (width, height);
    }

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    /// <summary>The largest image spatial extents ("ispe") property: the main picture, not a thumbnail.</summary>
    private static (long, long) HeifSize(ReadOnlySpan<byte> data)
    {
        (long, long) largest = (0, 0);

        // meta is a full box: its version and flags come before its boxes.
        if (!Child(data, "meta"u8, out var meta) || meta.Length < 4 || !Child(meta[4..], "iprp"u8, out var properties) || !Child(properties, "ipco"u8, out var container))
        {
            return largest;
        }

        var at = 0;
        while (NextBox(container, ref at, out var type, out var body))
        {
            if (type.SequenceEqual("ispe"u8) && body.Length >= 12)
            {
                long width = BinaryPrimitives.ReadUInt32BigEndian(body[4..]), height = BinaryPrimitives.ReadUInt32BigEndian(body[8..]);
                if (width * height > largest.Item1 * largest.Item2)
                {
                    largest = (width, height);
                }
            }
        }

        return largest;
    }

    /// <summary>The body of the first box of <paramref name="wanted"/> type among <paramref name="boxes"/>; false when there is none.</summary>
    private static bool Child(ReadOnlySpan<byte> boxes, ReadOnlySpan<byte> wanted, out ReadOnlySpan<byte> found)
    {
        var at = 0;
        while (NextBox(boxes, ref at, out var type, out found))
        {
            if (type.SequenceEqual(wanted))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    /// <summary>Reads the box at <paramref name="at"/> and moves past it; false at the end or at a box that does not fit.</summary>
    private static bool NextBox(ReadOnlySpan<byte> boxes, scoped ref int at, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
    {
        type = body = default;
        if (at + 8 > boxes.Length)
        {
            return false;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(boxes[at..]);
        var header = 8;
        if (size == 1 && at + 16 <= boxes.Length)
        {
            size = BinaryPrimitives.ReadInt64BigEndian(boxes[(at + 8)..]);
            header = 16;
        }
        else if (size == 0)
        {
            size = boxes.Length - at;
        }

        if (size < header || at + size > boxes.Length)
        {
            return false;
        }

        type = boxes.Slice(at + 4, 4);
        body = boxes.Slice(at + header, (int)size - header);
        at += (int)size;
        return true;
    }
}
