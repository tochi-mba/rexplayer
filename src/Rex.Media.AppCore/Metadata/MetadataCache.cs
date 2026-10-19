using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Fetched metadata on disk, in one folder of the app's data beside the library's own store, so
/// everything works offline once it is fetched and the user can see and clear it as one thing.
/// Downloads live under <see cref="Downloads"/> and are all re-creatable; the user's own match
/// choices live beside them, so clearing the downloads never forgets a correction.
/// </summary>
public sealed class MetadataCache(string folder)
{
    /// <summary>The cache's own folder, holding the downloads and the user's choices.</summary>
    public string Folder { get; } = folder ?? throw new ArgumentNullException(nameof(folder));

    /// <summary>Where fetched records and artwork live: everything under it can be cleared and fetched again.</summary>
    public string Downloads => System.IO.Path.Combine(Folder, "downloads");

    /// <summary>The user's own match choices, kept outside <see cref="Downloads"/> so a clear never undoes them.</summary>
    public string ChoicesPath => System.IO.Path.Combine(Folder, "choices.json");

    /// <summary>
    /// A file name for any key (a show's name, a file's path): its letters and digits for a person
    /// reading the folder, and a hash of the case-folded key so two keys never share a file.
    /// </summary>
    public static string FileName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant())))[..16].ToLowerInvariant();
        var safe = new string([.. key.Where(char.IsLetterOrDigit).Take(40)]).ToLowerInvariant();
        return safe.Length > 0 ? safe + "-" + hash : hash;
    }

    /// <summary>Where <paramref name="key"/>'s record of <paramref name="kind"/> lives, fetched or not.</summary>
    public string PathFor(string kind, string key, string extension) =>
        System.IO.Path.Combine(Downloads, kind, FileName(key) + extension);

    /// <summary>The cached text for <paramref name="key"/>, or null when it has not been fetched.</summary>
    public string? ReadText(string kind, string key, string extension = ".json")
    {
        var path = PathFor(kind, key, extension);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>The cached bytes (artwork, say) for <paramref name="key"/>, or null when they have not been fetched.</summary>
    public byte[]? ReadBytes(string kind, string key, string extension)
    {
        var path = PathFor(kind, key, extension);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Keeps <paramref name="content"/> for <paramref name="key"/>, whole or not at all: written beside, then moved into place.</summary>
    public void Write(string kind, string key, string extension, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(kind, key, extension);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var staging = path + ".new";
        File.WriteAllBytes(staging, content);
        File.Move(staging, path, overwrite: true);
    }

    /// <summary>Keeps <paramref name="text"/> for <paramref name="key"/>.</summary>
    public void WriteText(string kind, string key, string text, string extension = ".json") =>
        Write(kind, key, extension, Encoding.UTF8.GetBytes(text ?? throw new ArgumentNullException(nameof(text))));

    /// <summary>Forgets one key's record, for a refresh; nothing happens when it was never fetched.</summary>
    public void Remove(string kind, string key, string extension)
    {
        var path = PathFor(kind, key, extension);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>How many bytes the downloads take on the disk; 0 before anything is fetched.</summary>
    public long Size() =>
        !Directory.Exists(Downloads)
            ? 0
            : Directory.EnumerateFiles(Downloads, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    /// <summary>Deletes every download; the user's own choices stay. The next fetch starts the folder again.</summary>
    public void Clear()
    {
        if (Directory.Exists(Downloads))
        {
            Directory.Delete(Downloads, recursive: true);
        }
    }

    /// <summary>A size in words for the Preferences line: "nothing yet", "412 KB", "3.2 MB".</summary>
    public static string Describe(long bytes) => bytes switch
    {
        <= 0 => "nothing yet",
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB",
    };
}
