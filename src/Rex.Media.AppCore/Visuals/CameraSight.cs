namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// What the camera visualisations share (AU-18): where the listener is in the camera's outline,
/// how a camera picture sits on the stage, and what to show while there is no picture yet (the camera
/// starting, or not allowed): a ring of light that breathes with the bass and flares on the kick.
/// </summary>
internal static class CameraSight
{
    /// <summary>Whether the outline <paramref name="mask"/> (<paramref name="w"/> by <paramref name="h"/>) holds the listener at (<paramref name="x"/>, <paramref name="y"/>); outside it, no.</summary>
    public static bool Inside(byte[] mask, int w, int h, double x, double y)
    {
        var (u, v) = ((int)x, (int)y);
        return u >= 0 && v >= 0 && u < w && v < h && mask[(v * w) + u] > 0;
    }

    /// <summary>The stage's point for a picture of <paramref name="across"/> by <paramref name="down"/>, filling the stage, cut at its edges.</summary>
    public static (double Scale, double Left, double Top) Cover(int width, int height, int across, int down)
    {
        var scale = Math.Max((double)width / across, (double)height / down);
        return (scale, (width - (across * scale)) / 2, (height - (down * scale)) / 2);
    }

    /// <summary>
    /// The picture at (<paramref name="px"/>, <paramref name="py"/>) in the camera's own colours, or,
    /// as the visualisations show it unless asked otherwise, its brightness in the palette's colour
    /// <paramref name="share"/> of the way along.
    /// </summary>
    public static Rgb Tone(VisualContext context, VisualPicture picture, double px, double py, bool natural, double share)
    {
        var color = new Rgb(picture.Sample(px, py, 2), picture.Sample(px, py, 1), picture.Sample(px, py, 0));
        return natural ? color : context.Paint(share).Times(0.1f + (1.1f * color.Luma));
    }

    /// <summary>The middle of the listener in the outline, or the middle of the outline (or of nothing) when there is no one.</summary>
    public static (double X, double Y) Centre(byte[]? mask, int w, int h)
    {
        if (mask is null || w < 1 || h < 1)
        {
            return (w / 2.0, h / 2.0);
        }

        var (sx, sy, n) = (0.0, 0.0, 0.0);
        for (var y = 0; y < h; y += 2)
        {
            for (var x = 0; x < w; x += 2)
            {
                if (mask[(y * w) + x] > 0)
                {
                    (sx, sy, n) = (sx + x, sy + y, n + 1);
                }
            }
        }

        return n > 0 ? (sx / n, sy / n) : (w / 2.0, h / 2.0);
    }

    /// <summary>While there is no camera picture: a ring that breathes with the bass and flares with the kick.</summary>
    public static void Waiting(VisualContext context)
    {
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var size = Math.Min(canvas.Width, canvas.Height);
        canvas.Clear(Rgb.Black);
        canvas.Ring(canvas.Width / 2.0, canvas.Height / 2.0, size * (0.22 + (0.08 * pulse.Bass)), Math.Max(1, size * (0.01 + (0.03 * pulse.Punch))), context.Paint(pulse.Pitch), 0.4f + pulse.Punch);
        canvas.Glow(canvas.Width / 2.0, canvas.Height / 2.0, size * 0.3, context.Paint(0.5), 0.1f + (0.3f * pulse.Bass));
    }
}
