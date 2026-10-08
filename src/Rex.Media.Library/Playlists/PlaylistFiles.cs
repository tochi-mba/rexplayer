// Spec: The signatures that tell playlist formats apart: #EXTM3U (RFC 8216 section 4.3.1.1), [playlist] (PLS), the XSPF namespace http://xspf.org/ns/0/, <asx, <?wpl and <smil, and a cue sheet's FILE and TRACK commands.
namespace Rex.Media.Library.Playlists;

/// <summary>The playlist formats rexplayer reads, and the ones it writes.</summary>
public enum PlaylistFormat
{
    M3u,
    Pls,
    Xspf,
    Cue,
    Asx,
    Wpl,
}

/// <summary>
/// Reads playlist files of every format (PLF-01 to PLF-06) into <see cref="PlaylistEntry"/> lists,
/// and writes M3U8, PLS and XSPF. A file is told apart by its contents first and its name second.
/// </summary>
public static class PlaylistFiles
{
    /// <summary>The names playlist files have.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".m3u", ".m3u8", ".pls", ".xspf", ".cue", ".asx", ".wax", ".wvx", ".wpl", ".zpl"];

    /// <summary>The formats <see cref="Write"/> writes.</summary>
    public static IReadOnlyList<PlaylistFormat> Writable { get; } = [PlaylistFormat.M3u, PlaylistFormat.Pls, PlaylistFormat.Xspf];

    /// <summary>Whether a file with this name is a playlist.</summary>
    public static bool IsPlaylist(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The format of <paramref name="text"/> read from <paramref name="path"/>, or null when it is none of them.</summary>
    public static PlaylistFormat? FormatOf(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        var start = text.TrimStart();
        if (start.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            return PlaylistFormat.M3u;
        }

        if (start.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase))
        {
            return PlaylistFormat.Pls;
        }

        if (start.StartsWith('<'))
        {
            var head = start[..Math.Min(start.Length, 512)];
            if (head.Contains("xspf.org/ns/0", StringComparison.OrdinalIgnoreCase))
            {
                return PlaylistFormat.Xspf;
            }

            if (head.Contains("<asx", StringComparison.OrdinalIgnoreCase))
            {
                return PlaylistFormat.Asx;
            }

            if (head.Contains("<?wpl", StringComparison.OrdinalIgnoreCase) || head.Contains("<?zpl", StringComparison.OrdinalIgnoreCase) || head.Contains("<smil", StringComparison.OrdinalIgnoreCase))
            {
                return PlaylistFormat.Wpl;
            }
        }

        var lines = PlaylistText.Lines(start).Select(line => line.TrimStart()).ToList();
        if (lines.Any(line => line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase)) && lines.Any(line => line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase)))
        {
            return PlaylistFormat.Cue;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".m3u" or ".m3u8" => PlaylistFormat.M3u,
            ".pls" => PlaylistFormat.Pls,
            _ => null,
        };
    }

    /// <summary>The entries of the playlist file at <paramref name="path"/>, whose bytes are <paramref name="bytes"/>.</summary>
    /// <exception cref="FormatException">The file is not a playlist rexplayer can read.</exception>
    public static IReadOnlyList<PlaylistEntry> Read(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        var text = PlaylistText.Decode(bytes);
        var format = FormatOf(path, text) ?? throw new FormatException($"{Path.GetFileName(path)} is not a playlist rexplayer can read.");
        return Parse(text, format, Folder(path));
    }

    /// <summary>The entries of <paramref name="text"/> in <paramref name="format"/>, with locations resolved against <paramref name="folder"/>.</summary>
    public static IReadOnlyList<PlaylistEntry> Parse(string text, PlaylistFormat format, string folder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folder);
        return format switch
        {
            PlaylistFormat.M3u => M3u.Parse(text, folder),
            PlaylistFormat.Pls => Pls.Parse(text, folder),
            PlaylistFormat.Xspf => Xspf.Parse(text, folder),
            PlaylistFormat.Cue => Cue.Parse(text, folder),
            PlaylistFormat.Asx => Asx.Parse(text, folder),
            PlaylistFormat.Wpl => Wpl.Parse(text, folder),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>
    /// <paramref name="entries"/> as a playlist file in <paramref name="format"/> kept in
    /// <paramref name="folder"/>: the media beside it is written relative to it. UTF-8 text.
    /// </summary>
    public static string Write(IReadOnlyList<PlaylistEntry> entries, PlaylistFormat format, string folder, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folder);
        return format switch
        {
            PlaylistFormat.M3u => M3u.Write(entries, folder),
            PlaylistFormat.Pls => Pls.Write(entries, folder),
            PlaylistFormat.Xspf => Xspf.Write(entries, folder, title),
            _ => throw new NotSupportedException($"rexplayer reads {format} playlists but does not write them."),
        };
    }

    /// <summary>The format a playlist saved under <paramref name="path"/> is written in, by its name; M3U8 for any other.</summary>
    public static PlaylistFormat FormatForSaving(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pls" => PlaylistFormat.Pls,
        ".xspf" => PlaylistFormat.Xspf,
        _ => PlaylistFormat.M3u,
    };

    /// <summary>The folder a playlist's relative locations are relative to, by text (either separator).</summary>
    public static string Folder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var at = path.LastIndexOfAny(['\\', '/']);
        return at < 0 ? "" : at == 0 ? path[..1] : path[..at];
    }
}
