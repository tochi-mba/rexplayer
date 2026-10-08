// Spec: Vorbis I specification, section 2.1 (bitpacking: bits are read from the least significant end of each byte, in order).
namespace Rex.Media.Codecs.Vorbis;

/// <summary>
/// Reads a Vorbis packet's bits, least significant first. Reading past the end gives zeros and sets
/// <see cref="EndOfPacket"/>, which the decoder treats as the spec's end-of-packet condition.
/// </summary>
internal sealed class VorbisBits
{
    private byte[] _data = [];
    private int _length;
    private long _bit;

    public VorbisBits()
    {
    }

    public VorbisBits(byte[] data) => Reset(data, data.Length);

    /// <summary>Whether a read went past the end of the packet.</summary>
    public bool EndOfPacket { get; private set; }

    public void Reset(byte[] data, int length)
    {
        _data = data;
        _length = length;
        _bit = 0;
        EndOfPacket = false;
    }

    /// <summary>Reads <paramref name="count"/> bits (up to 32) as an unsigned number.</summary>
    public uint Read(int count)
    {
        uint value = 0;
        var shift = 0;
        while (count > 0)
        {
            var index = (int)(_bit >> 3);
            if (index >= _length)
            {
                EndOfPacket = true;
                _bit += count;
                return value;
            }

            var offset = (int)(_bit & 7);
            var take = Math.Min(8 - offset, count);
            var bits = (uint)(_data[index] >> offset) & ((1u << take) - 1);
            value |= bits << shift;
            shift += take;
            count -= take;
            _bit += take;
        }

        return value;
    }

    public int ReadInt(int count) => (int)Read(count);

    public bool ReadFlag() => Read(1) != 0;

    /// <summary>The next <paramref name="count"/> bits (up to 24) without moving on; zeros past the end.</summary>
    public uint Peek(int count)
    {
        uint value = 0;
        var position = _bit;
        var shift = 0;
        while (count > 0)
        {
            var index = (int)(position >> 3);
            if (index >= _length)
            {
                return value;
            }

            var offset = (int)(position & 7);
            var take = Math.Min(8 - offset, count);
            value |= ((uint)(_data[index] >> offset) & ((1u << take) - 1)) << shift;
            shift += take;
            count -= take;
            position += take;
        }

        return value;
    }

    /// <summary>Moves on <paramref name="count"/> bits.</summary>
    public void Skip(int count)
    {
        _bit += count;
        if (_bit > (long)_length * 8)
        {
            EndOfPacket = true;
        }
    }
}
