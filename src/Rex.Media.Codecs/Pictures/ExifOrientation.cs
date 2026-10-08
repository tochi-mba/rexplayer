// Spec: Exif 2.32 (CIPA DC-008-2019): APP1 "Exif" segment, TIFF header and 0th IFD, Orientation tag (0x0112); TIFF 6.0 (Adobe 1992) for the IFD layout; JPEG (ITU-T T.81 Annex B) for marker segments.
using System.Buffers.Binary;

namespace Rex.Media.Codecs.Pictures;

/// <summary>
/// Which way up a photo was taken (FMT-C19): the Exif orientation a camera writes instead of turning
/// the picture itself. 1 is as stored; 2 to 8 say how to turn and mirror it to show it upright.
/// </summary>
public static class ExifOrientation
{
    /// <summary>The orientation of a JPEG (its Exif segment) or a TIFF (its first directory); 1 when it does not say.</summary>
    public static int Of(ReadOnlySpan<byte> picture)
    {
        if (picture.Length >= 4 && (picture.StartsWith("II*\0"u8) || picture.StartsWith("MM\0*"u8)))
        {
            return FromTiff(picture);
        }

        if (picture.Length < 4 || picture[0] != 0xFF || picture[1] != 0xD8)
        {
            return 1;
        }

        // The Exif segment comes among the first segments, before the picture's data.
        var at = 2;
        while (at + 4 <= picture.Length && picture[at] == 0xFF)
        {
            var marker = picture[at + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(picture[(at + 2)..]);
            if (marker is 0xDA or 0xD9 || length < 2)
            {
                break;
            }

            var body = picture.Slice(at + 4, Math.Min(length - 2, picture.Length - at - 4));
            if (marker == 0xE1 && body.StartsWith("Exif\0\0"u8))
            {
                return FromTiff(body[6..]);
            }

            at += 2 + length;
        }

        return 1;
    }

    /// <summary>The Orientation tag of a TIFF structure's first directory; 1 when it has none or it makes no sense.</summary>
    private static int FromTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return 1;
        }

        var little = tiff[0] == (byte)'I';
        var directory = little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (directory + 2L > tiff.Length)
        {
            return 1;
        }

        var entries = U16(tiff, (int)directory, little);
        for (var i = 0; i < entries; i++)
        {
            var at = (int)directory + 2 + (12 * i);
            if (at + 12 > tiff.Length)
            {
                break;
            }

            // A SHORT, held in the entry's value field.
            if (U16(tiff, at, little) == 0x0112 && U16(tiff, at + 2, little) == 3)
            {
                var orientation = U16(tiff, at + 8, little);
                return orientation is >= 1 and <= 8 ? orientation : 1;
            }
        }

        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> tiff, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[at..]);
}
