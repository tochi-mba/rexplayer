using Rex.Media.AppCore.Player;

namespace Rex.Media.AppCore.Visuals;

/// <summary>A colour of light, each channel from 0 up; above 1 is brighter than white, which the tone curve rolls off.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(1, 1, 1);

    /// <summary>How bright the colour looks (Rec. 709 weights).</summary>
    public float Luma => (0.2126f * R) + (0.7152f * G) + (0.0722f * B);

    public static Rgb From(Argb color) => new(color.R / 255f, color.G / 255f, color.B / 255f);

    public Rgb Times(float k) => new(R * k, G * k, B * k);

    public Rgb Plus(Rgb other) => new(R + other.R, G + other.G, B + other.B);

    /// <summary>The colour a share <paramref name="t"/> of the way from this one to <paramref name="other"/>.</summary>
    public Rgb Toward(Rgb other, float t) => new(R + ((other.R - R) * t), G + ((other.G - G) * t), B + ((other.B - B) * t));
}

/// <summary>
/// The visualisations' canvas (AU-18): light added up in floating point, so overlapping strokes
/// glow brighter instead of covering each other, with the soft shapes, trails and bloom that make a
/// picture of music look alive, and a tone curve that turns it into BGRA pixels without clipping.
/// Everything is drawn in software, row by row, so it runs anywhere and every effect can be tested.
/// </summary>
public sealed class Raster
{
    private float[] _scratch;

    public Raster(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        (Width, Height) = (width, height);
        R = new float[width * height];
        G = new float[width * height];
        B = new float[width * height];
        _scratch = new float[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The red light of each pixel, row by row; <see cref="G"/> and <see cref="B"/> likewise.</summary>
    public float[] R { get; }

    public float[] G { get; }

    public float[] B { get; }

    /// <summary>The light at a pixel.</summary>
    public Rgb this[int x, int y] => new(R[(y * Width) + x], G[(y * Width) + x], B[(y * Width) + x]);

    public void Clear(Rgb color)
    {
        Array.Fill(R, color.R);
        Array.Fill(G, color.G);
        Array.Fill(B, color.B);
    }

    /// <summary>Keeps <paramref name="keep"/> of the light everywhere: what was drawn fades, leaving trails.</summary>
    public void Fade(float keep)
    {
        keep = Math.Clamp(keep, 0, 1);
        foreach (var plane in new[] { R, G, B })
        {
            for (var i = 0; i < plane.Length; i++)
            {
                plane[i] *= keep;
            }
        }
    }

    /// <summary>
    /// Feedback: the picture so far, faded to <paramref name="keep"/> and drawn back over itself
    /// scaled by <paramref name="zoom"/> and turned by <paramref name="turn"/> radians about the middle,
    /// then moved by (<paramref name="shiftX"/>, <paramref name="shiftY"/>) pixels. Repeated each
    /// frame this draws tunnels, smoke and trails.
    /// </summary>
    public void Feedback(float keep, double zoom, double turn, double shiftX, double shiftY)
    {
        keep = Math.Clamp(keep, 0, 1);
        zoom = zoom > 0.01 ? zoom : 1;
        var (cx, cy) = ((Width - 1) / 2.0, (Height - 1) / 2.0);
        var (cos, sin) = (Math.Cos(-turn) / zoom, Math.Sin(-turn) / zoom);
        foreach (var plane in new[] { R, G, B })
        {
            Array.Copy(plane, _scratch, plane.Length);
            for (var y = 0; y < Height; y++)
            {
                var dy = y - cy - shiftY;
                for (var x = 0; x < Width; x++)
                {
                    var dx = x - cx - shiftX;
                    var sx = cx + (dx * cos) - (dy * sin);
                    var sy = cy + (dx * sin) + (dy * cos);
                    plane[(y * Width) + x] = Sample(_scratch, sx, sy) * keep;
                }
            }
        }
    }

    /// <summary>A soft round light: brightest at the middle, gone at <paramref name="radius"/>.</summary>
    public void Glow(double x, double y, double radius, Rgb color, float strength = 1)
    {
        if (radius <= 0 || strength <= 0)
        {
            return;
        }

        var (left, right) = (Math.Max(0, (int)Math.Floor(x - radius)), Math.Min(Width - 1, (int)Math.Ceiling(x + radius)));
        var (top, bottom) = (Math.Max(0, (int)Math.Floor(y - radius)), Math.Min(Height - 1, (int)Math.Ceiling(y + radius)));
        var reach = radius * radius;
        for (var py = top; py <= bottom; py++)
        {
            for (var px = left; px <= right; px++)
            {
                var d = (((px - x) * (px - x)) + ((py - y) * (py - y))) / reach;
                if (d < 1)
                {
                    var falloff = (float)((1 - d) * (1 - d)) * strength;
                    Add((py * Width) + px, color, falloff);
                }
            }
        }
    }

    /// <summary>A disc of light with an edge a pixel soft.</summary>
    public void Disc(double x, double y, double radius, Rgb color, float strength = 1)
    {
        var (left, right) = (Math.Max(0, (int)Math.Floor(x - radius - 1)), Math.Min(Width - 1, (int)Math.Ceiling(x + radius + 1)));
        var (top, bottom) = (Math.Max(0, (int)Math.Floor(y - radius - 1)), Math.Min(Height - 1, (int)Math.Ceiling(y + radius + 1)));
        for (var py = top; py <= bottom; py++)
        {
            for (var px = left; px <= right; px++)
            {
                var d = Math.Sqrt(((px - x) * (px - x)) + ((py - y) * (py - y)));
                var cover = (float)Math.Clamp(radius + 0.5 - d, 0, 1);
                if (cover > 0)
                {
                    Add((py * Width) + px, color, cover * strength);
                }
            }
        }
    }

    /// <summary>
    /// A line of light <paramref name="width"/> across with soft edges, and a halo of
    /// <paramref name="halo"/> times its width around it, fainter.
    /// </summary>
    public void Line(double x0, double y0, double x1, double y1, double width, Rgb color, float strength = 1, double halo = 0)
    {
        var half = Math.Max(0.5, width / 2);
        var outer = half * (1 + Math.Max(0, halo));
        var (left, right) = (Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - outer - 1)), Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + outer + 1)));
        var (top, bottom) = (Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - outer - 1)), Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + outer + 1)));
        var (vx, vy) = (x1 - x0, y1 - y0);
        var length = (vx * vx) + (vy * vy);
        for (var py = top; py <= bottom; py++)
        {
            for (var px = left; px <= right; px++)
            {
                // The distance from the pixel to the nearest point of the segment.
                var t = length > 0 ? Math.Clamp((((px - x0) * vx) + ((py - y0) * vy)) / length, 0, 1) : 0;
                var (nx, ny) = (x0 + (t * vx) - px, y0 + (t * vy) - py);
                var d = Math.Sqrt((nx * nx) + (ny * ny));
                var core = Math.Clamp(half + 0.5 - d, 0, 1);
                var glow = outer > half && d < outer ? 0.35 * Math.Pow(1 - ((d - half) / (outer - half)), 2) : 0;
                var light = (float)Math.Max(core, d > half ? glow : 0) * strength;
                if (light > 0)
                {
                    Add((py * Width) + px, color, light);
                }
            }
        }
    }

    /// <summary>A path of lines through <paramref name="points"/>, closed back to the first when asked.</summary>
    public void Path(IReadOnlyList<(double X, double Y)> points, double width, Rgb color, float strength = 1, double halo = 0, bool closed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        for (var i = 1; i < points.Count; i++)
        {
            Line(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, width, color, strength, halo);
        }

        if (closed && points.Count > 2)
        {
            Line(points[^1].X, points[^1].Y, points[0].X, points[0].Y, width, color, strength, halo);
        }
    }

    /// <summary>A ring of light about (<paramref name="x"/>, <paramref name="y"/>), its edges soft.</summary>
    public void Ring(double x, double y, double radius, double width, Rgb color, float strength = 1)
    {
        var half = Math.Max(0.5, width / 2);
        var outer = radius + half + 1;
        var (left, right) = (Math.Max(0, (int)Math.Floor(x - outer)), Math.Min(Width - 1, (int)Math.Ceiling(x + outer)));
        var (top, bottom) = (Math.Max(0, (int)Math.Floor(y - outer)), Math.Min(Height - 1, (int)Math.Ceiling(y + outer)));
        for (var py = top; py <= bottom; py++)
        {
            for (var px = left; px <= right; px++)
            {
                var d = Math.Abs(Math.Sqrt(((px - x) * (px - x)) + ((py - y) * (py - y))) - radius);
                var light = (float)Math.Clamp(half + 0.5 - d, 0, 1) * strength;
                if (light > 0)
                {
                    Add((py * Width) + px, color, light);
                }
            }
        }
    }

    /// <summary>Adds <paramref name="color"/> over a rectangle (clipped to the canvas).</summary>
    public void Fill(int x, int y, int width, int height, Rgb color, float strength = 1)
    {
        for (var py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            {
                Add((py * Width) + px, color, strength);
            }
        }
    }

    /// <summary>
    /// Bloom: the light above <paramref name="threshold"/>, blurred over about <paramref name="radius"/>
    /// pixels and added back, so bright things glow into what is around them.
    /// </summary>
    public void Bloom(float threshold, float strength, int radius)
    {
        if (strength <= 0 || radius < 1)
        {
            return;
        }

        foreach (var plane in new[] { R, G, B })
        {
            for (var i = 0; i < plane.Length; i++)
            {
                _scratch[i] = Math.Max(0, plane[i] - threshold);
            }

            // Three box blurs come close to a Gaussian.
            for (var pass = 0; pass < 3; pass++)
            {
                BoxBlur(_scratch, radius);
            }

            for (var i = 0; i < plane.Length; i++)
            {
                plane[i] += _scratch[i] * strength;
            }
        }
    }

    /// <summary>
    /// Draws a BGRA picture over the whole canvas, through <paramref name="map"/> (from a canvas
    /// pixel to where in the picture it comes from), at <paramref name="strength"/>.
    /// </summary>
    public void Picture(ReadOnlySpan<byte> bgra, int width, int height, Func<double, double, (double X, double Y)> map, float strength = 1)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (bgra.Length < width * height * 4 || width < 1 || height < 1)
        {
            throw new ArgumentException("The picture is shorter than its size says.", nameof(bgra));
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (sx, sy) = map(x, y);
                if (sx < 0 || sy < 0 || sx > width - 1 || sy > height - 1)
                {
                    continue;
                }

                var at = (((int)sy * width) + (int)sx) * 4;
                var i = (y * Width) + x;
                R[i] += bgra[at + 2] / 255f * strength;
                G[i] += bgra[at + 1] / 255f * strength;
                B[i] += bgra[at] / 255f * strength;
            }
        }
    }

    /// <summary>
    /// The light as BGRA pixels: <paramref name="exposure"/> times the light through a soft
    /// shoulder (so bright light rolls off to white instead of clipping), darker toward the corners by
    /// <paramref name="vignette"/>.
    /// </summary>
    public void ToBgra(Span<byte> output, float exposure = 1, float vignette = 0)
    {
        if (output.Length < Width * Height * 4)
        {
            throw new ArgumentException($"{Width}x{Height} pixels need {Width * Height * 4} bytes.", nameof(output));
        }

        var (cx, cy) = ((Width - 1) / 2f, (Height - 1) / 2f);
        var corner = (cx * cx) + (cy * cy);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width) + x;
                var shade = vignette > 0 ? 1 - (vignette * ((((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / corner)) : 1;
                var k = exposure * shade;
                var at = i * 4;
                output[at] = Tone(B[i] * k);
                output[at + 1] = Tone(G[i] * k);
                output[at + 2] = Tone(R[i] * k);
                output[at + 3] = 255;
            }
        }
    }

    /// <summary>The average light of the canvas, from 0 (black) up.</summary>
    public float AverageLuma()
    {
        double sum = 0;
        for (var i = 0; i < R.Length; i++)
        {
            sum += (0.2126f * R[i]) + (0.7152f * G[i]) + (0.0722f * B[i]);
        }

        return (float)(sum / R.Length);
    }

    /// <summary>A shoulder that passes dark values nearly unchanged and brings any brightness under 255.</summary>
    internal static byte Tone(float value)
    {
        if (!(value > 0))
        {
            return 0;
        }

        // Linear to 0.6, then a smooth roll-off toward 1.
        var toned = value < 0.6f ? value : 0.6f + (0.4f * (1 - MathF.Exp(-(value - 0.6f) / 0.4f)));
        return (byte)Math.Round(Math.Min(1, toned) * 255);
    }

    private void Add(int i, Rgb color, float amount)
    {
        R[i] += color.R * amount;
        G[i] += color.G * amount;
        B[i] += color.B * amount;
    }

    /// <summary>The value at a point between pixels (bilinear), 0 outside the canvas.</summary>
    private float Sample(float[] plane, double x, double y)
    {
        if (x < 0 || y < 0 || x > Width - 1 || y > Height - 1)
        {
            return 0;
        }

        var (x0, y0) = ((int)x, (int)y);
        var (x1, y1) = (Math.Min(x0 + 1, Width - 1), Math.Min(y0 + 1, Height - 1));
        var (fx, fy) = ((float)(x - x0), (float)(y - y0));
        var top = plane[(y0 * Width) + x0] + ((plane[(y0 * Width) + x1] - plane[(y0 * Width) + x0]) * fx);
        var bottom = plane[(y1 * Width) + x0] + ((plane[(y1 * Width) + x1] - plane[(y1 * Width) + x0]) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>A running-sum box blur of <paramref name="radius"/>, across then down.</summary>
    private void BoxBlur(float[] plane, int radius)
    {
        var line = new float[Math.Max(Width, Height)];
        var span = (2 * radius) + 1;
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            float sum = 0;
            for (var x = -radius; x <= radius; x++)
            {
                sum += plane[row + Math.Clamp(x, 0, Width - 1)];
            }

            for (var x = 0; x < Width; x++)
            {
                line[x] = sum / span;
                sum += plane[row + Math.Min(Width - 1, x + radius + 1)] - plane[row + Math.Max(0, x - radius)];
            }

            Array.Copy(line, 0, plane, row, Width);
        }

        for (var x = 0; x < Width; x++)
        {
            float sum = 0;
            for (var y = -radius; y <= radius; y++)
            {
                sum += plane[(Math.Clamp(y, 0, Height - 1) * Width) + x];
            }

            for (var y = 0; y < Height; y++)
            {
                line[y] = sum / span;
                sum += plane[(Math.Min(Height - 1, y + radius + 1) * Width) + x] - plane[(Math.Max(0, y - radius) * Width) + x];
            }

            for (var y = 0; y < Height; y++)
            {
                plane[(y * Width) + x] = line[y];
            }
        }
    }
}

