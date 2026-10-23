using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Reads a video's show, season and episode from its path alone, without an online catalogue (LIB-05).
/// <para>
/// A season folder ("Season 6", "S06", "Series 6", "Staffel 6", "Saison 6", "Specials" or "Season 0",
/// alone or after the show's name) is strong evidence: every video in it is an episode, whatever its
/// ancestors are called ("Movies", "Films", "Downloads"), and the show is named by the folder above it.
/// The episode number then comes from a mark in the filename, a compact "601" or "0601" that repeats
/// the season, or a plain number, and is left unknown (natural order) when there is none.
/// </para>
/// <para>
/// Without a season folder the filename must carry a conventional mark: "S06E01", "s6e1", "6x01",
/// "Season 2 Episode 5", "Episode 1", "Ep 01" or "E01", double episodes ("S06E01-E02", "S06E01E02",
/// "6x01-02") included. A year after an "Episode" mark ("Saga Episode 4 - Title (1977)") says movie.
/// </para>
/// <para>
/// Specials go in the show's Specials (season 0), never among a season's numbered episodes, so a
/// season's count and order stay its broadcast run and "special 1" never sorts beside "episode 1".
/// A special that names its season ("S07X01", "S07 X01", "S07SP01", "S07 Special 1", "S07E00",
/// "S07.5") keeps that season in <see cref="VideoEpisode.SpecialOf"/>, which labels it "S07 Special 1".
/// A special without a season ("S00E05", "SP01", "Special", "OVA", "OAD") and a video in an "Extras",
/// "Featurettes" or "Behind the Scenes" folder is a special too. Outside a season folder those are
/// <see cref="VideoEpisode.Tentative"/>: a movie has extras and "Special" titles as well, so they join
/// a show only when the library already has that show (<see cref="LibraryViews.TvShows"/>).
/// </para>
/// </summary>
public static partial class EpisodeNames
{
    /// <summary>Folder names that hold a library, not a show: a season folder directly inside one names no show.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Movies", "Movie", "Films", "Film", "Videos", "Video", "My Videos", "TV", "TV Shows", "TV Series", "Shows",
        "Series", "Downloads", "Download", "Media", "Library", "Anime", "Cartoons", "Kids",
    };

    /// <summary>Folders of bonus material beside a show's (or a movie's) videos.</summary>
    private static readonly HashSet<string> ExtrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Extras", "Extra", "Featurettes", "Featurette", "Behind the Scenes", "Special Features", "Deleted Scenes", "Bonus",
    };

    /// <summary>The show, season and episode <paramref name="path"/> names, or null for a video that is not an episode.</summary>
    public static VideoEpisode? Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(parts[^1]);
        var parent = parts.Length > 1 ? parts[^2] : "";
        var above = parts.Length > 2 ? parts[^3] : "";
        if (ExtrasFolders.Contains(parent))
        {
            return Extra(above, parts.Length > 3 ? parts[^4] : "");
        }

        // Disc/release folders often append a quality tag to an otherwise exact season
        // name: "S00 (360p)", "Season 00 [720p]" and "Specials (480p)".
        var seasonName = ReleaseFolderSuffixPattern().Replace(parent, "");
        var folder = SeasonFolderPattern().Match(seasonName);
        if (!folder.Success)
        {
            folder = EmbeddedSeasonFolderPattern().Match(parent);
        }

        int? folderSeason = folder.Success ? folder.Groups["specials"].Success ? 0 : Number(folder.Groups["season"]) : null;
        var folderShow = !folder.Success ? null : folder.Groups["show"].Success
            ? folder.Groups["show"].Value : Named(above) ? ShowFolder(above) : null;
        var looseShow = Named(parent) && !folder.Success ? parent : null;

        // The conventional marks name the series before them, which is the most specific statement of the show.
        var mark = SeasonEpisodePattern().Match(name);
        if (!mark.Success)
        {
            mark = CrossPattern().Match(name);
        }

        if (mark.Success)
        {
            var prefix = name[..mark.Index];
            // A named season directory is stronger evidence for the show's identity than a
            // bonus episode's filename (e.g. "Tracy Ullman Shorts S00E01" inside The Simpsons).
            return Episode(folderShow ?? (Clean(prefix).Length > 0 ? prefix : looseShow), Number(mark.Groups["season"]), Optional(mark.Groups["episode"]), Optional(mark.Groups["through"]));
        }

        // A special that names its season: "S07X01", "S07 SP01", "S07 Special 1", "S07.5".
        mark = SeasonSpecialPattern().Match(name);
        if (mark.Success)
        {
            var prefix = name[..mark.Index];
            return Episode(folderShow ?? (Clean(prefix).Length > 0 ? prefix : looseShow), 0, Optional(mark.Groups["episode"]), specialOf: Number(mark.Groups["season"]));
        }

        mark = KeywordPattern().Match(name);
        if (mark.Success && (folderSeason is not null || (mark.Groups["episode"].Length > 1 || !mark.Groups["bare"].Success) && !YearPattern().IsMatch(name[(mark.Index + mark.Length)..])))
        {
            var prefix = name[..mark.Index];
            var season = mark.Groups["season"].Success ? Number(mark.Groups["season"]) : folderSeason ?? 1;
            return Episode(folderShow ?? (Clean(prefix).Length > 0 ? prefix : looseShow), season, Optional(mark.Groups["episode"]), Optional(mark.Groups["through"]));
        }

        // Release folders commonly carry the series once and use a bare episode number in each file:
        // "Show (1080p) V2\Show - 01 - Pilot" and "Show\Show 1 [release]". The numbered
        // members establish the show; an unnumbered member in the same folder is tentative and joins
        // only when one of those firm members exists (for example an anime opening named "Show (sigla)").
        if (folderSeason is null && FolderSeries(parent) is { } folderSeries)
        {
            if (!SeriesYearPattern().IsMatch(parent) && StartsWithSeries(name, folderSeries) &&
                SpecialPresentationPattern().IsMatch(name[folderSeries.Length..]))
            {
                return Episode(folderSeries, 0, null);
            }

            var numbered = FolderEpisodePattern().Match(name);
            if (numbered.Success && SameSeries(numbered.Groups["show"].Value, folderSeries))
            {
                return Episode(folderSeries, 1, Number(numbered.Groups["episode"]));
            }

            if (!SeriesYearPattern().IsMatch(parent) && StartsWithSeries(name, folderSeries))
            {
                return Episode(folderSeries, 1, null, tentative: true);
            }
        }

        // A special without a season: "SP01", "Special 2", "OVA", "OAD". Only a season folder makes it certain.
        var special = SpecialPattern().Match(name);
        if (folderSeason is not { } inFolder)
        {
            if (special.Success)
            {
                return Episode(looseShow ?? name[..special.Index], 0, Optional(special.Groups["episode"]), tentative: true);
            }

            // Some one-off continuations carry only a season mark: "Show S05 The Finale". A named
            // prefix and descriptive suffix make this stronger than "Show.S01.576p" release text.
            mark = SeasonOnlyPattern().Match(name);
            var tail = mark.Success ? name[(mark.Index + mark.Length)..] : "";
            return mark.Success && Clean(name[..mark.Index]).Length > 0 && Clean(tail).Length > 0
                && !ReleaseOnlyTailPattern().IsMatch(tail)
                    ? Episode(name[..mark.Index], Number(mark.Groups["season"]), null) : null;
        }

        // In a season folder without a mark: a number after the show's name orders the episode, if there is one.
        var show = folderShow ?? (name.IndexOf(" - ", StringComparison.Ordinal) is > 0 and var dash ? name[..dash] : null);
        var rest = show is not null && name.Replace('.', ' ').Replace('_', ' ').StartsWith(Clean(show), StringComparison.OrdinalIgnoreCase) ? name[Clean(show).Length..] : name;
        var number = CompactEpisode(rest, inFolder) ?? PlainNumber(rest);
        if (special.Success && (special.Groups["episode"].Success || number is null))
        {
            // "Holiday Special" in season 7's folder is its special; "05 - Holiday Special" is still episode 5.
            return Episode(show, 0, Optional(special.Groups["episode"]), specialOf: inFolder);
        }

        return Episode(show, inFolder, number);
    }

    /// <summary>
    /// A video in an extras folder: a special of the season folder above it, certainly a show's; or of
    /// the folder above it, which may be a movie's, so it joins only a show the library already has.
    /// </summary>
    private static VideoEpisode? Extra(string above, string aboveThat)
    {
        var folder = SeasonFolderPattern().Match(ReleaseFolderSuffixPattern().Replace(above, ""));
        if (!folder.Success)
        {
            folder = EmbeddedSeasonFolderPattern().Match(above);
        }

        if (!folder.Success)
        {
            return Named(above) ? Episode(ShowFolder(above), 0, null, tentative: true) : null;
        }

        var show = folder.Groups["show"].Success ? folder.Groups["show"].Value : Named(aboveThat) ? ShowFolder(aboveThat) : null;
        return Episode(show, 0, null, specialOf: folder.Groups["specials"].Success ? null : Number(folder.Groups["season"]));
    }

    private static VideoEpisode? Episode(string? rawSeries, int season, int? episode, int? through = null, int? specialOf = null, bool tentative = false)
    {
        var qualified = SeriesYearPattern().Match(rawSeries ?? "");
        var series = Clean(qualified.Success ? qualified.Groups["series"].Value : rawSeries ?? "");
        if (series.Length == 0)
        {
            return null;
        }

        if (season > 0 && episode == 0)
        {
            // "S07E00": the special that goes with season 7.
            (specialOf, season, episode, through) = (season, 0, null, null);
        }

        return new VideoEpisode(series, season, episode, qualified.Success ? Number(qualified.Groups["year"]) : null)
        {
            Through = through > episode ? through : null,
            SpecialOf = specialOf is > 0 ? specialOf : null,
            Tentative = tentative,
        };
    }

    private static int? Optional(Group group) => group.Success ? Number(group) : null;

    /// <summary>"601" or "0601" in season 6's folder: the season's number, then two digits of episode. Years never count.</summary>
    private static int? CompactEpisode(string name, int season)
    {
        foreach (Match match in CompactPattern().Matches(name))
        {
            var digits = match.Value;
            if (Number(match) is not (>= 1900 and < 2100) && Number(digits[..^2]) == season)
            {
                return Number(digits[^2..]);
            }
        }

        return null;
    }

    private static int? PlainNumber(string name) => PlainNumberPattern().Match(name) is { Success: true } match ? Number(match) : null;

    private static string? FolderSeries(string folder)
    {
        if (!Named(folder))
        {
            return null;
        }

        var release = ReleaseFolderSuffixPattern().Match(folder);
        return Clean(release.Success ? folder[..release.Index] : folder) is { Length: > 0 } series ? series : null;
    }

    private static bool SameSeries(string candidate, string series) =>
        string.Equals(ShowNames.Key(Clean(candidate)), ShowNames.Key(series), StringComparison.Ordinal);

    private static bool StartsWithSeries(string name, string series) =>
        Clean(name).StartsWith(Clean(series), StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder that can name a show: not empty, not a drive, not a library's own folder and not a season.</summary>
    private static bool Named(string folder) =>
        folder.Length > 0 && !folder.EndsWith(':') && !Generic.Contains(folder) && !ExtrasFolders.Contains(folder)
            && !SeasonFolderPattern().IsMatch(ReleaseFolderSuffixPattern().Replace(folder, ""));

    /// <summary>Strip a multi-season collection suffix, not the title of the show itself.</summary>
    private static string ShowFolder(string folder)
    {
        var collection = SeasonCollectionFolderPattern().Match(folder);
        return collection.Success ? collection.Groups["show"].Value : folder;
    }

    private static int Number(Group group) => Number(group.Value);

    private static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Clean(string raw) =>
        Separators().Replace(LeadingTagPattern().Replace(raw, ""), " ").Trim(' ', '-', '(', ')');

    /// <summary>"Season 6", "S06", "Series 6", "Staffel 6", "Saison 6", "Specials", optionally after the show's name.</summary>
    [GeneratedRegex(@"^(?:(?<show>.*?\S)[ ._-]+)?(?:(?:season|series|staffel|saison|temporada)[ ._-]*(?<season>\d{1,3})|s(?<season>\d{1,3})|(?<specials>specials?))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonFolderPattern();

    /// <summary>A multi-season release root such as "The Simpsons S00-S09 (1989-) + Shorts".</summary>
    [GeneratedRegex(@"^(?<show>.+?\S)[ ._-]+s\d{1,3}\s*[-–]\s*s?\d{1,3}(?=$|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonCollectionFolderPattern();

    /// <summary>A release folder with a season marker followed by descriptive text, such as "Show S00 Collection (240p)".</summary>
    [GeneratedRegex(@"^(?<show>.*?\S)[ ._-]+s(?<season>\d{1,3})(?=$|[ ._()\[\]-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex EmbeddedSeasonFolderPattern();

    /// <summary>A bare episode number after the folder's series name, separated from surrounding title or release text.</summary>
    [GeneratedRegex(@"^(?<show>.+?)(?:[ ._-]+)(?<episode>\d{1,3})(?=$|[ ._()\[\]-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex FolderEpisodePattern();

    /// <summary>Quality and revision decorations appended to a torrent or release folder's series name.</summary>
    [GeneratedRegex(@"\s*(?:\((?:\d{3,4}p|[^)]*(?:bluray|web[- .]?dl|dvd|remux)[^)]*)\)|\[(?:\d{3,4}p)[^]]*\])(?:\s+v\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseFolderSuffixPattern();

    /// <summary>Named one-off productions filed with their show rather than numbered broadcast episodes.</summary>
    [GeneratedRegex(@"(?:^\s+presents\b|\buntold[ ._-]+story\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialPresentationPattern();

    /// <summary>"S06E01", "s6e1", "S06 E01", "S06E01-E02", "S06E01E02", "S06E01-02".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])s(?<season>\d{1,3})[ ._-]?e(?<episode>\d{1,4})(?:(?:-?e|-)(?<through>\d{1,4}))*(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonEpisodePattern();

    /// <summary>"6x01", "6x01-02", "6x01x02", "6x01-6x02"; never a picture size such as "1920x1080".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<season>\d{1,2})x(?<episode>\d{1,3})(?:(?:-?x|-(?:\d{1,2}x)?)(?<through>\d{1,3}))*(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex CrossPattern();

    /// <summary>"S07X01", "S07 X01", "S07SP01", "S07 Special 1", "S07 Special", "Season 7 Special 2", "S07.5".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:s|season[ ._-]*)(?<season>\d{1,3})(?:[ ._-]?(?:x(?!26[45](?!\d))|sp)(?<episode>\d{1,3})|[ ._-]*special(?:[ ._-]*(?<episode>\d{1,3}))?|\.5)(?![\p{L}\p{N}]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonSpecialPattern();

    /// <summary>"Show S05 The Finale": a season mark without an episode number.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])s(?<season>\d{1,3})(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonOnlyPattern();

    /// <summary>A codec or resolution after S01 is release text, not an episode title.</summary>
    [GeneratedRegex(@"^[ ._-]*(?:\d{3,4}p|[xh][ ._-]?26[45]|hevc|avc|web[ ._-]?(?:dl|rip)|bluray|bdrip|dvdrip)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseOnlyTailPattern();

    /// <summary>"SP01", "Special 2", "OVA", "OVA 3", "OAD", and "Special" last in the name; never "Special Edition" or "Special Delivery".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:sp(?<episode>\d{1,3})|(?:ova|oad)(?:[ ._-]*(?<episode>\d{1,3}))?|special(?:[ ._-]*(?<episode>\d{1,3})|(?=[ ._-]*(?:$|[(\[]))))(?![\p{L}\p{N}]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SpecialPattern();

    /// <summary>"Episode 1", "Ep 01", "Ep.01-02", "E01", optionally after "Season 2" or "S02".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:(?:(?:season|series|staffel|saison)[ ._-]*|s)(?<season>\d{1,3})[ ._-]*)?(?:(?:episode|ep)[ ._-]*(?<episode>\d{1,4})|(?<bare>e)(?<episode>\d{1,4}))(?:-(?:ep?)?(?<through>\d{1,4}))?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex KeywordPattern();

    /// <summary>A standalone three- or four-digit number; not part of "x264", "h.264", "5.1" or "1080p".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}]|\d[.,]|[hx][ ._])\d{3,4}(?![\p{L}\p{N}]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompactPattern();

    /// <summary>A standalone number of up to three digits; not part of "x264", "h.264", "5.1" or "720p".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}]|\d[.,]|[hx][ ._])\d{1,3}(?![\p{L}\p{N}]|[.,]\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumberPattern();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"^(?<series>.+?)[ ._(\[\-]+(?<year>19\d{2}|20\d{2})[\])]?[ ._-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeriesYearPattern();

    /// <summary>A release group's tag before the name: "[Group] Show".</summary>
    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTagPattern();

    [GeneratedRegex("[._]+")]
    private static partial Regex Separators();
}
