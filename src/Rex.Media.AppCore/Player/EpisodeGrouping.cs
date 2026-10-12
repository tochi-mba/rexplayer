using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Player;

/// <summary>The show and season that qualify an episode for comparison with its siblings.</summary>
public sealed record EpisodeIdentity(string Show, int Season, int Episode);

/// <summary>Conservatively groups numbered series episodes and excludes specials, samples and extras.</summary>
public static class EpisodeGrouping
{
    private static readonly Regex EpisodeName = new(
        @"(?i)(?:^|[\s._-])(?:s(?<season>\d{1,2})e(?<episode>\d{1,3})|(?<season>\d{1,2})x(?<episode>\d{1,3}))(?:[\s._-]|$)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    public static EpisodeIdentity? Identify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var filename = Path.GetFileNameWithoutExtension(path);
        var match = EpisodeName.Match(filename);
        if (!match.Success || !int.TryParse(match.Groups["season"].Value, out var season) ||
            !int.TryParse(match.Groups["episode"].Value, out var episode) || episode == 0 || season > 99)
        {
            return null;
        }

        var title = filename[..match.Index].Replace('.', ' ').Replace('_', ' ').Trim(' ', '-', '_');
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
            // A season directory alone does not establish the identity of a show.
            if (Regex.IsMatch(title, @"(?i)^season\s*\d+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                title = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) ?? "";
            }
        }

        title = Regex.Replace(title, @"\s+", " ").Trim();
        if (title.Length < 2 || Regex.IsMatch(filename, @"(?i)(?:^|[. _-])(sample|trailer|extra|special)(?:[. _-]|$)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            return null;
        }

        return new EpisodeIdentity(title.ToUpperInvariant(), season, episode);
    }

    public static IReadOnlyList<string> Siblings(string target, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var identity = Identify(target);
        if (identity is null)
        {
            return [];
        }

        return candidates.Where(path => Identify(path) is { } other &&
                other.Season == identity.Season &&
                other.Episode != identity.Episode &&
                string.Equals(other.Show, identity.Show, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
