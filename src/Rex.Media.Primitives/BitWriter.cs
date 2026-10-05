namespace Rex.Media.Primitives;

/// <summary>
/// Writes a bitstream most-significant bit first into a caller's buffer: the mirror of
/// <see cref="BitReader"/>, for encoders and for headers rexplayer writes itself. Writing past the
/// end of the buffer is a programming error and throws <see cref="InvalidOperationException"/>.
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> _data;
    private long _position;

    public BitWriter(Span<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly long BitPosition => _position;

    /// <summary>Bytes touched so far, counting a partly written last byte.</summary>
    public readonly int BytesWritten => (int)((_position + 7) >> 3);

    public readonly bool IsByteAligned => (_position & 7) == 0;

    /// <summary>The low <paramref name="count"/> bits of <paramref name="value"/>, up to 32.</summary>
    public void Write(uint value, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, 32u, nameof(count));
        if (_position + count > (long)_data.Length * 8)
        {
            throw new InvalidOperationException("The bit writer's buffer is full.");
        }

        while (count > 0)
        {
            var byteIndex = (int)(_position >> 3);
            var free = 8 - (int)(_position & 7);
            var take = Math.Min(free, count);
            var bits = (int)((value >> (count - take)) & ((1u << take) - 1));
            var shift = free - take;
            var mask = ((1 << take) - 1) << shift;
            _data[byteIndex] = (byte)((_data[byteIndex] & ~mask) | (bits << shift));
            _position += take;
            count -= take;
        }
    }

    /// <summary>Up to 64 bits, for fields like FLAC's 36-bit sample count.</summary>
    public void Write64(ulong value, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, 64u, nameof(count));
        if (count > 32)
        {
            Write((uint)(value >> 32), count - 32);
            count = 32;
        }

        Write((uint)value, count);
    }

    public void WriteBit(bool bit) => Write(bit ? 1u : 0u, 1);

    /// <summary>A two's-complement field of <paramref name="count"/> bits.</summary>
    public void WriteSigned(int value, int count) => Write((uint)value, count);

    /// <summary><paramref name="zeros"/> zero bits and a terminating one bit (FLAC's unary code).</summary>
    public void WriteUnary(uint zeros)
    {
        while (zeros >= 32)
        {
            Write(0, 32);
            zeros -= 32;
        }

        Write(1, (int)zeros + 1);
    }

    /// <summary>Pads with zero bits to the next byte boundary.</summary>
    public void AlignToByte()
    {
        var padding = (int)((8 - (_position & 7)) & 7);
        Write(0, padding);
    }
}
