namespace Rex.Media.AppCore.Library;

/// <summary>
/// The fact lines under a library view's title, in one voice (LIB-05): parts joined by a middle dot,
/// counts with the thousands separator and never a count of nothing, lengths as "48 min", "2 h" or
/// "3 h 12 min". Pure; the window only shows the words.
/// </summary>
public static class ViewFacts
{
    /// <summary>What goes between two facts.</summary>
    public const string Separator = " · ";

    /// <summary>The facts that are known, in order, joined by <see cref="Separator"/>; empty for none.</summary>
    public static string Join(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(Separator, parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>"1 song", "1,204 songs"; null for none, so a line leaves it out rather than saying "0 songs".</summary>
    public static string? CountIfAny(int count, string noun) => count > 0 ? LibraryActions.Count(count, noun) : null;

    /// <summary>How long entries last together ("48 min", "2 h", "3 h 12 min"); empty when none has a length.</summary>
    public static string Length(IEnumerable<TimeSpan?> durations)
    {
        ArgumentNullException.ThrowIfNull(durations);
        return StreamingBrowse.Length(TimeSpan.FromTicks(durations.Sum(duration => duration?.Ticks ?? 0)));
    }

    /// <summary>A view's count and length together: "3 books · 1 h 30 min", "No books" when there are none.</summary>
    public static string CountAndLength(int count, string noun, IEnumerable<TimeSpan?> durations)
    {
        ArgumentNullException.ThrowIfNull(noun);
        return count == 0 ? "No " + noun + "s" : Join(LibraryActions.Count(count, noun), Length(durations));
    }

    /// <summary>
    /// What Home says the library holds: each kind it has, listening shelves included, and none it
    /// lacks ("3 songs · 1 podcast"); "Nothing here yet" for an empty library.
    /// </summary>
    public static string Home(int songs, int videos, int pictures, int podcasts, int audiobooks, int recordings)
    {
        var line = Join(CountIfAny(songs, "song"), CountIfAny(videos, "video"), CountIfAny(pictures, "picture"),
            CountIfAny(podcasts, "podcast"), CountIfAny(audiobooks, "audiobook"), CountIfAny(recordings, "recording"));
        return line.Length > 0 ? line : "Nothing here yet";
    }

    /// <summary>
    /// Whether the Songs table gives plays a column: only when one of its songs has been played, so an
    /// unplayed library gives that width to the titles and albums instead of an empty column.
    /// </summary>
    public static bool SongsShowPlays(IEnumerable<int> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);
        return plays.Any(count => count > 0);
    }
}
