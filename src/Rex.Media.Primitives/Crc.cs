namespace Rex.Media.Primitives;

/// <summary>
/// A table-driven cyclic redundancy check of up to 32 bits, parameterised the way the CRC catalogue
/// describes them (width, polynomial, initial value, reflection, final XOR). The named instances are
/// the ones media formats use; each is checked against its catalogue "check" value for "123456789".
/// </summary>
public sealed class Crc
{
    private readonly uint[] _table = new uint[256];
    private readonly uint _mask;

    public Crc(int width, uint polynomial, uint initial, bool reflected, uint finalXor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32);
        Width = width;
        Polynomial = polynomial;
        Initial = initial;
        Reflected = reflected;
        FinalXor = finalXor;
        _mask = width == 32 ? uint.MaxValue : (1u << width) - 1;
        for (uint i = 0; i < 256; i++)
        {
            _table[i] = reflected ? ReflectedEntry(i) : NormalEntry(i);
        }
    }

    /// <summary>FLAC frame header (CRC-8, polynomial x^8 + x^2 + x + 1).</summary>
    public static Crc Crc8Flac { get; } = new(8, 0x07, 0, reflected: false, 0);

    /// <summary>FLAC frame footer (CRC-16/BUYPASS: polynomial 0x8005, initial 0).</summary>
    public static Crc Crc16Flac { get; } = new(16, 0x8005, 0, reflected: false, 0);

    /// <summary>MPEG audio frame protection and AC-3 (polynomial 0x8005, initial 0xFFFF).</summary>
    public static Crc Crc16Mpeg { get; } = new(16, 0x8005, 0xFFFF, reflected: false, 0);

    /// <summary>MPEG-2 transport stream sections (CRC-32/MPEG-2).</summary>
    public static Crc Crc32Mpeg2 { get; } = new(32, 0x04C11DB7, 0xFFFFFFFF, reflected: false, 0);

    /// <summary>Ogg pages (polynomial 0x04C11DB7, initial 0, no reflection).</summary>
    public static Crc Crc32Ogg { get; } = new(32, 0x04C11DB7, 0, reflected: false, 0);

    /// <summary>CRC-32C (Castagnoli), used by the library store's record framing.</summary>
    public static Crc Crc32C { get; } = new(32, 0x1EDC6F41, 0xFFFFFFFF, reflected: true, 0xFFFFFFFF);

    public int Width { get; }

    public uint Polynomial { get; }

    public uint Initial { get; }

    public bool Reflected { get; }

    public uint FinalXor { get; }

    public uint Compute(ReadOnlySpan<byte> data) => Finish(Append(Start(), data));

    /// <summary>The register before any data, for checks computed in pieces.</summary>
    public uint Start() => Initial & _mask;

    public uint Append(uint register, ReadOnlySpan<byte> data)
    {
        if (Reflected)
        {
            foreach (var b in data)
            {
                register = _table[(register ^ b) & 0xFF] ^ (register >> 8);
            }

            return register & _mask;
        }

        var shift = Width - 8;
        foreach (var b in data)
        {
            var index = ((register >> shift) ^ b) & 0xFF;
            register = (_table[index] ^ (register << 8)) & _mask;
        }

        return register;
    }

    public uint Finish(uint register) => (register ^ FinalXor) & _mask;

    private uint NormalEntry(uint index)
    {
        var top = 1u << (Width - 1);
        var register = index << (Width - 8);
        for (var bit = 0; bit < 8; bit++)
        {
            register = (register & top) != 0 ? (register << 1) ^ Polynomial : register << 1;
        }

        return register & _mask;
    }

    private uint ReflectedEntry(uint index)
    {
        var reflectedPolynomial = Reverse(Polynomial, Width);
        var register = index;
        for (var bit = 0; bit < 8; bit++)
        {
            register = (register & 1) != 0 ? (register >> 1) ^ reflectedPolynomial : register >> 1;
        }

        return register & _mask;
    }

    private static uint Reverse(uint value, int width)
    {
        uint result = 0;
        for (var i = 0; i < width; i++)
        {
            result = (result << 1) | ((value >> i) & 1);
        }

        return result;
    }
}
