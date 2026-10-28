using System.Globalization;
using System.Text.RegularExpressions;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>Per-episode viewing history aggregated independently for a season or a whole show.</summary>
public sealed record EpisodeWatchSummary(int Total, int Completed, int InProgress)
{
    /// <summary>A season or show is complete only if all of its episodes are actually completed.</summary>
    public bool IsComplete => Total > 0 && Completed == Total;
}

/// <summary>
/// A show's own page: which season it opens on, the episode to go on with and what Play says,
/// and how each episode line reads (its number, its title, where it was left and how much of it
/// was watched). Pure decisions over the library's entries; the window only draws them.
/// </summary>
public static partial class ShowPage
{
    /// <summary>
    /// Count episode completion independently from its season or show. Left-part-way entries
    /// cannot be counted as watched even if they have an earlier play count.
    /// </summary>
    public static EpisodeWatchSummary WatchSummary(IReadOnlyList<LibraryEntry> episodes, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(leftAt);
        // Files and episodes are not interchangeable. A two-episode release
        // contributes two episodes, while two video resolutions of the same
        // episode contribute one. The completed version wins over an unfinished
        // alternative, without erasing the partial file's actual resume point.
        var status = new Dictionary<string, (bool Completed, bool InProgress)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in episodes)
        {
            var paused = leftAt(entry.Path) is not null;
            var finished = !paused && entry.Plays > 0;
            void Record(string key)
            {
                status.TryGetValue(key, out var previous);
                status[key] = (previous.Completed || finished, previous.InProgress || paused);
            }

            if (LibraryViews.EpisodeOf(entry) is { Episode: { } first } episode)
            {
                // Refuse an implausible giant range from malformed filenames.
                var last = episode.Through is { } through && through >= first && through - first <= 25
                    ? through : first;
                for (var n = first; n <= last; n++)
                {
                    Record($"{ShowNames.Key(episode.Series)}\u001f{episode.Season}\u001f{episode.SpecialOf}\u001f{n}");
                }
            }
            else
            {
                // Unnumbered specials have no episode identity, so each real
                // file remains a separately browsable and countable entry.
                Record("file:" + entry.Path);
            }
        }

        return new EpisodeWatchSummary(status.Count, status.Values.Count(value => value.Completed),
            status.Values.Count(value => !value.Completed && value.InProgress));
    }

    /// <summary>
    /// The episode to play next: the one left part-way last, else the first unseen one (the regular
    /// seasons before the specials), else the first.
    /// </summary>
    public static LibraryEntry? NextUp(LibraryGroup show, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(leftAt);
        // An unreadable episode must not become the show's primary Play/Resume target.
        // Keep its row visible so the user can inspect the error instead.
        var ordered = show.Entries.Where(entry => entry.Problem is null)
            .OrderBy(entry => LibraryViews.EpisodeOf(entry)?.Season == 0).ToList();
        return LibraryViews.ContinueWatching(ordered, leftAt).FirstOrDefault()
            ?? ordered.FirstOrDefault(entry => entry.Plays == 0)
            ?? ordered.FirstOrDefault();
    }

    /// <summary>The season a show opens on: the one holding <paramref name="nextUp"/>, else the first.</summary>
    public static LibraryGroup? DefaultSeason(IReadOnlyList<LibraryGroup> seasons, LibraryEntry? nextUp)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        return seasons.FirstOrDefault(season => nextUp is not null && season.Entries.Any(entry => LibraryViews.SameLocation(entry.Path, nextUp.Path)))
            ?? seasons.FirstOrDefault();
    }

    /// <summary>
    /// Follow the same season through an indexing or metadata refresh, but use
    /// the newest entries rather than the stale objects that were displayed before
    /// the change. If the opened season disappeared, choose the next playable one.
    /// </summary>
    public static LibraryGroup? RefreshSeason(IReadOnlyList<LibraryGroup> seasons, LibraryGroup? opened, LibraryEntry? nextUp)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        return (opened is null ? null : seasons.FirstOrDefault(season =>
            string.Equals(season.Name, opened.Name, StringComparison.OrdinalIgnoreCase)))
            ?? DefaultSeason(seasons, nextUp);
    }

    /// <summary>A season as the picker names it: "Season 2", and season zero by what it holds, "Specials".</summary>
    public static string SeasonPickerLabel(LibraryGroup season)
    {
        ArgumentNullException.ThrowIfNull(season);
        return season.Name;
    }

    /// <summary>
    /// The show page's viewing line: how much of the season shown and of the series has been watched,
    /// once each ("1 of 2 watched this season · 1 of 3 in the series"); one figure when the season is
    /// the whole series, and empty for a show with no episodes.
    /// </summary>
    public static string ViewingLine(EpisodeWatchSummary? season, EpisodeWatchSummary series)
    {
        ArgumentNullException.ThrowIfNull(series);
        static string Going(EpisodeWatchSummary summary) => summary.InProgress > 0 && !summary.IsComplete ? $", {summary.InProgress} in progress" : "";
        if (series.Total == 0)
        {
            return "";
        }

        if (season is null || season.Total == 0 || season.Total == series.Total)
        {
            var noun = series.Total == 1 ? "episode" : "episodes";
            return (series.IsComplete ? $"All {series.Total} {noun} watched" : $"{series.Completed} of {series.Total} {noun} watched") + Going(series);
        }

        return ViewFacts.Join(
            (season.IsComplete ? "Season watched" : $"{season.Completed} of {season.Total} watched this season") + Going(season),
            $"{series.Completed} of {series.Total} in the series");
    }

    /// <summary>
    /// What the page's Play button says: "Resume S01 E03", "Play S01 E01", "Play Special 2", or just "Play"
    /// when the episode has no number to name (a bare "S01" would read as the whole season).
    /// </summary>
    public static string PlayLabel(LibraryEntry? nextUp, bool resumes)
    {
        if (nextUp is null)
        {
            return "Play";
        }

        var episode = LibraryViews.EpisodeOf(nextUp);
        return (resumes ? "Resume" : "Play") + (episode is null || (episode.Episode is null && episode.Season != 0) ? "" : " " + episode.Badge);
    }

    /// <summary>
    /// How much of an episode has been watched, 0 to 1: where it was left for one left part-way,
    /// all of it for one played to the end, nothing for the rest.
    /// </summary>
    public static double Progress(LibraryEntry entry, TimeSpan? leftAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (leftAt is { } left)
        {
            return entry.Duration is { TotalSeconds: > 0 } length ? Math.Clamp(left / length, 0, 1) : 0;
        }

        return entry.Plays > 0 ? 1 : 0;
    }

    /// <summary>The number beside an episode: "3", "1-2" for a double episode, empty when it has none.</summary>
    public static string Number(VideoEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return episode.Episode is not { } first ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{first}{(episode.Through is { } last ? $"-{last}" : "")}");
    }

    /// <summary>
    /// An episode's title: its embedded title when the file has one, else the words after its mark in
    /// the file name ("Show.S01E03.The.Long.Night.1080p" reads "The Long Night"), else "Episode 3" ("Special 3").
    /// </summary>
    public static string EpisodeTitle(LibraryEntry entry, VideoEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(episode);
        if (entry.Title != MediaLibrary.TitleOf(entry.Path))
        {
            return entry.Title;
        }

        var name = Path.GetFileNameWithoutExtension(entry.Path.Replace('\\', '/'));
        var mark = MarkPattern().Match(name);
        if (mark.Success)
        {
            var rest = ReleaseTagsPattern().Replace(name[(mark.Index + mark.Length)..], "");
            rest = Separators().Replace(rest, " ").Trim(' ', '-');
            if (rest.Length > 0)
            {
                return rest;
            }
        }

        return episode.Episode is null ? Separators().Replace(name, " ").Trim()
            : episode.Season == 0 ? $"Special {Number(episode)}"
            : episode.Through is not null ? $"Episodes {Number(episode)}" : $"Episode {Number(episode)}";
    }

    /// <summary>The line under an episode's title: its season and number, how long it runs, then where it was left or that it was watched.</summary>
    public static string EpisodeDetail(LibraryEntry entry, VideoEpisode? episode, TimeSpan? leftAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Problem is { } problem)
        {
            return "Could not be read: " + problem;
        }

        var watched = leftAt is { } left ? "Stopped at " + TimeText.Format(left, entry.Duration ?? TimeSpan.Zero)
            : entry.Plays > 0 ? "Watched" : null;
        return ViewFacts.Join(episode?.Spoken, StreamingBrowse.Length(entry.Duration), watched);
    }

    /// <summary>The page's subtitle: how many seasons and episodes, and how long the season shown lasts.</summary>
    public static string Subtitle(LibraryGroup show, LibraryGroup? season)
    {
        ArgumentNullException.ThrowIfNull(show);
        return ViewFacts.Join(show.Detail, season is null ? "" : ViewFacts.Length(season.Entries.Select(entry => entry.Duration)));
    }

    /// <summary>An episode mark: "S01E03", "S01E01-E02", "1x03", "Episode 3", "Ep 03", "E03", "S07X01", "S07 Special 1", "SP01", "OVA 2".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:s\d{1,3}[ ._-]?e\d{1,4}(?:(?:-?e|-)\d{1,4})*|s\d{1,3}(?:[ ._-]?(?:x|sp)\d{1,3}|[ ._-]*special(?:[ ._-]*\d{1,3})?|\.5)|sp\d{1,3}|(?:ova|oad)(?:[ ._-]*\d{1,3})?|\d{1,2}x\d{1,3}(?:(?:-?x|-)\d{1,3})*|(?:episode|ep)[ ._-]*\d{1,4}|e\d{1,4})(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkPattern();

    /// <summary>What a release adds after the title: picture size, source, codecs and a group's tag, to the end.</summary>
    [GeneratedRegex(@"(?:[ ._-]+|^)(?:\[[^\]]*\]|2160p|1080p|720p|480p|4k|BluRay|BDRip|WEBRip|WEB[.-]?DL|HDTV|x26[45]|h[.]?26[45]|HEVC|AAC|DDP?5[.]1|PROPER|REPACK)(?![\p{L}\p{N}]).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagsPattern();

    [GeneratedRegex("[._]+")]
    private static partial Regex Separators();
}
