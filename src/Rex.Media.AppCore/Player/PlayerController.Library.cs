using Rex.Media.Library;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>The media library (LIB-05), when the window keeps one: what plays is counted in it.</summary>
    public MediaLibrary? Library { get; set; }

    /// <summary>
    /// Plays <paramref name="entries"/> (a library view, in its order) from the one at
    /// <paramref name="start"/>, in place of the playlist, so the rest of the view follows it.
    /// </summary>
    public void PlayFromLibrary(IReadOnlyList<LibraryEntry> entries, int start = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        _log.Info(LogSource, $"Playing {entries.Count} item(s) from the library, from {entries[Math.Clamp(start, 0, entries.Count - 1)].Path}.");
        Playlist.Clear();
        Playlist.Add(entries.Select(ItemOf));
        Start(Playlist.JumpTo(Math.Clamp(start, 0, entries.Count - 1)));
    }

    /// <summary>Puts <paramref name="entries"/> at the end of the playlist, playing them if nothing plays.</summary>
    public void EnqueueFromLibrary(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var items = entries.Select(ItemOf).ToList();
        if (items.Count > 0)
        {
            OpenItems(items, enqueue: true);
        }
    }

    /// <summary>A library entry as a playlist item, with the title and artist the library read.</summary>
    public static PlaylistItem ItemOf(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new PlaylistItem(entry.Path) { Title = entry.Title, Artist = entry.Artist };
    }

    /// <summary>Where the library's <paramref name="path"/> was left, for "Continue watching".</summary>
    public TimeSpan? LeftAt(string path) => Memory.ResumePoint(path);

    /// <summary>Counts a play of what opened in the library, unless history is not kept.</summary>
    private void CountPlay(PlaylistItem item)
    {
        if (Memory.KeepsHistory)
        {
            Library?.Played(item.Location);
        }
    }
}
