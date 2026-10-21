using System.Globalization;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>An artist's album with the release year its songs carry, for the artist page.</summary>
public sealed record ArtistAlbum(LibraryGroup Album, int? Year);

/// <summary>
/// What an artist's page shows (LIB-05): the songs of theirs played the most, then their albums
/// with the newest first, then other artists' albums they are featured on, the way a
/// streaming-style artist screen reads. All are plain queries over the artist's songs, which take
/// in the songs they are a featured or second artist on; the window only lays them out.
/// </summary>
public static class ArtistPages
{
    /// <summary>How many top songs the page lists at most.</summary>
    public const int TopSongCount = 10;

    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary>
    /// The artist's most played songs, the most played first; only songs actually played count,
    /// so a fresh library simply has no top songs yet. Ties keep the songs' view order. Long mixes
    /// and sets (<see cref="AudioRules.IsLongMix"/>) are not ranked.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> TopSongs(LibraryGroup artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return [.. AudioRules.Ranked(artist.Entries).Where(entry => entry.Plays > 0)
            .OrderByDescending(entry => entry.Plays)
            .Take(TopSongCount)];
    }

    /// <summary>
    /// The artist's albums, the newest year first; albums without a year after those with one,
    /// by name, and songs filed under no album last of all. An album's year is the latest its
    /// songs carry, so a deluxe reissue's bonus tracks do not pull the album back in time. An album
    /// is theirs when the artist it is filed under names them ("A$AP Rocky/Brent Faiyaz" is both's).
    /// </summary>
    public static IReadOnlyList<ArtistAlbum> Albums(LibraryGroup artist) => Arranged(artist, theirs: true);

    /// <summary>
    /// Other artists' albums with songs the artist is on, in the same order as <see cref="Albums"/>;
    /// each holds only those songs.
    /// </summary>
    public static IReadOnlyList<ArtistAlbum> FeaturedOn(LibraryGroup artist) => Arranged(artist, theirs: false);

    private static List<ArtistAlbum> Arranged(LibraryGroup artist, bool theirs)
    {
        ArgumentNullException.ThrowIfNull(artist);
        var names = ArtistNames.Learn(artist.Entries);
        var key = ArtistNames.Key(artist.Name);
        return [.. LibraryViews.Albums(artist.Entries)
            .Where(album => (album.Detail is null || album.Name == LibraryViews.UnknownAlbum
                || names.Split(album.Detail).Any(name => names.KeyOf(name) == key)) == theirs)
            .Select(album => new ArtistAlbum(album, album.Entries.Max(entry => entry.Year)))
            .OrderBy(album => album.Album.Name == LibraryViews.UnknownAlbum)
            .ThenBy(album => album.Year is null)
            .ThenByDescending(album => album.Year)
            .ThenBy(album => album.Album.Name, ByName)];
    }
}
