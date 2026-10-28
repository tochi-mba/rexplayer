using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// What has been worked out from one library snapshot, kept beside the snapshot for as long as it
/// lives. The library hands out the same immutable list until it changes, so a view asked for
/// again over an unchanged library is found here instead of worked out again, and a change (a new
/// list) leaves the old answers to the collector. Safe to use from any thread: a value asked for
/// on two threads at once is worked out once.
/// </summary>
public static class LibraryMemo
{
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, Lazy<object?>>> Kept = [];

    /// <summary>The value named <paramref name="key"/> for <paramref name="snapshot"/>, worked out by <paramref name="compute"/> the first time.</summary>
    public static T Of<T>(object snapshot, string key, Func<T> compute)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);
        var values = Kept.GetValue(snapshot, _ => new ConcurrentDictionary<string, Lazy<object?>>(StringComparer.Ordinal));
        return (T)values.GetOrAdd(key, _ => new Lazy<object?>(() => compute(), LazyThreadSafetyMode.ExecutionAndPublication)).Value!;
    }

    /// <summary><see cref="LibraryViews.Songs"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryEntry> Songs(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Songs), () => LibraryViews.Songs(snapshot));

    /// <summary><see cref="LibraryViews.Videos"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryEntry> Videos(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Videos), () => LibraryViews.Videos(snapshot));

    /// <summary><see cref="LibraryViews.Pictures"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryEntry> Pictures(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Pictures), () => LibraryViews.Pictures(snapshot));

    /// <summary><see cref="LibraryViews.RecentlyAdded"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryEntry> RecentlyAdded(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(RecentlyAdded), () => LibraryViews.RecentlyAdded(snapshot));

    /// <summary><see cref="LibraryViews.Albums"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryGroup> Albums(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Albums), () => LibraryViews.Albums(snapshot));

    /// <summary>A snapshot's albums, the latest added first, kept.</summary>
    public static IReadOnlyList<LibraryGroup> AlbumsByAdded(IReadOnlyList<LibraryEntry> snapshot) =>
        Of(snapshot, nameof(AlbumsByAdded), () => LibraryArrangement.Sort(Albums(snapshot), LibrarySort.Added, false));

    /// <summary><see cref="LibraryViews.Artists"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryGroup> Artists(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Artists), () => LibraryViews.Artists(snapshot));

    /// <summary><see cref="LibraryViews.TvShows"/> of a snapshot, kept.</summary>
    public static IReadOnlyList<LibraryGroup> TvShows(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(TvShows), () => LibraryViews.TvShows(snapshot));

    /// <summary>A snapshot's twenty most played songs (<see cref="HomeShelves.MostPlayed"/>), kept.</summary>
    public static IReadOnlyList<LibraryEntry> MostPlayed(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(MostPlayed), () => HomeShelves.MostPlayed(snapshot, 20));

    /// <summary>How many songs, videos and pictures a snapshot holds, kept.</summary>
    public static (int Music, int Videos, int Pictures) Counts(IReadOnlyList<LibraryEntry> snapshot) => Of(snapshot, nameof(Counts), () =>
        (snapshot.Count(entry => entry.Kind == LibraryKind.Music), snapshot.Count(entry => entry.Kind == LibraryKind.Video), snapshot.Count(entry => entry.Kind == LibraryKind.Picture)));

    /// <summary>
    /// Works out ahead, on the calling thread (a background one), what Home, the plain views and
    /// search ask of <paramref name="snapshot"/>, so the window finds it ready.
    /// </summary>
    public static void Warm(IReadOnlyList<LibraryEntry> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Counts(snapshot);
        _ = RecentlyAdded(snapshot);
        _ = AlbumsByAdded(snapshot);
        _ = TvShows(snapshot);
        _ = MostPlayed(snapshot);
        _ = Songs(snapshot);
        _ = Videos(snapshot);
        _ = Pictures(snapshot);
        _ = Artists(snapshot);
        _ = LibrarySearchIndex.For(snapshot);
    }
}
