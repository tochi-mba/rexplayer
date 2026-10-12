namespace Rex.Media.Vision;

/// <summary>
/// A borrowed view of a picture in system memory, 8-bit blue, green, red and alpha per pixel, rows
/// <see cref="Stride"/> bytes apart: what a <c>VideoFrame</c>'s first plane holds for BGRA video.
/// </summary>
public readonly ref struct BgraPicture
{
    public BgraPicture(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (pixels.Length < ((long)stride * (height - 1)) + (width * 4))
        {
            throw new ArgumentException("The pixels are shorter than the picture they describe.", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public ReadOnlySpan<byte> Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    /// <summary>The <see cref="Width"/> pixels of row <paramref name="y"/>, four bytes each.</summary>
    public ReadOnlySpan<byte> Row(int y) => Pixels.Slice(y * Stride, Width * 4);
}

/// <summary>
/// What identifies one picture for caching: a model's encoding of a picture is reused while the key
/// stays the same, so several clicks on a paused picture run the expensive encoder once.
/// </summary>
public readonly record struct PictureKey(long Generation, long Timestamp);

/// <summary>A rectangle of whole pixels.</summary>
public readonly record struct PixelBox(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The box grown by <paramref name="fraction"/> of its size on every side, kept inside a picture.</summary>
    public PixelBox Inflate(double fraction, int pictureWidth, int pictureHeight)
    {
        var dx = (int)Math.Round(Width * fraction);
        var dy = (int)Math.Round(Height * fraction);
        var left = Math.Clamp(Left - dx, 0, pictureWidth);
        var top = Math.Clamp(Top - dy, 0, pictureHeight);
        var right = Math.Clamp(Right + dx, 0, pictureWidth);
        var bottom = Math.Clamp(Bottom + dy, 0, pictureHeight);
        return new PixelBox(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// What the user pointed at: a pixel inside the object, a box around it, or both. Coordinates are
/// in the picture's own pixels, independent of the window and any zoom.
/// </summary>
public readonly record struct SegmentPrompt
{
    /// <param name="x">A pixel on the object, across.</param>
    /// <param name="y">A pixel on the object, down.</param>
    /// <param name="box">A box around the object.</param>
    /// <param name="expectedArea">
    /// When following an object, its area in the last picture: of the model's candidate outlines,
    /// the one closest to that size is wanted rather than the largest.
    /// </param>
    public SegmentPrompt(int? x = null, int? y = null, PixelBox? box = null, int? expectedArea = null)
    {
        if (expectedArea is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedArea), expectedArea, "An expected area is positive.");
        }

        if (x.HasValue != y.HasValue)
        {
            throw new ArgumentException("A point needs both coordinates.", nameof(x));
        }

        if (!x.HasValue && box is null)
        {
            throw new ArgumentException("A prompt needs a point, a box or both.", nameof(box));
        }

        if (box is { IsEmpty: true })
        {
            throw new ArgumentException("A prompt box has an area.", nameof(box));
        }

        X = x;
        Y = y;
        Box = box;
        ExpectedArea = expectedArea;
    }

    public int? X { get; }

    public int? Y { get; }

    public PixelBox? Box { get; }

    public int? ExpectedArea { get; }

    public static SegmentPrompt At(int x, int y) => new(x, y);

    public static SegmentPrompt Around(PixelBox box) => new(box: box);

    /// <summary>The prompt that looks for an object of <paramref name="area"/> pixels inside <paramref name="box"/>.</summary>
    public static SegmentPrompt Following(PixelBox box, int area) => new(box: box, expectedArea: area);
}

/// <summary>
/// A cut-out: one byte per pixel, 255 inside the object and 0 outside, at the picture's size, with
/// its bounds and the model's own estimate of how good the outline is (0 to 1).
/// </summary>
public sealed class PictureMask
{
    public PictureMask(byte[] values, int width, int height, double score)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (values.Length != width * height)
        {
            throw new ArgumentException("A mask has one value per pixel.", nameof(values));
        }

        Values = values;
        Width = width;
        Height = height;
        Score = Math.Clamp(score, 0, 1);
        int left = width, top = height, right = -1, bottom = -1, area = 0;
        for (var y = 0; y < height; y++)
        {
            var row = values.AsSpan(y * width, width);
            var first = row.IndexOfAnyExcept((byte)0);
            if (first < 0)
            {
                continue;
            }

            var last = row.LastIndexOfAnyExcept((byte)0);
            left = Math.Min(left, first);
            right = Math.Max(right, last);
            top = Math.Min(top, y);
            bottom = y;
            area += width - row.Count((byte)0);
        }

        Area = area;
        Bounds = area == 0 ? default : new PixelBox(left, top, right - left + 1, bottom - top + 1);
    }

    public byte[] Values { get; }

    public int Width { get; }

    public int Height { get; }

    public double Score { get; }

    /// <summary>Pixels inside the object.</summary>
    public int Area { get; }

    /// <summary>The smallest box holding every pixel inside, or an empty box for an empty mask.</summary>
    public PixelBox Bounds { get; }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Values[(y * Width) + x] != 0;
}

/// <summary>Whether the followed object was found again.</summary>
public enum TrackOutcome
{
    Tracked,
    Lost,
}

/// <summary>A tracker's answer for one picture: the new mask, or why it gave up, in plain words.</summary>
public readonly record struct TrackResult(TrackOutcome Outcome, PictureMask? Mask, string Reason)
{
    public static TrackResult Found(PictureMask mask) => new(TrackOutcome.Tracked, mask, "");

    public static TrackResult Lost(string reason) => new(TrackOutcome.Lost, null, reason);
}
