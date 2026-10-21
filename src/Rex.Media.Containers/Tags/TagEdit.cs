// Spec: none. This file defines the edit a person makes to a song's details, not a published format.
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>
/// The details a person changed for one or more songs. A null field is left as it is (a field with
/// several values across a selection that nobody touched); an empty one is removed. Values joined
/// with <see cref="VorbisComments.Separator"/> are written as separate values where the format allows.
/// </summary>
public sealed record TagEdit
{
    /// <summary>The largest cover rexplayer writes into a file; the readers skip tags larger than 16 MiB.</summary>
    public const int MaxCoverBytes = 8 << 20;

    /// <summary>The fields a person can edit, in the order the dialog shows them.</summary>
    public static IReadOnlyList<string> EditableKeys { get; } =
    [
        MetadataKeys.Title, MetadataKeys.Artist, MetadataKeys.Album, MetadataKeys.AlbumArtist,
        MetadataKeys.Track, MetadataKeys.Disc, MetadataKeys.Date, MetadataKeys.Genre,
    ];

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Track { get; init; }

    public string? Disc { get; init; }

    public string? Date { get; init; }

    public string? Genre { get; init; }

    /// <summary>A new front cover, or null to keep the cover as it is.</summary>
    public PictureBlock? Cover { get; init; }

    /// <summary>Remove the front cover (ignored when <see cref="Cover"/> is set).</summary>
    public bool RemoveCover { get; init; }

    /// <summary>Whether the edit changes nothing.</summary>
    public bool IsEmpty => Cover is null && !RemoveCover && Fields.Count == 0;

    /// <summary>Whether the cover changes.</summary>
    public bool ChangesCover => Cover is not null || RemoveCover;

    /// <summary>The changed fields by metadata key, trimmed; an empty value means "remove".</summary>
    public IReadOnlyList<(string Key, string Value)> Fields
    {
        get
        {
            var result = new List<(string, string)>();
            foreach (var key in EditableKeys)
            {
                if (Value(key) is { } value)
                {
                    result.Add((key, value.Trim()));
                }
            }

            return result;
        }
    }

    /// <summary>The edit's value for a metadata key, or null when the key is unchanged or not editable.</summary>
    public string? Value(string key) => key switch
    {
        MetadataKeys.Title => Title,
        MetadataKeys.Artist => Artist,
        MetadataKeys.Album => Album,
        MetadataKeys.AlbumArtist => AlbumArtist,
        MetadataKeys.Track => Track,
        MetadataKeys.Disc => Disc,
        MetadataKeys.Date => Date,
        MetadataKeys.Genre => Genre,
        _ => null,
    };

    /// <summary>The values of a field: one per value joined with "; ", empty ones dropped.</summary>
    public static string[] Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Split(VorbisComments.Separator.Trim(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Throws <see cref="TagWriteException"/> when the edit cannot go into any file: a cover too large
    /// or with no image, or text holding characters a tag cannot carry.
    /// </summary>
    public void Validate()
    {
        if (Cover is { } cover && (cover.Data.Length == 0 || cover.Data.Length > MaxCoverBytes || cover.MimeType.Length == 0))
        {
            throw new TagWriteException("The cover must be an image of at most 8 MB.");
        }

        foreach (var (_, value) in Fields)
        {
            if (value.Contains('\0', StringComparison.Ordinal))
            {
                throw new TagWriteException("A detail holds a character tags cannot store.");
            }
        }
    }
}

/// <summary>A tag writer declining to write: the file's tags are damaged, or the edit cannot be stored safely.</summary>
public sealed class TagWriteException : Exception
{
    public TagWriteException()
    {
    }

    public TagWriteException(string message)
        : base(message)
    {
    }

    public TagWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
