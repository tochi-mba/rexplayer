namespace Rex.Media.IO;

/// <summary>
/// Somewhere media bytes come from: a file, a block of memory, an HTTP resource, a disc. Reads are
/// positional and synchronous because a demuxer runs on its own thread and parses as it reads; a
/// network source blocks until bytes arrive or the token is cancelled. Implementations must allow
/// reads from more than one thread at once (the probe and a thumbnailer may share a source).
/// </summary>
public interface IByteSource : IDisposable
{
    /// <summary>What to call the source in messages: a file name or a URL.</summary>
    string Name { get; }

    /// <summary>The total size, or null for a live stream or a server that did not say.</summary>
    long? Length { get; }

    /// <summary>Whether reads at arbitrary positions are cheap. A live stream reads forward only.</summary>
    bool CanSeek { get; }

    /// <summary>
    /// Copies bytes starting at <paramref name="position"/> into <paramref name="destination"/> and
    /// returns how many were copied: fewer than asked means the end was reached, zero means the
    /// position is at or past the end.
    /// </summary>
    int Read(long position, Span<byte> destination, CancellationToken cancellationToken);
}

/// <summary>
/// A byte source whose currently readable extent can change. A temporary end is not necessarily
/// the end of the media: callers may wait for the writer without polling the source themselves.
/// </summary>
public interface IGrowingByteSource : IByteSource
{
    /// <summary>A stable observation used to distinguish growth, truncation and replacement.</summary>
    ByteSourceExtent Extent { get; }

    /// <summary>
    /// Waits until the extent differs from <paramref name="observed"/>, the timeout expires, or the
    /// operation is cancelled. Implementations wake promptly on cancellation.
    /// </summary>
    ByteSourceChange WaitForChange(ByteSourceExtent observed, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>The observable identity and readable length of a source at one instant.</summary>
public readonly record struct ByteSourceExtent(long Length, long Revision);

/// <summary>What happened while waiting at a temporary end.</summary>
public enum ByteSourceChange
{
    TimedOut,
    Grew,
    Truncated,
    Replaced,
}
