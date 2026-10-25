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
    /// orders them by Most played (ties keep the Songs view's order). Long mixes and sets
    /// (<see cref="AudioRules.IsLongMix"/>) are left out, so one long play does not skew it.
    /// </summary>
    /// <param name="entries">The library's entries.</param>
    /// <param name="count">How many the shelf holds.</param>
    public static IReadOnlyList<LibraryEntry> MostPlayed(IEnumerable<LibraryEntry> entries, int count)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Only played songs are ordered: a subset keeps the Songs view's relative order.
        var played = LibraryViews.Songs(AudioRules.Ranked(entries.Where(entry => entry.Plays > 0)));
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

    /// <summary>
    /// "Downloading": the files still arriving in the watched folders, newest first; empty, and so
    /// not shown, once every file is whole.
    /// </summary>
    /// <param name="entries">The library's entries.</param>
    /// <param name="count">How many the shelf holds.</param>
    public static IReadOnlyList<LibraryEntry> Downloading(IEnumerable<LibraryEntry> entries, int count)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. LibraryArrangement.Sort([.. entries.Where(entry => entry.Downloading)], LibrarySort.Added, descending: false).Take(count)];
    }

    /// <summary>
    /// The view "See all" opens from the Downloading shelf: the one that holds most of what is
    /// downloading (Videos, Songs or Pictures; Videos when nothing is), filtered to Still downloading
    /// and ordered newest first, the way the shelf shows it, with no headings.
    /// </summary>
    /// <param name="downloading">What the shelf shows.</param>
    /// <param name="kept">How the user keeps each view, by its name.</param>
    public static (string View, LibraryViewChoice Choice) SeeAllDownloading(IReadOnlyList<LibraryEntry> downloading, Func<string, LibraryViewChoice> kept)
    {
        ArgumentNullException.ThrowIfNull(downloading);
        ArgumentNullException.ThrowIfNull(kept);
        var view = downloading.GroupBy(entry => entry.Kind).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault(LibraryKind.Video) switch
        {
            LibraryKind.Music => "Songs",
            LibraryKind.Picture => "Pictures",
            _ => "Videos",
        };
        return (view, kept(view) with { Download = LibraryDownloadFilter.Downloading, Sort = LibrarySort.Added, Descending = false, Grouping = LibraryGrouping.None });
    }
}
