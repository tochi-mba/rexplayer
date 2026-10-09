using System.Globalization;
using Rex.Media.Library.Playlists;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>Whether <paramref name="item"/>'s file is not where the playlist says (LIB-09); never so for a URL.</summary>
    public bool IsMissing(PlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !PlaylistPaths.IsUrl(item.Location) && !_exists(item.Location);
    }

    /// <summary>
    /// Points the entry at <paramref name="index"/> at <paramref name="location"/>, where its file
    /// is now; a title the playlist gave it stays, one made from the old file name follows the new.
    /// </summary>
    public void Relocate(int index, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        var old = Playlist.Items[index];
        var title = old.Title == PlaylistItem.TitleOf(old.Location) ? PlaylistItem.TitleOf(location) : old.Title;
        Playlist.Replace(index, new PlaylistItem(location) { Title = title, Artist = old.Artist, Start = old.Start, End = old.End });
        _log.Info(LogSource, $"Found {old.Location} at {location}.");
    }

    /// <summary>
    /// Looks through <paramref name="folder"/> and the folders inside it for every missing entry's
    /// file, by its name, and points the entry there; gives how many were found. A name found more
    /// than once is left alone, since either could be the one.
    /// </summary>
    public int RelinkMissing(string folder, Func<string, IEnumerable<string>>? listAll = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        listAll ??= root => Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        var missing = Playlist.Items.Select((item, index) => (item, index)).Where(entry => IsMissing(entry.item)).ToList();
        if (missing.Count == 0)
        {
            Say("Nothing in the playlist is missing.");
            return 0;
        }

        Dictionary<string, List<string>> byName;
        try
        {
            byName = listAll(folder).GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"{folder} could not be looked through: {ex.Message}");
            return 0;
        }

        var found = 0;
        foreach (var (item, index) in missing)
        {
            if (byName.TryGetValue(Path.GetFileName(item.Location), out var paths) && paths is [var only])
            {
                Relocate(index, only);
                found++;
            }
        }

        Say(found == missing.Count
            ? $"Found {(found == 1 ? "the missing file" : $"all {found.ToString(CultureInfo.CurrentCulture)} missing files")}"
            : $"Found {found.ToString(CultureInfo.CurrentCulture)} of {missing.Count.ToString(CultureInfo.CurrentCulture)} missing files");
        return found;
    }
}
