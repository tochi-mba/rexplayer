// Spec: RFC 3986 section 5.2 (resolving a reference against a base) and RFC 8089 (file URIs), applied to the paths and URIs playlists hold.
namespace Rex.Media.Library.Playlists;

/// <summary>
/// The locations in playlists: paths relative to the playlist's folder, Windows paths, file URIs
/// and URLs. Worked out by text rather than by the running system, so a playlist made on Windows
/// reads the same everywhere.
/// </summary>
public static class PlaylistPaths
{
    /// <summary>
    /// Where <paramref name="reference"/>, written in a playlist in <paramref name="folder"/>, points:
    /// a full path, or a URL as it is; null for nothing.
    /// </summary>
    public static string? Resolve(string? reference, string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var text = reference?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var file))
        {
            var path = Uri.UnescapeDataString(file.AbsolutePath);
            if (file.Host.Length > 0)
            {
                return @"\\" + file.Host + path.Replace('/', '\\');
            }

            // file:///C:/Music/a.mp3 is C:\Music\a.mp3 (its path reads /C:/Music on some systems); file:///srv/a.mp3 stays as it is.
            var drive = path.Length > 2 && path[0] == '/' && path[2] == ':' ? path[1..] : path;
            return drive.Length > 1 && drive[1] == ':' ? drive.Replace('/', '\\') : drive;
        }

        if (IsUrl(text) || HasDriveOrShare(text))
        {
            return text;
        }

        if (text[0] is '\\' or '/')
        {
            // Rooted but without a drive: on the playlist's own drive, or from the root elsewhere.
            return Root(folder) is { } root ? Normalize(root + text.TrimStart('\\', '/'), '\\') : text;
        }

        var separator = folder.Contains('\\', StringComparison.Ordinal) || (!folder.Contains('/', StringComparison.Ordinal) && text.Contains('\\', StringComparison.Ordinal)) ? '\\' : '/';
        return Normalize(folder.Length == 0 ? text : folder.TrimEnd('\\', '/') + separator + text, separator);
    }

    /// <summary>
    /// How to write <paramref name="location"/> in a playlist kept in <paramref name="folder"/>: relative
    /// when it is inside that folder, else in full.
    /// </summary>
    public static string Relative(string location, string folder)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(folder);
        var trimmed = folder.TrimEnd('\\', '/');
        if (trimmed.Length > 0 && !IsUrl(location) && location.Length > trimmed.Length + 1
            && location.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) && location[trimmed.Length] is '\\' or '/')
        {
            return location[(trimmed.Length + 1)..];
        }

        return location;
    }

    /// <summary>A URL other than a file URI: http, https, rtsp and the like.</summary>
    public static bool IsUrl(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme.Length > 1 && location.Contains("://", StringComparison.Ordinal);

    /// <summary>A Windows path with a drive (C:\) or on a share (\\server\share).</summary>
    public static bool HasDriveOrShare(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        || path.StartsWith(@"\\", StringComparison.Ordinal);

    private static string? Root(string folder) =>
        folder.Length >= 2 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' ? folder[..2] + "\\" : null;

    /// <summary>Takes out "." and ".." and uses one separator throughout.</summary>
    private static string Normalize(string path, char separator)
    {
        var share = path.StartsWith(@"\\", StringComparison.Ordinal);
        var parts = path.Split(['\\', '/']);
        var kept = new List<string>();
        foreach (var part in parts.Skip(share ? 2 : 0))
        {
            if (part == "." || (part.Length == 0 && kept.Count > 0))
            {
                continue;
            }

            if (part == ".." && kept.Count > 1)
            {
                kept.RemoveAt(kept.Count - 1);
                continue;
            }

            kept.Add(part);
        }

        return (share ? @"\\" : "") + string.Join(separator, kept);
    }
}
