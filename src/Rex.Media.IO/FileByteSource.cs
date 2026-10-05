using Microsoft.Win32.SafeHandles;

namespace Rex.Media.IO;

/// <summary>
/// A local file read with positional I/O, so concurrent readers never fight over a shared file
/// position. The file is opened for reading with others allowed to read and write, because media
/// being recorded is often played while it grows.
/// </summary>
public sealed class FileByteSource : IByteSource
{
    private readonly SafeFileHandle _handle;

    public FileByteSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _handle = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The current size; a file still being written grows between reads.</summary>
    public long? Length => RandomAccess.GetLength(_handle);

    public bool CanSeek => true;

    public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        cancellationToken.ThrowIfCancellationRequested();
        var total = 0;
        while (total < destination.Length)
        {
            var read = RandomAccess.Read(_handle, destination[total..], position + total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public void Dispose() => _handle.Dispose();
}
