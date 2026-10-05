// Spec: none. A sliding read window for demuxers that find frames by scanning the bytes.
using Rex.Media.IO;

namespace Rex.Media.Containers;

/// <summary>
/// The unread bytes in front of a demuxer that finds frames by looking for sync codes (native FLAC,
/// MPEG audio, raw elementary streams). It grows when a frame is longer than what is buffered, up to
/// <see cref="MaxCapacity"/>, so one absurd "frame" in a damaged file cannot exhaust memory.
/// </summary>
internal sealed class ScanWindow
{
    public const int MaxCapacity = 64 << 20;

    private readonly ByteCursor _stream;
    private readonly int _maxCapacity;
    private byte[] _buffer;
    private long _start;
    private int _offset;
    private int _length;

    public ScanWindow(ByteCursor stream, long position, int capacity = 64 * 1024, int maxCapacity = MaxCapacity)
    {
        _stream = stream;
        _maxCapacity = maxCapacity;
        _buffer = new byte[capacity];
        _start = position;
    }

    /// <summary>The file offset of the first unread byte.</summary>
    public long Position => _start + _offset;

    public int Available => _length - _offset;

    /// <summary>True once a read found no more bytes (or the window reached its size limit).</summary>
    public bool AtEnd { get; private set; }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(_offset, _length - _offset);

    /// <summary>Forgets the buffered bytes and continues reading at <paramref name="position"/>.</summary>
    public void Reset(long position)
    {
        _start = position;
        _offset = 0;
        _length = 0;
        AtEnd = false;
    }

    public void Consume(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Available);
        _offset += count;
    }

    /// <summary>Buffers at least <paramref name="count"/> bytes if the source has them.</summary>
    public bool Ensure(int count)
    {
        while (Available < count)
        {
            if (!Grow())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads more bytes behind the buffered ones; false at the end of the source.</summary>
    public bool Grow()
    {
        if (AtEnd)
        {
            return false;
        }

        if (_offset > 0)
        {
            Array.Copy(_buffer, _offset, _buffer, 0, Available);
            _start += _offset;
            _length -= _offset;
            _offset = 0;
        }

        if (_length == _buffer.Length)
        {
            if (_buffer.Length >= _maxCapacity)
            {
                AtEnd = true;
                return false;
            }

            Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, _maxCapacity));
        }

        _stream.Seek(_start + _length);
        var read = _stream.Read(_buffer.AsSpan(_length));
        _length += read;
        AtEnd = read == 0;
        return read > 0;
    }
}
