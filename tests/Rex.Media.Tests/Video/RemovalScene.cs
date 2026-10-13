using Rex.Media.Primitives;

namespace Rex.Media.Tests.Video;

/// <summary>
/// A synthetic shot with a known answer: a brick wall with fine grain, filmed by a camera that may
/// pan, and a figure with a soft edge walking across it. Every picture comes with the wall alone and
/// the figure's exact opacity, so a removal can be scored against the truth.
/// </summary>
internal sealed class RemovalScene
{
    private readonly Func<int, (float X, float Y)> _camera;
    private readonly Func<int, (float X, float Y)> _figure;

    public RemovalScene(int width, int height, Func<int, (float X, float Y)> camera, Func<int, (float X, float Y)> figure, float zoom = 1)
    {
        (Width, Height, _camera, _figure, Zoom) = (width, height, camera, figure, zoom);
    }

    /// <summary>Picture pixels per scene unit: the same shot filmed at a higher resolution, or closer.</summary>
    public float Zoom { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Picture <paramref name="index"/>, and optionally the wall alone and the figure's opacity (one value a pixel).</summary>
    public VideoFrame Render(int index, VideoFrame? wall = null, float[]? opacity = null)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, Width, Height);
        frame.Pts = MediaTime.FromSeconds(1 + index / 25.0);
        var (cx, cy) = _camera(index);
        var (fx, fy) = _figure(index);
        for (var y = 0; y < Height; y++)
        {
            var row = frame.Row(0, y);
            var plain = wall is null ? default : wall.Row(0, y);
            for (var x = 0; x < Width; x++)
            {
                // Four samples a pixel, as a lens and a sensor would average, so a pan of a fraction
                // of a pixel moves the picture by that fraction instead of aliasing it.
                float b = 0, g = 0, r = 0, alpha = 0, fb = 0, fg = 0, fr = 0;
                for (var k = 0; k < 4; k++)
                {
                    var (sx, sy) = ((x + 0.25f + 0.5f * (k & 1)) / Zoom, (y + 0.25f + 0.5f * (k >> 1)) / Zoom);
                    var (wb, wg, wr) = Backdrop(sx + cx, sy + cy);
                    var (wa, pb, pg, pr) = Figure(Zoom, sx - fx - 0.5f / Zoom, sy - fy - 0.5f / Zoom);
                    (b, g, r) = (b + wb / 4, g + wg / 4, r + wr / 4);
                    (alpha, fb, fg, fr) = (alpha + wa / 4, fb + wa * pb, fg + wa * pg, fr + wa * pr);
                }

                (fb, fg, fr) = alpha > 0 ? (fb / (alpha * 4), fg / (alpha * 4), fr / (alpha * 4)) : (0, 0, 0);
                row[x * 4] = Mix(b, fb, alpha);
                row[x * 4 + 1] = Mix(g, fg, alpha);
                row[x * 4 + 2] = Mix(r, fr, alpha);
                row[x * 4 + 3] = 255;
                if (wall is not null)
                {
                    (plain[x * 4], plain[x * 4 + 1], plain[x * 4 + 2], plain[x * 4 + 3]) = ((byte)b, (byte)g, (byte)r, 255);
                }

                if (opacity is not null)
                {
                    opacity[y * Width + x] = alpha;
                }
            }
        }

        return frame;
    }

    /// <summary>The figure's bounding box in picture <paramref name="index"/>, widened by <paramref name="loose"/> pixels.</summary>
    public (int Left, int Top, int Right, int Bottom) Box(int index, int loose = 3)
    {
        var (fx, fy) = _figure(index);
        return (Math.Max(0, (int)MathF.Floor((fx - 14) * Zoom - loose)), Math.Max(0, (int)MathF.Floor((fy - 34) * Zoom - loose)),
            Math.Min(Width, (int)MathF.Ceiling((fx + 14) * Zoom + loose)), Math.Min(Height, (int)MathF.Ceiling((fy + 35) * Zoom + loose)));
    }

    private static byte Mix(float under, float over, float alpha) => (byte)Math.Clamp(MathF.Round(under * (1 - alpha) + over * alpha), 0, 255);

    /// <summary>A figure: a body and a head, striped jacket, with a soft edge one picture pixel wide.</summary>
    private static (float Alpha, float B, float G, float R) Figure(float zoom, float x, float y)
    {
        var body = (MathF.Sqrt(x * x / (13f * 13f) + (y - 8) * (y - 8) / (26f * 26f)) - 1) * 13;
        var head = MathF.Sqrt(x * x + (y + 24) * (y + 24)) - 8;
        var distance = Math.Min(body, head) * zoom;
        var alpha = Math.Clamp(0.5f - distance, 0, 1);
        if (head < body)
        {
            return (alpha, 120, 160, 214);
        }

        var stripe = ((int)MathF.Floor((y + 40) / 5) & 1) == 0;
        return (alpha, stripe ? 190 : 150, stripe ? 96 : 70, stripe ? 36 : 28);
    }

    /// <summary>The wall at scene point (x, y): bricks of varied colour, soft mortar, fine grain and a slow shading.</summary>
    public static (float B, float G, float R) Backdrop(float x, float y)
    {
        var row = (int)MathF.Floor(y / 12);
        var shift = (row & 1) * 13;
        var column = (int)MathF.Floor((x + shift) / 26);
        var across = x + shift - column * 26f;
        var down = y - row * 12f;
        var edge = Math.Min(Math.Min(across, 26 - across), Math.Min(down, 12 - down));
        var mortar = 1 - Smooth(0.6f, 1.8f, edge);
        var tone = Hash(column, row);
        float brickR = 140 + tone % 50;
        float brickG = 62 + (tone >> 8) % 34;
        float brickB = 42 + (tone >> 16) % 26;
        var grain = Noise(x / 3, y / 3) * 18 - 9;
        var shade = 14 * MathF.Sin(x / 70) + 8 * MathF.Cos(y / 45);
        return (Math.Clamp(brickB + (176 - brickB) * mortar + grain + shade, 0, 255),
            Math.Clamp(brickG + (182 - brickG) * mortar + grain + shade, 0, 255),
            Math.Clamp(brickR + (186 - brickR) * mortar + grain + shade, 0, 255));
    }

    private static float Smooth(float from, float to, float value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Noise(float x, float y)
    {
        var (ix, iy) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
        var (fx, fy) = (x - ix, y - iy);
        float At(int i, int j) => (Hash(i, j) & 0xFFFF) / 65535f;
        var top = At(ix, iy) + (At(ix + 1, iy) - At(ix, iy)) * fx;
        var bottom = At(ix, iy + 1) + (At(ix + 1, iy + 1) - At(ix, iy + 1)) * fx;
        return top + (bottom - top) * fy;
    }

    private static uint Hash(int x, int y)
    {
        var h = (uint)x * 374761393u + (uint)y * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}

/// <summary>Scores of one removal against the truth.</summary>
internal static class RemovalScore
{
    /// <summary>Peak signal-to-noise ratio, in decibels, of the preview against the wall where the figure was (its edge widened by two pixels).</summary>
    public static (double SquaredError, int Count) FillError(VideoFrame preview, VideoFrame wall, float[] opacity)
    {
        double total = 0;
        var count = 0;
        var (w, h) = (preview.Width, preview.Height);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!Near(opacity, w, h, x, y, 2))
                {
                    continue;
                }

                var a = preview.Row(0, y);
                var b = wall.Row(0, y);
                for (var c = 0; c < 3; c++)
                {
                    double d = a[x * 4 + c] - b[x * 4 + c];
                    total += d * d;
                }

                count += 3;
            }
        }

        return (total, count);
    }

    /// <summary>
    /// How much of the figure is still visible in the preview, from 0 (none) to 1 (all of it): the
    /// regression of the preview's error onto the figure's own difference from the wall, over the
    /// figure. Errors unrelated to the figure, such as a fill whose bricks are a little off, cancel
    /// out; a ghost of the figure scores its strength.
    /// </summary>
    public static (double Projected, double Energy) Ghost(VideoFrame picture, VideoFrame preview, VideoFrame wall, float[] opacity)
    {
        double projected = 0, energy = 0;
        for (var y = 0; y < picture.Height; y++)
        {
            var a = picture.Row(0, y);
            var b = preview.Row(0, y);
            var c = wall.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                if (opacity[y * picture.Width + x] < 0.5f)
                {
                    continue;
                }

                double dot = 0, square = 0;
                for (var k = 0; k < 3; k++)
                {
                    double figure = a[x * 4 + k] - c[x * 4 + k];
                    double error = b[x * 4 + k] - c[x * 4 + k];
                    dot += figure * error;
                    square += figure * figure;
                }

                projected += dot;
                energy += square;
            }
        }

        return (projected, energy);
    }

    /// <summary>
    /// The fine texture of the preview and of the wall where the figure was: the summed brightness
    /// steps between neighbouring pixels. A smear has little; a convincing fill about the wall's.
    /// </summary>
    public static (double Preview, double Wall) Texture(VideoFrame preview, VideoFrame wall, float[] opacity)
    {
        double found = 0, truth = 0;
        for (var y = 0; y + 1 < preview.Height; y++)
        {
            for (var x = 0; x + 1 < preview.Width; x++)
            {
                if (opacity[y * preview.Width + x] < 0.5f)
                {
                    continue;
                }

                found += Steps(preview, x, y);
                truth += Steps(wall, x, y);
            }
        }

        return (found, truth);
    }

    private static double Steps(VideoFrame frame, int x, int y)
    {
        static double Grey(ReadOnlySpan<byte> row, int x) => 0.114 * row[x * 4] + 0.587 * row[x * 4 + 1] + 0.299 * row[x * 4 + 2];
        var here = Grey(frame.Row(0, y), x);
        return Math.Abs(Grey(frame.Row(0, y), x + 1) - here) + Math.Abs(Grey(frame.Row(0, y + 1), x) - here);
    }

    public static double Psnr(double squaredError, int count) =>
        10 * Math.Log10(255.0 * 255.0 / Math.Max(1e-9, squaredError / Math.Max(1, count)));

    /// <summary>Intersection over union of a found subject (true where found) and the figure (opacity at least a half).</summary>
    public static (int Both, int Either) Overlap(Func<int, int, bool> found, float[] opacity, int width, int height)
    {
        int both = 0, either = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var truth = opacity[y * width + x] >= 0.5f;
                var mine = found(x, y);
                both += truth && mine ? 1 : 0;
                either += truth || mine ? 1 : 0;
            }
        }

        return (both, either);
    }

    private static bool Near(float[] opacity, int width, int height, int x, int y, int reach)
    {
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                var (px, py) = (x + dx, y + dy);
                if (px >= 0 && py >= 0 && px < width && py < height && opacity[py * width + px] > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
