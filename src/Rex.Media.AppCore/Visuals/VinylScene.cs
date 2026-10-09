namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A record playing on a turntable under a lamp (AU-18).
/// <para>
/// The needle cuts the music into the record as it plays: the groove under it lights with the
/// sound's loudness, turns away with the record and stays, so the song so far is written round the
/// disc in rings of light, quiet passages dark and loud ones bright, the needle working inward as the
/// song goes on. The vinyl is black with fine grooves that catch the lamp in two opposite fans, the
/// way a real record does; the fans stay with the lamp while the record turns beneath them, and the
/// smooth bands between tracks gleam. The cover is the label. Round the platter the sound itself is
/// drawn as a ring of light, and the strobe dots on the platter's rim flash on the beat. The tone-arm
/// swings from its pivot to wherever the needle is.
/// </para>
/// </summary>
public sealed class VinylScene : VisualScene
{
    private const int Angles = 720;
    private const int Rings = 240;
    private const double Inner = 0.36;
    private const double Outer = 0.96;
    private const double LabelEdge = 0.33;
    private const double Lamp = -0.85;
    private static readonly double[] TrackGaps = [0.5, 0.63, 0.77, 0.88];

    private readonly float[] _groove = new float[Angles * Rings];
    private double _turned = double.NaN;
    private Disc? _disc;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var radius = Math.Min(h * 0.36, w * 0.28);
        var (cx, cy) = (w * 0.42, h / 2.0);
        var turnsPerSecond = context.Pick("speed") switch
        {
            1 => 45.0 / 60,
            2 => 78.0 / 60,
            _ => 100.0 / 180,
        };

        // The record turns with the song, so pausing stops it.
        var turned = context.Seconds * turnsPerSecond * Math.Tau;
        var (pivotX, pivotY, length) = (cx + (radius * 1.42), cy - (radius * 0.78), radius * 1.32);
        var (sx, sy) = Reach(cx, cy, Needle(context.Progress) * radius, pivotX, pivotY, length);
        Cut(context, turned, Math.Atan2(sy - cy, sx - cx));

        canvas.Clear(new Rgb(0.008f, 0.007f, 0.011f));
        canvas.Glow(cx - (radius * 0.2), cy - (radius * 0.4), radius * 2.4, context.Paint(0.35).Times(0.09f));
        if (context.Toggle("wave"))
        {
            Waveform(context, cx, cy, radius);
        }

        Platter(context, cx, cy, radius, turned);
        Record(context, cx, cy, radius, turned);
        if (context.Toggle("arm"))
        {
            Arm(context, radius, (pivotX, pivotY), (sx, sy), length);
        }

        canvas.Bloom(0.35f, (float)context.Number("glow"), 6);
    }

    /// <summary>Lights the groove the needle passed over since the last frame with the sound just heard.</summary>
    private void Cut(VisualContext context, double turned, double needleAngle)
    {
        var ring = Math.Clamp((int)((Needle(context.Progress) - Inner) / (Outer - Inner) * Rings), 0, Rings - 1);
        var wave = context.Pulse.Wave;
        var from = double.IsNaN(_turned) || Math.Abs(turned - _turned) > Math.Tau ? turned - 0.05 : _turned;
        _turned = turned;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(turned - from) / Math.Tau * Angles));
        for (var step = 0; step < steps; step++)
        {
            // The newest sound spread over the arc: each step the loudest of its share.
            var (start, end) = (step * wave.Count / steps, (step + 1) * wave.Count / steps);
            var loudest = 0f;
            for (var s = start; s < end; s++)
            {
                loudest = Math.Max(loudest, Math.Abs(wave[s]));
            }

            var at = needleAngle - (from + ((turned - from) * (step + 1) / steps));
            var bin = (int)(Wrap(at) / Math.Tau * Angles) % Angles;
            for (var spread = -1; spread <= 1; spread++)
            {
                var row = ring + spread;
                if (row is >= 0 and < Rings)
                {
                    var value = loudest * (spread == 0 ? 1 : 0.5f);
                    _groove[(row * Angles) + bin] = Math.Max(_groove[(row * Angles) + bin] * 0.6f, value);
                }
            }
        }
    }

    /// <summary>How far out the needle is, from the outside in as the song goes on.</summary>
    private static double Needle(double progress) => Outer - 0.02 - ((Outer - Inner - 0.04) * Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1));

    private static double Wrap(double angle) => ((angle % Math.Tau) + Math.Tau) % Math.Tau;

    /// <summary>The newest sound as a ring of light round the platter, mirrored left to right.</summary>
    private static void Waveform(VisualContext context, double cx, double cy, double radius)
    {
        var wave = context.Pulse.Wave;
        const int Points = 180;
        var reach = radius * 1.17;
        var depth = radius * 0.2 * context.Number("depth");
        var outer = new List<(double, double)>(Points + 1);
        var inner = new List<(double, double)>(Points + 1);
        for (var p = 0; p <= Points; p++)
        {
            var share = p <= Points / 2 ? (double)p / (Points / 2) : (double)(Points - p) / (Points / 2);
            var sample = wave[Math.Min(wave.Count - 1, (int)(share * (wave.Count - 1)))];
            var angle = ((double)p / Points * Math.Tau) - (Math.PI / 2);
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            outer.Add((cx + ((reach + (Math.Abs(sample) * depth)) * cos), cy + ((reach + (Math.Abs(sample) * depth)) * sin)));
            inner.Add((cx + ((reach - (Math.Abs(sample) * depth * 0.4)) * cos), cy + ((reach - (Math.Abs(sample) * depth * 0.4)) * sin)));
        }

        var color = context.Paint(context.Pulse.Pitch);
        context.Canvas.Path(outer, Math.Max(1.2, radius * 0.012), color, 0.9f, 2.5);
        context.Canvas.Path(inner, Math.Max(1, radius * 0.008), context.Paint(1 - context.Pulse.Pitch), 0.45f, 1.5);
    }

    /// <summary>The platter under the record: dark metal, a bright rim, and strobe dots that turn with it and flash on the beat.</summary>
    private static void Platter(VisualContext context, double cx, double cy, double radius, double turned)
    {
        var canvas = context.Canvas;
        canvas.Disc(cx, cy, radius * 1.06, new Rgb(0.045f, 0.045f, 0.05f));
        canvas.Ring(cx, cy, radius * 1.055, Math.Max(1, radius * 0.012), new Rgb(0.3f, 0.3f, 0.33f), 0.8f);
        if (!context.Toggle("strobe"))
        {
            return;
        }

        const int Dots = 60;
        var lit = context.Paint(context.Pulse.Pitch);
        for (var dot = 0; dot < Dots; dot++)
        {
            var angle = ((double)dot / Dots * Math.Tau) + turned;
            var (x, y) = (cx + (radius * 1.03 * Math.Cos(angle)), cy + (radius * 1.03 * Math.Sin(angle)));
            var on = context.Pulse.Kick.Level * (dot % 2 == 0 ? 1 : 0.5f);
            canvas.Disc(x, y, Math.Max(0.7, radius * 0.008), new Rgb(0.16f, 0.16f, 0.17f).Plus(lit.Times(on * 1.2f)));
        }
    }

    /// <summary>The record, pixel by pixel: grooves and their sheen, the light of the music cut in, the label.</summary>
    private void Record(VisualContext context, double cx, double cy, double radius, double turned)
    {
        var canvas = context.Canvas;
        var sheen = context.Toggle("sheen");
        var disc = DiscFor(canvas.Width, canvas.Height, cx, cy, radius, sheen);
        var depth = (float)context.Number("depth") * 2.6f;
        var label = context.Toggle("label") ? context.Cover : null;
        var (labelColor, labelInk) = (context.Paint(0.15), context.Paint(0.85));
        var colors = new Rgb[Rings];
        for (var ring = 0; ring < Rings; ring++)
        {
            colors[ring] = context.Paint((double)ring / Rings);
        }

        var turn = turned / Math.Tau * Angles;
        const int Run = 512;
        Raster.Rows((disc.Pixels.Length + Run - 1) / Run, run =>
        {
            for (var n = run * Run; n < Math.Min(disc.Pixels.Length, (run + 1) * Run); n++)
            {
                var (i, r, angle, edge, color) = disc.Pixels[n];
                if (r < LabelEdge && r >= 0.022)
                {
                    color = Label(label, labelColor, labelInk, r, angle - turned);
                }
                else if (r is >= Inner and <= Outer)
                {
                    // The music shows as light down in the fine grooves, turning with the record.
                    var ring = Math.Min(Rings - 1, (int)((r - Inner) / (Outer - Inner) * Rings));
                    var bin = (int)((((angle / Math.Tau * Angles) - turn) % Angles) + Angles) % Angles;
                    var value = _groove[(ring * Angles) + bin];
                    if (value > 0)
                    {
                        color = color.Plus(colors[ring].Times(value * depth * disc.Ridges[n]));
                    }
                }

                canvas.R[i] = (canvas.R[i] * (1 - edge)) + (color.R * edge);
                canvas.G[i] = (canvas.G[i] * (1 - edge)) + (color.G * edge);
                canvas.B[i] = (canvas.B[i] * (1 - edge)) + (color.B * edge);
            }
        });
    }

    /// <summary>
    /// The still parts of the record, worked out once for its size: where each of its pixels is (how
    /// far out, at what angle, how much of it the edge covers) and the vinyl's look there, which does
    /// not turn, the lamp's sheen staying where the lamp is.
    /// </summary>
    private Disc DiscFor(int width, int height, double cx, double cy, double radius, bool sheen)
    {
        var key = (width, height, sheen);
        if (_disc is { } known && known.Key == key)
        {
            return known;
        }

        var pixels = new List<(int, double, double, float, Rgb)>();
        var ridges = new List<float>();
        for (var y = Math.Max(0, (int)(cy - radius - 1)); y < Math.Min(height, (int)(cy + radius + 2)); y++)
        {
            for (var x = Math.Max(0, (int)(cx - radius - 1)); x < Math.Min(width, (int)(cx + radius + 2)); x++)
            {
                var (dx, dy) = (x - cx, y - cy);
                var r = Math.Sqrt((dx * dx) + (dy * dy)) / radius;
                if (r > 1.003)
                {
                    continue;
                }

                var angle = Math.Atan2(dy, dx);
                var color = r < 0.022 ? new Rgb(0.5f, 0.5f, 0.52f) : Vinyl(r, angle, radius, sheen);
                pixels.Add(((y * width) + x, r, angle, (float)Math.Clamp((1.003 - r) * radius, 0, 1), color));
                ridges.Add(0.65f + (0.35f * (float)Math.Sin(r * radius * 2.4)));
            }
        }

        _disc = new Disc(key, [.. pixels], [.. ridges]);
        return _disc;
    }

    private sealed record Disc((int Width, int Height, bool Sheen) Key, (int Index, double R, double Angle, float Edge, Rgb Color)[] Pixels, float[] Ridges);

    /// <summary>
    /// Black vinyl at radius <paramref name="r"/> (of 1) and <paramref name="angle"/>: fine grooves, the
    /// lamp's sheen in two opposite fans that stay where the lamp is, the smooth gaps between tracks
    /// gleaming in it, and a bevel at the edge.
    /// </summary>
    private static Rgb Vinyl(double r, double angle, double radius, bool sheen)
    {
        var groove = 0.5 + (0.5 * Math.Sin(r * radius * 2.4));
        var gap = 0.0;
        foreach (var at in TrackGaps)
        {
            var d = (r - at) / 0.006;
            gap = Math.Max(gap, Math.Exp(-d * d));
        }

        var bevel = r > 0.985 ? (r - 0.985) / 0.018 : 0;
        var light = 0.024 + (0.006 * groove * (1 - gap)) + (0.05 * bevel);
        if (sheen)
        {
            // Grooves reflect the lamp only near the line through the middle toward it.
            var c = Math.Cos(angle - Lamp);
            var c2 = c * c;
            var c8 = c2 * c2 * c2 * c2;
            var narrow = c8 * c8 * c8;
            var broad = c2 * c2;
            light += (narrow * (0.18 + (0.16 * groove)) * (1 - gap)) + (broad * 0.035) + (gap * (0.05 + (0.3 * narrow)));
        }

        return new Rgb((float)light, (float)light, (float)(light * 1.08));
    }

    /// <summary>The label: the cover, turned with the record, or the palette's colours with a printed ring.</summary>
    private static Rgb Label(VisualPicture? cover, Rgb plain, Rgb ink, double r, double angle)
    {
        var shade = (float)(1 - (0.25 * Math.Pow(r / LabelEdge, 6)));
        if (cover is null)
        {
            var printed = Math.Abs(r - 0.27) < 0.006 || Math.Abs(r - 0.1) < 0.004 ? 0.8f : 0f;
            var sweep = (float)(0.5 + (0.5 * Math.Cos(angle * 2)));
            return plain.Toward(ink, sweep * 0.35f).Times(0.7f * shade).Plus(ink.Times(printed * 0.6f));
        }

        var (u, v) = (r / LabelEdge * Math.Cos(angle), r / LabelEdge * Math.Sin(angle));
        var (x, y) = ((u + 1) / 2 * (cover.Width - 1), (v + 1) / 2 * (cover.Height - 1));
        return new Rgb(cover.Sample(x, y, 2), cover.Sample(x, y, 1), cover.Sample(x, y, 0)).Times(0.85f * shade);
    }

    /// <summary>
    /// The tone-arm: a base and pivot to the upper right, a counterweight behind, and the arm swung
    /// so its head rests on the needle's groove; the needle glows on the beat.
    /// </summary>
    private static void Arm(VisualContext context, double radius, (double X, double Y) pivot, (double X, double Y) stylus, double length)
    {
        var canvas = context.Canvas;
        var (px, py) = pivot;
        var (sx, sy) = stylus;
        var (ux, uy) = ((sx - px) / length, (sy - py) / length);
        var metal = new Rgb(0.5f, 0.51f, 0.54f);
        var dark = new Rgb(0.07f, 0.07f, 0.08f);

        canvas.Disc(px, py, radius * 0.17, dark);
        canvas.Ring(px, py, radius * 0.17, Math.Max(1, radius * 0.01), new Rgb(0.22f, 0.22f, 0.24f));
        canvas.Line(px - (ux * radius * 0.08), py - (uy * radius * 0.08), px - (ux * radius * 0.3), py - (uy * radius * 0.3), radius * 0.11, new Rgb(0.16f, 0.16f, 0.17f));
        canvas.Line(px - (ux * radius * 0.08), py - (uy * radius * 0.08), px - (ux * radius * 0.3), py - (uy * radius * 0.3), radius * 0.025, new Rgb(0.32f, 0.32f, 0.34f), 0.6f);
        canvas.Line(px, py, sx - (ux * radius * 0.13), sy - (uy * radius * 0.13), Math.Max(2, radius * 0.032), metal);
        canvas.Line(px + (uy * radius * 0.007), py - (ux * radius * 0.007), sx - (ux * radius * 0.13) + (uy * radius * 0.007), sy - (uy * radius * 0.13) - (ux * radius * 0.007), Math.Max(1, radius * 0.008), new Rgb(0.95f, 0.95f, 1f), 0.5f);
        canvas.Line(sx - (ux * radius * 0.14), sy - (uy * radius * 0.14), sx + (ux * radius * 0.02), sy + (uy * radius * 0.02), Math.Max(3, radius * 0.07), new Rgb(0.2f, 0.2f, 0.22f));
        canvas.Disc(px, py, radius * 0.065, new Rgb(0.42f, 0.42f, 0.45f));
        canvas.Glow(px - (radius * 0.02), py - (radius * 0.02), radius * 0.04, Rgb.White, 0.35f);
        var pulse = context.Pulse.Kick.Level;
        canvas.Glow(sx, sy, radius * (0.06 + (0.08 * pulse)), context.Paint(context.Pulse.Pitch), 0.5f + (1.2f * pulse));
    }

    /// <summary>
    /// Where an arm of <paramref name="length"/> from its pivot (<paramref name="px"/>, <paramref name="py"/>)
    /// meets the circle of <paramref name="needle"/> about the record's middle: the upper of the two
    /// crossings, or the nearest point on the circle if the arm cannot reach it.
    /// </summary>
    internal static (double X, double Y) Reach(double cx, double cy, double needle, double px, double py, double length)
    {
        var (dx, dy) = (px - cx, py - cy);
        var d = Math.Sqrt((dx * dx) + (dy * dy));
        var along = ((needle * needle) - (length * length) + (d * d)) / (2 * d);
        var across = (needle * needle) - (along * along);
        if (across < 0)
        {
            return (cx + (dx / d * needle), cy + (dy / d * needle));
        }

        var off = Math.Sqrt(across);
        var (mx, my) = (cx + (dx / d * along), cy + (dy / d * along));
        var (ax, ay) = (mx + (dy / d * off), my - (dx / d * off));
        var (bx, by) = (mx - (dy / d * off), my + (dx / d * off));
        return ay < by ? (ax, ay) : (bx, by);
    }
}
