// Spec: Xiph.Org Vorbis I specification, section 5 "comment field and header specification", and the Xiph field-name recommendations it cites; RFC 9639 section 8.6 (the same block in FLAC).
using System.Buffers.Binary;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>
/// Reads a Vorbis comment block (FLAC, Ogg Vorbis, Opus): a vendor string and NAME=value fields.
/// Names are case-insensitive and may repeat; repeated values are joined with "; " so a track with
/// two artists shows both. Fields rexplayer has no canonical name for are kept under their own name.
/// </summary>
public static class VorbisComments
{
    /// <summary>Joins the values of a field that appears more than once.</summary>
    public const string Separator = "; ";

    /// <summary>
    /// Adds the block's fields to <paramref name="metadata"/>. A block that claims more bytes than it
    /// has keeps whatever fields were complete, because a tag must never stop a file from playing.
    /// </summary>
    public static void Read(ReadOnlySpan<byte> block, IDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var offset = 0;
        if (!TrySkipString(block, ref offset) || offset + 4 > block.Length)
        {
            return;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
        offset += 4;
        var raw = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < count && offset + 4 <= block.Length; i++)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
            offset += 4;
            if (length > (uint)(block.Length - offset))
            {
                break;
            }

            var field = Encoding.UTF8.GetString(block.Slice(offset, (int)length));
            offset += (int)length;
            var equals = field.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == field.Length - 1)
            {
                continue;
            }

            var name = field[..equals];
            if (!raw.TryGetValue(name, out var values))
            {
                raw[name] = values = [];
            }

            values.Add(field[(equals + 1)..]);
        }

        foreach (var (name, values) in raw)
        {
            metadata[CanonicalName(name)] = string.Join(Separator, values);
        }

        if (raw.TryGetValue("TRACKTOTAL", out var totals) && metadata.TryGetValue(MetadataKeys.Track, out var track) && !track.Contains('/', StringComparison.Ordinal))
        {
            metadata[MetadataKeys.Track] = track + "/" + totals[0];
        }
    }

    /// <summary>The canonical metadata key for a Vorbis field name, or the name in lower case.</summary>
    public static string CanonicalName(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.ToUpperInvariant() switch
        {
            "TITLE" => MetadataKeys.Title,
            "ARTIST" => MetadataKeys.Artist,
            "ALBUM" => MetadataKeys.Album,
            "ALBUMARTIST" or "ALBUM ARTIST" or "ALBUM_ARTIST" => MetadataKeys.AlbumArtist,
            "GENRE" => MetadataKeys.Genre,
            "DATE" or "YEAR" => MetadataKeys.Date,
            "TRACKNUMBER" => MetadataKeys.Track,
            "DISCNUMBER" => MetadataKeys.Disc,
            "COMMENT" or "DESCRIPTION" => MetadataKeys.Comment,
            "COMPOSER" => MetadataKeys.Composer,
            "COPYRIGHT" => MetadataKeys.Copyright,
            "ENCODER" or "ENCODED-BY" => MetadataKeys.Encoder,
            "LYRICS" or "UNSYNCEDLYRICS" => MetadataKeys.Lyrics,
            _ => field.ToLowerInvariant(),
        };
    }

    private static bool TrySkipString(ReadOnlySpan<byte> block, ref int offset)
    {
        if (offset + 4 > block.Length)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
        if (length > (uint)(block.Length - offset - 4))
        {
            return false;
        }

        offset += 4 + (int)length;
        return true;
    }
}
