namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A kaleidoscope the music turns (AU-18): shards of light, each riding its part of the spectrum,
/// drift in a wedge that mirrors round the middle. The kaleidoscope clicks round a step on each
/// beat; at each bar it can gain or lose mirrors; the bass draws the eye in, and every kick lights
/// the shards up.
/// </summary>
public sealed class KaleidoscopeScene : VisualScene
{
    private double _turn;
    private double _turnTo;
    private double _drift;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var (cx, cy) = (w / 2.0, h / 2.0);
        var turning = context.Toggle("turn");
        _turnTo += turning && pulse.Beat ? Math.PI / 12 : 0;
        _turn += (_turnTo - _turn) * (1 - Math.Exp(-context.Dt / 0.1));
        _drift += context.Dt * (0.1 + (0.5 * pulse.Loudness));

        // The mirrors: as set, and with each bar one more or one fewer, round a cycle of three.
        var mirrors = (int)context.Number("mirrors") + (turning ? (int)(pulse.Beats / 4 % 3) - 1 : 0);
        mirrors = Math.Max(2, mirrors);
        var wedge = Math.PI / mirrors;
        var count = (int)context.Number("shards");
        var shards = new (double U, double V, double Reach, float Level, double Share)[count];
        for (var s = 0; s < count; s++)
        {
            var share = count == 1 ? 0.5 : (double)s / (count - 1);
            var level = pulse.Bands[(int)(share * (pulse.Bands.Count - 1))];
            var r = 0.15 + (0.75 * Noise.Fractal(_drift + (s * 9.7), 2));
            var a = Noise.Fractal((_drift * 0.8) + (s * 4.3) + 30, 2) * wedge;
            shards[s] = (Math.Cos(a) * r, Math.Sin(a) * r, 0.05 + (0.2 * level), level, share);
        }

        var zoom = 1 / (0.85 + (0.35 * pulse.Bass));
        var bright = 0.5f + (1.2f * pulse.Punch);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // Folded into the first wedge: turned, then reflected back and forth across its edges.
                var (dx, dy) = ((x - cx) / (size * 0.5) * zoom, (y - cy) / (size * 0.5) * zoom);
                var r = Math.Sqrt((dx * dx) + (dy * dy));
                var a = Math.Atan2(dy, dx) + _turn;
                a = ((a % (2 * wedge)) + (2 * wedge)) % (2 * wedge);
                a = a > wedge ? (2 * wedge) - a : a;
                var (u, v) = (Math.Cos(a) * r, Math.Sin(a) * r);
                var light = Rgb.Black;
                foreach (var (su, sv, reach, level, share) in shards)
                {
                    var (du, dv) = (u - su, v - sv);
                    var near = Math.Exp(-((du * du) + (dv * dv)) / (reach * reach));
                    light = light.Plus(context.Paint(share).Times((float)(near * (0.25 + level)) * bright));
                }

                var fade = (float)Math.Clamp(1.4 - r, 0, 1);
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (light.R * fade, light.G * fade, light.B * fade);
            }
        });

        canvas.Bloom(0.6f, 0.5f, 4);
    }
}
