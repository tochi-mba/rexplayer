using System.Globalization;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>The playlists the user has named and kept (LIB-04), by name.</summary>
    public IReadOnlyList<NamedPlaylist> NamedPlaylists => Memory.Playlists;

    /// <summary>
    /// Keeps <paramref name="items"/> (the whole playlist when null) as a new named playlist; a name
    /// already taken gets a number after it.
    /// </summary>
    public NamedPlaylist CreatePlaylist(string name, IEnumerable<PlaylistItem>? items = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var playlist = new NamedPlaylist(Guid.NewGuid().ToString("N"), UniqueName(name.Trim(), except: null), [.. (items ?? Playlist.Items).Select(Kept)]);
        Memory.SetPlaylist(playlist);
        Say($"Made the playlist {playlist.Name} ({ItemCount(playlist.Items.Count)})");
        Changed?.Invoke(this, EventArgs.Empty);
        return playlist;
    }

    /// <summary>Adds <paramref name="items"/> to the end of the named playlist <paramref name="id"/>.</summary>
    public void AddToPlaylist(string id, IEnumerable<PlaylistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var added = items.Select(Kept).ToList();
        if (Find(id) is not { } playlist || added.Count == 0)
        {
            return;
        }

        Memory.SetPlaylist(playlist with { Items = [.. playlist.Items, .. added] });
        Say($"Added {ItemCount(added.Count)} to {playlist.Name}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds what is playing to the named playlist <paramref name="id"/>.</summary>
    public void AddPlayingToPlaylist(string id)
    {
        if (Item is not { } item)
        {
            Say("Open something to play first.");
            return;
        }

        AddToPlaylist(id, [item]);
    }

    public void RenamePlaylist(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Find(id) is { } playlist)
        {
            Memory.SetPlaylist(playlist with { Name = UniqueName(name.Trim(), except: id) });
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Keeps a copy of the named playlist <paramref name="id"/> under a new name; null when it is gone.</summary>
    public NamedPlaylist? DuplicatePlaylist(string id) => Find(id) is { } playlist ? CreatePlaylist(playlist.Name + " copy", playlist.Items.Select(Restored)) : null;

    public void DeletePlaylist(string id)
    {
        if (Find(id) is { } playlist)
        {
            Memory.RemovePlaylist(playlist.Id);
            Say($"Deleted the playlist {playlist.Name}");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Plays the named playlist <paramref name="id"/> in place of the playlist, or after it with <paramref name="enqueue"/>.</summary>
    public void PlayPlaylist(string id, bool enqueue = false)
    {
        if (Find(id) is not { } playlist)
        {
            return;
        }

        if (playlist.Items.Count == 0)
        {
            Say($"{playlist.Name} is empty.");
            return;
        }

        _log.Info(LogSource, $"Playing the playlist {playlist.Name} ({playlist.Items.Count} items){(enqueue ? " after the playlist" : "")}.");
        OpenItems([.. playlist.Items.Select(Restored)], enqueue);
    }

    private static QueuedItem Kept(PlaylistItem item) => new(item.Location, item.Title, item.Artist, item.Start, item.End);

    private static PlaylistItem Restored(QueuedItem kept) =>
        new(kept.Location) { Title = kept.Title ?? PlaylistItem.TitleOf(kept.Location), Artist = kept.Artist, Start = kept.Start, End = kept.End };

    private static string ItemCount(int count) => count == 1 ? "1 item" : count.ToString(CultureInfo.CurrentCulture) + " items";

    /// <summary>The named playlist <paramref name="id"/>; when it is gone (deleted elsewhere), says so and gives null.</summary>
    private NamedPlaylist? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var playlist = Memory.Playlist(id);
        if (playlist is null)
        {
            Say("That playlist is no longer there.");
        }

        return playlist;
    }

    /// <summary><paramref name="name"/>, or with " (2)", " (3)" and so on after it when another playlist has it.</summary>
    private string UniqueName(string name, string? except)
    {
        var taken = Memory.Playlists.Where(playlist => playlist.Id != except).Select(playlist => playlist.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var unique = name;
        for (var n = 2; taken.Contains(unique); n++)
        {
            unique = $"{name} ({n.ToString(CultureInfo.CurrentCulture)})";
        }

        return unique;
    }
}
