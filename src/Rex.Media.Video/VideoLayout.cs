using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>Where a picture goes in a window: as large as fits, at its display aspect, centred, with black bars.</summary>
public static class VideoLayout
{
    /// <summary>
    /// The rectangle a picture of <paramref name="width"/> by <paramref name="height"/> samples with
    /// <paramref name="pixelAspect"/> fills in a target of the given size, in whole pixels.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Fit(int width, int height, Rational pixelAspect, int targetWidth, int targetHeight)
    {
        if (width <= 0 || height <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return (0, 0, Math.Max(targetWidth, 0), Math.Max(targetHeight, 0));
        }

        // The display aspect is (width x numerator) : (height x denominator); exact integers, rounded half up.
        var (numerator, denominator) = pixelAspect.Numerator > 0 ? (pixelAspect.Numerator, pixelAspect.Denominator) : (1L, 1L);
        long across = width * numerator, down = height * denominator;
        var fitWidth = targetWidth;
        var fitHeight = (int)(((2 * targetWidth * down) + across) / (2 * across));
        if (fitHeight > targetHeight)
        {
            fitHeight = targetHeight;
            fitWidth = (int)(((2 * targetHeight * across) + down) / (2 * down));
        }

        return ((targetWidth - fitWidth) / 2, (targetHeight - fitHeight) / 2, fitWidth, fitHeight);
    }
}
