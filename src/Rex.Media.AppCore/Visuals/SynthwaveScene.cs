namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A drive into a neon sunset (AU-18): a striped sun on the horizon that swells with each kick,
/// mountains along it that the spectrum raises, and a glowing grid floor running toward the
/// viewer, its cross lines passing beneath at the tempo, flaring on the kick.
/// </summary>
public sealed class SynthwaveScene : VisualScene
{
    private readonly List<(double, double)> _ridge = [];
    private float[] _peaks = [];
    private double _travel;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var horizon = h * 0.55;

        // The sky: deep at the top, warming toward the horizon.
        for (var y = 0; y < (int)horizon; y++)
        {
            var down = (float)(y / horizon);
            var sky = context.Paint(0.75 - (0.45 * down)).Times(0.03f + (0.22f * down * down));
            Array.Fill(canvas.R, sky.R, y * w, w);
            Array.Fill(canvas.G, sky.G, y * w, w);
            Array.Fill(canvas.B, sky.B, y * w, w);
        }

        Array.Clear(canvas.R, (int)horizon * w, (h - (int)horizon) * w);
        Array.Clear(canvas.G, (int)horizon * w, (h - (int)horizon) * w);
        Array.Clear(canvas.B, (int)horizon * w, (h - (int)horizon) * w);

        if (context.Toggle("sun"))
        {
            Sun(context, w / 2.0, horizon, size * (0.22 + (0.05 * pulse.Bass) + (0.04 * pulse.Punch)));
        }

        if (context.Toggle("mountains"))
        {
            Mountains(context, horizon);
        }

        // The floor: lines to the vanishing point, and cross lines coming on at the tempo.
        var lines = context.Number("speed");
        var beat = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        _travel += context.Dt / beat * lines * (pulse.Loudness > 0 || pulse.Beat ? 1 : 0.1);
        var grid = context.Paint(0.15);
        var flare = 0.35f + (1.2f * pulse.Punch);
        for (var k = -12; k <= 12; k++)
        {
            canvas.Line(w / 2.0, horizon, (w / 2.0) + (k * w * 0.16), h, 1.2, grid, flare * 0.6f, 1.5);
        }

        for (var n = 0; n < 14; n++)
        {
            // Depth 1 is the horizon; nearer lines spread further apart, as a floor does.
            var depth = 1 / (1 + ((n + (1 - (_travel % 1))) * 0.35));
            var y = horizon + ((h - horizon) * (1 - depth) * 1.15);
            if (y < h)
            {
                canvas.Line(0, y, w, y, 1 + (2 * (1 - depth)), grid, flare * (float)(1 - depth), 1.5);
            }
        }

        canvas.Bloom(0.4f, 0.8f, 6);
    }

    /// <summary>The sun: a disc fading from gold to pink, cut by bands that thicken toward its foot.</summary>
    private static void Sun(VisualContext context, double cx, double horizon, double radius)
    {
        var canvas = context.Canvas;
        var w = canvas.Width;
        for (var y = Math.Max(0, (int)(horizon - radius)); y < (int)horizon; y++)
        {
            var up = (horizon - y) / radius;
            var cut = up < 0.55 && Math.Sin(up * 60) > 0.6 - up;
            if (cut)
            {
                continue;
            }

            var half = Math.Sqrt(Math.Max(0, 1 - ((1 - up) * (1 - up)))) * radius;
            var color = context.Paint(0.35 + (0.6 * up));
            for (var x = Math.Max(0, (int)(cx - half)); x < Math.Min(w, (int)(cx + half)); x++)
            {
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        }

        canvas.Glow(cx, horizon - (radius * 0.4), radius * 2.2, context.Paint(0.5), 0.25f + context.Pulse.Punch);
    }

    /// <summary>Mountains along the horizon, raised by the spectrum, the bass in the middle.</summary>
    private void Mountains(VisualContext context, double horizon)
    {
        var canvas = context.Canvas;
        var (w, pulse) = (canvas.Width, context.Pulse);
        const int Peaks = 40;
        if (_peaks.Length != Peaks)
        {
            _peaks = new float[Peaks];
        }

        _ridge.Clear();
        for (var p = 0; p < Peaks; p++)
        {
            var from = Math.Abs(((double)p / (Peaks - 1) * 2) - 1);
            var band = pulse.Bands[(int)(from * (pulse.Bands.Count - 1))];
            _peaks[p] = Math.Max(band, _peaks[p] - (float)(context.Dt * 1.5));
            var rock = 0.3 + (0.7 * Noise.At(p * 0.9));
            var y = horizon - (_peaks[p] * canvas.Height * 0.16 * rock) - 2;
            _ridge.Add((p * (w - 1.0) / (Peaks - 1), y));
        }

        // Dark rock below the ridge, edged in light.
        for (var x = 0; x < w; x++)
        {
            var at = (double)x / Math.Max(1, w - 1) * (Peaks - 1);
            var below = (int)at;
            var top = _ridge[below].Item2 + ((_ridge[Math.Min(Peaks - 1, below + 1)].Item2 - _ridge[below].Item2) * (at - below));
            for (var y = Math.Max(0, (int)top); y < (int)horizon; y++)
            {
                var i = (y * w) + x;
                canvas.R[i] *= 0.1f;
                canvas.G[i] *= 0.1f;
                canvas.B[i] *= 0.1f;
            }
        }

        canvas.Path(_ridge, 1.5, context.Paint(0.9), 0.8f + pulse.Punch, 2);
    }
}
