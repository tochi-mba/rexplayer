using System.Globalization;
using System.Text.RegularExpressions;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>Entries gathered under one name: an artist's, an album's (with its artist as the detail) or a genre's.</summary>
public sealed record LibraryGroup(string Name, string? Detail, IReadOnlyList<LibraryEntry> Entries);

/// <summary>The library route that contains a file which is already open in the player.</summary>
public sealed record CurrentLibraryLocation(LibraryEntry Entry, LibraryGroup? Show = null, LibraryGroup? Season = null);

/// <summary>
/// A show's season and episode inferred from a video's path (<see cref="EpisodeNames"/>). Season 0 holds
/// the specials; <see cref="Episode"/> is null when nothing numbers the episode, which then goes in
/// natural order; a double episode runs from <see cref="Episode"/> through <see cref="Through"/>.
/// </summary>
public sealed record VideoEpisode(string Series, int Season, int? Episode, int? SeriesYear = null)
{
    /// <summary>The last episode a double episode holds; null for a single one.</summary>
    public int? Through { get; init; }

    /// <summary>For a special (season 0), the season it names ("S07X01" names 7); null when it names none.</summary>
    public int? SpecialOf { get; init; }

    /// <summary>
    /// Read from weak evidence (a "Special" or an extras folder outside any season folder), so it joins a
    /// show only when the library already has that show, and is otherwise a movie.
    /// </summary>
    public bool Tentative { get; init; }

    /// <summary>"S06E01", "S06E01-E02", "S06" when the episode has no number, "S07 Special 1" for a season's special.</summary>
    public string Code => SpecialOf is not null ? Badge : string.Create(CultureInfo.InvariantCulture,
        $"S{Season:00}{(Episode is { } first ? $"E{first:00}" : "")}{(Through is { } last ? $"-E{last:00}" : "")}");

    public string DisplayName => $"{Series}{(SeriesYear is { } year ? $" ({year})" : "")} · {Code}";

    /// <summary>The short mark on a tile: "S06 E01", "S06", "Special 2" or "S07 Special 1".</summary>
    public string Badge => Season == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{(SpecialOf is { } season ? $"S{season:00} " : "")}Special{SpecialNumber}")
        : string.Create(CultureInfo.InvariantCulture, $"S{Season:00}{(Episode is { } first ? $" E{first:00}" : "")}");

    /// <summary>"Season 6 · Episode 1", "Season 7 · Episodes 1-2", "Special 2", "Season 7, special 1", as the detail line says it.</summary>
    public string Spoken => Season != 0
        ? string.Create(CultureInfo.InvariantCulture, $"Season {Season}{(Episode is not { } first ? "" : Through is { } last ? $" · Episodes {first}-{last}" : $" · Episode {first}")}")
        : SpecialOf is { } season ? string.Create(CultureInfo.InvariantCulture, $"Season {season}, special{SpecialNumber}") : "Special" + SpecialNumber;

    /// <summary>" 1", " 1-2", or nothing for a special without a number.</summary>
    private string SpecialNumber => string.Create(CultureInfo.InvariantCulture,
        $"{(Episode is { } first ? $" {first}" : "")}{(Through is { } last ? $"-{last}" : "")}");
}

/// <summary>
/// The ways the library is looked at (LIB-05): songs, albums, artists and genres of the music;
/// videos and pictures; what was played or added lately; videos left part-way; and a search across it all.
/// Each is a plain query over the entries, in the order the window shows it.
/// </summary>
public static partial class LibraryViews
{
    public const string UnknownArtist = "Unknown artist";
    public const string UnknownAlbum = "Unknown album";
    public const string UnknownGenre = "Unknown genre";

    /// <summary>How many entries the "lately" views list.</summary>
    public const int Lately = 50;

    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;
    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary>
    /// Finds the library entry represented by a player location and, for an episode, the show and
    /// season that must be opened to reveal it. File URIs and ordinary paths name the same file;
    /// network locations never accidentally match a local entry.
    /// </summary>
    public static CurrentLibraryLocation? LocateCurrent(IEnumerable<LibraryEntry> entries, string? location)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var snapshot = entries.ToList();
        var entry = snapshot.FirstOrDefault(candidate => SameLocation(candidate.Path, location));
        if (entry is null || entry.Kind != LibraryKind.Video || EpisodeOf(entry) is null)
        {
            return entry is null ? null : new CurrentLibraryLocation(entry);
        }

        var show = TvShows(snapshot).FirstOrDefault(group => group.Entries.Any(candidate => SameLocation(candidate.Path, entry.Path)));
        var season = show is null ? null : TvSeasons(show).FirstOrDefault(group => group.Entries.Any(candidate => SameLocation(candidate.Path, entry.Path)));
        return new CurrentLibraryLocation(entry, show, season);
    }

    /// <summary>Whether two player/library locations identify the same local file.</summary>
    public static bool SameLocation(string? first, string? second)
    {
        var left = LocalLocation(first);
        var right = LocalLocation(second);
        return left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? LocalLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
            {
                return null;
            }

            location = uri.LocalPath;
        }

        return location.Replace('/', '\\').TrimEnd('\\');
    }

    /// <summary>
    /// Refreshes an opened group without losing navigation when filters hide all its members.
    /// Album details identify the artist; show and season details are changing counts, not identity.
    /// Missing groups retain their heading but never retain stale entries.
    /// </summary>
    public static LibraryGroup RefreshGroup(IReadOnlyList<LibraryGroup> groups, LibraryGroup opened, bool detailIdentifiesGroup = false)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(opened);
        return groups.FirstOrDefault(group => ByName.Equals(group.Name, opened.Name)
            && (!detailIdentifiesGroup || ByName.Equals(group.Detail, opened.Detail)))
            ?? opened with { Entries = [] };
    }

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
    /// Albums are primarily identified by the album artist tag. Without one, use the lead artist
    /// rather than the whole track credit ("Rihanna feat.Jay-Z" is filed under Rihanna). If most
    /// tracks of a named release agree, include its differently credited guest tracks too. A tie
    /// leaves same-named releases by different artists separate; the original tags are unchanged.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Albums(IEnumerable<LibraryEntry> entries)
    {
        var songs = Songs(entries);
        var names = ArtistNames.Learn(songs);
        var credited = songs.Select(song => (Song: song, Artist: song.AlbumArtist ?? names.Split(song.Artist).FirstOrDefault())).ToList();
        var releaseArtists = new Dictionary<string, string>(ByName);
        foreach (var release in credited.Where(pair => pair.Song.Album is not null).GroupBy(pair => pair.Song.Album!, ByName))
        {
            var explicitArtists = release.Select(pair => pair.Song.AlbumArtist).OfType<string>()
                .Select(names.KeyOf).Distinct(StringComparer.Ordinal).ToList();
            if (explicitArtists.Count > 1)
            {
                continue;
            }

            var leading = release.Where(pair => pair.Artist is not null)
                .GroupBy(pair => names.KeyOf(pair.Artist!), StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).FirstOrDefault();
            if (leading is null || leading.Count() < 2 || leading.Count() * 2 <= release.Count()
                || (explicitArtists.Count == 1 && leading.Key != explicitArtists[0]))
            {
                continue;
            }

            releaseArtists[release.Key] = MostSpelled(leading.Select(pair => pair.Artist));
        }

        return [.. credited
            .Select(pair => (pair.Song, Artist: pair.Song.AlbumArtist
                ?? (pair.Song.Album is { } album && releaseArtists.TryGetValue(album, out var preferred) ? preferred : pair.Artist)))
            .GroupBy(pair => (pair.Song.Album ?? UnknownAlbum) + "\n" + pair.Artist, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(pair => pair.Song.Album ?? UnknownAlbum)),
                group.Any(pair => pair.Artist is not null) ? MostSpelled(group.Select(pair => pair.Artist)) : null,
                [.. group.Select(pair => pair.Song)]))
            .OrderBy(group => group.Name == UnknownAlbum)
            .ThenBy(group => group.Name, ByName)
            .ThenBy(group => group.Detail is null)
            .ThenBy(group => group.Detail ?? "", ByName)];
    }

    /// <summary>
    /// The artists, by name, each with their songs. A song is filed under everyone it is by, once
    /// under each: its album's artist and its own artists ("Various Artists" and "Asake"), every
    /// artist a combined credit names ("beabadoobee/The Marías") and those its title features, as
    /// <see cref="ArtistNames"/> reads them. Spellings that differ only in case, accents or
    /// punctuation are one artist, named the way most of their songs spell it. It is worked out from
    /// the stored tags each time, so a library read under older rules shows the current ones at once.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Artists(IEnumerable<LibraryEntry> entries)
    {
        var songs = Songs(entries);
        var names = ArtistNames.Learn(songs);
        return [.. songs
            .SelectMany(entry => names.Of(entry).DefaultIfEmpty(UnknownArtist).Select(name => (Name: name, Entry: entry)))
            .GroupBy(pair => names.KeyOf(pair.Name), StringComparer.Ordinal)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(pair => pair.Name)), null, [.. group.Select(pair => pair.Entry)]))
            .OrderBy(group => group.Name == UnknownArtist)
            .ThenBy(group => group.Name, ByName)];
    }

    /// <summary>
    /// The genres, by name, each with its songs. A song tagged with several genres ("Dance;Pop")
    /// appears under each of them, as <see cref="GenreNames"/> reads the tag. When its file has no
    /// genre, <paramref name="fetchedGenres"/> may supply the album genres found online.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Genres(IEnumerable<LibraryEntry> entries,
        Func<LibraryEntry, IReadOnlyList<string>>? fetchedGenres = null) =>
        [.. Songs(entries)
            .SelectMany(entry => (GenreNames.Split(entry.Genre) is { Count: > 0 } tagged
                    ? tagged : fetchedGenres?.Invoke(entry) ?? [])
                .DefaultIfEmpty(UnknownGenre).Select(name => (Name: name, Entry: entry)))
            .GroupBy(pair => pair.Name, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(pair => pair.Name)), null, [.. group.Select(pair => pair.Entry).Distinct()]))
            .OrderBy(group => group.Name == UnknownGenre)
            .ThenBy(group => group.Name, ByName)];

    /// <summary>The videos, by title in natural order ("Episode 2" before "Episode 10").</summary>
    public static IReadOnlyList<LibraryEntry> Videos(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Video).OrderBy(entry => entry.Title, NaturalOrder.Instance).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The videos that are not part of a show; only a positively identified episode leaves this set,
    /// and a tentative one (a movie's extras, a "Special" title) leaves it only when it joins a show.
    /// <see cref="Movies"/> and <see cref="OtherVideos"/> split it.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Standalone(IEnumerable<LibraryEntry> entries)
    {
        var videos = Videos(entries);
        var joined = videos.Any(entry => EpisodeOf(entry) is { Tentative: true })
            ? ShowMembers(videos).Select(member => member.Entry).ToHashSet()
            : [];
        return [.. videos.Where(entry => EpisodeOf(entry) is not { } episode || episode.Tentative && !joined.Contains(entry))];
    }

    /// <summary>The films (<see cref="MovieRules"/>) among the standalone videos, given what the movie lookups said about each.</summary>
    public static IReadOnlyList<LibraryEntry> Movies(IEnumerable<LibraryEntry> entries, Func<LibraryEntry, MovieLookup> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return [.. Standalone(entries).Where(entry => MovieRules.IsMovie(entry, lookup))];
    }

    /// <summary>The standalone videos that are not films: clips, recordings, music videos and the rest.</summary>
    public static IReadOnlyList<LibraryEntry> OtherVideos(IEnumerable<LibraryEntry> entries, Func<LibraryEntry, MovieLookup> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return [.. Standalone(entries).Where(entry => MovieRules.IsOther(entry, lookup))];
    }

    /// <summary>
    /// Series inferred locally from filenames, without contacting an online catalogue. Names that differ
    /// only in case, accents or punctuation are one show (<see cref="ShowNames.Key"/>), named the way
    /// most of its episodes spell it; a lone episode filed under a longer name joins the show it extends
    /// (<see cref="ShowNames.Extends"/>); tentative specials and extras join a show that is already there.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> TvShows(IEnumerable<LibraryEntry> entries) =>
        [.. ShowMembers(Videos(entries))
            .GroupBy(member => member.Show, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = InEpisodeOrder(group.Select(member => (member.Entry, (VideoEpisode?)member.Episode))).ToList();
                var seasons = ordered.Select(pair => pair.Episode!.Season).Distinct().Count();
                return new LibraryGroup(group.Key,
                    $"{seasons} {(seasons == 1 ? "season" : "seasons")} · {ordered.Count} {(ordered.Count == 1 ? "episode" : "episodes")}",
                    [.. ordered.Select(pair => pair.Entry)]);
            })
            .OrderBy(group => group.Name, ByName)];

    /// <summary>Every episode of <paramref name="videos"/> that belongs to a show, with the show's name.</summary>
    private static List<(LibraryEntry Entry, VideoEpisode Episode, string Show)> ShowMembers(IReadOnlyList<LibraryEntry> videos)
    {
        var parsed = videos.Select(entry => (Entry: entry, Episode: EpisodeOf(entry))).Where(pair => pair.Episode is not null).ToList();
        var firm = parsed.Where(pair => !pair.Episode!.Tentative).ToList();
        var firmEpisodes = firm.Select(pair => pair.Episode!).ToList();
        var shows = new Dictionary<string, (List<string> Names, List<(LibraryEntry Entry, VideoEpisode Episode)> Members)>(StringComparer.Ordinal);
        foreach (var (entry, episode) in firm)
        {
            var name = SeriesGroupKey(episode!, firmEpisodes);
            var key = ShowNames.Key(name);
            if (!shows.TryGetValue(key, out var show))
            {
                shows[key] = show = ([], []);
            }

            show.Names.Add(name);
            show.Members.Add((entry, episode!));
        }

        // A lone episode whose show name is another show's followed by a season mark is that show's.
        foreach (var key in shows.Keys.OrderByDescending(key => key.Length).ToList())
        {
            if (shows[key].Members.Count == 1 && shows.Keys.Where(host => ShowNames.Extends(key, host)).MaxBy(host => host.Length) is { } found)
            {
                shows[found].Members.Add(shows[key].Members[0]);
                shows.Remove(key);
            }
        }

        foreach (var (entry, episode) in parsed.Where(pair => pair.Episode!.Tentative))
        {
            if (HostOf(episode!, shows) is { } found)
            {
                shows[found].Members.Add((entry, episode!));
            }
        }

        return [.. shows.Values.SelectMany(show =>
        {
            var name = MostSpelled(show.Names);
            return show.Members.Select(member => (member.Entry, member.Episode, name));
        })];
    }

    /// <summary>
    /// The show a tentative special joins: one of its name (or extended by it), and with its year when
    /// it has one, so "X-Men (2000)\Extras" stays the movie's beside a show called "X-Men".
    /// </summary>
    private static string? HostOf(VideoEpisode episode, Dictionary<string, (List<string> Names, List<(LibraryEntry Entry, VideoEpisode Episode)> Members)> shows)
    {
        var key = ShowNames.Key(episode.Series);
        var keyed = episode.SeriesYear is { } year ? ShowNames.Key($"{episode.Series} ({year})") : key;
        return shows.Keys
            .Where(host => host == key || host == keyed || ShowNames.Extends(key, host))
            .Where(host => episode.SeriesYear is not { } wanted || shows[host].Members.Any(member => member.Episode.SeriesYear == wanted))
            .MaxBy(host => host.Length);
    }

    /// <summary>
    /// The seasons of one series in chronological order, the specials (season 0) after them; numbered
    /// episodes come first in number order, then the unnumbered ones by file name in natural order.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> TvSeasons(LibraryGroup show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return [.. show.Entries
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is not null)
            .GroupBy(pair => pair.Episode!.Season).OrderBy(group => group.Key == 0).ThenBy(group => group.Key)
            .Select(group => new LibraryGroup(group.Key == 0 ? "Specials" : $"Season {group.Key}",
                $"{group.Count()} {(group.Count() == 1 ? "episode" : "episodes")}",
                [.. InEpisodeOrder(group).Select(pair => pair.Entry)]))];
    }

    /// <summary>By season (specials by the season they name); in each, numbered episodes by number, then unnumbered ones by file name in natural order, then by path.</summary>
    private static IOrderedEnumerable<(LibraryEntry Entry, VideoEpisode? Episode)> InEpisodeOrder(IEnumerable<(LibraryEntry Entry, VideoEpisode? Episode)> pairs) =>
        pairs.OrderBy(pair => pair.Episode!.Season)
            .ThenBy(pair => pair.Episode!.SpecialOf ?? 0)
            .ThenBy(pair => pair.Episode!.Episode is null)
            .ThenBy(pair => pair.Episode!.Episode)
            .ThenBy(pair => pair.Episode!.Episode is null ? Path.GetFileNameWithoutExtension(pair.Entry.Path) : "", NaturalOrder.Instance)
            .ThenBy(pair => pair.Entry.Path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Clean a filename for a movie card; prefer a real embedded title when available.</summary>
    public static string MovieTitleOf(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Title != MediaLibrary.TitleOf(entry.Path))
        {
            return entry.Title;
        }

        var name = MovieReleaseName(entry.Path);
        var match = MovieYearPattern().Match(name);
        if (match.Success)
        {
            name = match.Groups["title"].Value;
        }
        else
        {
            name = QualitySuffixPattern().Replace(name, "");
        }

        return Separators().Replace(name, " ").Trim(' ', '-', '(', ')');
    }

    /// <summary>The release year from tags, or a year explicitly present in the movie filename.</summary>
    public static int? MovieYearOf(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Year is { } year)
        {
            return year;
        }

        var name = MovieReleaseName(entry.Path);
        var match = MovieYearPattern().Match(name);
        return match.Success && int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year) ? year : null;
    }

    /// <summary>
    /// Drop a bracketed release-group prefix only when a title followed by a release year remains.
    /// Bracketed film titles such as [REC] (2007) keep their own name.
    /// </summary>
    private static string MovieReleaseName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(normalized);
        var withoutGroup = ReleaseGroupPrefix().Replace(name, "");
        if (MovieYearPattern().IsMatch(withoutGroup))
        {
            return withoutGroup;
        }

        // Disc files and quality-only rips often omit a year that their release folder contains.
        // Prefer that folder's title and year, rather than searching for "feature" or "1080p".
        var folder = Path.GetFileName(Path.GetDirectoryName(normalized));
        return folder is not null && MovieYearPattern().IsMatch(folder) ? folder : name;
    }

    /// <summary>
    /// Alternate files of the same feature. A known release year is required to avoid grouping
    /// unrelated homonyms; a noticeably different runtime is probably a different cut.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> MovieVersions(IEnumerable<LibraryEntry> entries, LibraryEntry playing)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(playing);
        var year = MovieYearOf(playing);
        if (year is null || !MovieRules.WorthLookingUp(playing) || playing.Downloading)
        {
            return [];
        }

        var name = Rex.Media.AppCore.Metadata.MetadataMatch.Normalize(MovieTitleOf(playing));
        var cut = MovieCutOf(playing);
        if (name.Length == 0)
        {
            return [];
        }

        return [.. entries.Where(entry => entry.Kind == LibraryKind.Video && !entry.Downloading
                && MovieRules.WorthLookingUp(entry) && MovieYearOf(entry) == year
                && Rex.Media.AppCore.Metadata.MetadataMatch.Normalize(MovieTitleOf(entry)) == name
                && MovieCutOf(entry) == cut
                && (entry.Duration is not { } other || playing.Duration is not { } current
                    || Math.Abs((other - current).TotalSeconds) <= Math.Max(120, current.TotalSeconds * 0.03)))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => MovieResolutionHeight(entry))
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Keep director's cuts, extended, unrated and theatrical releases distinct even if their runtimes happen to be similar.</summary>
    private static string MovieCutOf(LibraryEntry entry)
    {
        var name = MovieReleaseName(entry.Path);
        var year = MovieYearPattern().Match(name);
        var suffix = year.Success ? name[(year.Index + year.Length)..] : "";
        var cut = MovieCutPattern().Match(suffix);
        return cut.Success ? new string(cut.Groups["cut"].Value.Where(char.IsLetter).ToArray()).ToLowerInvariant() : "";
    }

    /// <summary>The resolution named by the release, or "Unknown" when no resolution is stated.</summary>
    public static string MovieResolution(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var match = MovieResolutionPattern().Match(Path.GetFileNameWithoutExtension(entry.Path.Replace('\\', '/')));
        var quality = match.Groups["quality"].Value.ToLowerInvariant();
        return quality switch { "4k" or "uhd" => "2160p", "" => "Unknown", _ => quality };
    }

    private static int MovieResolutionHeight(LibraryEntry entry)
    {
        var resolution = MovieResolution(entry);
        return resolution.EndsWith('p') && int.TryParse(resolution[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ? height : 0;
    }

    /// <summary>The pictures, by their folders and then file names in natural order.</summary>
    public static IReadOnlyList<LibraryEntry> Pictures(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Picture)
            .OrderBy(entry => Path.GetDirectoryName(entry.Path) ?? "", ByName)
            .ThenBy(entry => Path.GetFileName(entry.Path), NaturalOrder.Instance)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The show, season and episode a video's path identifies (<see cref="EpisodeNames"/>), or null for
    /// a movie. It is read from the path every time, never stored, so a library saved under older
    /// rules is classified by the current ones as soon as it loads.
    /// </summary>
    public static VideoEpisode? EpisodeOf(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return EpisodeNames.Parse(entry.Path);
    }

    /// <summary>
    /// What follows a video automatically. A recognised episode plays with the same show's season
    /// in its folder, in episode order; an unrelated video stays on its own. This avoids turning a
    /// broad library folder into an accidental queue of thousands of unrelated videos.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> AutoplayVideos(IEnumerable<LibraryEntry> entries, LibraryEntry selected)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Kind != LibraryKind.Video || EpisodeOf(selected) is not { } wanted)
        {
            return [selected];
        }

        var folder = Path.GetDirectoryName(selected.Path) ?? "";
        var candidates = entries
            .Where(entry => entry.Kind == LibraryKind.Video && string.Equals(Path.GetDirectoryName(entry.Path) ?? "", folder, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is { } episode && episode.Season == wanted.Season && episode.Series.Equals(wanted.Series, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var wantedGroup = SeriesGroupKey(wanted, candidates.Select(pair => pair.Episode!));
        var season = InEpisodeOrder(candidates
            .Where(pair => ByName.Equals(SeriesGroupKey(pair.Episode!, candidates.Select(item => item.Episode!)), wantedGroup)))
            .Select(pair => pair.Entry)
            .ToList();
        return season.Any(entry => string.Equals(entry.Path, selected.Path, StringComparison.OrdinalIgnoreCase)) ? season : [selected];
    }

    /// <summary>What was played most lately, newest first.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyPlayed(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.LastPlayed is not null).OrderByDescending(entry => entry.LastPlayed)];

    /// <summary>Recent history first, including supported files played outside watched library folders.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyPlayed(IEnumerable<LibraryEntry> entries, IEnumerable<string> recentPaths)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(recentPaths);
        var known = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var played = new List<LibraryEntry>();
        foreach (var path in recentPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            if (known.TryGetValue(path, out var entry))
            {
                played.Add(entry);
            }
            else if (MediaFiles.LibraryKindOf(path) is { } kind)
            {
                played.Add(new LibraryEntry
                {
                    Path = path,
                    Kind = kind,
                    Title = Path.GetFileNameWithoutExtension(path.Replace('\\', '/')),
                    LastPlayed = DateTime.MaxValue.AddTicks(-played.Count),
                });
            }
        }

        played.AddRange(RecentlyPlayed(known.Values).Where(entry => seen.Add(entry.Path)));
        return played;
    }

    /// <summary>What the library found most lately, newest first.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyAdded(IEnumerable<LibraryEntry> entries) =>
        [.. entries.OrderByDescending(entry => entry.Added).ThenBy(entry => entry.Title, NaturalOrder.Instance)];

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
        return [.. Songs(found), .. Videos(found), .. Pictures(found)];
    }

    private static bool Matches(LibraryEntry entry, string word)
    {
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
        return new[] { entry.Title, entry.Artist, entry.AlbumArtist, entry.Album, entry.Genre, Path.GetFileName(entry.Path) }
            .Any(field => field is not null && Compare.IndexOf(field, word, Loose) >= 0);
    }

    [GeneratedRegex(@"^(?<title>.+)[ ._(\-]+(?<year>19\d{2}|20\d{2})(?=[ ._)\-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieYearPattern();

    [GeneratedRegex(@"[ ._-]+(?:4320p|2160p|1440p|1080p|720p|576p|480p|360p|4K|UHD|BluRay|Blu-Ray|BDRip|BRRip|WEBRip|WEB[ ._-]?DL|HDRip|DVDRip|HDTV|REMUX|x26[45]|H[ ._]?26[45]|HEVC|AVC|HDR)(?=[ ._-]|$).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualitySuffixPattern();

    [GeneratedRegex(@"^(?:\[[A-Za-z0-9][A-Za-z0-9._-]{1,17}\][ ._-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseGroupPrefix();

    [GeneratedRegex(@"(?:^|[^A-Za-z0-9])(?<quality>4320p|2160p|1440p|1080p|720p|576p|480p|360p|4K|UHD)(?=$|[^A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieResolutionPattern();

    [GeneratedRegex(@"(?:^|[ ._-])(?<cut>extended|director'?s?[ ._-]*cut|theatrical|unrated|special[ ._-]*edition|final[ ._-]*cut)(?=$|[ ._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieCutPattern();

    [GeneratedRegex("[._]+")]
    private static partial Regex Separators();

    private static string SeriesGroupKey(VideoEpisode episode, IEnumerable<VideoEpisode> peers)
    {
        var years = peers.Where(peer => ByName.Equals(peer.Series, episode.Series) && peer.SeriesYear is not null)
            .Select(peer => peer.SeriesYear!.Value).Distinct().Take(2).ToList();
        return years.Count < 2 ? episode.Series : episode.SeriesYear is { } year ? $"{episode.Series} ({year})" : episode.Series;
    }

    /// <summary>A group's name as most of its songs spell it ("Asake" over one "asake"), the first in order on a tie.</summary>
    private static string MostSpelled(IEnumerable<string?> names) =>
        names.OfType<string>().GroupBy(name => name, StringComparer.Ordinal).OrderByDescending(spelling => spelling.Count()).ThenBy(spelling => spelling.Key, StringComparer.Ordinal).First().Key;
}
