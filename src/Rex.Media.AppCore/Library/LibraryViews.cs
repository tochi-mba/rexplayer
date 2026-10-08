using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>Entries gathered under one name: an artist's, an album's (with its artist as the detail) or a genre's.</summary>
public sealed record LibraryGroup(string Name, string? Detail, IReadOnlyList<LibraryEntry> Entries);

/// <summary>
/// The ways the library is looked at (LIB-05): songs, albums, artists and genres of the music;
/// the videos; what was played or added lately; videos left part-way; and a search across it all.
/// Each is a plain query over the entries, in the order the window shows it.
/// </summary>
public static class LibraryViews
{
    public const string UnknownArtist = "Unknown artist";
    public const string UnknownAlbum = "Unknown album";
    public const string UnknownGenre = "Unknown genre";

    /// <summary>How many entries the "lately" views list.</summary>
    public const int Lately = 50;

    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;
    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary>The music, by artist, album, disc and track, then title; songs without an artist or album after those with.</summary>
    public static IReadOnlyList<LibraryEntry> Songs(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Music)
            .OrderBy(entry => entry.FiledArtist is null)
            .ThenBy(entry => entry.FiledArtist ?? "", ByName)
            .ThenBy(entry => entry.Album is null)
            .ThenBy(entry => entry.Album ?? "", ByName)
            .ThenBy(entry => entry.Disc ?? 0)
            .ThenBy(entry => entry.Track ?? int.MaxValue)
            .ThenBy(entry => entry.Title, NaturalOrder.Instance)];

    /// <summary>
    /// The albums, by name, each with its artist and its songs in order. An album is its name and the
    /// artist it is filed under (its album artist, else its artist), so two artists' albums of one
    /// name stay apart and a compilation tagged with an album artist stays together.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Albums(IEnumerable<LibraryEntry> entries) =>
        [.. Songs(entries)
            .GroupBy(entry => (entry.Album ?? UnknownAlbum) + "\n" + entry.FiledArtist, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(entry => entry.Album ?? UnknownAlbum)), group.Any(entry => entry.FiledArtist is not null) ? MostSpelled(group.Select(entry => entry.FiledArtist)) : null, [.. group]))
            .OrderBy(group => group.Name == UnknownAlbum)
            .ThenBy(group => group.Name, ByName)
            .ThenBy(group => group.Detail is null)
            .ThenBy(group => group.Detail ?? "", ByName)];

    /// <summary>The artists, by name, each with their songs.</summary>
    public static IReadOnlyList<LibraryGroup> Artists(IEnumerable<LibraryEntry> entries) => GroupedBy(Songs(entries), entry => entry.FiledArtist, UnknownArtist);

    /// <summary>The genres, by name, each with its songs.</summary>
    public static IReadOnlyList<LibraryGroup> Genres(IEnumerable<LibraryEntry> entries) => GroupedBy(Songs(entries), entry => entry.Genre, UnknownGenre);

    /// <summary>The videos, by title in natural order ("Episode 2" before "Episode 10").</summary>
    public static IReadOnlyList<LibraryEntry> Videos(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Video).OrderBy(entry => entry.Title, NaturalOrder.Instance).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>What was played most lately, newest first.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyPlayed(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.LastPlayed is not null).OrderByDescending(entry => entry.LastPlayed).Take(Lately)];

    /// <summary>What the library found most lately, newest first.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyAdded(IEnumerable<LibraryEntry> entries) =>
        [.. entries.OrderByDescending(entry => entry.Added).ThenBy(entry => entry.Title, NaturalOrder.Instance).Take(Lately)];

    /// <summary>The videos left part-way (<paramref name="leftAt"/> knows where), the latest played first.</summary>
    public static IReadOnlyList<LibraryEntry> ContinueWatching(IEnumerable<LibraryEntry> entries, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(leftAt);
        return [.. entries.Where(entry => entry.Kind == LibraryKind.Video && leftAt(entry.Path) is not null).OrderByDescending(entry => entry.LastPlayed ?? DateTime.MinValue).Take(Lately)];
    }

    /// <summary>
    /// The entries every word of <paramref name="text"/> is found in (in the title, artist, album,
    /// genre or file name, whatever the case or accents), music first then videos, each in its
    /// view's order; nothing for blank text.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Search(IEnumerable<LibraryEntry> entries, string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var found = entries.Where(entry => words.All(word => Matches(entry, word))).ToList();
        return [.. Songs(found), .. Videos(found)];
    }

    private static bool Matches(LibraryEntry entry, string word)
    {
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
        return new[] { entry.Title, entry.Artist, entry.AlbumArtist, entry.Album, entry.Genre, Path.GetFileName(entry.Path) }
            .Any(field => field is not null && Compare.IndexOf(field, word, Loose) >= 0);
    }

    private static List<LibraryGroup> GroupedBy(IReadOnlyList<LibraryEntry> songs, Func<LibraryEntry, string?> key, string unknown) =>
        [.. songs.GroupBy(entry => key(entry) ?? unknown, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(entry => key(entry) ?? unknown)), null, [.. group]))
            .OrderBy(group => group.Name == unknown)
            .ThenBy(group => group.Name, ByName)];

    /// <summary>A group's name as most of its songs spell it ("Asake" over one "asake"), the first in order on a tie.</summary>
    private static string MostSpelled(IEnumerable<string?> names) =>
        names.OfType<string>().GroupBy(name => name, StringComparer.Ordinal).OrderByDescending(spelling => spelling.Count()).ThenBy(spelling => spelling.Key, StringComparer.Ordinal).First().Key;
}
