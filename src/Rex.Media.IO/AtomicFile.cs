namespace Rex.Media.IO;

/// <summary>
/// Writes a file so that a crash or power cut leaves either the old contents or the new, never half
/// of each: the new contents go to a temporary file beside it, which then replaces the original in
/// one rename, and the original is kept as a backup the reader falls back to. A replacement another
/// process briefly blocks (an indexer or antivirus scanner holding the file open) is retried.
/// </summary>
public static class AtomicFile
{
    /// <summary>The suffix of the copy of the previous contents.</summary>
    public const string BackupSuffix = ".rex-backup";

    private static readonly TimeSpan[] Retries = [TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)];

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="contents"/>. <paramref name="wait"/>
    /// sleeps between retries (tests pass their own so they need not wait).
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> contents, Action<TimeSpan>? wait = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        wait ??= Thread.Sleep;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, path + BackupSuffix, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, path);
                }

                return;
            }
            catch (IOException) when (attempt < Retries.Length)
            {
                // Another process has the file open for a moment; it is usually gone within a second.
                wait(Retries[attempt]);
            }
            catch (IOException)
            {
                File.Delete(temporary);
                throw;
            }
        }
    }

    /// <summary>
    /// The file's contents, or its backup's when the file is missing or <paramref name="valid"/>
    /// rejects it; null when neither exists or neither is valid.
    /// </summary>
    public static byte[]? Read(string path, Func<byte[], bool>? valid = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        foreach (var candidate in new[] { path, path + BackupSuffix })
        {
            if (File.Exists(candidate) && File.ReadAllBytes(candidate) is var bytes && (valid is null || valid(bytes)))
            {
                return bytes;
            }
        }

        return null;
    }
}
