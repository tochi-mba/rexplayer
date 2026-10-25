using Microsoft.Win32.SafeHandles;

namespace Rex.Media.IO;

/// <summary>
/// A local file read with positional I/O, so concurrent readers never fight over a shared file
/// position. The file is opened for reading with others allowed to read and write, because media
/// being recorded is often played while it grows. A sparse file still filling in (a torrent's
/// download, laid out at full size) reads only as far as the bytes on the disk go: a read stops
/// where a part not yet written begins, and that part filling in counts as the file growing.
/// </summary>
public sealed class FileByteSource : IGrowingByteSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private readonly object _gate = new();
    private readonly IFileAllocation? _allocation;
    private SafeFileHandle _handle;
    private long _revision;
    private DateTime _lastWriteUtc;
    private long _allocated;
    private volatile bool _filling;

    public FileByteSource(string path)
        : this(path, Allocation is { } allocation && (File.GetAttributes(path) & FileAttributes.SparseFile) != 0 ? allocation : null)
    {
    }

    /// <param name="path">The file.</param>
    /// <param name="allocation">Which of its bytes are on the disk, for a sparse file; null reads it as it is.</param>
    public FileByteSource(string path, IFileAllocation? allocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _handle = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        _lastWriteUtc = File.GetLastWriteTimeUtc(Path);
        if (allocation is not null)
        {
            var length = RandomAccess.GetLength(_handle);
            _allocated = allocation.Allocated(_handle, length);
            _filling = _allocated < length;
            _allocation = _filling ? allocation : null;
        }
    }

    /// <summary>Which bytes of a sparse file are on the disk, set once by the app where the system can tell; null elsewhere, and sparse files read as they are.</summary>
    public static IFileAllocation? Allocation { get; set; }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The current size; a file still being written grows between reads.</summary>
    public long? Length
    {
        get
        {
            lock (_gate)
            {
                return RandomAccess.GetLength(_handle);
            }
        }
    }

    public bool CanSeek => true;

    public ByteSourceExtent Extent
    {
        get
        {
            lock (_gate)
            {
                return new ByteSourceExtent(RandomAccess.GetLength(_handle), _revision);
            }
        }
    }

    public ByteSourceChange WaitForChange(ByteSourceExtent observed, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = Refresh(observed);
            if (change != ByteSourceChange.TimedOut || DateTime.UtcNow >= deadline)
            {
                return change;
            }

            var left = deadline - DateTime.UtcNow;
            cancellationToken.WaitHandle.WaitOne(left < PollInterval ? left : PollInterval);
        }
    }

    public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        cancellationToken.ThrowIfCancellationRequested();
        if (_filling)
        {
            // Only what is on the disk: a part not yet written reads as zeros, not media.
            long end;
            lock (_gate)
            {
                end = _allocation!.AllocatedEnd(_handle, position, destination.Length);
            }

            destination = destination[..(int)Math.Clamp(end - position, 0, destination.Length)];
        }

        var total = 0;
        while (total < destination.Length)
        {
            int read;
            lock (_gate)
            {
                read = RandomAccess.Read(_handle, destination[total..], position + total);
            }
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public bool IsPending(long position, long length)
    {
        if (!_filling || length <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            return position + length <= RandomAccess.GetLength(_handle) && _allocation!.AllocatedEnd(_handle, position, length) < position + length;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _handle.Dispose();
        }
    }

    private ByteSourceChange Refresh(ByteSourceExtent observed)
    {
        lock (_gate)
        {
            var handleLength = RandomAccess.GetLength(_handle);
            var info = new FileInfo(Path);
            info.Refresh();
            if (!info.Exists)
            {
                return ByteSourceChange.TimedOut;
            }

            // A sparse download filling in is written in place all the time: that is growth, not a new file.
            if (info.Length == handleLength && FilledIn(handleLength))
            {
                _lastWriteUtc = info.LastWriteTimeUtc;
                return ByteSourceChange.Grew;
            }

            if (info.Length != handleLength || (info.LastWriteTimeUtc != _lastWriteUtc && info.Length <= handleLength))
            {
                var replacement = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
                var previous = _handle;
                _handle = replacement;
                previous.Dispose();
                _lastWriteUtc = info.LastWriteTimeUtc;
                _revision++;
                return info.Length < observed.Length
                    ? ByteSourceChange.Truncated
                    : info.Length > observed.Length && handleLength == observed.Length
                        ? ByteSourceChange.Replaced
                        : ByteSourceChange.Replaced;
            }

            _lastWriteUtc = info.LastWriteTimeUtc;
            if (handleLength > observed.Length)
            {
                return ByteSourceChange.Grew;
            }

            return handleLength < observed.Length ? ByteSourceChange.Truncated : ByteSourceChange.TimedOut;
        }
    }

    /// <summary>Whether more of a sparse file filling in reached the disk since the last look; once all of it has, reads stop asking. Called holding the gate.</summary>
    private bool FilledIn(long length)
    {
        if (!_filling)
        {
            return false;
        }

        var allocated = _allocation!.Allocated(_handle, length);
        if (allocated <= _allocated)
        {
            return false;
        }

        _allocated = allocated;
        // Sparse downloads grow in readable bytes without changing the file length. Update
        // the extent so a player waiting at a hole can distinguish new bytes from settled EOF.
        _revision++;
        _filling = allocated < length;
        return true;
    }
}
