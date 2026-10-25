using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>A rectangle of whole pixels inside a picture.</summary>
public readonly record struct PictureArea(int X, int Y, int Width, int Height);

/// <summary>
/// Finds the black or grey bars recorded into a picture: a wide film letterboxed into a 16:9 frame,
/// a 4:3 programme pillarboxed into one, or both. A thumbnail cut from the picture inside them fills
/// a card's frame with picture rather than bars. Each edge is walked inward while its lines stay one
/// dark, neutral colour; a picture that is all one colour (a fade to black) is left whole.
/// </summary>
public static class PictureBorders
{
    /// <summary>How far a channel may stray from the bar's colour: compression noise stays inside it.</summary>
    private const int Tolerance = 24;

    /// <summary>The brightest a bar may be, so a white or bright sky is never taken for one.</summary>
    private const int Darkest = 96;

    /// <summary>One pixel in this many may stray, for noise at a bar's inner edge.</summary>
    private const int Strays = 50;

    /// <summary>The picture left must keep at least this share of each side.</summary>
    private const int KeptShare = 3;

    /// <summary>
    /// How many lines past a bar may still be its fringe: where a decoder's colour, shared by
    /// neighbouring lines, bleeds from the picture into the bar's last lines, so they are no longer
    /// one colour but still dark.
    /// </summary>
    private const int Fringe = 3;

    /// <summary>The brightest a fringe line's pixels may be: far darker than any picture worth keeping.</summary>
    private const int FringeDarkest = 48;

    /// <summary>The part of <paramref name="frame"/> (BGRA) inside its bars; the whole picture when it has none.</summary>
    public static PictureArea Find(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format != PixelFormat.Bgra32)
        {
            throw new ArgumentException("The picture must be BGRA.", nameof(frame));
        }

        var whole = new PictureArea(0, 0, frame.Width, frame.Height);
        var top = Bar(frame, horizontal: true, from: 0, step: 1, limit: frame.Height);
        var bottom = Bar(frame, horizontal: true, from: frame.Height - 1, step: -1, limit: frame.Height);
        var left = Bar(frame, horizontal: false, from: 0, step: 1, limit: frame.Width);
        var right = Bar(frame, horizontal: false, from: frame.Width - 1, step: -1, limit: frame.Width);
        var area = new PictureArea(left, top, frame.Width - left - right, frame.Height - top - bottom);
        return area.Width * KeptShare < frame.Width || area.Height * KeptShare < frame.Height ? whole : area;
    }

    /// <summary>
    /// Writes <paramref name="frame"/> as a PNG without its bars, converting it to BGRA first if it is
    /// not already: the picture a card's thumbnail is made from.
    /// </summary>
    public static void WritePng(Stream output, VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var converted = frame.Format == PixelFormat.Bgra32 ? null : ColorConverter.ToBgra(frame);
        var bgra = converted ?? frame;
        PngWriter.Write(output, bgra, Find(bgra));
    }

    /// <summary>How many lines from one edge are bar, walking inward from <paramref name="from"/>.</summary>
    private static int Bar(VideoFrame frame, bool horizontal, int from, int step, int limit)
    {
        var length = horizontal ? frame.Width : frame.Height;
        var (blue, green, red) = Colour(frame, horizontal, from, length);
        if (Math.Max(red, Math.Max(green, blue)) > Darkest
            || Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > Tolerance)
        {
            return 0;
        }

        var lines = 0;
        var at = from;
        for (; lines < limit && Uniform(frame, horizontal, at, length, blue, green, red); at += step)
        {
            lines++;
        }

        // A bar's last lines, tinted by the picture beside them, are bar too: shown magnified on a
        // card they would be a dark band along its edge.
        for (var fringe = 0; lines > 0 && fringe < Fringe && lines < limit && Dark(frame, horizontal, at, length); fringe++, at += step)
        {
            lines++;
        }

        return lines;
    }

    /// <summary>Whether every pixel of a line but the odd stray is dark in every channel.</summary>
    private static bool Dark(VideoFrame frame, bool horizontal, int line, int length)
    {
        var strays = 0;
        for (var i = 0; i < length; i++)
        {
            var (b, g, r) = Pixel(frame, horizontal, line, i);
            if (Math.Max(r, Math.Max(g, b)) > FringeDarkest && ++strays * Strays > length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The average colour of one line.</summary>
    private static (int Blue, int Green, int Red) Colour(VideoFrame frame, bool horizontal, int line, int length)
    {
        long blue = 0, green = 0, red = 0;
        for (var i = 0; i < length; i++)
        {
            var (b, g, r) = Pixel(frame, horizontal, line, i);
            blue += b;
            green += g;
            red += r;
        }

        return ((int)(blue / length), (int)(green / length), (int)(red / length));
    }

    /// <summary>Whether every pixel of a line but the odd stray is within reach of the bar's colour.</summary>
    private static bool Uniform(VideoFrame frame, bool horizontal, int line, int length, int blue, int green, int red)
    {
        var strays = 0;
        for (var i = 0; i < length; i++)
        {
            var (b, g, r) = Pixel(frame, horizontal, line, i);
            if ((Math.Abs(b - blue) > Tolerance || Math.Abs(g - green) > Tolerance || Math.Abs(r - red) > Tolerance)
                && ++strays * Strays > length)
            {
                return false;
            }
        }

        return true;
    }

    private static (int Blue, int Green, int Red) Pixel(VideoFrame frame, bool horizontal, int line, int i)
    {
        var (x, y) = horizontal ? (i, line) : (line, i);
        var row = frame.Row(0, y);
        return (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2]);
    }
}
