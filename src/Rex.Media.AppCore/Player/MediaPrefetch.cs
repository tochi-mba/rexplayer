namespace Rex.Media.AppCore.Player;

/// <summary>
/// Reads the start and the end of what is likely to play next (the next episode, the next item of
/// the playlist) in the background, so its container's headers and index are already in memory
/// when it opens: an MP4 keeps its index at either end, Matroska its cues near the end.
/// </summary>
public sealed class MediaPrefetch
{
    /// <summary>How much of each end is read: enough for the headers and the index of a long episode.</summary>
    public const int EndBytes = 1 << 20;

    private readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Stream> _open;

    /// <param name="open">Opens a file for reading; by default from disk, shared with every writer.</param>
    public MediaPrefetch(Func<string, Stream>? open = null) =>
        _open = open ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan));

    /// <summary>The byte ranges read from a file of <paramref name="length"/> bytes: its first and last <see cref="EndBytes"/>, once each.</summary>
    public static IReadOnlyList<(long Offset, int Count)> Ranges(long length)
    {
        if (length <= 0)
        {
            return [];
        }

        if (length <= 2L * EndBytes)
        {
            return [(0, (int)length)];
        }

        return [(0, EndBytes), (length - EndBytes, EndBytes)];
    }

    /// <summary>
    /// The candidates not read yet: local files only, each at most once in the player's life, so
    /// what was read stays a small set.
    /// </summary>
    public IReadOnlyList<string> Choose(IEnumerable<string?> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var chosen = new List<string>();
        lock (_done)
        {
            foreach (var candidate in candidates)
            {
                if (candidate is { Length: > 0 } && !candidate.Contains("://", StringComparison.Ordinal) && _done.Add(candidate))
                {
                    chosen.Add(candidate);
                }
            }
        }

        return chosen;
    }

    /// <summary>Reads <paramref name="path"/>'s ranges and throws the bytes away; a file that cannot be read is left for the real open to report.</summary>
    public void Read(string path)
    {
        try
        {
            using var stream = _open(path);
            var buffer = new byte[64 * 1024];
            foreach (var (offset, count) in Ranges(stream.Length))
            {
                stream.Position = offset;
                for (var left = count; left > 0;)
                {
                    var read = stream.Read(buffer, 0, Math.Min(buffer.Length, left));
                    left = read == 0 ? 0 : left - read;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Nothing is lost: the file is read again when it opens, and any problem is reported then.
        }
    }
}
