using System.Globalization;
using System.Text.RegularExpressions;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>Entries gathered under one name: an artist's, an album's (with its artist as the detail) or a genre's.</summary>
public sealed record LibraryGroup(string Name, string? Detail, IReadOnlyList<LibraryEntry> Entries);

/// <summary>A season-and-episode mark inferred from a video's filename.</summary>
public sealed record VideoEpisode(string Series, int Season, int Episode)
{
    public string DisplayName => $"{Series} · S{Season:00}E{Episode:00}";
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
    /// The artists, by name, each with their songs. Tag formats can carry several artist values;
    /// rexplayer's canonical metadata joins those with semicolons, so every credited artist gets a
    /// library entry while the song keeps its original combined credit.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Artists(IEnumerable<LibraryEntry> entries) =>
        [.. Songs(entries)
            .SelectMany(entry => ArtistNames(entry.FiledArtist).Select(name => (Name: name, Entry: entry)))
            .GroupBy(pair => pair.Name, ByName)
            .Select(group => new LibraryGroup(MostSpelled(group.Select(pair => pair.Name)), null, [.. group.Select(pair => pair.Entry).Distinct()]))
            .OrderBy(group => group.Name == UnknownArtist)
            .ThenBy(group => group.Name, ByName)];

    /// <summary>The genres, by name, each with its songs.</summary>
    public static IReadOnlyList<LibraryGroup> Genres(IEnumerable<LibraryEntry> entries) => GroupedBy(Songs(entries), entry => entry.Genre, UnknownGenre);

    /// <summary>The videos, by title in natural order ("Episode 2" before "Episode 10").</summary>
    public static IReadOnlyList<LibraryEntry> Videos(IEnumerable<LibraryEntry> entries) =>
        [.. entries.Where(entry => entry.Kind == LibraryKind.Video).OrderBy(entry => entry.Title, NaturalOrder.Instance).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Standalone videos; only a positively identified episode leaves this view.</summary>
    public static IReadOnlyList<LibraryEntry> Movies(IEnumerable<LibraryEntry> entries) =>
        [.. Videos(entries).Where(entry => EpisodeOf(entry) is null)];

    /// <summary>Series inferred locally from filenames, without contacting an online catalogue.</summary>
    public static IReadOnlyList<LibraryGroup> TvShows(IEnumerable<LibraryEntry> entries) =>
        [.. Videos(entries)
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is not null)
            .GroupBy(pair => pair.Episode!.Series, ByName)
            .Select(group =>
            {
                var episodes = group.OrderBy(pair => pair.Episode!.Season)
                    .ThenBy(pair => pair.Episode!.Episode).ThenBy(pair => pair.Entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
                var seasons = episodes.Select(pair => pair.Episode!.Season).Distinct().Count();
                return new LibraryGroup(group.First().Episode!.Series,
                    $"{seasons} {(seasons == 1 ? "season" : "seasons")} · {episodes.Count} {(episodes.Count == 1 ? "episode" : "episodes")}",
                    [.. episodes.Select(pair => pair.Entry)]);
            })
            .OrderBy(group => group.Name, ByName)];

    /// <summary>The seasons of one series in chronological order, with naturally ordered episodes.</summary>
    public static IReadOnlyList<LibraryGroup> TvSeasons(LibraryGroup show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return [.. show.Entries
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is not null)
            .GroupBy(pair => pair.Episode!.Season).OrderBy(group => group.Key)
            .Select(group => new LibraryGroup($"Season {group.Key}",
                $"{group.Count()} {(group.Count() == 1 ? "episode" : "episodes")}",
                [.. group.OrderBy(pair => pair.Episode!.Episode)
                    .ThenBy(pair => pair.Entry.Path, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Entry)]))];
    }

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

    /// <summary>The season and episode a conventional S01E02 filename identifies, or null.</summary>
    public static VideoEpisode? EpisodeOf(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var name = Path.GetFileNameWithoutExtension(entry.Path);
        var match = EpisodePattern().Match(name);
        if (!match.Success)
        {
            match = AlternateEpisodePattern().Match(name);
        }
        if (!match.Success
            || !int.TryParse(match.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var season)
            || !int.TryParse(match.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episode))
        {
            return null;
        }

        var series = Separators().Replace(match.Groups["series"].Value, " ").Trim();
        return series.Length > 0 ? new VideoEpisode(series, season, episode) : null;
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
        var season = entries
            .Where(entry => entry.Kind == LibraryKind.Video && string.Equals(Path.GetDirectoryName(entry.Path) ?? "", folder, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Episode: EpisodeOf(entry)))
            .Where(pair => pair.Episode is { } episode && episode.Season == wanted.Season && episode.Series.Equals(wanted.Series, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Episode!.Episode)
            .ThenBy(pair => pair.Entry.Title, NaturalOrder.Instance)
            .Select(pair => pair.Entry)
            .ToList();
        return season.Any(entry => string.Equals(entry.Path, selected.Path, StringComparison.OrdinalIgnoreCase)) ? season : [selected];
    }

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
        return [.. Songs(found), .. Videos(found), .. Pictures(found)];
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

    /// <summary>"Show.Name.S01E02.mkv", "Show - s1e2", "Show_S01E002": the show, then the season and episode.</summary>
    [GeneratedRegex(@"^(?<series>.+?)[ ._-]+S(?<season>\d{1,3})E(?<episode>\d{1,4})(?=[ ._-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"^(?<series>.+?)[ ._-]+(?<season>\d{1,2})x(?<episode>\d{1,3})(?=[ ._-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlternateEpisodePattern();

    [GeneratedRegex(@"^(?<title>.+?)[ ._(\-]+(?<year>19\d{2}|20\d{2})(?=[ ._)\-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovieYearPattern();

    [GeneratedRegex(@"[ ._-]+(?:2160p|1080p|720p|480p|BluRay|WEBRip|WEB[.-]DL)(?=[ ._-]|$).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualitySuffixPattern();

    [GeneratedRegex("[._]+")]
    private static partial Regex Separators();

    private static IEnumerable<string> ArtistNames(string? credit)
    {
        var names = (credit ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(ByName).ToList();
        return names.Count == 0 ? [UnknownArtist] : names;
    }

    /// <summary>A group's name as most of its songs spell it ("Asake" over one "asake"), the first in order on a tie.</summary>
    private static string MostSpelled(IEnumerable<string?> names) =>
        names.OfType<string>().GroupBy(name => name, StringComparer.Ordinal).OrderByDescending(spelling => spelling.Count()).ThenBy(spelling => spelling.Key, StringComparer.Ordinal).First().Key;
}
