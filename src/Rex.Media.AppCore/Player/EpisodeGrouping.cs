using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// The show and season that qualify an episode for comparison with its siblings. A double episode
/// ("S01E01E02", "S01E01-E02", "S01E01-02") runs from <see cref="Episode"/> through <see cref="Through"/>.
/// </summary>
public sealed record EpisodeIdentity(string Show, int Season, int Episode, int? Through = null)
{
    /// <summary>The last episode number this file holds.</summary>
    public int Last => Through ?? Episode;
}

/// <summary>
/// Conservatively groups numbered series episodes and excludes specials (season 0), samples and
/// extras, whether the file or its folder says so.
/// </summary>
public static class EpisodeGrouping
{
    private static readonly Regex EpisodeName = new(
        @"(?i)(?:^|[\s._-])(?:s(?<season>\d{1,2})e(?<episode>\d{1,3})(?:-?e(?<through>\d{1,3})|-(?<through>\d{1,3}))?|(?<season>\d{1,2})x(?<episode>\d{1,3})(?:-?x(?<through>\d{1,3}))?)(?:[\s._-]|$)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private static readonly Regex ExtraName = new(
        @"(?i)(?:^|[. _-])(sample|trailer|extras?|specials?|featurettes?|bonus|deleted[. _-]scenes?|behind[. _-]the[. _-]scenes|interviews?|making[. _-]of)(?:[. _-]|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static EpisodeIdentity? Identify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var filename = Path.GetFileNameWithoutExtension(path);
        var match = EpisodeName.Match(filename);
        if (!match.Success || !int.TryParse(match.Groups["season"].Value, out var season) ||
            !int.TryParse(match.Groups["episode"].Value, out var episode) || episode == 0 || season is 0 or > 99)
        {
            return null;
        }

        int? through = int.TryParse(match.Groups["through"].Value, out var last) && last > episode ? last : null;
        var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        var title = filename[..match.Index].Replace('.', ' ').Replace('_', ' ').Trim(' ', '-', '_');
        if (string.IsNullOrWhiteSpace(title))
        {
            title = folder;
            // A season directory alone does not establish the identity of a show.
            if (Regex.IsMatch(title, @"(?i)^season\s*\d+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                title = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) ?? "";
            }
        }

        title = Regex.Replace(title, @"\s+", " ").Trim();
        if (title.Length < 2 || ExtraName.IsMatch(filename) || ExtraName.IsMatch(folder))
        {
            return null;
        }

        return new EpisodeIdentity(title.ToUpperInvariant(), season, episode, through);
    }

    /// <summary>Whether <paramref name="other"/> is another episode of the same show and season.</summary>
    public static bool AreSiblings(EpisodeIdentity episode, EpisodeIdentity other)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(other);
        // Two filenames are not independent sibling episodes when their covered ranges overlap.
        // For example, S01E01E02 already contains the standalone S01E02.
        return other.Season == episode.Season &&
            (other.Episode > episode.Last || other.Last < episode.Episode) &&
            string.Equals(other.Show, episode.Show, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other episodes of <paramref name="target"/>'s show and season among <paramref name="candidates"/>,
    /// nearest first (neighbours share a season's title sequence most reliably), one file per episode,
    /// at most <paramref name="limit"/> of them.
    /// </summary>
    public static IReadOnlyList<string> Siblings(string target, IEnumerable<string> candidates, int limit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (Identify(target) is not { } identity)
        {
            return [];
        }

        return candidates.Select(path => (Path: path, Identity: Identify(path)))
            .Where(item => item.Identity is { } other && AreSiblings(identity, other))
            .OrderBy(item => Math.Abs(item.Identity!.Episode - identity.Episode))
            .ThenBy(item => item.Identity!.Episode)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(item => item.Identity!.Episode)
            .Take(limit)
            .Select(item => item.Path).ToList();
    }

    /// <summary>
    /// The episode that follows <paramref name="target"/> among <paramref name="candidates"/>: the
    /// lowest number after the target's last in the same show and season or, after that
    /// season's last episode, the earliest numbered episode in a later available season.
    /// Missing season numbers are not invented and specials remain excluded. Null when there is none.
    /// </summary>
    public static string? Next(string target, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (Identify(target) is not { } identity)
        {
            return null;
        }

        return candidates.Select(path => (Path: path, Identity: Identify(path)))
            .Where(item => item.Identity is { } other && string.Equals(other.Show, identity.Show, StringComparison.Ordinal) &&
                (other.Season == identity.Season ? other.Episode > identity.Last : other.Season > identity.Season))
            .OrderBy(item => item.Identity!.Season).ThenBy(item => item.Identity!.Episode)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path).FirstOrDefault();
    }

    /// <summary>Whether a folder called <paramref name="name"/> holds one season of several ("Season 2", "S02").</summary>
    public static bool IsSeasonFolder(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        Regex.IsMatch(name, @"(?i)^(season\s*\d{1,2}|s\d{1,2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
