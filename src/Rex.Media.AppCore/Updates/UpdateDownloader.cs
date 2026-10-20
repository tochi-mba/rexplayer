using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>How a download attempt ended.</summary>
public enum DownloadOutcome
{
    /// <summary>The installer is in place and matches its published checksum.</summary>
    Ready,

    /// <summary>Another rexplayer window is downloading the same file; nothing was changed.</summary>
    Busy,

    Failed,

    /// <summary>The finished file did not match its checksum and was deleted.</summary>
    Mismatch,

    NoSpace,

    /// <summary>Stopped on request (the window closed, or a newer version replaced this one).</summary>
    Cancelled,
}

/// <summary>A download attempt's outcome and, when it failed, why, in words for the user.</summary>
public sealed record DownloadReport(DownloadOutcome Outcome, string? Reason = null);

/// <summary>
/// Downloads an update's installer into rexplayer's data folder (TOOL-09): resuming a partial
/// file with a range request, slowing down while something plays, checking the free space first,
/// giving up on a connection that stalls, and keeping the file only if it matches the published
/// SHA-256 checksum. The partial file is held exclusively, so two windows never write it at once.
/// It blocks, so the window runs it on a low-priority background thread.
/// </summary>
public sealed class UpdateDownloader(HttpClient http, string folder)
{
    /// <summary>Room kept free beyond the installer itself, so the download never fills the disk.</summary>
    public const long SpareBytes = 64L * 1024 * 1024;

    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    /// <summary>
    /// How Linux reports a file another process holds exclusively (EWOULDBLOCK): the error number
    /// itself, with none of the bits of a Windows error code, which is how the two are told apart.
    /// Windows' error 11 is a bad format, nothing to do with sharing.
    /// </summary>
    private const int UnixWouldBlock = 11;

    /// <summary>The same refusal on macOS, whose EWOULDBLOCK is 35; Windows' error 35 never arrives bare either.</summary>
    private const int MacWouldBlock = 35;

    /// <summary>Whether something is playing; while it is, the download keeps to <see cref="BusyBytesPerSecond"/>.</summary>
    public Func<bool> Busy { get; init; } = () => false;

    /// <summary>The download's speed limit while something plays, so playback never competes for the disk or the network.</summary>
    public long BusyBytesPerSecond { get; init; } = 1024 * 1024;

    /// <summary>The free space where <c>folder</c> is, in bytes.</summary>
    public Func<string, long> FreeSpace { get; init; } = path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;

    /// <summary>A connection that sends nothing for this long is given up, to be tried again later.</summary>
    public TimeSpan StallAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Where <paramref name="pending"/>'s verified installer is kept.</summary>
    public static string InstallerPath(string folder, PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return Path.Combine(folder, pending.InstallerName);
    }

    /// <summary>
    /// Brings <paramref name="pending"/>'s installer in, reporting the bytes in and the total (0 when
    /// unknown) to <paramref name="progress"/>; a report of every byte means the file is being
    /// checked. A verified installer already there is used as it is.
    /// </summary>
    public DownloadReport Download(PendingUpdate pending, Action<long, long>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(pending);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            Directory.CreateDirectory(folder);
            var installer = InstallerPath(folder, pending);
            stall.CancelAfter(StallAfter);
            var checksum = Text(new Uri(pending.ChecksumUrl), stall.Token);
            if (File.Exists(installer))
            {
                if (Matches(installer, checksum, pending))
                {
                    File.WriteAllText(installer + ".sha256", checksum);
                    return new DownloadReport(DownloadOutcome.Ready);
                }

                File.Delete(installer);
            }

            var part = installer + ".part";
            using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (pending.Size > 0 && file.Length > pending.Size)
                {
                    file.SetLength(0);
                }

                if (Free(folder) < Math.Max(0, pending.Size - file.Length) + SpareBytes)
                {
                    return new DownloadReport(DownloadOutcome.NoSpace, "there is not enough free disk space");
                }

                if ((pending.Size == 0 || file.Length < pending.Size) && Fetch(file, pending, progress, stall) is { } failed)
                {
                    return failed;
                }

                if (pending.Size > 0 && file.Length != pending.Size)
                {
                    return new DownloadReport(DownloadOutcome.Failed, "the download was incomplete");
                }

                // Every byte is in; what follows is the check against the checksum.
                progress?.Invoke(file.Length, Math.Max(pending.Size, file.Length));
            }

            if (!Matches(part, checksum, pending))
            {
                File.Delete(part);
                return new DownloadReport(DownloadOutcome.Mismatch, "the download did not match its published checksum");
            }

            File.Move(part, installer, overwrite: true);
            File.WriteAllText(installer + ".sha256", checksum);
            return new DownloadReport(DownloadOutcome.Ready);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            return cancellation.IsCancellationRequested ? new DownloadReport(DownloadOutcome.Cancelled)
                : stall.IsCancellationRequested ? new DownloadReport(DownloadOutcome.Failed, "the connection stopped sending")
                : ex is IOException io ? Classify(io)
                : ex is HttpRequestException { StatusCode: { } status } ? new DownloadReport(DownloadOutcome.Failed, $"the server answered {(int)status}")
                : ex is HttpRequestException ? new DownloadReport(DownloadOutcome.Failed, "the download server could not be reached")
                : new DownloadReport(DownloadOutcome.Failed, ex.Message);
        }
    }

    /// <summary>A file error in words: a full disk, another window holding the file, or anything else.</summary>
    public static DownloadReport Classify(IOException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var busy = new DownloadReport(DownloadOutcome.Busy, "another rexplayer window is downloading it");
        return error.HResult is UnixWouldBlock or MacWouldBlock ? busy : (error.HResult & 0xFFFF) switch
        {
            ErrorDiskFull or ErrorHandleDiskFull => new DownloadReport(DownloadOutcome.NoSpace, "the disk is full"),
            ErrorSharingViolation or ErrorLockViolation => busy,
            _ => new DownloadReport(DownloadOutcome.Failed, error.Message),
        };
    }

    /// <summary>Whether <paramref name="pending"/>'s installer is here, whole, and matches the checksum saved with it.</summary>
    public static bool IsReady(string folder, PendingUpdate pending)
    {
        var installer = InstallerPath(folder, pending);
        try
        {
            return File.Exists(installer) && File.Exists(installer + ".sha256")
                && Matches(installer, File.ReadAllText(installer + ".sha256"), pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes every file in <paramref name="folder"/> but <paramref name="keep"/>'s installer, its
    /// partial download and its checksum. A file still in use stays until the next time.
    /// </summary>
    public static void Clean(string folder, PendingUpdate? keep)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        string[] kept = keep is null ? [] : [keep.InstallerName, keep.InstallerName + ".part", keep.InstallerName + ".sha256"];
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!kept.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use (the hand-off helper, say): it goes next time.
                }
            }
        }
    }

    /// <summary>
    /// The free space, or "plenty" when the drive cannot say (a network share, say): the write
    /// itself then reports a full disk.
    /// </summary>
    private long Free(string path)
    {
        try
        {
            return FreeSpace(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    private static bool Matches(string path, string checksum, PendingUpdate pending)
    {
        using var file = File.OpenRead(path);
        return (pending.Size == 0 || file.Length == pending.Size) && UpdateCheck.Verify(file, checksum, pending.InstallerName);
    }

    private string Text(Uri link, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, link);
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(response.Content.ReadAsStream(cancellation));
        return reader.ReadToEnd();
    }

    /// <summary>Appends the rest of the installer to <paramref name="file"/>; a report when that fails, else null.</summary>
    private DownloadReport? Fetch(FileStream file, PendingUpdate pending, Action<long, long>? progress, CancellationTokenSource stall)
    {
        var offset = file.Length;
        using var request = new HttpRequestMessage(HttpMethod.Get, pending.InstallerUrl);
        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
        }

        stall.CancelAfter(StallAfter);
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset)
        {
            file.Position = offset;
        }
        else if (response.StatusCode is HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            file.SetLength(0);
            return new DownloadReport(DownloadOutcome.Failed, "the download could not carry on from where it stopped");
        }
        else if (response.IsSuccessStatusCode)
        {
            file.SetLength(0);
            offset = 0;
        }
        else
        {
            return new DownloadReport(DownloadOutcome.Failed, $"the server answered {(int)response.StatusCode}");
        }

        var total = pending.Size > 0 ? pending.Size : response.Content.Headers.ContentLength is { } length ? offset + length : 0;
        using var body = response.Content.ReadAsStream(stall.Token);
        using var watch = stall.Token.Register(body.Dispose);
        var buffer = new byte[64 * 1024];
        var clock = Stopwatch.StartNew();
        while (true)
        {
            stall.CancelAfter(StallAfter);
            var read = body.Read(buffer);
            if (read == 0)
            {
                break;
            }

            file.Write(buffer, 0, read);
            progress?.Invoke(file.Length, total);

            // While something plays, each read takes at least as long as the gentle speed allows.
            var due = TimeSpan.FromSeconds((double)read / BusyBytesPerSecond) - clock.Elapsed;
            if (Busy() && due > TimeSpan.Zero)
            {
                Sleep(due);
            }

            clock.Restart();
        }

        stall.Token.ThrowIfCancellationRequested();
        file.Flush(flushToDisk: true);
        return null;
    }
}
