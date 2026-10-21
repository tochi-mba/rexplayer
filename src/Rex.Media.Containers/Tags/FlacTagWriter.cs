// Spec: IETF RFC 9639 (FLAC), section 8 "File-level metadata": block header, STREAMINFO first, PADDING, VORBIS_COMMENT (8.6) and PICTURE (8.8); section 9.1 frame sync code; Xiph.Org Vorbis I specification section 5 (comment fields).
using System.Buffers.Binary;
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>
/// Rewrites a FLAC file's metadata blocks: the Vorbis comments take the edit and the front-cover
/// PICTURE is replaced; every other block (stream information, seek table, cue sheet, application
/// data, other pictures) is copied byte for byte. When the new blocks fit in the old ones the padding
/// shrinks so the audio does not move. A damaged block is refused, never repaired by guesswork.
/// </summary>
public static class FlacTagWriter
{
    /// <summary>The padding added when the metadata has to grow, so the next edit fits.</summary>
    public const int Padding = 8192;

    /// <summary>The most metadata read and rewritten (several 16 MiB pictures is not a song).</summary>
    public const int MaxMetadataBytes = 64 << 20;

    private const int StreamInfo = 0;
    private const int PaddingType = 1;
    private const int VorbisComment = 4;
    private const int Picture = 6;
    private const int MaxBlockLength = (1 << 24) - 1;
    private const string Vendor = "rexplayer";

    /// <summary>
    /// The length of the metadata (from "fLaC" to the first frame), checking that a frame really
    /// starts where it ends. Throws <see cref="TagWriteException"/> to refuse.
    /// </summary>
    public static long MetadataLength(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var length = source.Length ?? 0;
        Span<byte> header = stackalloc byte[4];
        if (source.Read(0, header, cancellationToken) < 4 || !header.SequenceEqual("fLaC"u8))
        {
            throw new TagWriteException("The file does not start with FLAC metadata (a tag in front of it, perhaps).");
        }

        long offset = 4;
        var last = false;
        while (!last)
        {
            if (source.Read(offset, header, cancellationToken) < 4)
            {
                throw new TagWriteException("The file's metadata ends before its last block.");
            }

            last = (header[0] & 0x80) != 0;
            offset += 4 + ((header[1] << 16) | (header[2] << 8) | header[3]);
            if (offset > length || offset > MaxMetadataBytes)
            {
                throw new TagWriteException("A metadata block runs past the end of the file.");
            }
        }

        Span<byte> sync = stackalloc byte[2];
        if (source.Read(offset, sync, cancellationToken) < 2 || sync[0] != 0xFF || (sync[1] & 0xFE) != 0xF8)
        {
            throw new TagWriteException("The audio does not start where the file's metadata ends.");
        }

        return offset;
    }

    /// <summary>The new metadata for <paramref name="metadata"/> (from "fLaC" to the first frame) with the edit applied.</summary>
    public static TagRewrite Rewrite(ReadOnlySpan<byte> metadata, TagEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Validate();
        var blocks = Parse(metadata);
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var output = new List<(int Type, byte[] Body)> { blocks[0] };
        if (edit.Cover is { } cover)
        {
            output.Add((Picture, PictureBody(cover)));
        }

        var comments = blocks.Where(block => block.Type == VorbisComment).ToList();
        if (comments.Count > 1)
        {
            throw new TagWriteException("The file has more than one comment block.");
        }

        var commentBlock = Comments(comments.Count == 1 ? comments[0].Body : null, edit, expected);
        output.Add((VorbisComment, commentBlock));
        foreach (var (type, body) in blocks.Skip(1))
        {
            var dropPicture = type == Picture && (edit.RemoveCover || (edit.Cover is not null && PictureBlock.Parse(body)!.Type == PictureBlock.FrontCover));
            if (type is not (PaddingType or VorbisComment) && !dropPicture)
            {
                output.Add((type, body));
            }
        }

        var used = output.Sum(block => 4 + block.Body.Length) + 4;
        var spare = metadata.Length - used - 4;
        output.Add((PaddingType, new byte[spare >= 0 ? spare : Padding]));
        using var stream = new MemoryStream();
        stream.Write("fLaC"u8);
        for (var i = 0; i < output.Count; i++)
        {
            var (type, body) = output[i];
            if (body.Length > MaxBlockLength)
            {
                throw new TagWriteException("A metadata block would be too large.");
            }

            stream.WriteByte((byte)(type | (i == output.Count - 1 ? 0x80 : 0)));
            stream.WriteByte((byte)(body.Length >> 16));
            stream.WriteByte((byte)(body.Length >> 8));
            stream.WriteByte((byte)body.Length);
            stream.Write(body);
        }

        return new TagRewrite(stream.ToArray(), expected);
    }

    private static List<(int Type, byte[] Body)> Parse(ReadOnlySpan<byte> metadata)
    {
        if (metadata.Length < 8 || !metadata.StartsWith("fLaC"u8) || (metadata[4] & 0x7F) != StreamInfo)
        {
            throw new TagWriteException("The file's metadata does not start with stream information.");
        }

        var blocks = new List<(int, byte[])>();
        var offset = 4;
        var last = false;
        while (!last)
        {
            if (metadata.Length - offset < 4)
            {
                throw new TagWriteException("The file's metadata ends before its last block.");
            }

            var type = metadata[offset] & 0x7F;
            last = (metadata[offset] & 0x80) != 0;
            var length = (metadata[offset + 1] << 16) | (metadata[offset + 2] << 8) | metadata[offset + 3];
            offset += 4;
            if (length > metadata.Length - offset || type == 127 || (type == StreamInfo) != (blocks.Count == 0))
            {
                throw new TagWriteException("A metadata block in the file is damaged.");
            }

            var body = metadata.Slice(offset, length).ToArray();
            if (type == Picture && PictureBlock.Parse(body) is null)
            {
                throw new TagWriteException("A picture in the file is damaged.");
            }

            blocks.Add((type, body));
            offset += length;
        }

        if (offset != metadata.Length)
        {
            throw new TagWriteException("The file's metadata does not end where its last block ends.");
        }

        return blocks;
    }

    /// <summary>The comment block with the edit applied: untouched fields keep their order and spelling.</summary>
    private static byte[] Comments(byte[]? block, TagEdit edit, Dictionary<string, string?> expected)
    {
        var (vendor, fields) = block is null ? (Encoding.UTF8.GetBytes(Vendor), []) : ParseComments(block);
        var edited = edit.Fields;
        var kept = fields.Where(field => !IsReplaced(field, edit)).ToList();
        foreach (var (key, value) in edited)
        {
            expected[key] = null;
            if (value.Length > 0)
            {
                var values = key is MetadataKeys.Artist or MetadataKeys.AlbumArtist or MetadataKeys.Genre ? TagEdit.Split(value) : [value];
                kept.AddRange(values.Select(item => Encoding.UTF8.GetBytes(FieldName(key) + "=" + item)));
                expected[key] = values.Length > 0 ? string.Join(VorbisComments.Separator, values) : null;
            }
        }

        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(number, (uint)vendor.Length);
        stream.Write(number);
        stream.Write(vendor);
        BinaryPrimitives.WriteUInt32LittleEndian(number, (uint)kept.Count);
        stream.Write(number);
        foreach (var field in kept)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(number, (uint)field.Length);
            stream.Write(number);
            stream.Write(field);
        }

        return stream.ToArray();
    }

    /// <summary>The vendor string and every field, read strictly: a length that does not fit is a refusal.</summary>
    private static (byte[] Vendor, List<byte[]> Fields) ParseComments(byte[] block)
    {
        var offset = 0;
        var vendor = Next(block, ref offset);
        var count = Count(block, ref offset);
        var fields = new List<byte[]>();
        for (uint i = 0; i < count; i++)
        {
            fields.Add(Next(block, ref offset));
        }

        return (vendor, fields);
    }

    private static uint Count(byte[] block, ref int offset)
    {
        if (block.Length - offset < 4)
        {
            throw new TagWriteException("The file's comment block is damaged.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset));
        offset += 4;
        return value;
    }

    private static byte[] Next(byte[] block, ref int offset)
    {
        var length = Count(block, ref offset);
        if (length > (uint)(block.Length - offset))
        {
            throw new TagWriteException("The file's comment block is damaged.");
        }

        var bytes = block.AsSpan(offset, (int)length).ToArray();
        offset += (int)length;
        return bytes;
    }

    /// <summary>Whether a stored field is replaced by the edit (including totals and pictures kept in comments).</summary>
    private static bool IsReplaced(byte[] field, TagEdit edit)
    {
        var equals = Array.IndexOf(field, (byte)'=');
        if (equals <= 0)
        {
            return false;
        }

        var name = Encoding.UTF8.GetString(field, 0, equals).ToUpperInvariant();
        if (name == "METADATA_BLOCK_PICTURE")
        {
            return edit.ChangesCover;
        }

        var key = name switch
        {
            "TRACKTOTAL" or "TOTALTRACKS" => MetadataKeys.Track,
            "DISCTOTAL" or "TOTALDISCS" => MetadataKeys.Disc,
            _ => VorbisComments.CanonicalName(name),
        };
        return edit.Value(key) is not null;
    }

    private static string FieldName(string key) => key switch
    {
        MetadataKeys.Title => "TITLE",
        MetadataKeys.Artist => "ARTIST",
        MetadataKeys.Album => "ALBUM",
        MetadataKeys.AlbumArtist => "ALBUMARTIST",
        MetadataKeys.Track => "TRACKNUMBER",
        MetadataKeys.Disc => "DISCNUMBER",
        MetadataKeys.Date => "DATE",
        _ => "GENRE",
    };

    private static byte[] PictureBody(PictureBlock cover)
    {
        var mime = Encoding.ASCII.GetBytes(cover.MimeType);
        var body = new byte[4 + 4 + mime.Length + 4 + 16 + 4 + cover.Data.Length];
        var span = body.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, PictureBlock.FrontCover);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)mime.Length);
        mime.CopyTo(span[8..]);
        var offset = 8 + mime.Length + 4 + 16;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)cover.Data.Length);
        cover.Data.CopyTo(span[(offset + 4)..]);
        return body;
    }
}
