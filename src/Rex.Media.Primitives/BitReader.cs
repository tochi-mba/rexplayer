namespace Rex.Media.Primitives;

/// <summary>
/// Reads a bitstream most-significant bit first, the order MPEG audio and video, FLAC, AC-3 and the
/// H.264/HEVC parameter sets use. Reading past the end throws <see cref="MediaFormatException"/>
/// rather than returning zeros, so a truncated header can never be mistaken for a valid one.
/// </summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly long BitPosition => _position;

    public readonly long BitLength => (long)_data.Length * 8;

    public readonly long BitsRemaining => BitLength - _position;

    public readonly bool IsByteAligned => (_position & 7) == 0;

    /// <summary>The byte the next read starts in.</summary>
    public readonly int BytePosition => (int)(_position >> 3);

    /// <summary>Up to 32 bits as an unsigned value.</summary>
    public uint ReadBits(int count)
    {
        var value = PeekBits(count);
        _position += count;
        return value;
    }

    /// <summary>Up to 64 bits, for fields like FLAC's 36-bit total sample count.</summary>
    public ulong ReadBits64(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, 64u, nameof(count));
        if (count <= 32)
        {
            return ReadBits(count);
        }

        var high = (ulong)ReadBits(count - 32);
        return (high << 32) | ReadBits(32);
    }

    public readonly uint PeekBits(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, 32u, nameof(count));
        if (count == 0)
        {
            return 0;
        }

        if (count > BitsRemaining)
        {
            throw new MediaFormatException("The bitstream ended inside a field.");
        }

        var byteIndex = (int)(_position >> 3);
        var bitOffset = (int)(_position & 7);
        var byteCount = (bitOffset + count + 7) >> 3;
        ulong window = 0;
        for (var i = 0; i < byteCount; i++)
        {
            window = (window << 8) | _data[byteIndex + i];
        }

        var shift = (byteCount * 8) - bitOffset - count;
        return (uint)((window >> shift) & ((1UL << count) - 1));
    }

    public bool ReadBit() => ReadBits(1) != 0;

    /// <summary>A two's-complement field of <paramref name="count"/> bits, sign-extended.</summary>
    public int ReadSignedBits(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var value = ReadBits(count);
        var shift = 32 - count;
        return (int)(value << shift) >> shift;
    }

    public void SkipBits(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > BitsRemaining)
        {
            throw new MediaFormatException("The bitstream ended inside a skipped field.");
        }

        _position += count;
    }

    public void AlignToByte() => _position = (_position + 7) & ~7L;

    /// <summary>
    /// Moves to an absolute bit position, backwards or forwards. Formats whose fields have a
    /// declared length (a Layer III granule) jump to its end whatever the parse inside consumed.
    /// </summary>
    public void Seek(long bitPosition)
    {
        if (bitPosition < 0 || bitPosition > BitLength)
        {
            throw new MediaFormatException("A field points outside its bitstream.");
        }

        _position = bitPosition;
    }

    /// <summary>
    /// Counts zero bits up to and including the terminating one bit (FLAC's unary code). It looks at
    /// up to 32 bits at a time, because Rice-coded residuals make this the hottest read in a decoder.
    /// </summary>
    public uint ReadUnary()
    {
        uint zeros = 0;
        while (true)
        {
            var available = (int)Math.Min(32, BitsRemaining);
            if (available == 0)
            {
                throw new MediaFormatException("The bitstream ended inside a unary code.");
            }

            var window = PeekBits(available) << (32 - available);
            if (window != 0)
            {
                var leading = System.Numerics.BitOperations.LeadingZeroCount(window);
                _position += leading + 1;
                return zeros + (uint)leading;
            }

            zeros += (uint)available;
            _position += available;
        }
    }

    /// <summary>An unsigned Exp-Golomb code, ue(v) in the H.264 and HEVC specifications.</summary>
    public uint ReadUnsignedExpGolomb()
    {
        var leadingZeros = ReadUnary();
        if (leadingZeros > 31)
        {
            throw new MediaFormatException("An Exp-Golomb code is longer than 32 bits.");
        }

        return leadingZeros == 0 ? 0 : (uint)((1UL << (int)leadingZeros) - 1 + ReadBits((int)leadingZeros));
    }

    /// <summary>
    /// An unsigned Exp-Golomb code that the specification limits to <paramref name="maximum"/>; a larger
    /// one means the stream is damaged or hostile, so it is refused before it can size anything.
    /// </summary>
    public int ReadUnsignedExpGolomb(int maximum, string element)
    {
        var value = ReadUnsignedExpGolomb();
        if (value > maximum)
        {
            throw new MediaFormatException($"{element} is {value}, above the {maximum} the specification allows.");
        }

        return (int)value;
    }

    /// <summary>A signed Exp-Golomb code, se(v): 1, -1, 2, -2, ... for codes 1, 2, 3, 4, ...</summary>
    public int ReadSignedExpGolomb()
    {
        var code = ReadUnsignedExpGolomb();
        var magnitude = (int)((code + 1) >> 1);
        return (code & 1) == 1 ? magnitude : -magnitude;
    }
}
