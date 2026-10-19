// Spec: ID3 tag version 2.3.0 and 2.4.0 informal standards (id3.org): header, extended header, frame header and flags, unsynchronisation, text frames (TIT2, TPE1, TALB, TPE2, TRCK, TPOS, TYER, TDRC, TCON), APIC; ID3 v1 layout (id3.org "ID3v1").
using System.Buffers.Binary;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>A rewritten tag and the values the app's own reader must find in it afterwards (null: absent).</summary>
public sealed record TagRewrite(byte[] Bytes, IReadOnlyDictionary<string, string?> Expected);

/// <summary>
/// Rewrites the ID3v2 tag in front of an MP3 file. Frames the edit does not touch are copied byte for
/// byte; the tag keeps its version (2.3 or 2.4; a file with no tag gets 2.4) and, when the new frames
/// fit, its length, so the audio does not move. Anything the writer does not fully understand (a 2.2
/// tag, unknown flags, a frame running past the tag, a size that is not sync-safe) is refused rather
/// than guessed at, because a guess could lose the person's other tags.
/// </summary>
public static class Id3v2Writer
{
    /// <summary>The padding added when a tag has to grow, so the next edit fits without moving the audio.</summary>
    public const int Padding = 1024;

    /// <summary>The largest tag written; the demuxers skip tags larger than 16 MiB.</summary>
    public const int MaxTagBytes = 16 << 20;

    /// <summary>
    /// The new tag for <paramref name="existing"/> (exactly one complete tag, or empty when the file has
    /// none) with <paramref name="edit"/> applied. Throws <see cref="TagWriteException"/> to refuse.
    /// </summary>
    public static TagRewrite Rewrite(ReadOnlySpan<byte> existing, TagEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Validate();
        var (version, oldBodyLength, kept) = existing.IsEmpty ? (4, 0, []) : Parse(existing);
        var dropped = DroppedIds(edit);
        var frames = new List<byte[]>();
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (edit.Cover is { } cover)
        {
            frames.Add(Frame(version, "APIC", PictureBody(cover)));
        }

        foreach (var (id, raw) in kept)
        {
            var isDroppedPicture = id == "APIC" && (edit.RemoveCover || (edit.Cover is not null && IsPlainFrontCover(raw)));
            if (!dropped.Contains(id) && !isDroppedPicture)
            {
                frames.Add(raw);
            }
        }

        foreach (var (key, value) in edit.Fields)
        {
            expected[key] = null;
            if (value.Length > 0)
            {
                var (id, text, shown) = TextFrame(version, key, value);
                frames.Add(Frame(version, id, text));
                expected[key] = shown;
            }
        }

        var framesLength = frames.Sum(frame => frame.Length);
        var bodyLength = framesLength <= oldBodyLength ? oldBodyLength : framesLength + Padding;
        if (Id3v2.HeaderSize + bodyLength > MaxTagBytes)
        {
            throw new TagWriteException("The details are too large to store in the file's tag.");
        }

        var tag = new byte[Id3v2.HeaderSize + bodyLength];
        "ID3"u8.CopyTo(tag);
        tag[3] = (byte)version;
        WriteSyncSafe(tag.AsSpan(6), bodyLength);
        var offset = Id3v2.HeaderSize;
        foreach (var frame in frames)
        {
            frame.CopyTo(tag, offset);
            offset += frame.Length;
        }

        return new TagRewrite(tag, expected);
    }

    /// <summary>The tag's version, body length and frames (id, whole frame), or a refusal.</summary>
    private static (int Version, int BodyLength, List<(string Id, byte[] Raw)> Frames) Parse(ReadOnlySpan<byte> tag)
    {
        var length = Id3v2.TagLength(tag);
        if (length == 0 || length != tag.Length)
        {
            throw new TagWriteException("The file's tag is damaged or there is more than one.");
        }

        var version = tag[3];
        var flags = tag[5];
        if (version == 2)
        {
            throw new TagWriteException("The file has an ID3v2.2 tag, which rexplayer does not rewrite.");
        }

        if ((flags & (version == 3 ? 0x1F : 0x0F)) != 0)
        {
            throw new TagWriteException("The file's tag uses flags rexplayer does not know.");
        }

        var size = Id3v2.SyncSafe(tag[6..10]);
        ReadOnlySpan<byte> body = tag.Slice(Id3v2.HeaderSize, size);
        if (version == 3 && (flags & 0x80) != 0)
        {
            body = Id3v2.RemoveUnsynchronisation(body);
        }

        var offset = (flags & 0x40) != 0 ? ExtendedHeaderLength(body, version) : 0;
        var frames = new List<(string, byte[])>();
        while (offset < body.Length && body[offset] != 0)
        {
            if (body.Length - offset < 10)
            {
                throw new TagWriteException("The file's tag ends in the middle of a frame.");
            }

            var header = body.Slice(offset, 10);
            var id = Encoding.ASCII.GetString(header[..4]);
            if (!IsFrameId(header[..4]) || (version == 4 && !Id3v2.IsSyncSafe(header[4..8])))
            {
                throw new TagWriteException("The file's tag holds a frame rexplayer cannot read.");
            }

            var frameSize = version == 3 ? BinaryPrimitives.ReadUInt32BigEndian(header[4..]) : (uint)Id3v2.SyncSafe(header[4..8]);
            if (frameSize > (uint)(body.Length - offset - 10))
            {
                throw new TagWriteException("A frame in the file's tag runs past the tag's end.");
            }

            frames.Add((id, body.Slice(offset, 10 + (int)frameSize).ToArray()));
            offset += 10 + (int)frameSize;
        }

        // A tag rewritten without unsynchronisation or an extended header keeps the same body size
        // only when nothing grows; the padding absorbs the difference.
        return (version, size, frames);
    }

    private static int ExtendedHeaderLength(ReadOnlySpan<byte> body, int version)
    {
        if (body.Length < 4)
        {
            throw new TagWriteException("The file's tag has a damaged extended header.");
        }

        // 2.3 counts the bytes after the size field; 2.4 counts the whole extended header, sync-safe.
        var size = version == 3 ? (long)BinaryPrimitives.ReadUInt32BigEndian(body) + 4 : Id3v2.SyncSafe(body);
        if (size > body.Length || size < 4)
        {
            throw new TagWriteException("The file's tag has a damaged extended header.");
        }

        return (int)size;
    }

    private static bool IsFrameId(ReadOnlySpan<byte> id)
    {
        foreach (var c in id)
        {
            if (c is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> DroppedIds(TagEdit edit)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in edit.Fields)
        {
            ids.UnionWith(key switch
            {
                MetadataKeys.Title => ["TIT2"],
                MetadataKeys.Artist => ["TPE1"],
                MetadataKeys.Album => ["TALB"],
                MetadataKeys.AlbumArtist => ["TPE2"],
                MetadataKeys.Track => ["TRCK"],
                MetadataKeys.Disc => ["TPOS"],
                MetadataKeys.Date => ["TDRC", "TYER"],
                _ => ["TCON"],
            });
        }

        return ids;
    }

    /// <summary>Whether a frame is an APIC frame with no format flags whose picture type is the front cover.</summary>
    private static bool IsPlainFrontCover(byte[] frame)
    {
        if (frame[9] != 0)
        {
            // A compressed or encrypted picture stays; the new cover is written first, so it wins.
            return false;
        }

        var mimeEnd = Array.IndexOf(frame, (byte)0, 11);
        return mimeEnd >= 0 && mimeEnd + 1 < frame.Length && frame[mimeEnd + 1] == PictureBlock.FrontCover;
    }

    /// <summary>The frame id, its body and the value the reader will show for one edited field.</summary>
    private static (string Id, byte[] Body, string Shown) TextFrame(int version, string key, string value)
    {
        var id = key switch
        {
            MetadataKeys.Title => "TIT2",
            MetadataKeys.Artist => "TPE1",
            MetadataKeys.Album => "TALB",
            MetadataKeys.AlbumArtist => "TPE2",
            MetadataKeys.Track => "TRCK",
            MetadataKeys.Disc => "TPOS",
            MetadataKeys.Date => version == 3 ? "TYER" : "TDRC",
            _ => "TCON",
        };
        if (version == 4)
        {
            // 2.4 separates values with NUL in UTF-8; the reader joins them back with "; ".
            var values = key is MetadataKeys.Artist or MetadataKeys.AlbumArtist or MetadataKeys.Genre ? TagEdit.Split(value) : [value];
            return (id, [3, .. Encoding.UTF8.GetBytes(string.Join('\0', values))], string.Join(VorbisComments.Separator, values));
        }

        if (id == "TYER")
        {
            // 2.3 stores only the year, always four digits.
            if (value.Length < 4 || !value[..4].All(char.IsAsciiDigit))
            {
                throw new TagWriteException("This file's tag stores only a four-digit year.");
            }

            value = value[..4];
        }

        return (id, [1, 0xFF, 0xFE, .. Encoding.Unicode.GetBytes(value)], value);
    }

    private static byte[] PictureBody(PictureBlock cover) =>
        [0, .. Encoding.Latin1.GetBytes(cover.MimeType), 0, PictureBlock.FrontCover, 0, .. cover.Data];

    private static byte[] Frame(int version, string id, byte[] body)
    {
        var frame = new byte[10 + body.Length];
        Encoding.ASCII.GetBytes(id, frame);
        if (version == 3)
        {
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)body.Length);
        }
        else
        {
            WriteSyncSafe(frame.AsSpan(4), body.Length);
        }

        body.CopyTo(frame, 10);
        return frame;
    }

    private static void WriteSyncSafe(Span<byte> destination, int value)
    {
        destination[0] = (byte)((value >> 21) & 0x7F);
        destination[1] = (byte)((value >> 14) & 0x7F);
        destination[2] = (byte)((value >> 7) & 0x7F);
        destination[3] = (byte)(value & 0x7F);
    }
}

/// <summary>Updates the fixed 128-byte ID3v1 tag at the end of an MP3 file so it agrees with an edit.</summary>
public static class Id3v1Writer
{
    /// <summary>The tag with the edit's title, artist, album, year, track and genre written in.</summary>
    public static byte[] Update(ReadOnlySpan<byte> tag, TagEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!Id3v1.IsTag(tag))
        {
            throw new TagWriteException("The file's ID3v1 tag is damaged.");
        }

        var result = tag.ToArray();
        foreach (var (key, value) in edit.Fields)
        {
            switch (key)
            {
                case MetadataKeys.Title:
                    Text(result.AsSpan(3, 30), value);
                    break;
                case MetadataKeys.Artist:
                    Text(result.AsSpan(33, 30), value);
                    break;
                case MetadataKeys.Album:
                    Text(result.AsSpan(63, 30), value);
                    break;
                case MetadataKeys.Date:
                    Text(result.AsSpan(93, 4), value);
                    break;
                case MetadataKeys.Track:
                    // ID3 v1.1: a zero byte ends a 28-byte comment and the next byte is the track.
                    var digits = new string(value.TakeWhile(char.IsAsciiDigit).ToArray());
                    var number = digits.Length is > 0 and <= 3 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    result[125] = 0;
                    result[126] = number <= 255 ? (byte)number : (byte)0;
                    break;
                case MetadataKeys.Genre:
                    var index = Id3v1.GenreNames.ToList().FindIndex(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
                    result[127] = index >= 0 ? (byte)index : (byte)255;
                    break;
            }
        }

        return result;
    }

    private static void Text(Span<byte> field, string value)
    {
        field.Clear();
        var bytes = Encoding.Latin1.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, field.Length)).CopyTo(field);
    }
}
