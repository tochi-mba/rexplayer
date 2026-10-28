namespace Rex.Media.AppCore.Library;

/// <summary>Which kind of library view has nothing to show, as far as its empty words differ.</summary>
public enum LibraryEmptyView
{
    /// <summary>A view of the library's media (songs, albums, videos, a group opened).</summary>
    Media,

    /// <summary>Videos left part-way.</summary>
    ContinueWatching,

    /// <summary>What was played lately.</summary>
    RecentlyPlayed,

    /// <summary>The named playlists.</summary>
    Playlists,

    /// <summary>The watched folders.</summary>
    Folders,

    /// <summary>Home, with no shelf to show: the whole library is empty.</summary>
    Home,
}

/// <summary>What an empty library view says, and which ways out it offers under the words.</summary>
/// <param name="Text">The words in the middle of the view.</param>
/// <param name="OffersFolders">Offer to add a folder (and the usual Music, Videos and Pictures folders).</param>
/// <param name="OffersClearFilters">Offer to clear the view's quick filters, which hide everything it holds.</param>
/// <param name="OffersRescan">Offer a direct retry if the last folder scan failed.</param>
public sealed record LibraryEmptyState(string Text, bool OffersFolders, bool OffersClearFilters, bool OffersRescan = false)
{
    /// <summary>A short heading over the words, for the states that need one; null for none.</summary>
    public string? Heading { get; init; }

    /// <summary>Something went wrong (a folder out of reach), so the view shows a warning rather than the library's mark.</summary>
    public bool IsProblem { get; init; }

    /// <summary>Offer the Folders view, where each watched folder can be checked, scanned again or removed.</summary>
    public bool OffersFolderList { get; init; }

    /// <summary>
    /// The words for a view with nothing to show (LIB-05). A search that finds nothing says so; a
    /// library with no folders yet explains itself and offers some; a view whose quick filters hide
    /// everything names them and offers to clear them, rather than claiming the library has none of
    /// it; a folder out of reach is named, with a retry and the folder list; a scan still under way
    /// says it is looking; an empty Home asks for a folder; otherwise the view says what would appear in it.
    /// </summary>
    /// <param name="view">The kind of view.</param>
    /// <param name="searching">Whether a search is shown.</param>
    /// <param name="hasFolders">Whether the library watches any folder.</param>
    /// <param name="filters">The quick filters in use, as the filter button says them, or null for none.</param>
    /// <param name="scanning">Whether the library is looking through its folders now.</param>
    /// <param name="scanProblem">The most recent folder-scan error, if one is available.</param>
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
            return new("The library shows the music, videos and pictures in the folders you choose, and keeps up as files come and go.", true, false)
            {
                Heading = "Add a folder to start your library",
            };
        }

        if (filters is not null && view is LibraryEmptyView.Media or LibraryEmptyView.ContinueWatching)
        {
            return new($"Nothing here matches these filters: {filters}.", false, true);
        }

        if (!scanning && !string.IsNullOrWhiteSpace(scanProblem) && view is LibraryEmptyView.Media or LibraryEmptyView.Home)
        {
            return new(scanProblem, false, false, OffersRescan: true)
            {
                Heading = "A library folder is out of reach",
                IsProblem = true,
                OffersFolderList = true,
            };
        }

        if (!scanning && view == LibraryEmptyView.Home)
        {
            return new("Add a folder that holds music, videos or pictures, or put some in a folder the library already watches.", true, false)
            {
                Heading = "Nothing in your library yet",
            };
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
