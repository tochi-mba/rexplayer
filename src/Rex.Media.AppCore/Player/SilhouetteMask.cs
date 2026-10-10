namespace Rex.Media.AppCore.Player;

/// <summary>
/// Estimates foreground in a fixed camera for the silhouette visualisation (AU-18), without
/// claiming semantic person recognition. Learns an empty-room reference, compensates for uniform
/// lighting changes and colour contrast, closes small holes and removes disconnected noise. Connected
/// outlines, including limbs and multiple people, remain. It works best with a stable camera and an
/// empty background during calibration; no frame is saved or sent anywhere.
/// </summary>
public sealed class SilhouetteMask
{
    /// <summary>How many pictures are watched before anything is marked.</summary>
    public const int LearningPictures = 15;

    private const float RoomRate = 0.03f;
    private const float PersonRate = 0.0015f;

    private readonly float[] _room;
    private readonly float[] _roomBlue;
    private readonly float[] _roomRed;
    private readonly byte[] _currentLuma;
    private readonly short[] _currentBlue;
    private readonly short[] _currentRed;
    private readonly byte[] _raw;
    private readonly byte[] _expanded;
    private readonly byte[] _clean;
    private readonly bool[] _visited;
    private readonly int[] _queue;
    private int _seen;

    public SilhouetteMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 3);
        (Width, Height) = (width, height);
        _room = new float[width * height];
        _roomBlue = new float[width * height];
        _roomRed = new float[width * height];
        _currentLuma = new byte[width * height];
        _currentBlue = new short[width * height];
        _currentRed = new short[width * height];
        _raw = new byte[width * height];
        _expanded = new byte[width * height];
        _clean = new byte[width * height];
        _visited = new bool[width * height];
        _queue = new int[width * height];
        Mask = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>255 where the person is, 0 elsewhere, row by row.</summary>
    public byte[] Mask { get; }

    /// <summary>Whether it is still watching the room.</summary>
    public bool IsLearning => _seen < LearningPictures;

    /// <summary>
    /// Takes the next picture's brightness (<see cref="Width"/> by <see cref="Height"/>) and marks
    /// what differs from the room by more than <paramref name="threshold"/> (0 to 1); gives the share
    /// of the picture marked.
    /// </summary>
    public double Update(ReadOnlySpan<byte> brightness, double threshold) =>
        UpdateCore(brightness, threshold, colour: false);

    /// <summary>
    /// Convert a BGRA camera frame into area-averaged luminance and two signed colour
    /// differences. Unlike brightness-only subtraction, this distinguishes similarly bright
    /// clothing and walls. Mirroring applies to the camera mask, not the source image.
    /// </summary>
    public double UpdateColour(ReadOnlySpan<byte> bgra, int sourceWidth, int sourceHeight,
        double threshold, bool mirror)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        if (bgra.Length < (long)sourceWidth * sourceHeight * 4)
        {
            throw new ArgumentException("The camera picture is shorter than its dimensions.", nameof(bgra));
        }

        for (var y = 0; y < Height; y++)
        {
            var top = y * sourceHeight / Height;
            var bottom = Math.Max(top + 1, (y + 1) * sourceHeight / Height);
            for (var x = 0; x < Width; x++)
            {
                var left = x * sourceWidth / Width;
                var right = Math.Max(left + 1, (x + 1) * sourceWidth / Width);
                var b = 0;
                var g = 0;
                var r = 0;
                var count = 0;
                for (var sy = top; sy < Math.Min(sourceHeight, bottom); sy++)
                {
                    for (var sx = left; sx < Math.Min(sourceWidth, right); sx++)
                    {
                        var at = (sy * sourceWidth + sx) * 4;
                        b += bgra[at];
                        g += bgra[at + 1];
                        r += bgra[at + 2];
                        count++;
                    }
                }

                b /= count;
                g /= count;
                r /= count;
                var luma = (r * 77 + g * 150 + b * 29) >> 8;
                var index = y * Width + (mirror ? Width - 1 - x : x);
                _currentLuma[index] = (byte)luma;
                _currentBlue[index] = (short)(b - luma);
                _currentRed[index] = (short)(r - luma);
            }
        }

        return UpdateCore(_currentLuma, threshold, colour: true);
    }

    private double UpdateCore(ReadOnlySpan<byte> brightness, double threshold, bool colour)
    {
        if (brightness.Length != _room.Length)
        {
            throw new ArgumentException($"A picture of {Width}x{Height} has {_room.Length} pixels, not {brightness.Length}.", nameof(brightness));
        }

        var limit = (float)(Math.Clamp(double.IsFinite(threshold) ? threshold : 0.12, 0.01, 1) * 255);
        if (IsLearning)
        {
            // The room is the average of the pictures watched so far.
            _seen++;
            for (var i = 0; i < _room.Length; i++)
            {
                _room[i] += (brightness[i] - _room[i]) / _seen;
                if (colour)
                {
                    _roomBlue[i] += (_currentBlue[i] - _roomBlue[i]) / _seen;
                    _roomRed[i] += (_currentRed[i] - _roomRed[i]) / _seen;
                }
            }

            Array.Clear(Mask);
            return 0;
        }

        // Median per-frame change is robust when a person occupies less than half the picture.
        // Compensating for it prevents an automatic exposure adjustment from outlining the room.
        // A histogram is stack-only: no per-camera-frame allocation.
        Span<int> histogram = stackalloc int[511];
        histogram.Clear(); // stackalloc storage is not initialized; the median must be deterministic.
        for (var i = 0; i < _room.Length; i += 4)
        {
            histogram[Math.Clamp((int)MathF.Round(brightness[i] - _room[i]), -255, 255) + 255]++;
        }

        var middle = ((_room.Length + 3) / 4 + 1) / 2;
        var seen = 0;
        var global = 0;
        for (var bucket = 0; bucket < histogram.Length; bucket++)
        {
            seen += histogram[bucket];
            if (seen >= middle)
            {
                global = bucket - 255;
                break;
            }
        }

        // With a tiny synthetic image there is no stable background majority to estimate.
        if (_room.Length < 256)
        {
            global = 0;
        }

        for (var i = 0; i < _room.Length; i++)
        {
            var difference = brightness[i] - _room[i] - global;
            // A weak threshold for last frame's confirmed mask preserves slender limbs
            // that would flicker around the edge. The preceding frame is still intact.
            var cutoff = Mask[i] != 0 ? limit * 0.78f : limit;
            var chroma = colour && MathF.Abs(_currentBlue[i] - _roomBlue[i])
                + MathF.Abs(_currentRed[i] - _roomRed[i]) > cutoff * 0.85f;
            var foreground = Math.Abs(difference) > cutoff || chroma;
            _raw[i] = foreground ? (byte)1 : (byte)0;
            var rate = foreground ? PersonRate : RoomRate;
            _room[i] += (brightness[i] - _room[i]) * rate;
            if (colour)
            {
                _roomBlue[i] += (_currentBlue[i] - _roomBlue[i]) * rate;
                _roomRed[i] += (_currentRed[i] - _roomRed[i]) * rate;
            }
        }

        // Morphological closing preserves connected thin limbs and fills tiny gaps, unlike a
        // majority filter, which shaves fingers and arms off an otherwise good silhouette.
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                byte on = 0;
                for (var dy = -1; dy <= 1 && on == 0; dy++)
                {
                    var row = Math.Clamp(y + dy, 0, Height - 1) * Width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (_raw[row + Math.Clamp(x + dx, 0, Width - 1)] != 0)
                        {
                            on = 1;
                            break;
                        }
                    }
                }

                _expanded[(y * Width) + x] = on;
            }
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                byte on = 1;
                for (var dy = -1; dy <= 1 && on != 0; dy++)
                {
                    var row = Math.Clamp(y + dy, 0, Height - 1) * Width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (_expanded[row + Math.Clamp(x + dx, 0, Width - 1)] == 0)
                        {
                            on = 0;
                            break;
                        }
                    }
                }

                _clean[(y * Width) + x] = on;
            }
        }

        // Keep every sufficiently large connected region (not merely the largest person). A
        // one-pixel noise island is discarded, even when contrast makes it pass the threshold.
        Array.Clear(_visited);
        Array.Clear(Mask);
        var minArea = Math.Max(2, Mask.Length / 4000);
        var marked = 0;
        for (var i = 0; i < _clean.Length; i++)
        {
            if (_clean[i] == 0 || _visited[i])
            {
                continue;
            }

            _visited[i] = true;
            var start = 0;
            var count = 1;
            _queue[0] = i;
            while (start < count)
            {
                var point = _queue[start++];
                var x = point % Width;
                var y = point / Width;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= Height)
                    {
                        continue;
                    }

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= Width)
                        {
                            continue;
                        }

                        var neighbour = (ny * Width) + nx;
                        if (_clean[neighbour] == 0 || _visited[neighbour])
                        {
                            continue;
                        }

                        _visited[neighbour] = true;
                        _queue[count++] = neighbour;
                    }
                }
            }

            // Border-clamped closing can expand a lone bright speck to two pixels. Require
            // enough actual raw foreground evidence as well as a connected cleaned region.
            var rawCount = 0;
            for (var n = 0; n < count; n++)
            {
                rawCount += _raw[_queue[n]];
            }

            if (count >= minArea && rawCount >= minArea)
            {
                for (var n = 0; n < count; n++)
                {
                    Mask[_queue[n]] = 255;
                }

                marked += count;
            }
        }

        return (double)marked / Mask.Length;
    }

    /// <summary>Whether a marked pixel of <paramref name="mask"/> is on the person's edge: one of the four beside it is not marked.</summary>
    public static bool IsEdge(ReadOnlySpan<byte> mask, int width, int height, int x, int y)
    {
        if (mask[(y * width) + x] == 0)
        {
            return false;
        }

        return x == 0 || y == 0 || x == width - 1 || y == height - 1
            || mask[(y * width) + x - 1] == 0 || mask[(y * width) + x + 1] == 0 || mask[((y - 1) * width) + x] == 0 || mask[((y + 1) * width) + x] == 0;
    }

    /// <summary>Forgets the room and watches it again (the camera moved, say).</summary>
    public void Reset()
    {
        _seen = 0;
        Array.Clear(_room);
        Array.Clear(_roomBlue);
        Array.Clear(_roomRed);
        Array.Clear(_currentLuma);
        Array.Clear(_currentBlue);
        Array.Clear(_currentRed);
        Array.Clear(Mask);
    }

    /// <summary>
    /// The brightness of a BGRA picture, shrunk to <paramref name="width"/> by <paramref name="height"/>
    /// (each pixel the nearest of the original), mirrored left for right when asked.
    /// </summary>
    public static byte[] Brightness(ReadOnlySpan<byte> bgra, int sourceWidth, int sourceHeight, int stride, int width, int height, bool mirror)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, sourceWidth * 4);
        if (bgra.Length < ((sourceHeight - 1) * stride) + (sourceWidth * 4))
        {
            throw new ArgumentException("The picture is shorter than its size says.", nameof(bgra));
        }

        var output = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = Math.Min(sourceHeight - 1, y * sourceHeight / height) * stride;
            for (var x = 0; x < width; x++)
            {
                var column = Math.Min(sourceWidth - 1, (mirror ? width - 1 - x : x) * sourceWidth / width) * 4;
                var (b, g, r) = (bgra[row + column], bgra[row + column + 1], bgra[row + column + 2]);

                // Rec. 601 weights, in whole numbers.
                output[(y * width) + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) >> 8);
            }
        }

        return output;
    }
}
