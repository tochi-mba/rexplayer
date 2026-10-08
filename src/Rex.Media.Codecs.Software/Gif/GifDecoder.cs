// Spec: Graphics Interchange Format Version 89a (CompuServe, 1990): color tables, graphic control extension (disposal, transparency), image descriptor, interlaced rows, variable-length-code LZW (Appendix F).
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Gif;

/// <summary>Decodes GIF pictures (FMT-C19) with rexplayer's own decoder.</summary>
public sealed class GifDecoderFactory : IDecoderFactory
{
    public string Name => "rexplayer GIF";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.Gif && track.Video is not null;
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException("GIF is pictures, not sound.");

    public IVideoDecoder CreateVideo(TrackInfo track) => new GifDecoder(track);
}

/// <summary>
/// GIF pictures drawn onto the logical screen, as the GIF demuxer packs them: each packet is a
/// graphic control extension, an image descriptor, its color table and its LZW image data. Each
/// picture is drawn over the screen as the one before left it (its disposal: kept, cleared, or put
/// back as it was); transparent pixels let what is beneath show through. The screen starts clear at
/// a keyframe (each loop's first picture), and every packet gives a whole BGRA frame of the screen.
/// </summary>
public sealed class GifDecoder : IVideoDecoder
{
    private const int MaxCodes = 4096;

    private readonly int _width;
    private readonly int _height;
    private readonly uint[] _globalColors;
    private readonly uint[] _screen;
    private uint[]? _saved;

    // What the last picture asks to be done before the next is drawn.
    private int _lastDisposal;
    private (int Left, int Top, int Width, int Height) _lastArea;

    // LZW tables, kept between pictures.
    private readonly short[] _prefix = new short[MaxCodes];
    private readonly byte[] _suffix = new byte[MaxCodes];
    private readonly byte[] _stack = new byte[MaxCodes + 1];

    public GifDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var header = track.CodecPrivate;
        if (header.Length < 13)
        {
            throw new MediaFormatException("The GIF has no screen descriptor.");
        }

        _width = header[6] | (header[7] << 8);
        _height = header[8] | (header[9] << 8);
        if (_width == 0 || _height == 0)
        {
            (_width, _height) = (track.Video?.Width ?? 0, track.Video?.Height ?? 0);
        }

        if (_width is <= 0 or > PixelFormats.MaxDimension || _height is <= 0 or > PixelFormats.MaxDimension)
        {
            throw new MediaFormatException($"A GIF of {_width}x{_height} pixels is not one rexplayer shows.");
        }

        _globalColors = (header[10] & 0x80) != 0 ? Colors(header.AsSpan(13), header[10]) : [];
        _screen = new uint[_width * _height];
    }

    public string Name => "rexplayer GIF";

    public DecoderSource Source => DecoderSource.Own;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = packet.Data.Span;
        if (data.Length < 18 || data[0] != 0x21 || data[8] != 0x2C)
        {
            throw new MediaFormatException("A GIF picture is cut short.");
        }

        if (packet.IsKeyframe)
        {
            Array.Clear(_screen);
            _lastDisposal = 0;
        }

        ApplyDisposal(_lastDisposal, _lastArea);

        // The graphic control extension: disposal and transparency.
        var control = data[3];
        var disposal = (control >> 2) & 7;
        int? transparent = (control & 1) != 0 ? data[6] : null;

        var descriptor = data[8..];
        var left = descriptor[1] | (descriptor[2] << 8);
        var top = descriptor[3] | (descriptor[4] << 8);
        var width = descriptor[5] | (descriptor[6] << 8);
        var height = descriptor[7] | (descriptor[8] << 8);
        var flags = descriptor[9];
        var tableLength = (flags & 0x80) != 0 ? 3 << ((flags & 7) + 1) : 0;
        if (10 + tableLength + 1 > descriptor.Length)
        {
            throw new MediaFormatException("A GIF picture is cut short.");
        }

        var colors = tableLength > 0 ? Colors(descriptor[10..], flags) : _globalColors;
        if (disposal == 3)
        {
            _saved ??= new uint[_screen.Length];
            _screen.CopyTo(_saved, 0);
        }

        var indexes = new byte[width * height];
        var decoded = Lzw(descriptor[(10 + tableLength)..], indexes);
        Draw(indexes, decoded, left, top, width, height, (flags & 0x40) != 0, colors, transparent);
        (_lastDisposal, _lastArea) = (disposal, (left, top, width, height));

        var frame = VideoFrame.Rent(PixelFormat.Bgra32, _width, _height);
        frame.Pts = packet.Pts;
        frame.Duration = packet.Duration;
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        for (var y = 0; y < _height; y++)
        {
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(_screen.AsSpan(y * _width, _width)).CopyTo(plane[(y * stride)..]);
        }

        output.Add(frame);
    }

    public bool Drain(ICollection<VideoFrame> output) => true;

    /// <summary>A seek starts again at a loop's first picture, which clears the screen itself.</summary>
    public void Flush() => _lastDisposal = 0;

    public void Dispose()
    {
    }

    /// <summary>A color table as BGRA pixels, opaque.</summary>
    private static uint[] Colors(ReadOnlySpan<byte> table, byte flags)
    {
        var count = 2 << (flags & 7);
        if (table.Length < count * 3)
        {
            throw new MediaFormatException("A GIF color table is cut short.");
        }

        var colors = new uint[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = 0xFF000000u | ((uint)table[i * 3] << 16) | ((uint)table[(i * 3) + 1] << 8) | table[(i * 3) + 2];
        }

        return colors;
    }

    /// <summary>What the last picture asked for before the next: its area cleared, or the screen put back as it was before it.</summary>
    private void ApplyDisposal(int disposal, (int Left, int Top, int Width, int Height) area)
    {
        if (disposal == 2)
        {
            for (var y = Math.Max(0, area.Top); y < Math.Min(_height, area.Top + area.Height); y++)
            {
                var from = Math.Max(0, area.Left);
                var to = Math.Min(_width, area.Left + area.Width);
                if (to > from)
                {
                    _screen.AsSpan((y * _width) + from, to - from).Clear();
                }
            }
        }
        else if (disposal == 3 && _saved is not null)
        {
            _saved.CopyTo(_screen, 0);
        }
    }

    private void Draw(byte[] indexes, int decoded, int left, int top, int width, int height, bool interlaced, uint[] colors, int? transparent)
    {
        for (var row = 0; row < height; row++)
        {
            var y = top + (interlaced ? InterlacedRow(row, height) : row);
            if (y >= _height)
            {
                continue;
            }

            for (var x = 0; x < width && left + x < _width; x++)
            {
                var at = (row * width) + x;

                // Pixels the data stopped short of are left as they were.
                if (at >= decoded)
                {
                    return;
                }

                var index = indexes[at];
                if (index != transparent)
                {
                    _screen[(y * _width) + left + x] = index < colors.Length ? colors[index] : 0xFF000000u;
                }
            }
        }
    }

    /// <summary>The screen row of the <paramref name="row"/>th row stored: every 8th from 0, every 8th from 4, every 4th from 2, then every 2nd from 1.</summary>
    private static int InterlacedRow(int row, int height)
    {
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4) })
        {
            var count = Math.Max(0, (height - start + step - 1) / step);
            if (row < count)
            {
                return start + (row * step);
            }

            row -= count;
        }

        // The last pass: the odd rows.
        return 1 + (row * 2);
    }

    /// <summary>
    /// Decodes the LZW data (a code size, then sub-blocks) into <paramref name="output"/>; gives how
    /// many indexes it held. Damaged data ends the picture where it goes wrong, as browsers do.
    /// </summary>
    private int Lzw(ReadOnlySpan<byte> data, byte[] output)
    {
        int minimum = data[0];
        if (minimum is < 1 or > 11)
        {
            throw new MediaFormatException($"A GIF picture has an LZW code size of {minimum}.");
        }

        var clear = 1 << minimum;
        var end = clear + 1;
        int size = minimum + 1, next = clear + 2, previous = -1, written = 0;
        int bits = 0, buffer = 0;
        byte first = 0;
        for (var i = 0; i < clear; i++)
        {
            _prefix[i] = -1;
            _suffix[i] = (byte)i;
        }

        var at = 1;
        var blockLeft = 0;
        while (written < output.Length)
        {
            // Bytes come in sub-blocks, each after its length.
            while (bits < size)
            {
                if (blockLeft == 0)
                {
                    if (at >= data.Length || data[at] == 0)
                    {
                        return written;
                    }

                    blockLeft = data[at++];
                }

                if (at >= data.Length)
                {
                    return written;
                }

                buffer |= data[at++] << bits;
                bits += 8;
                blockLeft--;
            }

            var code = buffer & ((1 << size) - 1);
            buffer >>= size;
            bits -= size;
            if (code == clear)
            {
                (size, next, previous) = (minimum + 1, clear + 2, -1);
                continue;
            }

            if (code == end || (code > next) || (previous < 0 && code >= clear))
            {
                return written;
            }

            // A code not yet in the table is the previous string and its own first byte.
            var depth = 0;
            var walk = code;
            if (code == next)
            {
                _stack[depth++] = first;
                walk = previous;
            }

            while (walk >= clear)
            {
                _stack[depth++] = _suffix[walk];
                walk = _prefix[walk];
            }

            first = _suffix[walk];
            _stack[depth++] = first;
            while (depth > 0 && written < output.Length)
            {
                output[written++] = _stack[--depth];
            }

            if (previous >= 0 && next < MaxCodes)
            {
                _prefix[next] = (short)previous;
                _suffix[next] = first;
                next++;
                if (next == 1 << size && size < 12)
                {
                    size++;
                }
            }

            previous = code;
        }

        return written;
    }
}
