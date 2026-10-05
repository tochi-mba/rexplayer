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
