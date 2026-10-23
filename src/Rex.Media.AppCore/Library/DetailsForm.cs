using System.Globalization;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Library;

/// <summary>One box of the Edit details dialog: what it holds now, or that the songs disagree.</summary>
public sealed record DetailField(string Key, string Label, string Value, bool Several);

/// <summary>
/// The Edit details dialog's logic for one song or many: each field shows the songs' shared value,
/// or "(several values)" when they differ, and only fields the person changed are saved.
/// </summary>
public static class DetailsForm
{
    /// <summary>What a box shows when the chosen songs hold different values.</summary>
    public const string SeveralValues = "(several values)";

    private static readonly (string Key, string Label)[] Layout =
    [
        (MetadataKeys.Title, "Title"),
        (MetadataKeys.Artist, "Artist"),
        (MetadataKeys.Album, "Album"),
        (MetadataKeys.AlbumArtist, "Album artist"),
        (MetadataKeys.Track, "Track"),
        (MetadataKeys.Disc, "Disc"),
        (MetadataKeys.Date, "Year"),
        (MetadataKeys.Genre, "Genre"),
    ];

    /// <summary>
    /// What the dialog says about where the details go: into the files, or kept in rexplayer, and
    /// why. A file still downloading (<paramref name="downloading"/> of them) is never written into.
    /// </summary>
    public static string Note(int songs, int writable, int downloading, bool writing)
    {
        if (!writing)
        {
            return "Saved in rexplayer only, as Preferences asks; the files are not changed.";
        }

        if (writable == songs)
        {
            return "Saved into the files.";
        }

        if (downloading == songs)
        {
            return songs == 1
                ? "This file is still downloading, so its details are saved in rexplayer only until it is whole."
                : "These files are still downloading, so their details are saved in rexplayer only until they are whole.";
        }

        if (writable == 0 && downloading == 0)
        {
            return Containers.Tags.TagFileWriter.LibraryOnlyMessage;
        }

        var held = songs - writable;
        return downloading > 0
            ? $"{held} of these files can't be written to ({downloading} still downloading); their details are saved in rexplayer only."
            : $"{held} of these files can't be written to; their details are saved in rexplayer only.";
    }

    /// <summary>The dialog's fields for <paramref name="entries"/> (at least one song).</summary>
    public static IReadOnlyList<DetailField> Fields(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
        return [.. Layout.Select(field =>
        {
            var values = entries.Select(entry => ValueOf(entry, field.Key)).Distinct(StringComparer.Ordinal).ToList();
            return new DetailField(field.Key, field.Label, values.Count == 1 ? values[0] : "", values.Count > 1);
        })];
    }

    /// <summary>
    /// The change the person made: <paramref name="typed"/> holds each box's text by key. A box left
    /// as it was (or left empty over several values) changes nothing; an emptied box clears the field.
    /// </summary>
    public static DetailOverride Change(IReadOnlyList<DetailField> fields, IReadOnlyDictionary<string, string> typed)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(typed);
        string? Changed(string key)
        {
            var field = fields.First(candidate => candidate.Key == key);
            var text = typed.GetValueOrDefault(key, field.Value).Trim();
            return text == field.Value || (field.Several && text.Length == 0) ? null : text;
        }

        return new DetailOverride
        {
            Title = Changed(MetadataKeys.Title),
            Artist = Changed(MetadataKeys.Artist),
            Album = Changed(MetadataKeys.Album),
            AlbumArtist = Changed(MetadataKeys.AlbumArtist),
            Track = Changed(MetadataKeys.Track),
            Disc = Changed(MetadataKeys.Disc),
            Year = Changed(MetadataKeys.Date),
            Genre = Changed(MetadataKeys.Genre),
        };
    }

    /// <summary>
    /// Errors that would make changed numeric tags ambiguous or destructive.
    /// Empty values intentionally mean "clear this tag"; untouched mixed
    /// values are never included in a DetailOverride.
    /// </summary>
    public static IReadOnlyList<string> Problems(DetailOverride change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var errors = new List<string>();
        bool Numbered(string? value)
        {
            if (value is null or "")
            {
                return true;
            }

            var pieces = value.Split('/', 2);
            if (!int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index is < 1 or > 9999)
            {
                return false;
            }

            return pieces.Length == 1 || int.TryParse(pieces[1], NumberStyles.None,
                CultureInfo.InvariantCulture, out var total) && total is >= 1 and <= 9999 && index <= total;
        }

        if (!Numbered(change.Track))
        {
            errors.Add("Track number must be positive, optionally followed by /total.");
        }

        if (!Numbered(change.Disc))
        {
            errors.Add("Disc number must be positive, optionally followed by /total.");
        }

        if (change.Year is { Length: > 0 } year
            && !DateTime.TryParseExact(year, ["yyyy", "yyyy-MM", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("Date must be a year, year-month or ISO date.");
        }

        return errors;
    }

    /// <summary>A file's metadata with an edit laid over it: a cleared field is dropped.</summary>
    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> metadata, DetailOverride change)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(change);
        var result = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in new[]
        {
            (MetadataKeys.Title, change.Title), (MetadataKeys.Artist, change.Artist), (MetadataKeys.Album, change.Album),
            (MetadataKeys.AlbumArtist, change.AlbumArtist), (MetadataKeys.Track, change.Track), (MetadataKeys.Disc, change.Disc),
            (MetadataKeys.Date, change.Year), (MetadataKeys.Genre, change.Genre),
        })
        {
            if (value is { Length: > 0 })
            {
                result[key] = value;
            }
            else if (value is not null)
            {
                result.Remove(key);
            }
        }

        return result;
    }

    private static string ValueOf(LibraryEntry entry, string key) => key switch
    {
        MetadataKeys.Title => entry.Title,
        MetadataKeys.Artist => entry.Artist ?? "",
        MetadataKeys.Album => entry.Album ?? "",
        MetadataKeys.AlbumArtist => entry.AlbumArtist ?? "",
        MetadataKeys.Track => Number(entry.Track),
        MetadataKeys.Disc => Number(entry.Disc),
        MetadataKeys.Date => Number(entry.Year),
        _ => entry.Genre ?? "",
    };

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
