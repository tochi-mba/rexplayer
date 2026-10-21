namespace Rex.Media.AppCore.Library;

/// <summary>One artist related to another, with what ties them: songs they share and genres in common.</summary>
public sealed record ArtistKin(LibraryGroup Artist, int SharedSongs, int SharedGenres)
{
    /// <summary>How close they are: a shared song counts three times a shared genre.</summary>
    public int Score => (SharedSongs * 3) + SharedGenres;
}

/// <summary>
/// Which other artists in the library are like one artist, for "Fans also like" and the artist
/// mixes: those they have songs with, then those sharing their genres (the songs' genre tags, and
/// any genres a lookup found for the artist). Only the library is read; nothing leaves the computer.
/// </summary>
public static class ArtistKinship
{
    /// <summary>
    /// The artists like <paramref name="artist"/>, closest first; ties go to the artist with more
    /// songs, then by name. An artist sharing nothing is not like them at all and is left out.
    /// </summary>
    /// <param name="artist">The artist, as <see cref="LibraryViews.Artists"/> groups them.</param>
    /// <param name="artists">Every artist in the library, grouped the same way.</param>
    /// <param name="moreGenres">Genres known for an artist beyond their songs' tags (from a lookup); null for none.</param>
    public static IReadOnlyList<ArtistKin> Like(LibraryGroup artist, IReadOnlyList<LibraryGroup> artists, Func<string, IReadOnlyList<string>>? moreGenres = null)
    {
        ArgumentNullException.ThrowIfNull(artist);
        ArgumentNullException.ThrowIfNull(artists);
        var key = ArtistNames.Key(artist.Name);
        var songs = artist.Entries.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var genres = Genres(artist, moreGenres);
        return [.. artists
            .Where(other => other.Name != LibraryViews.UnknownArtist && ArtistNames.Key(other.Name) != key)
            .Select(other => new ArtistKin(other, other.Entries.Count(entry => songs.Contains(entry.Path)), Genres(other, moreGenres).Count(genres.Contains)))
            .Where(kin => kin.Score > 0)
            .OrderByDescending(kin => kin.Score)
            .ThenByDescending(kin => kin.Artist.Entries.Count)
            .ThenBy(kin => kin.Artist.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static HashSet<string> Genres(LibraryGroup artist, Func<string, IReadOnlyList<string>>? moreGenres) =>
        artist.Entries.SelectMany(entry => GenreNames.Split(entry.Genre))
            .Concat(moreGenres?.Invoke(artist.Name) ?? [])
            .Select(genre => ArtistNames.Key(genre))
            .Where(genre => genre.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}
