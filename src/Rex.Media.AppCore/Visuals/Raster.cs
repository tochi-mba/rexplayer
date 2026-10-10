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
/// picture of music look alive, and a filmic tone curve that turns it into BGRA pixels.
/// <para>
/// Light must never build up from frame to frame, or every picture ends white. So trails are kept
/// apart (<see cref="Feedback"/>) and joined to the new frame by the brighter of the two, never the
/// sum (<see cref="Settle"/>), and bloom is added only on the way out (<see cref="ToBgra"/>), never
/// into the light that the next frame starts from.
/// </para>
/// Everything is drawn in software, row by row, so it runs anywhere and every effect can be tested.
/// </summary>
public sealed class Raster
{
    private readonly float[] _stroke;
    private readonly List<int> _touched = [];
    private readonly float[][] _glow;
    private readonly float[][] _lines;
    private readonly float[][] _sums;
    private readonly float[][] _copies;
    private float[]? _shades;
    private float _shadesFor;
    private readonly float[][] _trail;
    private bool _trailing;
    private (float Threshold, float Strength, int Radius) _bloom;

    public Raster(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        (Width, Height) = (width, height);
        R = new float[width * height];
        G = new float[width * height];
        B = new float[width * height];
        _stroke = new float[width * height];
        _trail = [new float[width * height], new float[width * height], new float[width * height]];
        var half = ((width + 1) / 2) * ((height + 1) / 2);
        _glow = [new float[half], new float[half], new float[half]];
        _lines = [new float[Math.Max(width, height)], new float[Math.Max(width, height)], new float[Math.Max(width, height)]];
        _sums = [new float[width], new float[width], new float[width]];
        _copies = [new float[half], new float[half], new float[half]];
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
    /// Starts a frame with trails: the last frame, faded to <paramref name="keep"/>, scaled by
    /// <paramref name="zoom"/> and turned by <paramref name="turn"/> radians about the middle, then
    /// moved by (<paramref name="shiftX"/>, <paramref name="shiftY"/>) pixels, is kept aside, and the
    /// canvas is cleared for the new frame. <see cref="Settle"/> joins them. Repeated each frame this
    /// draws tunnels, smoke and trails that fade but never pile up.
    /// </summary>
    public void Feedback(float keep, double zoom, double turn, double shiftX, double shiftY)
    {
        keep = Math.Clamp(keep, 0, 1);
        zoom = zoom > 0.01 ? zoom : 1;
        var (cx, cy) = ((Width - 1) / 2f, (Height - 1) / 2f);
        var (cos, sin) = ((float)(Math.Cos(-turn) / zoom), (float)(Math.Sin(-turn) / zoom));
        var (sx0, sy0) = ((float)shiftX, (float)shiftY);
        var (tr, tg, tb) = (_trail[0], _trail[1], _trail[2]);
        Rows(Height, y =>
        {
            var dy = y - cy - sy0;
            for (var x = 0; x < Width; x++)
            {
                // Where this pixel was, found once for the three colours.
                var dx = x - cx - sx0;
                var sx = cx + (dx * cos) - (dy * sin);
                var sy = cy + (dx * sin) + (dy * cos);
                var i = (y * Width) + x;
                if (sx < 0 || sy < 0 || sx > Width - 1 || sy > Height - 1)
                {
                    tr[i] = tg[i] = tb[i] = 0;
                    continue;
                }

                var (x0, y0) = ((int)sx, (int)sy);
                var (x1, y1) = (Math.Min(x0 + 1, Width - 1), Math.Min(y0 + 1, Height - 1));
                var (fx, fy) = (sx - x0, sy - y0);
                var (a, b, c, d) = ((y0 * Width) + x0, (y0 * Width) + x1, (y1 * Width) + x0, (y1 * Width) + x1);
                var (wa, wb, wc, wd) = ((1 - fx) * (1 - fy) * keep, fx * (1 - fy) * keep, (1 - fx) * fy * keep, fx * fy * keep);
                tr[i] = (R[a] * wa) + (R[b] * wb) + (R[c] * wc) + (R[d] * wd);
                tg[i] = (G[a] * wa) + (G[b] * wb) + (G[c] * wc) + (G[d] * wd);
                tb[i] = (B[a] * wa) + (B[b] * wb) + (B[c] * wc) + (B[d] * wd);
            }
        });

        Array.Clear(R);
        Array.Clear(G);
        Array.Clear(B);
        _trailing = true;
    }

    /// <summary>Ends a frame: where the trails kept by <see cref="Feedback"/> are brighter than the new light, they show.</summary>
    public void Settle()
    {
        if (!_trailing)
        {
            return;
        }

        _trailing = false;
        var (tr, tg, tb) = (_trail[0], _trail[1], _trail[2]);
        Rows(Height, y =>
        {
            for (var i = y * Width; i < (y + 1) * Width; i++)
            {
                R[i] = Math.Max(R[i], tr[i]);
                G[i] = Math.Max(G[i], tg[i]);
                B[i] = Math.Max(B[i], tb[i]);
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="row"/> for each of <paramref name="count"/> rows, spread over the
    /// processor's cores when there are enough to be worth it. Rows write only their own pixels, so
    /// the picture is the same however they are shared out.
    /// </summary>
    internal static void Rows(int count, Action<int> row)
    {
        // A few large blocks, one a core: fewer hand-overs than a row each.
        var blocks = Math.Min(Environment.ProcessorCount, count / 32);
        if (blocks < 2)
        {
            for (var i = 0; i < count; i++)
            {
                row(i);
            }

            return;
        }

        Parallel.For(0, blocks, block =>
        {
            for (var i = block * count / blocks; i < (block + 1) * count / blocks; i++)
            {
                row(i);
            }
        });
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
    public void Line(double x0, double y0, double x1, double y1, double width, Rgb color, float strength = 1, double halo = 0) =>
        Stroke(x0, y0, x1, y1, width, halo, (i, light) => Add(i, color, light * strength));

    /// <summary>
    /// A path of lines through <paramref name="points"/>, closed back to the first when asked. Where
    /// its segments meet they are not lit twice: the path is one stroke, without beads at its joints.
    /// </summary>
    public void Path(IReadOnlyList<(double X, double Y)> points, double width, Rgb color, float strength = 1, double halo = 0, bool closed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        var count = closed && points.Count > 2 ? points.Count + 1 : points.Count;
        if (count < 2)
        {
            return;
        }

        _touched.Clear();
        for (var n = 1; n < count; n++)
        {
            var (from, to) = (points[n - 1], points[n % points.Count]);
            Stroke(from.X, from.Y, to.X, to.Y, width, halo, (i, light) =>
            {
                if (_stroke[i] == 0)
                {
                    _touched.Add(i);
                }

                _stroke[i] = Math.Max(_stroke[i], light);
            });
        }

        foreach (var i in _touched)
        {
            Add(i, color, _stroke[i] * strength);
            _stroke[i] = 0;
        }
    }

    /// <summary>
    /// Hands <paramref name="lit"/> each pixel a segment lights and how much (0 to 1): walking along
    /// its longer direction and crossing it only as far as the stroke reaches, so a long diagonal
    /// line costs its length, not the square of it.
    /// </summary>
    private void Stroke(double x0, double y0, double x1, double y1, double width, double halo, Action<int, float> lit)
    {
        var half = Math.Max(0.5, width / 2);
        var outer = half * (1 + Math.Max(0, halo));
        var reach = outer + 1;
        var (vx, vy) = (x1 - x0, y1 - y0);
        var length = (vx * vx) + (vy * vy);
        var steep = Math.Abs(vy) > Math.Abs(vx);
        var (along0, along1) = steep ? (Math.Min(y0, y1), Math.Max(y0, y1)) : (Math.Min(x0, x1), Math.Max(x0, x1));
        var (limit, crossLimit) = steep ? (Height - 1, Width - 1) : (Width - 1, Height - 1);
        var first = Math.Max(0, (int)Math.Floor(along0 - reach));
        var last = Math.Min(limit, (int)Math.Ceiling(along1 + reach));

        // How far across the stroke can reach from its centre line, per step along it.
        var slope = steep ? (vy == 0 ? 0 : vx / vy) : (vx == 0 ? 0 : vy / vx);
        var spread = reach * Math.Sqrt(1 + (slope * slope));
        for (var a = first; a <= last; a++)
        {
            var t = steep ? (vy == 0 ? 0 : Math.Clamp((a - y0) / vy, 0, 1)) : (vx == 0 ? 0 : Math.Clamp((a - x0) / vx, 0, 1));
            var centre = steep ? x0 + (t * vx) : y0 + (t * vy);
            var from = Math.Max(0, (int)Math.Floor(centre - spread));
            var to = Math.Min(crossLimit, (int)Math.Ceiling(centre + spread));
            for (var c = from; c <= to; c++)
            {
                var (px, py) = steep ? (c, a) : (a, c);
                var light = Light(x0, y0, vx, vy, length, half, outer, px, py);
                if (light > 0)
                {
                    lit((py * Width) + px, light);
                }
            }
        }
    }

    /// <summary>How much of a soft line (and its halo) lights one pixel, 0 to 1.</summary>
    private static float Light(double x0, double y0, double vx, double vy, double length, double half, double outer, int px, int py)
    {
        // The distance from the pixel to the nearest point of the segment.
        var t = length > 0 ? Math.Clamp((((px - x0) * vx) + ((py - y0) * vy)) / length, 0, 1) : 0;
        var (nx, ny) = (x0 + (t * vx) - px, y0 + (t * vy) - py);
        var squared = (nx * nx) + (ny * ny);
        if (squared >= outer * outer && squared >= (half + 0.5) * (half + 0.5))
        {
            return 0;
        }

        var d = Math.Sqrt(squared);
        var core = Math.Clamp(half + 0.5 - d, 0, 1);
        var fall = 1 - ((d - half) / (outer - half));
        var glow = outer > half && d > half && d < outer ? 0.35 * fall * fall : 0;
        return (float)Math.Max(core, glow);
    }

    /// <summary>A ring of light about (<paramref name="x"/>, <paramref name="y"/>), its edges soft.</summary>
    public void Ring(double x, double y, double radius, double width, Rgb color, float strength = 1)
    {
        var half = Math.Max(0.5, width / 2);
        var (inner, outer) = (Math.Max(0, radius - half - 1), radius + half + 1);
        var (top, bottom) = (Math.Max(0, (int)Math.Floor(y - outer)), Math.Min(Height - 1, (int)Math.Ceiling(y + outer)));
        for (var py = top; py <= bottom; py++)
        {
            var dy = py - y;
            var across = Math.Sqrt(Math.Max(0, (outer * outer) - (dy * dy)));
            var hole = dy * dy < inner * inner ? Math.Sqrt((inner * inner) - (dy * dy)) : -1;

            // The ring crosses this row in at most two runs, left and right of its hole.
            PaintRun(x - across, hole <= 0 ? x + across : x - hole);
            if (hole > 0)
            {
                PaintRun(Math.Max(x + hole, Math.Ceiling(x - hole) + 1), x + across);
            }

            void PaintRun(double from, double to)
            {
                for (var px = Math.Max(0, (int)Math.Floor(from)); px <= Math.Min(Width - 1, (int)Math.Ceiling(to)); px++)
                {
                    var d = Math.Abs(Math.Sqrt(((px - x) * (px - x)) + (dy * dy)) - radius);
                    var light = (float)Math.Clamp(half + 0.5 - d, 0, 1) * strength;
                    if (light > 0)
                    {
                        Add((py * Width) + px, color, light);
                    }
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
    /// Bloom for this frame: the light above <paramref name="threshold"/>, blurred over about
    /// <paramref name="radius"/> pixels, glows round bright things in the picture <see cref="ToBgra"/>
    /// makes. It is never added to the canvas itself, so it cannot feed the next frame.
    /// </summary>
    public void Bloom(float threshold, float strength, int radius) => _bloom = (threshold, strength, radius);

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
    /// The light as BGRA pixels: <paramref name="exposure"/> times the light, with this frame's
    /// bloom, through a filmic shoulder (so bright light rolls off instead of clipping), darker toward
    /// the corners by <paramref name="vignette"/>. The canvas itself is left as it was. The bloom is
    /// worked at half size, where a blur costs a quarter as much and looks the same.
    /// </summary>
    public void ToBgra(byte[] output, float exposure = 1, float vignette = 0)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Length < Width * Height * 4)
        {
            throw new ArgumentException($"{Width}x{Height} pixels need {Width * Height * 4} bytes.", nameof(output));
        }

        var shades = Shades(vignette);
        var (threshold, strength, radius) = _bloom;
        var bloom = strength > 0 && radius >= 1;
        _bloom = default;
        var (halfWidth, halfHeight) = ((Width + 1) / 2, (Height + 1) / 2);
        if (bloom)
        {
            // The light above the threshold, two by two pixels at a time, then blurred: each colour on its own core.
            var planes = new[] { R, G, B };
            Parallel.For(0, 3, channel =>
            {
                var (plane, glow) = (planes[channel], _glow[channel]);
                for (var y = 0; y < halfHeight; y++)
                {
                    for (var x = 0; x < halfWidth; x++)
                    {
                        var (sx, sy) = (Math.Min(Width - 1, x * 2), Math.Min(Height - 1, y * 2));
                        var (ex, ey) = (Math.Min(Width - 1, sx + 1), Math.Min(Height - 1, sy + 1));
                        var mean = (plane[(sy * Width) + sx] + plane[(sy * Width) + ex] + plane[(ey * Width) + sx] + plane[(ey * Width) + ex]) / 4;
                        glow[(y * halfWidth) + x] = Math.Max(0, mean - threshold);
                    }
                }

                var small = Math.Max(1, radius / 2);
                for (var pass = 0; pass < 3; pass++)
                {
                    BoxBlur(glow, halfWidth, halfHeight, small, channel);
                }
            });
        }

        var (gr, gg, gb) = (_glow[0], _glow[1], _glow[2]);
        Rows(Height, y =>
        {
            // Where this row falls in the half-size bloom.
            var by = Math.Clamp((y - 0.5f) / 2, 0, halfHeight - 1);
            var y0 = (int)by;
            var y1 = Math.Min(y0 + 1, halfHeight - 1);
            var fy = by - y0;
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width) + x;
                var (r, g, b) = (R[i], G[i], B[i]);
                if (bloom)
                {
                    var bx = Math.Clamp((x - 0.5f) / 2, 0, halfWidth - 1);
                    var x0 = (int)bx;
                    var x1 = Math.Min(x0 + 1, halfWidth - 1);
                    var fx = bx - x0;
                    var (a, c, d, e) = ((y0 * halfWidth) + x0, (y0 * halfWidth) + x1, (y1 * halfWidth) + x0, (y1 * halfWidth) + x1);
                    var (wa, wc, wd, we) = ((1 - fx) * (1 - fy) * strength, fx * (1 - fy) * strength, (1 - fx) * fy * strength, fx * fy * strength);
                    r += (gr[a] * wa) + (gr[c] * wc) + (gr[d] * wd) + (gr[e] * we);
                    g += (gg[a] * wa) + (gg[c] * wc) + (gg[d] * wd) + (gg[e] * we);
                    b += (gb[a] * wa) + (gb[c] * wc) + (gb[d] * wd) + (gb[e] * we);
                }

                var k = exposure * shades[i] * ToneScale;
                var at = i * 4;
                output[at] = Tones[ToneIndex(b * k)];
                output[at + 1] = Tones[ToneIndex(g * k)];
                output[at + 2] = Tones[ToneIndex(r * k)];
                output[at + 3] = 255;
            }
        });
    }

    /// <summary>How much each pixel is kept by a vignette of <paramref name="vignette"/>, worked out once for each strength.</summary>
    private float[] Shades(float vignette)
    {
        if (_shades is { } known && _shadesFor == vignette)
        {
            return known;
        }

        var (cx, cy) = ((Width - 1) / 2f, (Height - 1) / 2f);
        var corner = Math.Max(1e-6f, (cx * cx) + (cy * cy));
        var shades = new float[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                shades[(y * Width) + x] = vignette > 0 ? 1 - (vignette * ((((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / corner)) : 1;
            }
        }

        (_shades, _shadesFor) = (shades, vignette);
        return shades;
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

    /// <summary>
    /// The filmic curve (Narkowicz's fit of ACES): a gentle toe, mid-tones a little lifted, and a long
    /// shoulder that brings any brightness under 255, so light over-full rolls off instead of clipping.
    /// Read from a table of 4096 steps up to 8 times full light; beyond that it is white.
    /// </summary>
    internal static byte Tone(float value) => Tones[ToneIndex(value * ToneScale)];

    private const float ToneTop = 8;
    private const int ToneSteps = 4096;
    private const float ToneScale = ToneSteps / ToneTop;

    /// <summary>The table's step for light already scaled by <see cref="ToneScale"/>: 0 for none (or not a number), the last for beyond the top.</summary>
    private static int ToneIndex(float scaled) => scaled > 0 ? (scaled < ToneSteps ? (int)scaled : ToneSteps) : 0;

    private static readonly byte[] Tones = [.. Enumerable.Range(0, ToneSteps + 1).Select(step =>
    {
        var value = step == 0 ? 0 : (step + 0.5f) * ToneTop / ToneSteps;
        var toned = value * ((2.51f * value) + 0.03f) / ((value * ((2.43f * value) + 0.59f)) + 0.14f);
        return (byte)Math.Round(Math.Min(1, toned) * 255);
    })];

    private void Add(int i, Rgb color, float amount)
    {
        R[i] += color.R * amount;
        G[i] += color.G * amount;
        B[i] += color.B * amount;
    }

    /// <summary>
    /// A running-sum box blur of <paramref name="radius"/> over the first <paramref name="width"/> by
    /// <paramref name="height"/> values of <paramref name="plane"/>: across each row, then down, keeping
    /// every column's sum at once so memory is read in order. <paramref name="lane"/> picks the working
    /// space, so the three colours can blur at once.
    /// </summary>
    private void BoxBlur(float[] plane, int width, int height, int radius, int lane)
    {
        var span = (2 * radius) + 1;
        var (line, sums, copy) = (_lines[lane], _sums[lane], _copies[lane]);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            float sum = 0;
            for (var x = -radius; x <= radius; x++)
            {
                sum += plane[row + Math.Clamp(x, 0, width - 1)];
            }

            for (var x = 0; x < width; x++)
            {
                line[x] = sum / span;
                sum += plane[row + Math.Min(width - 1, x + radius + 1)] - plane[row + Math.Max(0, x - radius)];
            }

            Array.Copy(line, 0, plane, row, width);
        }

        Array.Copy(plane, copy, width * height);
        Array.Clear(sums, 0, width);
        for (var y = -radius; y <= radius; y++)
        {
            var row = Math.Clamp(y, 0, height - 1) * width;
            for (var x = 0; x < width; x++)
            {
                sums[x] += copy[row + x];
            }
        }

        for (var y = 0; y < height; y++)
        {
            var (add, remove) = (Math.Min(height - 1, y + radius + 1) * width, Math.Max(0, y - radius) * width);
            for (var x = 0; x < width; x++)
            {
                plane[(y * width) + x] = sums[x] / span;
                sums[x] += copy[add + x] - copy[remove + x];
            }
        }
    }
}



