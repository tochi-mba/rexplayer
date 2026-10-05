// Spec: ID3 tag version 2.2.0, 2.3.0 and 2.4.0 informal standards (id3.org): header, extended header, frame layout, unsynchronisation, text encodings, TXXX, COMM, USLT, APIC/PIC; ID3v2 Chapter Frame Addendum 1.0 (CHAP).
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>What an ID3v2 tag holds that rexplayer shows: text fields, pictures and chapters.</summary>
public sealed class Id3v2Tag
{
    public int Version { get; init; }

    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PictureBlock> Pictures { get; } = [];

    public List<Chapter> Chapters { get; } = [];
}

/// <summary>
/// Reads ID3v2 tags, the tag format in front of most MP3 files and some FLAC files. Every length is
/// checked against what is really there; a damaged frame is skipped and the rest still read, because
/// a tag must never stop a file from playing.
/// </summary>
public static class Id3v2
{
    public const int HeaderSize = 10;

    /// <summary>The whole tag's length (header, body and footer), or 0 if no tag starts here.</summary>
    public static long TagLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || header[0] != 'I' || header[1] != 'D' || header[2] != '3'
            || header[3] is < 2 or > 4 || header[4] == 0xFF || !IsSyncSafe(header[6..10]))
        {
            return 0;
        }

        var footer = header[3] == 4 && (header[5] & 0x10) != 0 ? 10 : 0;
        return HeaderSize + SyncSafe(header[6..10]) + footer;
    }

    /// <summary>The bytes taken by ID3v2 tags at the start of a source (taggers sometimes stack two).</summary>
    public static long LeadingTagsLength(Rex.Media.IO.IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        long offset = 0;
        Span<byte> header = stackalloc byte[HeaderSize];
        while (source.Read(offset, header, cancellationToken) == HeaderSize && TagLength(header) is var length and > 0)
        {
            offset += length;
        }

        return offset;
    }

    /// <summary>Reads a complete tag (as measured by <see cref="TagLength"/>), or null if it is not one.</summary>
    public static Id3v2Tag? Read(ReadOnlySpan<byte> tag)
    {
        var length = TagLength(tag);
        if (length == 0 || length > tag.Length)
        {
            return null;
        }

        var version = tag[3];
        var flags = tag[5];
        ReadOnlySpan<byte> body = tag[HeaderSize..(int)(HeaderSize + SyncSafe(tag[6..10]))];
        if (version < 4 && (flags & 0x80) != 0)
        {
            // Before 2.4 unsynchronisation applies to the whole tag at once.
            body = RemoveUnsynchronisation(body);
        }

        var offset = 0;
        if ((flags & 0x40) != 0 && version >= 3)
        {
            offset = ExtendedHeaderLength(body, version);
        }

        var result = new Id3v2Tag { Version = version };
        ReadFrames(body, offset, version, result.Metadata, result.Pictures, result.Chapters);
        return result;
    }

    internal static bool IsSyncSafe(ReadOnlySpan<byte> bytes) => (bytes[0] | bytes[1] | bytes[2] | bytes[3]) < 0x80;

    internal static int SyncSafe(ReadOnlySpan<byte> bytes) => (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];

    /// <summary>Undoes unsynchronisation: every 0xFF 0x00 pair becomes 0xFF.</summary>
    internal static byte[] RemoveUnsynchronisation(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        var length = 0;
        for (var i = 0; i < data.Length; i++)
        {
            result[length++] = data[i];
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0)
            {
                i++;
            }
        }

        return result[..length];
    }

    private static int ExtendedHeaderLength(ReadOnlySpan<byte> body, int version)
    {
        if (body.Length < 4)
        {
            return body.Length;
        }

        // 2.3 counts the bytes after the size field; 2.4 counts the whole extended header, sync-safe.
        var size = version == 3 ? (long)BinaryPrimitives.ReadUInt32BigEndian(body) + 4 : SyncSafe(body);
        return (int)Math.Min(size, body.Length);
    }

    private static void ReadFrames(ReadOnlySpan<byte> body, int offset, int version, Dictionary<string, string> metadata, List<PictureBlock> pictures, List<Chapter> chapters)
    {
        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        while (offset + headerLength <= body.Length && body[offset] != 0)
        {
            var id = Encoding.ASCII.GetString(body.Slice(offset, idLength));
            var size = version switch
            {
                2 => (body[offset + 3] << 16) | (body[offset + 4] << 8) | body[offset + 5],
                3 => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[(offset + 4)..]), int.MaxValue),
                _ => SyncSafe(body.Slice(offset + 4, 4)),
            };
            var formatFlags = version == 2 ? (byte)0 : body[offset + 9];
            offset += headerLength;
            if (size > body.Length - offset)
            {
                return;
            }

            var content = DecodeFrameBody(body.Slice(offset, size), version, formatFlags);
            offset += size;
            if (content is not null)
            {
                ReadFrame(id, content, version, metadata, pictures, chapters);
            }
        }
    }

    /// <summary>Undoes the per-frame encodings (2.3/2.4 compression and grouping, 2.4 unsynchronisation), or null for an encrypted frame.</summary>
    private static byte[]? DecodeFrameBody(ReadOnlySpan<byte> raw, int version, byte flags)
    {
        bool compressed, encrypted, grouped, unsynchronised, lengthIndicator;
        if (version == 3)
        {
            (compressed, encrypted, grouped, unsynchronised) = ((flags & 0x80) != 0, (flags & 0x40) != 0, (flags & 0x20) != 0, false);
            lengthIndicator = compressed;
        }
        else
        {
            (compressed, encrypted, grouped, unsynchronised) = ((flags & 0x08) != 0, (flags & 0x04) != 0, (flags & 0x40) != 0, (flags & 0x02) != 0);
            lengthIndicator = (flags & 0x01) != 0;
        }

        if (encrypted)
        {
            return null;
        }

        var skip = (grouped ? 1 : 0) + (lengthIndicator ? 4 : 0);
        if (skip > raw.Length)
        {
            return null;
        }

        var data = unsynchronised ? RemoveUnsynchronisation(raw[skip..]) : raw[skip..].ToArray();
        if (!compressed)
        {
            return data;
        }

        try
        {
            using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void ReadFrame(string id, byte[] content, int version, Dictionary<string, string> metadata, List<PictureBlock> pictures, List<Chapter> chapters)
    {
        if (content.Length == 0)
        {
            return;
        }

        switch (id)
        {
            case "TXXX" or "TXX":
                var (description, value) = SplitDescribed(content);
                if (description.Length > 0 && value.Length > 0)
                {
                    metadata[description.ToLowerInvariant()] = value;
                }

                return;
            case "COMM" or "COM" or "USLT" or "ULT":
                if (content.Length > 4)
                {
                    // A comment with a description is usually a tool's private data ("iTunNORM"); one
                    // without a description is what people typed, so it wins.
                    var (shortDescription, text) = SplitDescribed([content[0], .. content.AsSpan(4)]);
                    var isTyped = shortDescription.Length == 0 || !shortDescription.StartsWith("iTun", StringComparison.Ordinal);
                    if (text.Length > 0 && (id is "USLT" or "ULT" || shortDescription.Length == 0 || (isTyped && !metadata.ContainsKey(MetadataKeys.Comment))))
                    {
                        metadata[id is "USLT" or "ULT" ? MetadataKeys.Lyrics : MetadataKeys.Comment] = text;
                    }
                }

                return;
            case "APIC" or "PIC":
                if (ReadPicture(content, version) is { } picture)
                {
                    pictures.Add(picture);
                }

                return;
            case "CHAP":
                if (ReadChapter(content, version) is { } chapter)
                {
                    chapters.Add(chapter);
                }

                return;
        }

        if (id[0] == 'T' && TextKey(id) is { } key)
        {
            var text = string.Join(VorbisComments.Separator, DecodeText(content[0], content.AsSpan(1)).Split('\0', StringSplitOptions.RemoveEmptyEntries));
            if (key == MetadataKeys.Genre)
            {
                text = Id3v1.ExpandGenre(text);
            }

            if (text.Length > 0)
            {
                metadata[key] = text;
            }
        }
    }

    private static string? TextKey(string id) => id switch
    {
        "TIT2" or "TT2" => MetadataKeys.Title,
        "TPE1" or "TP1" => MetadataKeys.Artist,
        "TALB" or "TAL" => MetadataKeys.Album,
        "TPE2" or "TP2" => MetadataKeys.AlbumArtist,
        "TCON" or "TCO" => MetadataKeys.Genre,
        "TDRC" or "TYER" or "TYE" => MetadataKeys.Date,
        "TRCK" or "TRK" => MetadataKeys.Track,
        "TPOS" or "TPA" => MetadataKeys.Disc,
        "TCOM" or "TCM" => MetadataKeys.Composer,
        "TCOP" or "TCR" => MetadataKeys.Copyright,
        "TSSE" or "TENC" or "TSS" or "TEN" => MetadataKeys.Encoder,
        _ => null,
    };

    private static PictureBlock? ReadPicture(byte[] content, int version)
    {
        var encoding = content[0];
        var offset = 1;
        string mime;
        if (version == 2)
        {
            if (content.Length < 5)
            {
                return null;
            }

            mime = Encoding.ASCII.GetString(content, 1, 3).ToUpperInvariant() switch
            {
                "PNG" => "image/png",
                "JPG" => "image/jpeg",
                var other => "image/" + other.ToLowerInvariant(),
            };
            offset = 4;
        }
        else
        {
            var end = Array.IndexOf(content, (byte)0, 1);
            if (end < 0)
            {
                return null;
            }

            mime = Encoding.ASCII.GetString(content, 1, end - 1);
            offset = end + 1;
        }

        if (offset >= content.Length)
        {
            return null;
        }

        var type = content[offset++];
        var descriptionEnd = TerminatorEnd(encoding, content.AsSpan(offset));
        if (descriptionEnd < 0)
        {
            return null;
        }

        return new PictureBlock(type, mime, content[(offset + descriptionEnd)..]);
    }

    private static Chapter? ReadChapter(byte[] content, int version)
    {
        var idEnd = Array.IndexOf(content, (byte)0);
        if (idEnd < 0 || idEnd + 17 > content.Length)
        {
            return null;
        }

        var start = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(idEnd + 1));
        var title = Encoding.ASCII.GetString(content, 0, idEnd);
        var sub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadFrames(content.AsSpan(idEnd + 17), 0, version, sub, [], []);
        if (sub.TryGetValue(MetadataKeys.Title, out var named))
        {
            title = named;
        }

        return new Chapter(MediaTime.FromSeconds(start / 1000.0), title);
    }

    /// <summary>A NUL-terminated description and the text after it, both in the frame's encoding.</summary>
    private static (string Description, string Value) SplitDescribed(byte[] content)
    {
        var encoding = content[0];
        var rest = content.AsSpan(1);
        var end = TerminatorEnd(encoding, rest);
        if (end < 0)
        {
            return (DecodeText(encoding, rest), string.Empty);
        }

        var terminator = encoding is 1 or 2 ? 2 : 1;
        return (DecodeText(encoding, rest[..(end - terminator)]), DecodeText(encoding, rest[end..]).TrimEnd('\0'));
    }

    /// <summary>The index just past the first terminator (one NUL byte, or two aligned for UTF-16), or -1.</summary>
    private static int TerminatorEnd(byte encoding, ReadOnlySpan<byte> data)
    {
        if (encoding is 1 or 2)
        {
            for (var i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    return i + 2;
                }
            }

            return -1;
        }

        var index = data.IndexOf((byte)0);
        return index < 0 ? -1 : index + 1;
    }

    private static string DecodeText(byte encoding, ReadOnlySpan<byte> data)
    {
        var text = encoding switch
        {
            1 => DecodeUtf16WithBom(data),
            2 => Encoding.BigEndianUnicode.GetString(data),
            3 => Encoding.UTF8.GetString(data),
            _ => Encoding.Latin1.GetString(data),
        };
        return text.TrimEnd('\0').Trim();
    }

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(data[2..]);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            data = data[2..];
        }

        // UTF-16 values after a NUL separator in 2.4 may each carry their own BOM.
        return Encoding.Unicode.GetString(data).Replace("\0﻿", "\0", StringComparison.Ordinal);
    }
}

/// <summary>The fixed 128-byte ID3v1 tag at the end of older MP3 files, and its numbered genres.</summary>
public static class Id3v1
{
    public const int Size = 128;

    private static readonly string[] Genres =
    [
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal",
        "New Age", "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial",
        "Alternative", "Ska", "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk",
        "Fusion", "Trance", "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise",
        "Alternative Rock", "Bass", "Soul", "Punk", "Space", "Meditative", "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic",
        "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta",
        "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave", "Psychedelic", "Rave", "Showtunes",
        "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock",
    ];

    /// <summary>Whether the last 128 bytes of a file are an ID3v1 tag.</summary>
    public static bool IsTag(ReadOnlySpan<byte> last128) => last128.Length == Size && last128[0] == 'T' && last128[1] == 'A' && last128[2] == 'G';

    /// <summary>Adds the tag's fields to <paramref name="metadata"/> without overwriting richer ones already there.</summary>
    public static void Read(ReadOnlySpan<byte> tag, IDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!IsTag(tag))
        {
            return;
        }

        Add(metadata, MetadataKeys.Title, tag.Slice(3, 30));
        Add(metadata, MetadataKeys.Artist, tag.Slice(33, 30));
        Add(metadata, MetadataKeys.Album, tag.Slice(63, 30));
        Add(metadata, MetadataKeys.Date, tag.Slice(93, 4));
        var hasTrack = tag[125] == 0 && tag[126] != 0;
        Add(metadata, MetadataKeys.Comment, tag.Slice(97, hasTrack ? 28 : 30));
        if (hasTrack)
        {
            metadata.TryAdd(MetadataKeys.Track, tag[126].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (tag[127] < Genres.Length)
        {
            metadata.TryAdd(MetadataKeys.Genre, Genres[tag[127]]);
        }
    }

    /// <summary>Turns the 2.3 forms "(17)" and "(17)Rock" and the 2.4 form "17" into genre names.</summary>
    public static string ExpandGenre(string genre)
    {
        ArgumentNullException.ThrowIfNull(genre);
        var text = genre.Trim();
        if (text.StartsWith('(') && text.IndexOf(')', StringComparison.Ordinal) is var close and > 1)
        {
            var number = text[1..close];
            var after = text[(close + 1)..];
            return after.Length > 0 ? after : Name(number) ?? text;
        }

        return Name(text) ?? text;
    }

    private static string? Name(string number) =>
        int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < Genres.Length ? Genres[index] : null;

    private static void Add(IDictionary<string, string> metadata, string key, ReadOnlySpan<byte> field)
    {
        var text = Encoding.Latin1.GetString(field).TrimEnd('\0', ' ');
        var nul = text.IndexOf('\0', StringComparison.Ordinal);
        if (nul >= 0)
        {
            text = text[..nul];
        }

        if (text.Length > 0)
        {
            metadata.TryAdd(key, text);
        }
    }
}
