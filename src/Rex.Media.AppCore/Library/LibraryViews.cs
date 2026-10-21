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

    /// <summary>"S06E01", "S06E01-E02", or "S06" when the episode has no number.</summary>
    public string Code => string.Create(CultureInfo.InvariantCulture,
        $"S{Season:00}{(Episode is { } first ? $"E{first:00}" : "")}{(Through is { } last ? $"-E{last:00}" : "")}");

    public string DisplayName => $"{Series}{(SeriesYear is { } year ? $" ({year})" : "")} · {Code}";

    /// <summary>The short mark on a tile: "S06 E01", or "S06".</summary>
    public string Badge => string.Create(CultureInfo.InvariantCulture, $"S{Season:00}{(Episode is { } first ? $" E{first:00}" : "")}");

    /// <summary>"Season 6 · Episode 1", "Season 7 · Episodes 1-2", "Specials", as the detail line says it.</summary>
    public string Spoken => string.Create(CultureInfo.InvariantCulture,
        $"{(Season == 0 ? "Specials" : $"Season {Season}")}{(Episode is not { } first ? "" : Through is { } last ? $" · Episodes {first}-{last}" : $" · Episode {first}")}");
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
    /// appears under each of them, as <see cref="GenreNames"/> reads the tag.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Genres(IEnumerable<LibraryEntry> entries) =>
        [.. Songs(entries)
            .SelectMany(entry => GenreNames.Split(entry.Genre).DefaultIfEmpty(UnknownGenre).Select(name => (Name: name, Entry: entry)))
            .GroupBy(pair => pair.Name, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(pair => pair.Name)), null, [.. group.Select(pair => pair.Entry).Distinct()]))
            .OrderBy(group => group.Name == UnknownGenre)
            .ThenBy(group => group.Name, ByName)];

    /// <summary>The videos, by title in natural order ("Episode 2" before "Episode 10").</summary>
    public static IReadOnlyList<LibraryEntry> Videos(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Video).OrderBy(entry => entry.Title, NaturalOrder.Instance).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Standalone videos; only a positively identified episode leaves this view.</summary>
    public static IReadOnlyList<LibraryEntry> Movies(IEnumerable<LibraryEntry> entries) =>
        [.. Videos(entries).Where(entry => EpisodeOf(entry) is null)];

    /// <summary>Series inferred locally from filenames, without contacting an online catalogue.</summary>
    public static IReadOnlyList<LibraryGroup> TvShows(IEnumerable<LibraryEntry> entries)
    {
        var episodes = Videos(entries)
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is not null)
            .ToList();
        return [.. episodes
            .GroupBy(pair => SeriesGroupKey(pair.Episode!, episodes.Select(item => item.Episode!)), ByName)
            .Select(group =>
            {
                var ordered = InEpisodeOrder(group).ToList();
                var seasons = ordered.Select(pair => pair.Episode!.Season).Distinct().Count();
                return new LibraryGroup(group.Key,
                    $"{seasons} {(seasons == 1 ? "season" : "seasons")} · {ordered.Count} {(ordered.Count == 1 ? "episode" : "episodes")}",
                    [.. ordered.Select(pair => pair.Entry)]);
            })
            .OrderBy(group => group.Name, ByName)];
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

    /// <summary>By season; in each, numbered episodes by number, then unnumbered ones by file name in natural order, then by path.</summary>
    private static IOrderedEnumerable<(LibraryEntry Entry, VideoEpisode? Episode)> InEpisodeOrder(IEnumerable<(LibraryEntry Entry, VideoEpisode? Episode)> pairs) =>
        pairs.OrderBy(pair => pair.Episode!.Season)
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

        var name = Path.GetFileNameWithoutExtension(entry.Path.Replace('\\', '/'));
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

        var name = Path.GetFileNameWithoutExtension(entry.Path.Replace('\\', '/'));
        var match = MovieYearPattern().Match(name);
        return match.Success && int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year) ? year : null;
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
        [.. entries.Where(entry => entry.LastPlayed is not null).OrderByDescending(entry => entry.LastPlayed).Take(Lately)];

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
        return played.Take(Lately).ToArray();
    }

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
        return [.. Songs(found), .. Videos(found), .. Pictures(found)];
    }

    private static bool Matches(LibraryEntry entry, string word)
    {
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
        return new[] { entry.Title, entry.Artist, entry.AlbumArtist, entry.Album, entry.Genre, Path.GetFileName(entry.Path) }
            .Any(field => field is not null && Compare.IndexOf(field, word, Loose) >= 0);
    }

    [GeneratedRegex(@"^(?<title>.+?)[ ._(\-]+(?<year>19\d{2}|20\d{2})(?=[ ._)\-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieYearPattern();

    [GeneratedRegex(@"[ ._-]+(?:2160p|1080p|720p|480p|BluRay|WEBRip|WEB[.-]DL)(?=[ ._-]|$).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualitySuffixPattern();

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
