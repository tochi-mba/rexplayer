namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A record turning under a light (AU-18). The needle cuts the music into the record as it plays:
/// the groove under it lights with the sound's loudness, turns away with the record, and stays, so
/// the song so far is written round the disc in rings of light, quiet passages dark and loud ones
/// bright, the needle working inward as the song goes on. The sheen of the light on the vinyl stays
/// where the light is while the record turns beneath it; the cover is the label.
/// </summary>
public sealed class VinylScene : VisualScene
{
    private const int Angles = 720;
    private const int Rings = 200;
    private const double Inner = 0.36;
    private const double Outer = 0.96;

    private readonly float[] _groove = new float[Angles * Rings];
    private double _turned = double.NaN;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var radius = Math.Min(w, h) * 0.45;
        var (cx, cy) = ((w / 2.0) - (radius * 0.12), h / 2.0);
        var turnsPerSecond = context.Pick("speed") switch
        {
            1 => 45.0 / 60,
            2 => 78.0 / 60,
            _ => 100.0 / 180,
        };

        // The record turns with the song, so pausing stops it.
        var turned = context.Seconds * turnsPerSecond * Math.Tau;
        Cut(context, turned);

        canvas.Clear(new Rgb(0.012f, 0.011f, 0.016f));
        var light = context.Paint(0.15);
        canvas.Glow(cx, cy, radius * 1.45, light.Times(0.22f), 1);

        var depth = (float)context.Number("depth");
        var sheen = context.Toggle("sheen");
        var label = context.Toggle("label") ? context.Cover : null;
        var labelColor = context.Paint(0.8);
        for (var y = Math.Max(0, (int)(cy - radius - 1)); y < Math.Min(h, (int)(cy + radius + 2)); y++)
        {
            for (var x = Math.Max(0, (int)(cx - radius - 1)); x < Math.Min(w, (int)(cx + radius + 2)); x++)
            {
                var (dx, dy) = (x - cx, y - cy);
                var r = Math.Sqrt((dx * dx) + (dy * dy)) / radius;
                if (r > 1.004)
                {
                    continue;
                }

                var edge = (float)Math.Clamp((1.004 - r) * radius, 0, 1);
                var angle = Math.Atan2(dy, dx);
                var i = (y * w) + x;
                Rgb color;
                if (r < 0.025)
                {
                    color = Rgb.Black;
                }
                else if (r < 0.32)
                {
                    color = Label(label, labelColor, r, angle - turned);
                }
                else
                {
                    // Black vinyl with its fine grooves, and the light's sheen in two opposite fans.
                    var vinyl = 0.03f + (0.012f * (float)Math.Pow(Math.Sin(r * radius * 1.9), 2));
                    color = new Rgb(vinyl, vinyl, vinyl * 1.05f);
                    if (sheen)
                    {
                        var fan = (float)Math.Pow(Math.Max(0, Math.Cos(2 * (angle + 0.6))), 18) * 0.2f;
                        color = color.Plus(new Rgb(fan, fan, fan * 1.08f));
                    }

                    if (r is >= Inner and <= Outer)
                    {
                        var ring = Math.Min(Rings - 1, (int)((r - Inner) / (Outer - Inner) * Rings));
                        var local = Wrap(angle - turned);
                        var value = _groove[(ring * Angles) + (int)(local / Math.Tau * Angles) % Angles] * depth;
                        color = color.Plus(context.Paint((r - Inner) / (Outer - Inner)).Times(value));
                    }
                }

                canvas.R[i] = color.R * edge;
                canvas.G[i] = color.G * edge;
                canvas.B[i] = color.B * edge;
            }
        }

        if (context.Toggle("arm"))
        {
            DrawArm(context, cx, cy, radius);
        }

        canvas.Bloom(0.5f, (float)context.Number("glow"), 5);
    }

    /// <summary>Lights the groove the needle passed over since the last frame with the sound just heard.</summary>
    private void Cut(VisualContext context, double turned)
    {
        var ring = Math.Clamp((int)((Needle(context.Progress) - Inner) / (Outer - Inner) * Rings), 0, Rings - 1);
        var wave = context.Pulse.Wave;
        var from = double.IsNaN(_turned) || Math.Abs(turned - _turned) > Math.Tau ? turned - 0.05 : _turned;
        _turned = turned;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(turned - from) / Math.Tau * Angles));
        var needleAngle = -Math.PI / 3;
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
                    var value = loudest * (spread == 0 ? 1 : 0.45f);
                    _groove[(row * Angles) + bin] = Math.Max(_groove[(row * Angles) + bin] * 0.6f, value);
                }
            }
        }
    }

    /// <summary>Where the needle is across the record, from the outside in as the song goes on.</summary>
    private static double Needle(double progress) => Outer - 0.02 - ((Outer - Inner - 0.04) * Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1));

    private static double Wrap(double angle) => ((angle % Math.Tau) + Math.Tau) % Math.Tau;

    /// <summary>The label: the cover, turned with the record, or a plain colour with a ring.</summary>
    private static Rgb Label(VisualPicture? cover, Rgb plain, double r, double angle)
    {
        if (cover is null)
        {
            var ring = Math.Abs(r - 0.26) < 0.012 ? 0.5f : 0f;
            return plain.Times(0.55f).Plus(new Rgb(ring, ring, ring));
        }

        var (u, v) = (r / 0.32 * Math.Cos(angle), r / 0.32 * Math.Sin(angle));
        var (x, y) = ((u + 1) / 2 * (cover.Width - 1), (v + 1) / 2 * (cover.Height - 1));
        return new Rgb(cover.Sample(x, y, 2), cover.Sample(x, y, 1), cover.Sample(x, y, 0)).Times(0.9f);
    }

    /// <summary>The tone-arm from its pivot to the needle, the needle glowing on the beat.</summary>
    private static void DrawArm(VisualContext context, double cx, double cy, double radius)
    {
        var canvas = context.Canvas;
        var needle = Needle(context.Progress) * radius;
        var (sx, sy) = (cx + (needle * Math.Cos(-Math.PI / 3)), cy + (needle * Math.Sin(-Math.PI / 3)));
        var (px, py) = (Math.Min(canvas.Width - 6, cx + (radius * 1.18)), Math.Max(6, cy - (radius * 0.98)));
        var metal = new Rgb(0.62f, 0.63f, 0.66f);
        canvas.Disc(px, py, radius * 0.07, new Rgb(0.18f, 0.18f, 0.2f));
        canvas.Line(px, py, sx + ((px - sx) * 0.12), sy + ((py - sy) * 0.12), Math.Max(2, radius * 0.025), metal, 0.9f);
        canvas.Line(sx + ((px - sx) * 0.12), sy + ((py - sy) * 0.12), sx, sy, Math.Max(3, radius * 0.05), new Rgb(0.4f, 0.4f, 0.44f), 0.9f);
        var pulse = context.Pulse.BeatStrength;
        canvas.Glow(sx, sy, radius * (0.05 + (0.06 * pulse)), context.Paint(context.Pulse.Pitch), 0.6f + pulse);
    }
}

