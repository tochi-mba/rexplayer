using System.Buffers;

namespace Rex.Media.Primitives;

/// <summary>
/// A pooled block of bytes: a packet's payload or a decoded frame's planes. Whoever holds a buffer
/// owns it until they pass it on or dispose it, and disposing returns both the array and this shell
/// to their pools, so steady-state playback allocates nothing. Using a buffer after disposing it is a
/// bug, and <see cref="Span"/> throws rather than hand out an array someone else now owns.
/// </summary>
public sealed class MediaBuffer : IDisposable
{
    private static readonly SlotPool<MediaBuffer> Shells = new(512);

    private byte[]? _array;
    private SlotPool<MediaBuffer>? _home;

    private MediaBuffer()
    {
    }

    public int Length { get; private set; }

    public bool IsDisposed => _array is null;

    public Span<byte> Span => new(Array, 0, Length);

    public Memory<byte> Memory => new(Array, 0, Length);

    private byte[] Array => _array ?? throw new ObjectDisposedException(nameof(MediaBuffer));

    /// <summary>A buffer of exactly <paramref name="length"/> bytes. Its contents are undefined.</summary>
    public static MediaBuffer Rent(int length) => Rent(Shells, length);

    /// <summary>
    /// Rents a shell from <paramref name="shells"/>; with no pool the shell is dropped when disposed
    /// instead of being handed to someone else, which lets a test watch a disposed buffer stay disposed.
    /// </summary>
    internal static MediaBuffer Rent(SlotPool<MediaBuffer>? shells, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var buffer = shells?.Take() ?? new MediaBuffer();
        buffer._home = shells;
        buffer._array = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        buffer.Length = length;
        return buffer;
    }

    /// <summary>A new buffer holding a copy of <paramref name="data"/>.</summary>
    public static MediaBuffer CopyOf(ReadOnlySpan<byte> data)
    {
        var buffer = Rent(data.Length);
        data.CopyTo(buffer.Span);
        return buffer;
    }

    /// <summary>Makes the buffer shorter without reallocating.</summary>
    public void Truncate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length);
        _ = Array;
        Length = length;
    }

    public void Dispose()
    {
        var array = Interlocked.Exchange(ref _array, null);
        if (array is null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(array);
        Length = 0;
        _home?.Return(this);
    }
}
