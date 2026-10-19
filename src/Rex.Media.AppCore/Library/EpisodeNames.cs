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
/// </summary>
public static partial class EpisodeNames
{
    /// <summary>Folder names that hold a library, not a show: a season folder directly inside one names no show.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Movies", "Movie", "Films", "Film", "Videos", "Video", "My Videos", "TV", "TV Shows", "TV Series", "Shows",
        "Series", "Downloads", "Download", "Media", "Library", "Anime", "Cartoons", "Kids",
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
        var folder = SeasonFolderPattern().Match(parent);
        int? folderSeason = folder.Success ? folder.Groups["specials"].Success ? 0 : Number(folder.Groups["season"]) : null;
        var folderShow = !folder.Success ? null : folder.Groups["show"].Success ? folder.Groups["show"].Value : Named(above) ? above : null;
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
            return Episode(Clean(prefix).Length > 0 ? prefix : folderShow ?? looseShow, Number(mark.Groups["season"]), mark);
        }

        mark = KeywordPattern().Match(name);
        if (mark.Success && (folderSeason is not null || (mark.Groups["episode"].Length > 1 || !mark.Groups["bare"].Success) && !YearPattern().IsMatch(name[(mark.Index + mark.Length)..])))
        {
            var prefix = name[..mark.Index];
            var season = mark.Groups["season"].Success ? Number(mark.Groups["season"]) : folderSeason ?? 1;
            return Episode(folderShow ?? (Clean(prefix).Length > 0 ? prefix : looseShow), season, mark);
        }

        if (folderSeason is not { } inFolder)
        {
            return null;
        }

        // In a season folder without a mark: a number after the show's name orders the episode, if there is one.
        var show = folderShow ?? (name.IndexOf(" - ", StringComparison.Ordinal) is > 0 and var dash ? name[..dash] : null);
        var rest = show is not null && name.Replace('.', ' ').Replace('_', ' ').StartsWith(Clean(show), StringComparison.OrdinalIgnoreCase) ? name[Clean(show).Length..] : name;
        return Episode(show, inFolder, null, CompactEpisode(rest, inFolder) ?? PlainNumber(rest));
    }

    private static VideoEpisode? Episode(string? rawSeries, int season, Match? mark, int? number = null)
    {
        var qualified = SeriesYearPattern().Match(rawSeries ?? "");
        var series = Clean(qualified.Success ? qualified.Groups["series"].Value : rawSeries ?? "");
        if (series.Length == 0)
        {
            return null;
        }

        int? episode = mark is null ? number : Number(mark.Groups["episode"]);
        int? through = mark is not null && mark.Groups["through"].Success && Number(mark.Groups["through"]) is var last && last > episode ? last : null;
        return new VideoEpisode(series, season, episode, qualified.Success ? Number(qualified.Groups["year"]) : null) { Through = through };
    }

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

    /// <summary>A folder that can name a show: not empty, not a drive, not a library's own folder and not a season.</summary>
    private static bool Named(string folder) =>
        folder.Length > 0 && !folder.EndsWith(':') && !Generic.Contains(folder) && !SeasonFolderPattern().IsMatch(folder);

    private static int Number(Group group) => Number(group.Value);

    private static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Clean(string raw) =>
        Separators().Replace(LeadingTagPattern().Replace(raw, ""), " ").Trim(' ', '-', '(', ')');

    /// <summary>"Season 6", "S06", "Series 6", "Staffel 6", "Saison 6", "Specials", optionally after the show's name.</summary>
    [GeneratedRegex(@"^(?:(?<show>.*?\S)[ ._-]+)?(?:(?:season|series|staffel|saison|temporada)[ ._-]*(?<season>\d{1,3})|s(?<season>\d{1,3})|(?<specials>specials?))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonFolderPattern();

    /// <summary>"S06E01", "s6e1", "S06 E01", "S06E01-E02", "S06E01E02", "S06E01-02".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])s(?<season>\d{1,3})[ ._-]?e(?<episode>\d{1,4})(?:(?:-?e|-)(?<through>\d{1,4}))*(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex SeasonEpisodePattern();

    /// <summary>"6x01", "6x01-02", "6x01x02", "6x01-6x02"; never a picture size such as "1920x1080".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<season>\d{1,2})x(?<episode>\d{1,3})(?:(?:-?x|-(?:\d{1,2}x)?)(?<through>\d{1,3}))*(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex CrossPattern();

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
