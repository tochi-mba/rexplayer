using Microsoft.Win32.SafeHandles;

namespace Rex.Media.IO;

/// <summary>
/// A local file read with positional I/O, so concurrent readers never fight over a shared file
/// position. The file is opened for reading with others allowed to read and write, because media
/// being recorded is often played while it grows.
/// </summary>
public sealed class FileByteSource : IGrowingByteSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private readonly object _gate = new();
    private SafeFileHandle _handle;
    private long _revision;
    private DateTime _lastWriteUtc;

    public FileByteSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _handle = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        _lastWriteUtc = File.GetLastWriteTimeUtc(Path);
    }

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
}
