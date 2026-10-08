using System.Text;
using Rex.Media.Library.Playlists;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>How deep playlists naming other playlists are followed, so two naming each other end.</summary>
    private const int PlaylistDepth = 4;

    /// <summary>
    /// The items <paramref name="location"/> stands for: a playlist file's entries (PLF-01 to
    /// PLF-06), with their titles and, for a cue sheet, the part of the file each plays; anything
    /// else is one item. A playlist that cannot be read says why and adds nothing.
    /// </summary>
    private IEnumerable<PlaylistItem> ItemsFor(string location, int depth)
    {
        if (!PlaylistFiles.IsPlaylist(location) || PlaylistPaths.IsUrl(location))
        {
            return [new PlaylistItem(location)];
        }

        IReadOnlyList<PlaylistEntry> entries;
        try
        {
            entries = PlaylistFiles.Read(location, _readFile(location));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Say($"{PlaylistItem.TitleOf(location)} could not be read: {ex.Message}");
            return [];
        }

        return entries.SelectMany(entry => PlaylistFiles.IsPlaylist(entry.Location) && depth < PlaylistDepth
            ? ItemsFor(entry.Location, depth + 1)
            : PlaylistFiles.IsPlaylist(entry.Location) ? [] : [ItemFor(entry)]);
    }

    /// <summary>
    /// The playlist as a file of the kind <paramref name="path"/> names (LIB-03): M3U8 for .m3u8 and
    /// .m3u, PLS or XSPF, with the media beside it written relative to it.
    /// </summary>
    public string PlaylistFileText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var entries = Playlist.Items.Select(item => new PlaylistEntry(item.Location)
        {
            Title = item.Title == PlaylistItem.TitleOf(item.Location) ? null : item.Title,
            Artist = item.Artist,
            Start = item.Start,
            End = item.End,
        }).ToList();
        return PlaylistFiles.Write(entries, PlaylistFiles.FormatForSaving(path), PlaylistFiles.Folder(path), PlaylistItem.TitleOf(path));
    }

    /// <summary>Saves the playlist at <paramref name="path"/>, replacing what is there in one step; false, and why, when it cannot.</summary>
    public bool SavePlaylist(string path)
    {
        try
        {
            _writeFile(path, Encoding.UTF8.GetBytes(PlaylistFileText(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("The playlist could not be saved: " + ex.Message);
            return false;
        }

        Say($"Playlist saved as {PlaylistItem.TitleOf(path)}");
        return true;
    }

    private static PlaylistItem ItemFor(PlaylistEntry entry)
    {
        var item = new PlaylistItem(entry.Location) { Artist = entry.Artist, Start = entry.Start, End = entry.End };
        if (entry.Title is { Length: > 0 } title)
        {
            item.Title = title;
        }

        return item;
    }
}
