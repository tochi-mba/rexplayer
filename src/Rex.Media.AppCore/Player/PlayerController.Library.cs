using Rex.Media.AppCore.Library;
using Rex.Media.Engine;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>The media library (LIB-05), when the window keeps one: what plays is counted in it.</summary>
    public MediaLibrary? Library { get; set; }

    /// <summary>Shows details just edited for <paramref name="paths"/> on what plays, without reopening or interrupting it.</summary>
    public void ShowEditedDetails(IEnumerable<string> paths, DetailOverride change)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(change);
        if (Item is { } item && Info is { } info && paths.Contains(item.Location, StringComparer.OrdinalIgnoreCase))
        {
            Info = info with { Metadata = DetailsForm.Apply(info.Metadata, change) };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Plays <paramref name="entries"/> (a library view, in its order) from the one at
    /// <paramref name="start"/>, in place of the playlist, so the rest of the view follows it.
    /// <paramref name="resumeAt"/> is an explicit start chosen by the caller (the Continue watching
    /// view), so it starts there without offering the usual resume choice.
    /// </summary>
    public void PlayFromLibrary(IReadOnlyList<LibraryEntry> entries, int start = 0, TimeSpan? resumeAt = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        _log.Info(LogSource, $"Playing {entries.Count} item(s) from the library, from {entries[Math.Clamp(start, 0, entries.Count - 1)].Path}.");
        Playlist.Clear();
        Playlist.Add(entries.Select(ItemOf));
        Start(Playlist.JumpTo(Math.Clamp(start, 0, entries.Count - 1)), resumeAt is { } at && at > TimeSpan.Zero ? at : TimeSpan.Zero);
    }

    /// <summary>
    /// Changes to another local encoding of the currently playing film at the same position,
    /// leaving the rest of the playlist and paused/playing state intact. Version discovery and
    /// verification are the caller's responsibility; this never switches to a different title.
    /// </summary>
    public bool SwitchLibraryVersion(LibraryEntry version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var index = Playlist.CurrentIndex;
        if (Item is not { } current || index < 0 || version.Kind != LibraryKind.Video
            || !_exists(version.Path) || string.Equals(current.Location, version.Path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = Position;
        var paused = State is SessionState.Paused or SessionState.Ready;
        var replacement = ItemOf(version);
        Playlist.Replace(index, replacement);
        Start(replacement, position, paused: paused, exactPosition: true);
        return true;
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

    /// <summary>
    /// Puts <paramref name="items"/> right after what is playing, so they play next; when nothing
    /// plays, they go to the top and the first starts.
    /// </summary>
    public void PlayNext(IEnumerable<PlaylistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var added = items.ToList();
        if (added.Count == 0)
        {
            return;
        }

        var at = Playlist.NextIndex;
        Playlist.InsertNext(at, added);
        _log.Info(LogSource, $"{added.Count} item(s) play next, from {added[0].Location}.");
        if (_session is null || State is SessionState.Idle or SessionState.Ended or SessionState.Faulted)
        {
            Start(Playlist.JumpTo(at));
            return;
        }

        Say(added.Count == 1 ? "Playing next: " + added[0].Title : $"Playing {added.Count} items next");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts <paramref name="entries"/> right after what is playing, as <see cref="PlayNext(IEnumerable{PlaylistItem})"/>.</summary>
    public void PlayNextFromLibrary(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        PlayNext(entries.Select(ItemOf));
    }

    /// <summary>
    /// Pauses what is playing where it is; nothing happens when nothing plays or it is paused
    /// already, so a paused item is never restarted and its position is untouched.
    /// </summary>
    public void Pause()
    {
        if (_session is not null && State is SessionState.Playing or SessionState.Buffering)
        {
            _ = Observe(_session.PauseAsync());
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

    /// <summary>Marks <paramref name="paths"/> as watched through: nowhere to go back to, and played at least once.</summary>
    public void MarkWatched(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            Memory.Forget(path);
        }

        Library?.MarkPlayed(paths);
        Say(paths.Count == 1 ? "Marked as watched" : $"Marked {paths.Count} items as watched");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks <paramref name="paths"/> as never watched: nowhere to go back to, and no plays.</summary>
    public void MarkUnwatched(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            Memory.Forget(path);
        }

        Library?.ResetPlays(paths);
        Say(paths.Count == 1 ? "Marked as unwatched" : $"Marked {paths.Count} items as unwatched");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets where <paramref name="paths"/> were left, so they start from the top; plays stay counted.</summary>
    public void ResetProgress(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var forgotten = paths.Count(Memory.Forget);
        Say(forgotten == 0 ? "Nothing was left part-way" : forgotten == 1 ? "It starts from the top next time" : $"{forgotten} items start from the top next time");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes <paramref name="paths"/> from the library; the files stay where they are.</summary>
    public void RemoveFromLibrary(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var removed = Library?.Hide(paths) ?? 0;
        _log.Info(LogSource, $"Removed {removed} item(s) from the library at the user's asking; the files stay.");
        Say(removed == 1 ? "Removed from the library" : $"Removed {removed} items from the library");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// After <paramref name="paths"/> were deleted from the disk: the library forgets them, where
    /// they were left is forgotten, and they leave the playlist (what plays stops if it was one).
    /// </summary>
    public void ForgetDeleted(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var gone = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        // Stop can save the current resume point, so do it before forgetting deleted files.
        if (Item is { } playing && gone.Contains(playing.Location))
        {
            Stop();
        }

        foreach (var path in gone)
        {
            Memory.Forget(path);
        }

        Library?.Forget(gone);

        for (var index = Playlist.Items.Count - 1; index >= 0; index--)
        {
            if (gone.Contains(Playlist.Items[index].Location))
            {
                Playlist.RemoveAt(index);
            }
        }

        _log.Info(LogSource, $"{gone.Count} file(s) were deleted at the user's asking and are forgotten.");
        Say(gone.Count == 1 ? "Deleted to the Recycle Bin" : $"Deleted {gone.Count} files to the Recycle Bin");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Counts a play of what opened in the library, unless history is not kept.</summary>
    private void CountPlay(PlaylistItem item)
    {
        if (Memory.KeepsHistory)
        {
            Library?.Played(item.Location);
        }
    }
}
