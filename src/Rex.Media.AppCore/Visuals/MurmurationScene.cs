namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A murmuration of starlings over a dusk sky (AU-18): a great flock wheeling as one, each bird
/// following the turning air and the flock's lead. The melody leads, the flock swinging toward
/// the pitch; each kick draws it tight, so it pulses on the beat; the treble scatters its edges.
/// </summary>
public sealed class MurmurationScene : VisualScene
{
    private (double X, double Y, double Vx, double Vy)[] _birds = [];
    private double _time;
    private (double X, double Y) _lead;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var dt = context.Dt;
        var count = (int)context.Number("birds");
        if (_birds.Length != count)
        {
            _birds = [.. Enumerable.Range(0, count).Select(_ => ((w * 0.5) + ((random.NextDouble() - 0.5) * size * 0.4), (h * 0.45) + ((random.NextDouble() - 0.5) * size * 0.3), 0.0, 0.0))];
            _lead = (w * 0.5, h * 0.45);
        }

        _time += dt * (0.3 + (0.7 * pulse.Loudness));

        // The lead: drifting where the melody goes, the flock behind it.
        var goal = (w * (0.2 + (0.6 * pulse.Pitch)) + (Math.Sin(_time * 0.9) * w * 0.12), h * (0.4 + (0.15 * Math.Sin(_time * 0.6))));
        _lead = (_lead.X + ((goal.Item1 - _lead.X) * Math.Min(1, dt * 1.2)), _lead.Y + ((goal.Item2 - _lead.Y) * Math.Min(1, dt * 1.2)));

        if (context.Toggle("sky"))
        {
            for (var y = 0; y < h; y++)
            {
                var down = (float)y / h;
                var sky = context.Paint(0.85 - (0.6 * down)).Times(0.08f + (0.4f * down * down));
                Array.Fill(canvas.R, sky.R, y * w, w);
                Array.Fill(canvas.G, sky.G, y * w, w);
                Array.Fill(canvas.B, sky.B, y * w, w);
            }
        }
        else
        {
            canvas.Clear(Rgb.Black);
        }

        var pull = context.Number("tightness") * (1.2 + (6 * pulse.Punch));
        var scatter = size * (0.05 + (0.6 * pulse.Treble));
        var bird = context.Toggle("sky") ? Rgb.Black : context.Paint(0.5);
        var glow = context.Toggle("sky") ? 1f : 0.8f;
        for (var b = 0; b < count; b++)
        {
            var (x, y, vx, vy) = _birds[b];

            // The turning air: a flow that curls and drifts, the same for neighbours, so they turn together.
            var (fx, fy) = (x / size * 3, y / size * 3);
            var flowX = Math.Sin((fy * 1.7) + _time) + Math.Cos((fx * 0.9) - (_time * 1.3));
            var flowY = Math.Cos((fx * 1.3) - _time) - Math.Sin((fy * 1.1) + (_time * 0.7));
            var (ax, ay) = ((flowX * size * 0.6) + ((_lead.X - x) * pull), (flowY * size * 0.6) + ((_lead.Y - y) * pull));
            ax += (Noise.Hash((b * 2) + (long)(_time * 8)) - 0.5) * scatter * 4;
            ay += (Noise.Hash((b * 2) + 1 + (long)(_time * 8)) - 0.5) * scatter * 4;
            (vx, vy) = ((vx + (ax * dt)) * Math.Exp(-dt * 2), (vy + (ay * dt)) * Math.Exp(-dt * 2));
            (x, y) = (x + (vx * dt), y + (vy * dt));
            _birds[b] = (x, y, vx, vy);

            // A bird: a dash along its way, wings a little apart.
            var speed = Math.Max(1e-6, Math.Sqrt((vx * vx) + (vy * vy)));
            var (ux, uy) = (vx / speed * size * 0.006, vy / speed * size * 0.006);
            canvas.Line(x - ux, y - uy, x + ux, y + uy, Math.Max(1, size * 0.004), bird, glow);
        }

        if (!context.Toggle("sky"))
        {
            canvas.Bloom(0.3f, 0.7f, 3);
        }
    }
}
