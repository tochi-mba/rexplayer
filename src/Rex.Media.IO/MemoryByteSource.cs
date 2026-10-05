namespace Rex.Media.IO;

/// <summary>Bytes already in memory: test fixtures, a downloaded segment, an embedded file.</summary>
public sealed class MemoryByteSource : IByteSource
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryByteSource(ReadOnlyMemory<byte> data, string name = "memory", bool canSeek = true)
    {
        _data = data;
        Name = name;
        CanSeek = canSeek;
    }

    public string Name { get; }

    public long? Length => _data.Length;

    public bool CanSeek { get; }

    public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        cancellationToken.ThrowIfCancellationRequested();
        if (position >= _data.Length)
        {
            return 0;
        }

        var count = (int)Math.Min(destination.Length, _data.Length - position);
        _data.Span.Slice((int)position, count).CopyTo(destination);
        return count;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// A window onto part of another source, read as if it were the whole thing: a track inside a disc
/// image, an embedded file inside a container. The window does not own the source.
/// </summary>
public sealed class SliceByteSource : IByteSource
{
    private readonly IByteSource _inner;
    private readonly long _offset;
    private readonly long _length;

    public SliceByteSource(IByteSource inner, long offset, long length, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _inner = inner;
        _offset = offset;
        _length = length;
        Name = name ?? inner.Name;
    }

    public string Name { get; }

    public long? Length => _length;

    public bool CanSeek => _inner.CanSeek;

    public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position >= _length)
        {
            return 0;
        }

        var count = (int)Math.Min(destination.Length, _length - position);
        return _inner.Read(_offset + position, destination[..count], cancellationToken);
    }

    public void Dispose()
    {
    }
}
