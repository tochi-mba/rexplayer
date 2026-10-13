namespace Rex.Media.AppCore.Shell;

/// <summary>The pictures on the taskbar's thumbnail buttons and over its button.</summary>
public enum TaskbarGlyph
{
    Previous,
    Play,
    Pause,
    Next,

    /// <summary>Over the taskbar button while something is paused.</summary>
    PausedBadge,

    /// <summary>Over the taskbar button when what was asked for could not be played.</summary>
    FailedBadge,
}

/// <summary>
/// rexplayer's own transport pictures for the taskbar, drawn from shapes at the exact pixel size
/// Windows asks for, so they are crisp at every scale from 100 to 200 % and beyond. They are drawn on
/// a 16-unit grid; at each size the bars' edges land on whole pixels, the slants are smoothed with
/// 64 samples a pixel. The buttons' glyphs take the taskbar's ink, dark on a light taskbar and white
/// on a dark one; the badges carry their own disc, so they read on any colour. Drawn once per size
/// and theme, never while playing.
/// </summary>
public static class TaskbarGlyphs
{
    /// <summary>The design grid's size: the glyphs are drawn for 16 pixels at 100 % scale.</summary>
    public const int Grid = 16;

    private const int Samples = 8;

    /// <summary>The ink of the buttons' glyphs on a light taskbar: Windows' near-black text.</summary>
    public const uint DarkInk = 0xFF1C1C1C;

    /// <summary>The ink on a dark taskbar.</summary>
    public const uint LightInk = 0xFFFFFFFF;

    private const uint BadgeDisc = 0xFF2B2B2B;
    private const uint FailedDisc = 0xFFC42B1C;
    private const uint BadgeRing = 0xFFFFFFFF;

    /// <summary>The small-icon size at <paramref name="dpi"/> dots an inch, for when Windows cannot say.</summary>
    public static int SizeFor(int dpi) => Math.Max(Grid, (int)Math.Round(Grid * Math.Max(dpi, 96) / 96.0));

    /// <summary>The glyphs' ink on a light or a dark taskbar.</summary>
    public static uint InkFor(bool lightTaskbar) => lightTaskbar ? DarkInk : LightInk;

    /// <summary>
    /// <paramref name="glyph"/> at <paramref name="size"/> pixels a side in <paramref name="ink"/>
    /// (0xAARRGGBB): rows top to bottom, four bytes a pixel in blue, green, red, alpha order, alpha not
    /// multiplied in, as an icon holds them.
    /// </summary>
    public static byte[] Render(TaskbarGlyph glyph, int size, uint ink)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
        var u = size / (double)Grid;
        Layer[] layers = glyph switch
        {
            TaskbarGlyph.Play => [new Layer(ink, [Triangle(4.5, 2.5, 4.5, 13.5, 13, 8, u)])],
            TaskbarGlyph.Pause => [new Layer(ink, [Bar(4, 2.5, 7, 13.5, u), Bar(9, 2.5, 12, 13.5, u)])],
            TaskbarGlyph.Next => [new Layer(ink, [Triangle(3, 2.5, 3, 13.5, 10.5, 8, u), Bar(11, 2.5, 13, 13.5, u)])],
            TaskbarGlyph.Previous => [new Layer(ink, [Triangle(13, 2.5, 13, 13.5, 5.5, 8, u), Bar(3, 2.5, 5, 13.5, u)])],
            TaskbarGlyph.PausedBadge => [new Layer(BadgeRing, [Disc(8, 8, 8, u)]), new Layer(BadgeDisc, [Disc(8, 8, 7, u)]), new Layer(BadgeRing, [Bar(5, 4.5, 7, 11.5, u), Bar(9, 4.5, 11, 11.5, u)])],
            TaskbarGlyph.FailedBadge => [new Layer(BadgeRing, [Disc(8, 8, 8, u)]), new Layer(FailedDisc, [Disc(8, 8, 7, u)]), new Layer(BadgeRing, [Bar(7, 3.5, 9, 9.5, u), Disc(8, 11.75, 1.25, u)])],
            _ => throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "There is no such glyph."),
        };

        return Draw(layers, size);
    }

    /// <summary>Paints the layers in order, each smoothed by its coverage, and un-multiplies the alpha at the end.</summary>
    private static byte[] Draw(Layer[] layers, int size)
    {
        var pixels = new byte[size * size * 4];
        var premultiplied = new double[4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Array.Clear(premultiplied);
                foreach (var layer in layers)
                {
                    var cover = Coverage(layer.Shapes, x, y);
                    var alpha = cover * ((layer.Ink >> 24) / 255.0);
                    for (var c = 0; c < 3; c++)
                    {
                        var channel = ((layer.Ink >> (8 * c)) & 0xFF) / 255.0;
                        premultiplied[c] = (channel * alpha) + (premultiplied[c] * (1 - alpha));
                    }

                    premultiplied[3] = alpha + (premultiplied[3] * (1 - alpha));
                }

                var at = ((y * size) + x) * 4;
                var a = premultiplied[3];
                for (var c = 0; c < 3; c++)
                {
                    pixels[at + c] = a > 0 ? (byte)Math.Round(Math.Clamp(premultiplied[c] / a, 0, 1) * 255) : (byte)0;
                }

                pixels[at + 3] = (byte)Math.Round(a * 255);
            }
        }

        return pixels;
    }

    /// <summary>The share of pixel (x, y) the shapes cover, from an 8 by 8 grid of samples.</summary>
    private static double Coverage(Shape[] shapes, int x, int y)
    {
        var inside = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
            for (var sx = 0; sx < Samples; sx++)
            {
                var px = x + ((sx + 0.5) / Samples);
                var py = y + ((sy + 0.5) / Samples);
                foreach (var shape in shapes)
                {
                    if (shape.Contains(px, py))
                    {
                        inside++;
                        break;
                    }
                }
            }
        }

        return inside / (double)(Samples * Samples);
    }

    /// <summary>A rectangle in grid units whose edges are rounded to whole pixels, so bars stay sharp at every size.</summary>
    private static Shape Bar(double left, double top, double right, double bottom, double u)
    {
        var (x0, x1) = (Math.Round(left * u), Math.Round(right * u));
        if (x1 <= x0)
        {
            x1 = x0 + 1;
        }

        var (y0, y1) = (Math.Round(top * u), Math.Round(bottom * u));
        return new Shape(ShapeKind.Rectangle, x0, y0, x1, y1, 0, 0);
    }

    /// <summary>A triangle in grid units; its upright edge is on a whole pixel, its slants smoothed.</summary>
    private static Shape Triangle(double x0, double y0, double x1, double y1, double x2, double y2, double u) =>
        new(ShapeKind.Triangle, Math.Round(x0 * u), Math.Round(y0 * u), Math.Round(x1 * u), Math.Round(y1 * u), x2 * u, y2 * u);

    private static Shape Disc(double cx, double cy, double radius, double u) => new(ShapeKind.Disc, cx * u, cy * u, radius * u, 0, 0, 0);

    private enum ShapeKind
    {
        Rectangle,
        Triangle,
        Disc,
    }

    private sealed record Layer(uint Ink, Shape[] Shapes);

    private readonly record struct Shape(ShapeKind Kind, double A, double B, double C, double D, double E, double F)
    {
        public bool Contains(double x, double y) => Kind switch
        {
            ShapeKind.Rectangle => x >= A && x < C && y >= B && y < D,
            ShapeKind.Disc => ((x - A) * (x - A)) + ((y - B) * (y - B)) <= C * C,
            _ => InTriangle(x, y),
        };

        /// <summary>Whether the point is on the inner side of all three edges, whichever way the corners wind.</summary>
        private bool InTriangle(double x, double y)
        {
            var d1 = Side(x, y, A, B, C, D);
            var d2 = Side(x, y, C, D, E, F);
            var d3 = Side(x, y, E, F, A, B);
            return !(((d1 < 0) || (d2 < 0) || (d3 < 0)) && ((d1 > 0) || (d2 > 0) || (d3 > 0)));
        }

        private static double Side(double px, double py, double ax, double ay, double bx, double by) => ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));
    }
}
