using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// How each library view starts, and how a kept choice is read back (LIB-05). Movies and TV shows
/// open on the streaming-style browse unless the user picked another layout for them; a Cards
/// layout saved only because an order, size or filter changed, or kept from before Browse
/// existed, is not a pick.
/// </summary>
public static class LibraryViewDefaults
{
    /// <summary>The choice a view starts with when the user has kept none, by its view key.</summary>
    public static LibraryViewChoice For(string key) => key switch
    {
        "Songs" or "RecentlyAdded" or "Search" or "Albums group" or "Artists group" or "Artists album" or "Genres group" or "TvShows page" => new LibraryViewChoice(LibraryLook.List),
        "Pictures" => new LibraryViewChoice(LibraryLook.Grid, CardSize: 200, Grouping: LibraryGrouping.Folder),
        "Movies" or "TvShows" => new LibraryViewChoice(LibraryLook.Browse, CardSize: 200),
        "Videos" or "ContinueWatching" or "TvShows group" => new LibraryViewChoice(LibraryLook.Grid, CardSize: 180),
        _ => new LibraryViewChoice(LibraryLook.Grid),
    };

    /// <summary>
    /// The choice a view shows: the default when nothing is kept; otherwise what is kept, except that
    /// a view that starts on Browse keeps Browse over a Cards layout the user never picked.
    /// </summary>
    public static LibraryViewChoice Resolve(string key, LibraryViewChoice? kept)
    {
        var start = For(key);
        if (kept is null)
        {
            return start;
        }

        return start.Look == LibraryLook.Browse && kept is { LookChosen: false, Look: LibraryLook.Grid }
            ? kept with { Look = LibraryLook.Browse }
            : kept;
    }

    /// <summary>
    /// The choice to keep after a change from <paramref name="shown"/> to <paramref name="next"/>:
    /// a change of layout marks the layout as picked, and a pick stays a pick.
    /// </summary>
    public static LibraryViewChoice Keep(LibraryViewChoice shown, LibraryViewChoice next)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(next);
        return next.LookChosen || next.Look == shown.Look ? next : next with { LookChosen = true };
    }
}
