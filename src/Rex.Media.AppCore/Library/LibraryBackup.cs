using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Library.Playlists;

namespace Rex.Media.AppCore.Library;

/// <summary>Everything a library backup holds (LIB-10).</summary>
public sealed record LibraryBackupData(int Version, IReadOnlyList<string> Folders, IReadOnlyList<LibraryEntry> Entries, IReadOnlyList<NamedPlaylist> Playlists);

/// <summary>What a restore brought in.</summary>
public sealed record BackupSummary(int Folders, int Entries, int Playlists);

/// <summary>
/// Backing the library up and bringing it back (LIB-10): one JSON file with the watched folders,
/// every file the library knows (with its play count) and the named playlists, and beside it each
/// playlist as an M3U8 any player opens. A restore adds what is missing and keeps what is there:
/// folders already watched stay, a file's higher play count and later last play win, and a
/// playlist with the same name and entries as one already kept is not made twice.
/// </summary>
public static class LibraryBackup
{
    public const string FileName = "rexplayer library.json";
    public const string PlaylistFolder = "Playlists";
    public const int CurrentVersion = 1;

    /// <summary>The backup's files, by their path inside the folder it goes in.</summary>
    public static IReadOnlyList<(string Path, byte[] Bytes)> Export(MediaLibrary library, IReadOnlyList<NamedPlaylist> playlists)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(playlists);
        var data = new LibraryBackupData(
            CurrentVersion,
            library.Folders,
            [.. library.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)],
            playlists);
        var files = new List<(string, byte[])> { (FileName, JsonSerializer.SerializeToUtf8Bytes(data, BackupJson.Default.LibraryBackupData)) };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var playlist in playlists)
        {
            var name = SafeName(playlist.Name);
            var unique = name;
            for (var n = 2; !names.Add(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            // Full paths: a backup is often brought back somewhere else.
            var entries = playlist.Items.Select(item => new PlaylistEntry(item.Location) { Title = item.Title, Artist = item.Artist, Start = item.Start, End = item.End }).ToList();
            files.Add((Path.Combine(PlaylistFolder, unique + ".m3u8"), Encoding.UTF8.GetBytes(PlaylistFiles.Write(entries, PlaylistFormat.M3u, ""))));
        }

        return files;
    }

    /// <summary>A backup's contents; text that is no backup, or one from a newer rexplayer, throws FormatException.</summary>
    public static LibraryBackupData Read(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        LibraryBackupData? data;
        try
        {
            data = JsonSerializer.Deserialize(json, BackupJson.Default.LibraryBackupData);
        }
        catch (JsonException ex)
        {
            throw new FormatException("That is not a library backup from rexplayer.", ex);
        }

        if (data is null || data.Version < 1)
        {
            throw new FormatException("That is not a library backup from rexplayer.");
        }

        if (data.Version > CurrentVersion)
        {
            throw new FormatException("That backup is from a newer rexplayer; update rexplayer to bring it back.");
        }

        return data with { Folders = data.Folders ?? [], Entries = data.Entries ?? [], Playlists = data.Playlists ?? [] };
    }

    /// <summary>Brings <paramref name="data"/> into the library and the named playlists, adding to what is there.</summary>
    public static BackupSummary Restore(LibraryBackupData data, MediaLibrary library, PlayerController controller)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(controller);
        var folders = data.Folders.Count(folder => !string.IsNullOrWhiteSpace(folder) && library.AddFolder(folder));
        var entries = library.Restore(data.Entries.Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Path)));
        var playlists = 0;
        foreach (var playlist in data.Playlists.Where(playlist => playlist is not null && !string.IsNullOrWhiteSpace(playlist.Name)))
        {
            var items = (playlist.Items ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Location)).ToArray();
            var same = controller.NamedPlaylists.Any(kept => kept.Name.Equals(playlist.Name, StringComparison.OrdinalIgnoreCase)
                && kept.Items.Select(item => item.Location).SequenceEqual(items.Select(item => item.Location), StringComparer.OrdinalIgnoreCase));
            if (!same)
            {
                controller.CreatePlaylist(playlist.Name, items.Select(item => new PlaylistItem(item.Location) { Title = item.Title ?? PlaylistItem.TitleOf(item.Location), Artist = item.Artist, Start = item.Start, End = item.End }));
                playlists++;
            }
        }

        return new BackupSummary(folders, entries, playlists);
    }

    /// <summary>A playlist's name as a file name: characters Windows refuses become underscores.</summary>
    public static string SafeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var safe = new string([.. name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim().TrimEnd('.');
        return safe.Length == 0 ? "Playlist" : safe;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryBackupData))]
internal sealed partial class BackupJson : JsonSerializerContext;
