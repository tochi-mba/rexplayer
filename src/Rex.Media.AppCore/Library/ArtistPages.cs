using System.Globalization;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>An artist's album with the release year its songs carry, for the artist page.</summary>
public sealed record ArtistAlbum(LibraryGroup Album, int? Year);

/// <summary>
/// What an artist's page shows (LIB-05): the songs of theirs played the most, then their albums
/// with the newest first, the way a streaming-style artist screen reads. Both are plain queries
/// over the artist's songs; the window only lays them out.
/// </summary>
public static class ArtistPages
{
    /// <summary>How many top songs the page lists at most.</summary>
    public const int TopSongCount = 10;

    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary>
    /// The artist's most played songs, the most played first; only songs actually played count,
    /// so a fresh library simply has no top songs yet. Ties keep the songs' view order.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> TopSongs(LibraryGroup artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return [.. artist.Entries.Where(entry => entry.Plays > 0)
            .OrderByDescending(entry => entry.Plays)
            .Take(TopSongCount)];
    }

    /// <summary>
    /// The artist's albums, the newest year first; albums without a year after those with one,
    /// by name, and songs filed under no album last of all. An album's year is the latest its
    /// songs carry, so a deluxe reissue's bonus tracks do not pull the album back in time.
    /// </summary>
    public static IReadOnlyList<ArtistAlbum> Albums(LibraryGroup artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return [.. LibraryViews.Albums(artist.Entries)
            .Select(album => new ArtistAlbum(album, album.Entries.Max(entry => entry.Year)))
            .OrderBy(album => album.Album.Name == LibraryViews.UnknownAlbum)
            .ThenBy(album => album.Year is null)
            .ThenByDescending(album => album.Year)
            .ThenBy(album => album.Album.Name, ByName)];
    }
}
