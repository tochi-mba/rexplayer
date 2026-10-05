using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.IO;

/// <summary>
/// A buffered, forward-reading cursor over an <see cref="IByteSource"/>, with the big- and
/// little-endian readers every container parser needs. "Exactly" reads throw
/// <see cref="MediaFormatException"/> when the source ends early, because a box or chunk that claims
/// more bytes than exist is a broken file, not a programming error. Not thread-safe: one demuxer owns it.
/// </summary>
public sealed class ByteCursor
{
    private const int BufferSize = 64 * 1024;

    private readonly IByteSource _source;
    private readonly byte[] _buffer = new byte[BufferSize];
    private long _bufferStart;
    private int _bufferLength;
    private int _bufferOffset;

    public ByteCursor(IByteSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        CancellationToken = cancellationToken;
    }

    public IByteSource Source => _source;

    /// <summary>Cancels the reads in progress, for a seek that supersedes the current parse.</summary>
    public CancellationToken CancellationToken { get; set; }

    public long Position => _bufferStart + _bufferOffset;

    public long? Length => _source.Length;

    /// <summary>Bytes left when the length is known; otherwise null.</summary>
    public long? Remaining => Length is { } length ? Math.Max(0, length - Position) : null;

    /// <summary>True when no byte remains. On a source without a length this may block to find out.</summary>
    public bool EndOfStream => _bufferOffset == _bufferLength && !Refill();

    public void Seek(long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position >= _bufferStart && position <= _bufferStart + _bufferLength)
        {
            _bufferOffset = (int)(position - _bufferStart);
            return;
        }

        _bufferStart = position;
        _bufferLength = 0;
        _bufferOffset = 0;
    }

    public void Skip(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (Remaining is { } remaining && count > remaining)
        {
            throw new MediaFormatException("The file ends inside a section the format says to skip.");
        }

        Seek(Position + count);
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length bytes and returns how many.</summary>
    public int Read(Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            if (_bufferOffset == _bufferLength && !Refill())
            {
                break;
            }

            var count = Math.Min(destination.Length - total, _bufferLength - _bufferOffset);
            _buffer.AsSpan(_bufferOffset, count).CopyTo(destination[total..]);
            _bufferOffset += count;
            total += count;
        }

        return total;
    }

    public void ReadExactly(Span<byte> destination)
    {
        if (Read(destination) != destination.Length)
        {
            throw new MediaFormatException("The file ends in the middle of a structure.");
        }
    }

    /// <summary>A pooled buffer holding the next <paramref name="count"/> bytes.</summary>
    public MediaBuffer ReadBuffer(int count)
    {
        var buffer = MediaBuffer.Rent(count);
        try
        {
            ReadExactly(buffer.Span);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>Up to <paramref name="destination"/>.Length bytes without consuming them.</summary>
    public int Peek(Span<byte> destination)
    {
        var start = Position;
        var read = Read(destination);
        Seek(start);
        return read;
    }

    public byte ReadByte()
    {
        if (_bufferOffset == _bufferLength && !Refill())
        {
            throw new MediaFormatException("The file ends in the middle of a structure.");
        }

        return _buffer[_bufferOffset++];
    }

    public ushort ReadUInt16BigEndian() => BinaryPrimitives.ReadUInt16BigEndian(Take(stackalloc byte[2]));

    public ushort ReadUInt16LittleEndian() => BinaryPrimitives.ReadUInt16LittleEndian(Take(stackalloc byte[2]));

    public uint ReadUInt24BigEndian()
    {
        Span<byte> bytes = stackalloc byte[3];
        ReadExactly(bytes);
        return ((uint)bytes[0] << 16) | ((uint)bytes[1] << 8) | bytes[2];
    }

    public uint ReadUInt32BigEndian() => BinaryPrimitives.ReadUInt32BigEndian(Take(stackalloc byte[4]));

    public uint ReadUInt32LittleEndian() => BinaryPrimitives.ReadUInt32LittleEndian(Take(stackalloc byte[4]));

    public ulong ReadUInt64BigEndian() => BinaryPrimitives.ReadUInt64BigEndian(Take(stackalloc byte[8]));

    public ulong ReadUInt64LittleEndian() => BinaryPrimitives.ReadUInt64LittleEndian(Take(stackalloc byte[8]));

    /// <summary>A four-character code such as "RIFF" or "moov".</summary>
    public string ReadFourCC()
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(bytes);
        return FourCC.ToString(bytes);
    }

    private ReadOnlySpan<byte> Take(Span<byte> scratch)
    {
        ReadExactly(scratch);
        return scratch;
    }

    private bool Refill()
    {
        _bufferStart += _bufferLength;
        _bufferOffset = 0;
        _bufferLength = _source.Read(_bufferStart, _buffer, CancellationToken);
        return _bufferLength > 0;
    }
}
