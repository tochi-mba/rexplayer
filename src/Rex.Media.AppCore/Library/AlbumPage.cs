using System.Globalization;
using Rex.Media.AppCore.Metadata;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>What kind of release an album is, as its page and an artist's discography call it.</summary>
public enum ReleaseKind
{
    Album,
    EP,
    Single,
    Compilation,
}

/// <summary>One disc of an album: its number (null when the album has only one) and its songs in order.</summary>
public sealed record AlbumDisc(int? Number, IReadOnlyList<LibraryEntry> Songs);

/// <summary>
/// What an album's page shows (LIB-05), the streaming-style way: its kind (from MusicBrainz when a
/// lookup found it, else read from the songs themselves), year, songs and length, its discs, each
/// song's featured artists and share of the album's plays, the release facts a lookup found, and
/// more by the same artist. Pure queries; the window only lays them out, and a fact nobody knows
/// is simply not shown.
/// </summary>
public static class AlbumPage
{
    /// <summary>The name a compilation of many artists is filed under by most taggers.</summary>
    public const string VariousArtists = "Various Artists";

    /// <summary>A release this short (with few enough songs) is a single or an EP rather than an album.</summary>
    public static readonly TimeSpan ShortRelease = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The album's kind: MusicBrainz's when known (a "Compilation" secondary type first), else from
    /// the songs: filed under Various Artists or by four or more artists with no album artist is a
    /// compilation; under half an hour, up to three songs is a single and up to six an EP.
    /// </summary>
    public static ReleaseKind Kind(LibraryGroup album, EnrichedAlbum? facts)
    {
        ArgumentNullException.ThrowIfNull(album);
        if (facts?.Type is { } type)
        {
            if (facts.SecondaryTypes.Contains("Compilation", StringComparer.OrdinalIgnoreCase))
            {
                return ReleaseKind.Compilation;
            }

            if (type.Equals("Single", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseKind.Single;
            }

            if (type.Equals("EP", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseKind.EP;
            }

            return ReleaseKind.Album;
        }

        var songs = album.Entries;
        if (songs.Count == 0)
        {
            // A previously opened release can become empty while its drive is
            // disconnected or a background rescan removes its last file.
            // Do not invent a Single/EP classification for no actual songs.
            return ReleaseKind.Album;
        }

        if (string.Equals(album.Detail, VariousArtists, StringComparison.OrdinalIgnoreCase)
            || (songs.All(song => song.AlbumArtist is null) && songs.Select(song => ArtistNames.Key(song.Artist ?? "")).Distinct(StringComparer.Ordinal).Count() >= 4))
        {
            return ReleaseKind.Compilation;
        }

        var length = TimeSpan.FromTicks(songs.Sum(song => song.Duration?.Ticks ?? 0));
        return length >= ShortRelease || songs.Count > 6 ? ReleaseKind.Album
            : songs.Count <= 3 ? ReleaseKind.Single
            : ReleaseKind.EP;
    }

    /// <summary>The kind as the page says it: "Album", "EP", "Single" or "Compilation".</summary>
    public static string KindName(ReleaseKind kind) => kind.ToString();

    /// <summary>The year it came out: the catalogue's first release, else the latest its songs carry.</summary>
    public static int? Year(LibraryGroup album, EnrichedAlbum? facts)
    {
        ArgumentNullException.ThrowIfNull(album);
        return MusicBrainzFacts.YearOf(facts?.FirstReleased) ?? album.Entries.Select(song => song.Year).DefaultIfEmpty().Max();
    }

    /// <summary>The line under the title: "Album · 2024 · 14 songs, 48 min".</summary>
    public static string Line(LibraryGroup album, ReleaseKind kind, int? year)
    {
        ArgumentNullException.ThrowIfNull(album);
        var count = album.Entries.Count == 1 ? "1 song" : album.Entries.Count.ToString(CultureInfo.CurrentCulture) + " songs";
        var length = StreamingBrowse.Length(TimeSpan.FromTicks(album.Entries.Sum(song => song.Duration?.Ticks ?? 0)));
        return string.Join(" · ", new[] { KindName(kind), year?.ToString(CultureInfo.CurrentCulture), length.Length > 0 ? count + ", " + length : count }
            .Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>
    /// The album's songs by disc, each in track order (then title); one disc, or none numbered,
    /// is a single unnumbered disc, so headings appear only for a set of several.
    /// </summary>
    public static IReadOnlyList<AlbumDisc> Discs(LibraryGroup album)
    {
        ArgumentNullException.ThrowIfNull(album);
        if (album.Entries.Count == 0)
        {
            return [];
        }

        var discs = album.Entries
            .OrderBy(song => song.Disc ?? 1).ThenBy(song => song.Track ?? int.MaxValue).ThenBy(song => song.Title, NaturalOrder.Instance)
            .GroupBy(song => song.Disc ?? 1)
            .ToList();
        return discs.Count <= 1
            ? [new AlbumDisc(null, [.. discs.SelectMany(disc => disc)])]
            : [.. discs.Select(disc => new AlbumDisc(disc.Key, [.. disc]))];
    }

    /// <summary>The heading of a disc of several: "Disc 2".</summary>
    public static string DiscName(int number) => "Disc " + number.ToString(CultureInfo.CurrentCulture);

    /// <summary>The artists the album is by, each a link on its page: its album artist's names, else those of its first song.</summary>
    public static IReadOnlyList<string> Artists(LibraryGroup album, ArtistNames names)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(names);
        return album.Detail is { } detail ? names.Split(detail)
            : album.Entries.FirstOrDefault() is { } first ? names.Split(first.Artists ?? first.Artist)
            : [];
    }

    /// <summary>The artists a song is by beyond the album's own, for its line: "Olamide", from its credit or title.</summary>
    public static IReadOnlyList<string> Featured(LibraryEntry song, IReadOnlyList<string> albumArtists, ArtistNames names)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(albumArtists);
        ArgumentNullException.ThrowIfNull(names);
        var own = albumArtists.Select(names.KeyOf).ToHashSet(StringComparer.Ordinal);
        return [.. names.Of(song).Where(name => !own.Contains(names.KeyOf(name)))];
    }

    /// <summary>A song's plays as a share of the album's most played song's, 0 to 1, for the subtle bar on its line.</summary>
    public static double PlayShare(LibraryEntry song, LibraryGroup album)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(album);
        var most = album.Entries.Select(entry => entry.Plays).DefaultIfEmpty(0).Max();
        return most <= 0 ? 0 : Math.Clamp(song.Plays / (double)most, 0, 1);
    }

    /// <summary>When it first came out, as the page says it: "Released 8 August 2024", "Released 2018"; null when unknown.</summary>
    public static string? Released(EnrichedAlbum? facts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return facts?.FirstReleased switch
        {
            { Length: 10 } date when DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                => "Released " + day.ToString("d MMMM yyyy", culture),
            var other when MusicBrainzFacts.YearOf(other) is { } year => "Released " + year.ToString(culture),
            _ => null,
        };
    }

    /// <summary>The phonographic line from the catalogue's facts: "℗ 2024 EMPIRE, YBNL Nation"; null without both a year and a label.</summary>
    public static string? Phonographic(EnrichedAlbum? facts) =>
        MusicBrainzFacts.YearOf(facts?.FirstReleased) is { } year && facts!.Labels.Count > 0
            ? "℗ " + year.ToString(CultureInfo.InvariantCulture) + " " + string.Join(", ", facts.Labels)
            : null;

    /// <summary>The album's genres: the catalogue's when it has any, else its songs' tags, each once.</summary>
    public static IReadOnlyList<string> Genres(LibraryGroup album, EnrichedAlbum? facts)
    {
        ArgumentNullException.ThrowIfNull(album);
        return facts is { Genres.Count: > 0 }
            ? facts.Genres
            : [.. album.Entries.SelectMany(song => GenreNames.Split(song.Genre)).Distinct(StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// "More by": the other albums filed under the album's first artist, newest first, then by name;
    /// empty for an album of no named artist.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> MoreBy(LibraryGroup album, IReadOnlyList<LibraryGroup> albums, ArtistNames names)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(albums);
        ArgumentNullException.ThrowIfNull(names);
        if (Artists(album, names) is not [var first, ..] || string.Equals(first, VariousArtists, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var key = names.KeyOf(first);
        return [.. albums
            .Where(other => other.Name != LibraryViews.UnknownAlbum && !SameAlbum(other, album))
            .Where(other => Artists(other, names).Any(name => names.KeyOf(name) == key))
            .OrderByDescending(other => other.Entries.Max(song => song.Year) ?? 0)
            .ThenBy(other => other.Name, NaturalOrder.Instance)];
    }

    private static bool SameAlbum(LibraryGroup one, LibraryGroup other) =>
        string.Equals(one.Name, other.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(one.Detail, other.Detail, StringComparison.OrdinalIgnoreCase);
}
