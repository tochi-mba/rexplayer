using System.Globalization;

namespace Rex.Media.Library;

/// <summary>
/// Song details a person changed that rexplayer keeps itself, because the file could not (or should
/// not) be written to. A null field is not overridden; an empty one is cleared. The override is laid
/// over whatever the file's tags say each time they are read, so a rescan keeps it.
/// </summary>
public sealed record DetailOverride
{
    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Genre { get; init; }

    public string? Track { get; init; }

    public string? Disc { get; init; }

    public string? Year { get; init; }

    /// <summary>Whether the override changes nothing.</summary>
    public bool IsEmpty => this == new DetailOverride();

    /// <summary>This override with <paramref name="later"/>'s fields taking precedence.</summary>
    public DetailOverride Then(DetailOverride later)
    {
        ArgumentNullException.ThrowIfNull(later);
        return new DetailOverride
        {
            Title = later.Title ?? Title,
            Artist = later.Artist ?? Artist,
            Album = later.Album ?? Album,
            AlbumArtist = later.AlbumArtist ?? AlbumArtist,
            Genre = later.Genre ?? Genre,
            Track = later.Track ?? Track,
            Disc = later.Disc ?? Disc,
            Year = later.Year ?? Year,
        };
    }

    /// <summary>The entry with this override's fields laid over its own.</summary>
    public LibraryEntry ApplyTo(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry with
        {
            Title = Title is null ? entry.Title : Text(Title) ?? MediaLibrary.TitleOf(entry.Path),
            Artist = Artist is null ? entry.Artist : Text(Artist),
            // An artist the person typed replaces the file's list of artists as well.
            Artists = Artist is null ? entry.Artists : null,
            Album = Album is null ? entry.Album : Text(Album),
            AlbumArtist = AlbumArtist is null ? entry.AlbumArtist : Text(AlbumArtist),
            Genre = Genre is null ? entry.Genre : Text(Genre),
            Track = Track is null ? entry.Track : Number(Track),
            Disc = Disc is null ? entry.Disc : Number(Disc),
            Year = Year is null ? entry.Year : Number(Year) is { } year && year is >= 1000 and <= 9999 ? year : null,
        };
    }

    private static string? Text(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    /// <summary>The number a value starts with: "3/12" is 3, "2019-05-01" is 2019; null when there is none.</summary>
    private static int? Number(string value)
    {
        var digits = new string([.. value.Trim().TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    }
}
