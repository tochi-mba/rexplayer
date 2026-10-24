namespace Rex.Media.AppCore.Library;

/// <summary>Which kind of library view has nothing to show, as far as its empty words differ.</summary>
public enum LibraryEmptyView
{
    /// <summary>Home, or a view of the library's media (songs, albums, videos, a group opened).</summary>
    Media,

    /// <summary>Videos left part-way.</summary>
    ContinueWatching,

    /// <summary>What was played lately.</summary>
    RecentlyPlayed,

    /// <summary>The named playlists.</summary>
    Playlists,

    /// <summary>The watched folders.</summary>
    Folders,
}

/// <summary>What an empty library view says, and which ways out it offers under the words.</summary>
/// <param name="Text">The words in the middle of the view.</param>
/// <param name="OffersFolders">Offer to add a folder (and the usual Music, Videos and Pictures folders).</param>
/// <param name="OffersClearFilters">Offer to clear the view's quick filters, which hide everything it holds.</param>
/// <param name="OffersRescan">Offer a direct retry if the last folder scan failed.</param>
public sealed record LibraryEmptyState(string Text, bool OffersFolders, bool OffersClearFilters, bool OffersRescan = false)
{
    /// <summary>
    /// The words for a view with nothing to show (LIB-05). A search that finds nothing says so; a
    /// library with no folders yet explains itself and offers some; a view whose quick filters hide
    /// everything names them and offers to clear them, rather than claiming the library has none of
    /// it; a scan still under way says it is looking; otherwise the view says what would appear in it.
    /// </summary>
    /// <param name="view">The kind of view.</param>
    /// <param name="searching">Whether a search is shown.</param>
    /// <param name="hasFolders">Whether the library watches any folder.</param>
    /// <param name="filters">The quick filters in use, as the filter button says them, or null for none.</param>
    /// <param name="scanning">Whether the library is looking through its folders now.</param>
    public static LibraryEmptyState For(LibraryEmptyView view, bool searching, bool hasFolders, string? filters, bool scanning, string? scanProblem = null)
    {
        if (searching)
        {
            return filters is null
                ? new("Nothing in the library matches that.", false, false)
                : new($"Nothing in the library matches that with these filters: {filters}.", false, true);
        }

        if (!hasFolders && view != LibraryEmptyView.Playlists)
        {
            return new("The library shows the music, videos and pictures in the folders you choose, and keeps up as files come and go.", true, false);
        }

        if (filters is not null && view is LibraryEmptyView.Media or LibraryEmptyView.ContinueWatching)
        {
            return new($"Nothing here matches these filters: {filters}.", false, true);
        }

        if (!scanning && !string.IsNullOrWhiteSpace(scanProblem) && view == LibraryEmptyView.Media)
        {
            return new(scanProblem, false, false, OffersRescan: true);
        }

        return view switch
        {
            LibraryEmptyView.ContinueWatching => new("Videos you stop part-way through wait here.", false, false),
            LibraryEmptyView.RecentlyPlayed => new("What you play from the library shows here.", false, false),
            LibraryEmptyView.Playlists => new("Playlists you keep (Media, Playlists) show here.", false, false),
            _ => new(scanning ? "Looking through your folders..." : "There is nothing of this kind in the library's folders.", view == LibraryEmptyView.Folders, false),
        };
    }
}
