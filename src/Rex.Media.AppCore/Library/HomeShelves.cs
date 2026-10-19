using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Home's shelves that have an order of their own (LIB-05), and the view "See all" opens for them,
/// so the view starts with exactly what the shelf showed.
/// </summary>
public static class HomeShelves
{
    /// <summary>
    /// "Your most played": the songs played at least once, most played first, the way the Songs view
    /// orders them by Most played (ties keep the Songs view's order).
    /// </summary>
    /// <param name="entries">The library's entries.</param>
    /// <param name="count">How many the shelf holds.</param>
    public static IReadOnlyList<LibraryEntry> MostPlayed(IEnumerable<LibraryEntry> entries, int count)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Only played songs are ordered: a subset keeps the Songs view's relative order.
        var played = LibraryViews.Songs(entries.Where(entry => entry.Plays > 0));
        return [.. LibraryArrangement.Sort(played, LibrarySort.MostPlayed, descending: false).Take(count)];
    }

    /// <summary>
    /// The Songs view as "See all" opens it from "Your most played": the user's own layout, card size
    /// and filters, ordered by Most played, most first, with no headings to break that order up.
    /// </summary>
    /// <param name="kept">How the user keeps the Songs view.</param>
    public static LibraryViewChoice SeeAllMostPlayed(LibraryViewChoice kept)
    {
        ArgumentNullException.ThrowIfNull(kept);
        return kept with { Sort = LibrarySort.MostPlayed, Descending = false, Grouping = LibraryGrouping.None };
    }
}
