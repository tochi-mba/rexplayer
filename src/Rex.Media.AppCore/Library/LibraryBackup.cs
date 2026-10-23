using System.Security.Cryptography;
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

/// <summary>One file in a finished backup, with its exact byte length and SHA-256 digest.</summary>
public sealed record BackupFileDigest(string Path, long Length, string Sha256);

/// <summary>The final manifest published only after every backup payload has been flushed.</summary>
public sealed record BackupCompletion(int Format, IReadOnlyList<BackupFileDigest> Files);

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
    public const string CompletionName = "complete.json";
    public const string PublishedPrefix = "rexplayer backup ";
    public const int CompletionVersion = 1;

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

    /// <summary>
    /// Publish a complete snapshot as one new directory in the chosen parent folder.
    /// The stage is never offered as a valid backup; the previous published folders
    /// are not touched. A writer is injectable for deterministic fault tests.
    /// </summary>
    public static string PublishSnapshot(string parent, IReadOnlyList<(string Path, byte[] Bytes)> files,
        CancellationToken cancellationToken = default, Action<string, byte[]>? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0 || !files.Any(file => file.Path == FileName))
        {
            throw new FormatException("The backup has no rexplayer library manifest.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in files)
        {
            if (!SafeRelativePath(path) || !names.Add(path) || bytes is null)
            {
                throw new FormatException("The backup contains an unsafe or duplicate file name.");
            }
        }

        Directory.CreateDirectory(parent);
        var parentPath = Path.GetFullPath(parent);
        var nonce = Guid.NewGuid().ToString("N");
        var published = Path.Combine(parentPath, PublishedPrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + nonce);
        var stage = Path.Combine(parentPath, "." + nonce + ".staging");
        try
        {
            Directory.CreateDirectory(stage);
            var hashes = new List<BackupFileDigest>();
            foreach (var (relative, bytes) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (writer is { } custom)
                {
                    custom(file, bytes);
                }
                else
                {
                    using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                hashes.Add(new BackupFileDigest(relative, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var manifest = new BackupCompletion(CompletionVersion, hashes);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BackupJson.Default.BackupCompletion);
            using (var stream = new FileStream(Path.Combine(stage, CompletionName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(manifestBytes);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, published);
            return published;
        }
        finally
        {
            // A failure never publishes an incomplete folder and never damages
            // any prior successful backup.
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    /// <summary>
    /// Read a legacy v1 JSON backup or verify every file of a complete published
    /// snapshot before mutating the destination library.
    /// </summary>
    public static LibraryBackupData ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var completion = Path.Combine(folder, CompletionName);
        if (!File.Exists(completion))
        {
            if (Path.GetFileName(folder).StartsWith(PublishedPrefix, StringComparison.Ordinal))
            {
                throw new FormatException("That snapshot has no completion marker and was not published fully.");
            }

            // Old rexplayer backups have only a JSON file; retain their import path.
            return Read(File.ReadAllBytes(path));
        }

        BackupCompletion? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(File.ReadAllBytes(completion), BackupJson.Default.BackupCompletion);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The backup completion marker cannot be read.", ex);
        }

        if (manifest is not { Format: CompletionVersion, Files: { Count: > 0 } }
            || !manifest.Files.Any(file => file.Path == FileName))
        {
            throw new FormatException("The backup completion marker is missing or unsupported.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            if (!SafeRelativePath(item.Path) || !names.Add(item.Path)
                || item.Sha256 is not { Length: 64 } || item.Length < 0)
            {
                throw new FormatException("The backup completion marker has an unsafe entry.");
            }

            var file = Path.Combine(folder, item.Path);
            if (!File.Exists(file))
            {
                throw new FormatException("A file from the backup is missing: " + item.Path);
            }

            var bytes = File.ReadAllBytes(file);
            if (bytes.LongLength != item.Length || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("A file from the backup does not match its recorded digest: " + item.Path);
            }
        }

        if (!string.Equals(Path.GetFileName(path), FileName, StringComparison.Ordinal))
        {
            throw new FormatException("Choose the rexplayer library.json file inside the verified backup folder.");
        }

        return Read(File.ReadAllBytes(path));
    }

    /// <summary>Only file names inside the chosen folder, with no traversal or rooted paths.</summary>
    private static bool SafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path)
        && !path.Contains(':')
        && !path.Split(['/', '\\']).Any(part => part is "" or "." or "..")
        && !string.Equals(Path.GetFileName(path), CompletionName, StringComparison.OrdinalIgnoreCase);

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
[JsonSerializable(typeof(BackupCompletion))]
internal sealed partial class BackupJson : JsonSerializerContext;
