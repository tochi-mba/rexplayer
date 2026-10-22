using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>The library's views, in the fixed order its sidebar lists them.</summary>
public enum SidebarView
{
    Home,
    Songs,
    Albums,
    Artists,
    Genres,
    Audiobooks,
    Authors,
    Podcasts,
    Recordings,
    Movies,
    TvShows,
    OtherVideos,
    Videos,
    Pictures,
    ContinueWatching,
    RecentlyPlayed,
    RecentlyAdded,
    Playlists,
    Folders,
}

/// <summary>
/// What the library holds, as far as the sidebar needs to know: whether each view would have
/// anything in it. Worked out from the whole library, never from a view's filters, so a filter that
/// empties a view keeps the view (and its empty-state words) where they are.
/// </summary>
public sealed record LibraryFacts
{
    /// <summary>Songs in Music: the audio that is not an audiobook, podcast, recording or sound.</summary>
    public bool Music { get; init; }

    /// <summary>A song with an album tag.</summary>
    public bool Albums { get; init; }

    /// <summary>A song with a genre tag.</summary>
    public bool Genres { get; init; }

    public bool Audiobooks { get; init; }

    public bool Authors { get; init; }

    public bool Podcasts { get; init; }

    /// <summary>Recordings or sounds: the sounds sit at the foot of the Recordings view.</summary>
    public bool Recordings { get; init; }

    public bool Movies { get; init; }

    public bool TvShows { get; init; }

    public bool OtherVideos { get; init; }

    public bool Videos { get; init; }

    public bool Pictures { get; init; }

    /// <summary>A video left part-way.</summary>
    public bool ContinueWatching { get; init; }

    /// <summary>Something played lately that is still there.</summary>
    public bool RecentlyPlayed { get; init; }

    /// <summary>A playlist the person keeps.</summary>
    public bool Playlists { get; init; }

    /// <summary>A watched folder.</summary>
    public bool Folders { get; init; }

    /// <summary>
    /// The facts of a library: <paramref name="music"/> is its songs (the audio the rules call music),
    /// <paramref name="listening"/> the rest of its audio; <paramref name="isMovie"/>, <paramref name="isEpisode"/>
    /// and <paramref name="isOtherVideo"/> say where a video belongs (an Other videos view may not exist: null).
    /// </summary>
    public static LibraryFacts Of(IReadOnlyCollection<LibraryEntry> entries, IReadOnlyCollection<LibraryEntry> music, ListeningShelves listening,
        Func<LibraryEntry, bool> isMovie, Func<LibraryEntry, bool> isEpisode, Func<LibraryEntry, bool>? isOtherVideo,
        Func<string, TimeSpan?> leftAt, bool recentlyPlayed, bool playlists, bool folders)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(listening);
        ArgumentNullException.ThrowIfNull(isMovie);
        ArgumentNullException.ThrowIfNull(isEpisode);
        ArgumentNullException.ThrowIfNull(leftAt);
        var songs = music.Where(entry => entry.Kind == LibraryKind.Music).ToList();
        var videos = entries.Where(entry => entry.Kind == LibraryKind.Video).ToList();
        return new LibraryFacts
        {
            Music = songs.Count > 0,
            Albums = songs.Any(song => song.Album is not null),
            Genres = songs.Any(song => song.Genre is not null),
            Audiobooks = listening.Audiobooks.Count > 0,
            Authors = listening.Audiobooks.Count > 0,
            Podcasts = listening.Podcasts.Count > 0,
            Recordings = listening.Recordings.Count + listening.Sounds.Count > 0,
            Movies = videos.Any(isMovie),
            TvShows = videos.Any(isEpisode),
            OtherVideos = isOtherVideo is not null && videos.Any(isOtherVideo),
            Videos = videos.Count > 0,
            Pictures = entries.Any(entry => entry.Kind == LibraryKind.Picture),
            ContinueWatching = videos.Any(video => leftAt(video.Path) is not null),
            RecentlyPlayed = recentlyPlayed,
            Playlists = playlists,
            Folders = folders,
        };
    }
}

/// <summary>
/// Which views the library's sidebar lists: Home always, every other view only while it has
/// something in it (<see cref="Present"/>), always in <see cref="SidebarView"/> order. The first
/// look shows what is there at once; after that a change waits until it has held for
/// <see cref="Settle"/> with no scan running, so a scan never makes a view blink in and out. A change
/// the person made (moving a file, keeping a playlist, deleting a file) shows at once.
/// </summary>
public sealed class LibrarySections
{
    /// <summary>How long a change must hold, with no scan running, before the sidebar follows it.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private HashSet<SidebarView>? _shown;
    private HashSet<SidebarView>? _pending;
    private DateTimeOffset _pendingSince;

    /// <summary>The views listed now, in order; only Home before the first look.</summary>
    public IReadOnlyList<SidebarView> Shown => [.. Enum.GetValues<SidebarView>().Where(IsShown)];

    /// <summary>Whether the first look has been taken.</summary>
    public bool Looked => _shown is not null;

    /// <summary>When the sidebar should look again to settle a waiting change; null when nothing waits.</summary>
    public DateTimeOffset? NextLook => _pending is null ? null : _pendingSince + Settle;

    /// <summary>Whether <paramref name="section"/> is listed now.</summary>
    public bool IsShown(SidebarView section) => section == SidebarView.Home || _shown?.Contains(section) == true;

    /// <summary>The views that have something in them, Home always among them.</summary>
    public static IReadOnlySet<SidebarView> Present(LibraryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var present = new HashSet<SidebarView> { SidebarView.Home };
        foreach (var (section, has) in new[]
        {
            (SidebarView.Songs, facts.Music), (SidebarView.Artists, facts.Music),
            (SidebarView.Albums, facts.Albums), (SidebarView.Genres, facts.Genres),
            (SidebarView.Audiobooks, facts.Audiobooks), (SidebarView.Authors, facts.Authors), (SidebarView.Podcasts, facts.Podcasts), (SidebarView.Recordings, facts.Recordings),
            (SidebarView.Movies, facts.Movies), (SidebarView.TvShows, facts.TvShows), (SidebarView.OtherVideos, facts.OtherVideos),
            (SidebarView.Videos, facts.Videos), (SidebarView.Pictures, facts.Pictures),
            (SidebarView.ContinueWatching, facts.ContinueWatching), (SidebarView.RecentlyPlayed, facts.RecentlyPlayed),
            (SidebarView.RecentlyAdded, facts.Music || facts.Audiobooks || facts.Podcasts || facts.Recordings || facts.Videos || facts.Pictures),
            (SidebarView.Playlists, facts.Playlists), (SidebarView.Folders, facts.Folders),
        })
        {
            if (has)
            {
                present.Add(section);
            }
        }

        return present;
    }

    /// <summary>
    /// Takes what has something to show now (<paramref name="present"/>); true when the views listed
    /// changed. <paramref name="scanning"/> holds a change back; <paramref name="immediate"/> (the
    /// person's own doing) lets it through at once.
    /// </summary>
    public bool Observe(IReadOnlySet<SidebarView> present, bool scanning, DateTimeOffset now, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(present);
        var wanted = new HashSet<SidebarView>(present) { SidebarView.Home };
        if (_shown is null || immediate)
        {
            var changed = _shown is null ? wanted.Count > 1 : !_shown.SetEquals(wanted);
            _shown = wanted;
            _pending = null;
            return changed;
        }

        if (_shown.SetEquals(wanted))
        {
            _pending = null;
            return false;
        }

        if (_pending is null || !_pending.SetEquals(wanted))
        {
            _pending = wanted;
            _pendingSince = now;
        }

        if (scanning)
        {
            // The clock starts again once the scan is over.
            _pendingSince = now;
            return false;
        }

        if (now - _pendingSince < Settle)
        {
            return false;
        }

        _shown = wanted;
        _pending = null;
        return true;
    }
}
