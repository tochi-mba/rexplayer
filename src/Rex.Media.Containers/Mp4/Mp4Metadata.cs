// Spec: Apple QuickTime File Format "Metadata" (the ilst item list, data atoms and their type indicators, freeform "----" items with mean and name); the Nero "chpl" chapter list as written by its tools; ID3v1 genre numbering for "gnre".
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Rex.Media.Containers.Tags;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Mp4;

/// <summary>Tags, cover art and chapters as MP4 and M4A files store them.</summary>
internal static class Mp4Metadata
{
    private static readonly string Mark = ((char)0xA9).ToString();

    private static readonly Dictionary<string, string> TextItems = new(StringComparer.Ordinal)
    {
        [Mark + "nam"] = MetadataKeys.Title,
        [Mark + "ART"] = MetadataKeys.Artist,
        ["aART"] = MetadataKeys.AlbumArtist,
        [Mark + "alb"] = MetadataKeys.Album,
        [Mark + "gen"] = MetadataKeys.Genre,
        [Mark + "day"] = MetadataKeys.Date,
        [Mark + "cmt"] = MetadataKeys.Comment,
        [Mark + "wrt"] = MetadataKeys.Composer,
        ["cprt"] = MetadataKeys.Copyright,
        [Mark + "too"] = MetadataKeys.Encoder,
        [Mark + "lyr"] = MetadataKeys.Lyrics,
    };

    /// <summary>Reads an ilst box's items into <paramref name="metadata"/>; returns any cover art.</summary>
    public static byte[]? ReadItems(ReadOnlySpan<byte> ilst, IDictionary<string, string> metadata)
    {
        byte[]? cover = null;
        foreach (var item in Mp4Boxes.Children(ilst))
        {
            var body = ilst[item.Start..item.End];
            if (Mp4Boxes.Find(body, "data") is not { } data || data.Length < 8)
            {
                continue;
            }

            var value = body[(data.Start + 8)..data.End];
            switch (item.Type)
            {
                case "trkn" or "disk" when value.Length >= 6:
                    var number = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
                    var total = BinaryPrimitives.ReadUInt16BigEndian(value[4..]);
                    if (number > 0)
                    {
                        metadata[item.Type == "trkn" ? MetadataKeys.Track : MetadataKeys.Disc] = total > 0
                            ? string.Create(CultureInfo.InvariantCulture, $"{number}/{total}")
                            : number.ToString(CultureInfo.InvariantCulture);
                    }

                    break;
                case "gnre" when value.Length >= 2:
                    var genre = BinaryPrimitives.ReadUInt16BigEndian(value) - 1;
                    var name = Id3v1.ExpandGenre(genre.ToString(CultureInfo.InvariantCulture));
                    if (genre >= 0 && name != genre.ToString(CultureInfo.InvariantCulture))
                    {
                        metadata.TryAdd(MetadataKeys.Genre, name);
                    }

                    break;
                case "covr":
                    cover ??= value.ToArray();
                    break;
                case "----":
                    if (Mp4Boxes.Find(body, "name") is { } nameBox && nameBox.Length > 4)
                    {
                        var key = Encoding.UTF8.GetString(body[(nameBox.Start + 4)..nameBox.End]).ToLowerInvariant();
                        metadata[key] = Encoding.UTF8.GetString(value).Trim();
                    }

                    break;
                default:
                    // Item types start with the copyright sign (0xA9), which box types in general may not
                    // carry, so the four bytes are read as Latin-1 here rather than through FourCC.
                    if (TextItems.TryGetValue(Encoding.Latin1.GetString(ilst.Slice(item.Start - 4, 4)), out var textKey))
                    {
                        var text = Encoding.UTF8.GetString(value).Trim('\0', ' ');
                        if (text.Length > 0)
                        {
                            metadata[textKey] = text;
                        }
                    }

                    break;
            }
        }

        return cover;
    }

    /// <summary>
    /// The gapless figures an iTunes encoder leaves in "iTunSMPB": encoder delay, padding and the
    /// real sample count, as hexadecimal words; null when absent or unreadable.
    /// </summary>
    public static (int Delay, int Padding, long Samples)? Gapless(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("itunsmpb", out var value))
        {
            return null;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4
            || !int.TryParse(words[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var delay)
            || !int.TryParse(words[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var padding)
            || !long.TryParse(words[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var samples))
        {
            return null;
        }

        return (delay, padding, samples);
    }

    /// <summary>Reads a Nero chpl box: 100-nanosecond start times and titles.</summary>
    public static List<Chapter> ReadChapterList(ReadOnlySpan<byte> chpl)
    {
        var chapters = new List<Chapter>();
        if (chpl.Length < 5)
        {
            return chapters;
        }

        var at = chpl[0] == 1 ? 8 : 4;
        if (at >= chpl.Length)
        {
            return chapters;
        }

        var count = chpl[at++];
        for (var i = 0; i < count && at + 9 <= chpl.Length; i++)
        {
            var start = BinaryPrimitives.ReadInt64BigEndian(chpl[at..]);
            var length = chpl[at + 8];
            at += 9;
            if (at + length > chpl.Length)
            {
                break;
            }

            chapters.Add(new Chapter(new MediaTime(start), Encoding.UTF8.GetString(chpl.Slice(at, length))));
            at += length;
        }

        return chapters;
    }
}
