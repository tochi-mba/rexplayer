namespace Rex.Media.AppCore.Player;

/// <summary>
/// Picks a person out of a camera's pictures for the silhouette visualisation (AU-18), with no
/// learning model: it watches the room for its first pictures, then marks what differs from the
/// room as it has come to look. The room's picture keeps adjusting, quickly where nobody is (light
/// changing) and very slowly where somebody is, and a mark needs most of its neighbours marked too,
/// so specks of noise fall away. Pictures come in as rows of brightness, smallest first.
/// </summary>
public sealed class SilhouetteMask
{
    /// <summary>How many pictures are watched before anything is marked.</summary>
    public const int LearningPictures = 15;

    private const float RoomRate = 0.03f;
    private const float PersonRate = 0.0015f;

    private readonly float[] _room;
    private readonly byte[] _raw;
    private int _seen;

    public SilhouetteMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 3);
        (Width, Height) = (width, height);
        _room = new float[width * height];
        _raw = new byte[width * height];
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
    public double Update(ReadOnlySpan<byte> brightness, double threshold)
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
            }

            Array.Clear(Mask);
            return 0;
        }

        for (var i = 0; i < _room.Length; i++)
        {
            var person = Math.Abs(brightness[i] - _room[i]) > limit;
            _raw[i] = person ? (byte)1 : (byte)0;
            _room[i] += (brightness[i] - _room[i]) * (person ? PersonRate : RoomRate);
        }

        // A pixel stays marked when at least five of the nine around it (itself included) are.
        var marked = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var row = Math.Clamp(y + dy, 0, Height - 1) * Width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        count += _raw[row + Math.Clamp(x + dx, 0, Width - 1)];
                    }
                }

                var on = count >= 5;
                Mask[(y * Width) + x] = on ? (byte)255 : (byte)0;
                marked += on ? 1 : 0;
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
