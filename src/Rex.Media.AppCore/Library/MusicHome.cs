using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>What a mix is made around: one artist, one genre or one decade.</summary>
public enum MusicMixKind
{
    Artist,
    Genre,
    Decade,
}

/// <summary>A mix made from the library: what it is around, its title, the line under it and its songs in play order.</summary>
public sealed record MusicMix(MusicMixKind Kind, string Around, string Title, string Line, IReadOnlyList<LibraryEntry> Songs);

/// <summary>The music Home's featured album or artist, and why it is featured.</summary>
public sealed record MusicPick(LibraryGroup Group, bool IsArtist, string Reason);

/// <summary>A card of "Jump back in": an album, or one of the user's playlists.</summary>
public sealed record MusicPlace(LibraryGroup? Album, NamedPlaylist? Playlist);

/// <summary>Everything the music Home shows. A row a library cannot fill is empty, and the window leaves it out.</summary>
public sealed record MusicHomeRows(
    MusicPick? Hero,
    IReadOnlyList<MusicPlace> JumpBackIn,
    IReadOnlyList<LibraryGroup> Pinned,
    IReadOnlyList<LibraryEntry> OnRepeat,
    IReadOnlyList<LibraryGroup> RecentlyAdded,
    IReadOnlyList<MusicMix> Mixes,
    IReadOnlyList<LibraryGroup> NotLately,
    IReadOnlyList<LibraryGroup> NewArtists);

/// <summary>
/// The streaming-style music Home: a hero for what the user is into lately, then rows of albums,
/// songs, mixes and artists. Everything is worked out locally from the library's own plays, play
/// times and tags, never from a remote service. "Lately" weighs each song's plays by how recently
/// it was last played (halving every fortnight), so an old favourite gives way to a new one.
/// Long mixes and sets (tracks of <see cref="LongestSong"/> or more) stay out of the rankings.
/// </summary>
public static class MusicHome
{
    /// <summary>How many cards one row holds at most.</summary>
    public const int RowSize = 20;

    /// <summary>How many songs a mix holds at most.</summary>
    public const int MixSize = 40;

    /// <summary>The fewest songs a mix is made of.</summary>
    public const int SmallestMix = 4;

    /// <summary>How many artist mixes and genre mixes there are at most, each.</summary>
    public const int MixesOfEach = 3;

    /// <summary>A track this long or longer is a mix or a set, not a song to rank.</summary>
    public static readonly TimeSpan LongestSong = TimeSpan.FromMinutes(20);

    /// <summary>How long since an album was last played before it counts as not played in a while.</summary>
    public static readonly TimeSpan AWhile = TimeSpan.FromDays(45);

    /// <summary>The time over which a play's weight halves.</summary>
    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(14);

    /// <summary>The music Home's rows for <paramref name="entries"/> at <paramref name="now"/> (UTC).</summary>
    /// <param name="entries">The library's entries; only music is read.</param>
    /// <param name="playlists">The user's playlists, for "Jump back in".</param>
    /// <param name="now">The time "lately" is measured from.</param>
    /// <param name="moreGenres">Genres a lookup found for an artist, for the artist mixes; null for none.</param>
    /// <param name="pinnedAlbumPaths">Paths in real albums selected by the user; missing paths are ignored.</param>
    public static MusicHomeRows Rows(IEnumerable<LibraryEntry> entries, IReadOnlyList<NamedPlaylist> playlists, DateTime now,
        Func<string, IReadOnlyList<string>>? moreGenres = null, IReadOnlyList<string>? pinnedAlbumPaths = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(playlists);
        var songs = LibraryViews.Songs(entries);
        var albums = LibraryViews.Albums(songs);
        var artists = LibraryViews.Artists(songs).Where(artist => artist.Name != LibraryViews.UnknownArtist).ToList();
        double Lately(LibraryEntry song) => Score(song, now);

        var onRepeat = songs.Where(song => Lately(song) > 0)
            .OrderByDescending(Lately).ThenBy(song => song.Title, NaturalOrder.Instance).Take(RowSize).ToArray();
        var recentlyAdded = albums.OrderByDescending(album => album.Entries.Max(song => song.Added))
            .ThenBy(album => album.Name, NaturalOrder.Instance).Take(RowSize).ToArray();
        var notLately = albums.Where(album => album.Entries.Max(song => song.LastPlayed) is { } last && now - last >= AWhile)
            .OrderByDescending(album => album.Entries.Sum(song => song.Plays))
            .ThenBy(album => album.Name, NaturalOrder.Instance).Take(RowSize).ToArray();
        var newArtists = artists.OrderByDescending(artist => artist.Entries.Min(song => song.Added))
            .ThenBy(artist => artist.Name, NaturalOrder.Instance).Take(RowSize).ToArray();

        // A pin names a real track in the album, not a title: unrelated releases
        // with identical names must not be promoted together. Missing media does
        // not become a fabricated, clickable Home card.
        var pinned = new List<LibraryGroup>();
        foreach (var path in pinnedAlbumPaths ?? [])
        {
            var album = albums.FirstOrDefault(group => group.Entries.Any(song =>
                string.Equals(song.Path, path, StringComparison.OrdinalIgnoreCase)));
            if (album is not null && !pinned.Contains(album))
            {
                pinned.Add(album);
            }
        }

        return new MusicHomeRows(Hero(albums, artists, recentlyAdded, Lately), JumpBackIn(songs, albums, playlists),
            pinned.Take(RowSize).ToArray(), onRepeat, recentlyAdded,
            [.. ArtistMixes(artists, Lately, moreGenres), .. GenreMixes(songs, Lately), .. DecadeMixes(songs, Lately)], notLately, newArtists);
    }

    /// <summary>How much a song is played lately: its plays, halved for every fortnight since its last play; 0 for a long mix or set.</summary>
    public static double Score(LibraryEntry song, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (song.Plays <= 0 || IsLong(song))
        {
            return 0;
        }

        var since = song.LastPlayed is { } last ? Math.Max(0, (now - last).TotalDays) : 365;
        return song.Plays * Math.Pow(0.5, since / HalfLife.TotalDays);
    }

    /// <summary>Whether a track is a long mix or a set, which no ranking takes in.</summary>
    public static bool IsLong(LibraryEntry song) => (song ?? throw new ArgumentNullException(nameof(song))).Duration >= LongestSong;

    private static MusicPick? Hero(IReadOnlyList<LibraryGroup> albums, IReadOnlyList<LibraryGroup> artists, IReadOnlyList<LibraryGroup> recentlyAdded, Func<LibraryEntry, double> lately)
    {
        var album = albums.Select(group => (Group: group, Score: group.Entries.Sum(lately))).OrderByDescending(pair => pair.Score).FirstOrDefault();
        var artist = artists.Select(group => (Group: group, Score: group.Entries.Sum(lately))).OrderByDescending(pair => pair.Score).FirstOrDefault();
        if (album.Group is null || album.Score <= 0)
        {
            return recentlyAdded.FirstOrDefault() is { } fresh ? new MusicPick(fresh, false, "New in your library") : null;
        }

        // An artist is featured over an album only when they are played across several albums.
        return artist.Group is { } top && artist.Score > album.Score * 1.5 && LibraryViews.Albums(top.Entries).Count > 1
            ? new MusicPick(top, true, "Your top artist lately")
            : new MusicPick(album.Group, false, "On repeat lately");
    }

    private static List<MusicPlace> JumpBackIn(IReadOnlyList<LibraryEntry> songs, IReadOnlyList<LibraryGroup> albums, IReadOnlyList<NamedPlaylist> playlists)
    {
        var played = songs.Where(song => song.LastPlayed is not null).ToDictionary(song => song.Path, song => song.LastPlayed!.Value, StringComparer.OrdinalIgnoreCase);
        var places = albums.Select(album => (Place: new MusicPlace(album, null), When: album.Entries.Max(song => song.LastPlayed)))
            .Concat(playlists.Select(list => (Place: new MusicPlace(null, list),
                When: list.Items.Select(item => played.TryGetValue(item.Location, out var when) ? when : (DateTime?)null).Max())));
        return [.. places.Where(place => place.When is not null).OrderByDescending(place => place.When).Select(place => place.Place).Take(RowSize)];
    }

    private static IEnumerable<MusicMix> ArtistMixes(IReadOnlyList<LibraryGroup> artists, Func<LibraryEntry, double> lately, Func<string, IReadOnlyList<string>>? moreGenres)
    {
        foreach (var artist in Top(artists, lately).Where(artist => artist.Entries.Count(song => !IsLong(song)) >= 3).Take(MixesOfEach))
        {
            var kin = ArtistKinship.Like(artist, artists, moreGenres).Take(4).Select(like => like.Artist).ToList();
            var own = Ordered(artist.Entries, lately);
            var taken = own.Select(song => song.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var others = Interleave(kin.Select(other => Ordered(other.Entries.Where(song => !taken.Contains(song.Path)), lately)));
            var mix = Weave(own, others);
            if (mix.Count >= SmallestMix)
            {
                yield return new MusicMix(MusicMixKind.Artist, artist.Name, artist.Name + " Mix", Line([artist.Name, .. kin.Select(other => other.Name)]), mix);
            }
        }
    }

    private static IEnumerable<MusicMix> GenreMixes(IReadOnlyList<LibraryEntry> songs, Func<LibraryEntry, double> lately) =>
        LibraryViews.Genres(songs).Where(genre => genre.Name != LibraryViews.UnknownGenre)
            .Select(genre => (Genre: genre, Songs: Spread(genre.Entries, lately)))
            .Where(pair => pair.Songs.Count >= SmallestMix)
            .OrderByDescending(pair => pair.Genre.Entries.Sum(lately))
            .ThenByDescending(pair => pair.Genre.Entries.Count)
            .ThenBy(pair => pair.Genre.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MixesOfEach)
            .Select(pair => new MusicMix(MusicMixKind.Genre, pair.Genre.Name, pair.Genre.Name + " Mix", Line(ArtistsOf(pair.Songs)), pair.Songs));

    private static IEnumerable<MusicMix> DecadeMixes(IReadOnlyList<LibraryEntry> songs, Func<LibraryEntry, double> lately) =>
        songs.Where(song => song.Year is > 0)
            .GroupBy(song => song.Year!.Value / 10 * 10)
            .Select(decade => (Decade: decade.Key, Songs: Spread([.. decade], lately)))
            .Where(pair => pair.Songs.Count >= SmallestMix)
            .OrderByDescending(pair => pair.Songs.Count)
            .ThenByDescending(pair => pair.Decade)
            .Take(1)
            .Select(pair =>
            {
                var name = pair.Decade.ToString(CultureInfo.InvariantCulture) + "s";
                return new MusicMix(MusicMixKind.Decade, name, name + " Mix", Line(ArtistsOf(pair.Songs)), pair.Songs);
            });

    /// <summary>The groups played most lately, else those with the most songs, then by name.</summary>
    private static IEnumerable<LibraryGroup> Top(IEnumerable<LibraryGroup> groups, Func<LibraryEntry, double> lately) =>
        groups.OrderByDescending(group => group.Entries.Sum(lately)).ThenByDescending(group => group.Entries.Count).ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Songs (never long mixes) played most lately first, then most played, then by title.</summary>
    private static List<LibraryEntry> Ordered(IEnumerable<LibraryEntry> songs, Func<LibraryEntry, double> lately) =>
        [.. songs.Where(song => !IsLong(song)).OrderByDescending(lately).ThenByDescending(song => song.Plays).ThenBy(song => song.Title, NaturalOrder.Instance)];

    /// <summary>Songs taken in turn from each artist they are filed under, so one artist does not fill a mix.</summary>
    private static List<LibraryEntry> Spread(IReadOnlyList<LibraryEntry> songs, Func<LibraryEntry, double> lately) =>
        [.. Interleave(Ordered(songs, lately).GroupBy(song => ArtistNames.Key(song.FiledArtist ?? "")).Select(group => group.ToList())).Take(MixSize)];

    private static List<LibraryEntry> Interleave(IEnumerable<List<LibraryEntry>> lists)
    {
        var queues = lists.Select(list => new Queue<LibraryEntry>(list)).Where(queue => queue.Count > 0).ToList();
        var woven = new List<LibraryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queues.Count > 0)
        {
            foreach (var queue in queues)
            {
                var song = queue.Dequeue();
                if (seen.Add(song.Path))
                {
                    woven.Add(song);
                }
            }

            queues.RemoveAll(queue => queue.Count == 0);
        }

        return woven;
    }

    /// <summary>The artist's own songs two at a time with one of their kin's in between, up to <see cref="MixSize"/>.</summary>
    private static List<LibraryEntry> Weave(List<LibraryEntry> own, List<LibraryEntry> others)
    {
        var mix = new List<LibraryEntry>();
        int mine = 0, theirs = 0;
        while (mix.Count < MixSize && (mine < own.Count || theirs < others.Count))
        {
            for (var take = 0; take < 2 && mine < own.Count && mix.Count < MixSize; take++)
            {
                mix.Add(own[mine++]);
            }

            if (theirs < others.Count && mix.Count < MixSize)
            {
                mix.Add(others[theirs++]);
            }
        }

        return mix;
    }

    private static List<string> ArtistsOf(IEnumerable<LibraryEntry> songs) =>
        [.. songs.Select(song => ArtistNames.Display(song.FiledArtist)).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>"Asake", "Asake and Olamide", "Asake, Olamide and more".</summary>
    private static string Line(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        2 => names[0] + " and " + names[1],
        _ => names[0] + ", " + names[1] + " and more",
    };
}
