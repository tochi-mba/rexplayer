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
    private Raster? _shadow;

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
        var goal = (w * (0.2 + (0.6 * pulse.Pitch)) + (Math.Sin(_time * 0.9) * w * 0.12), h * (0.55 + (0.12 * Math.Sin(_time * 0.6))));
        _lead = (_lead.X + ((goal.Item1 - _lead.X) * Math.Min(1, dt * 1.2)), _lead.Y + ((goal.Item2 - _lead.Y) * Math.Min(1, dt * 1.2)));

        if (context.Toggle("sky"))
        {
            for (var y = 0; y < h; y++)
            {
                var down = (float)y / h;
                var sky = context.Paint(0.85 - (0.6 * down)).Times(0.12f + (0.45f * down));
                Array.Fill(canvas.R, sky.R, y * w, w);
                Array.Fill(canvas.G, sky.G, y * w, w);
                Array.Fill(canvas.B, sky.B, y * w, w);
            }
        }
        else
        {
            canvas.Clear(Rgb.Black);
        }

        // The music is the flock's energy: in silence it hangs, drifting; with the music it surges.
        var energy = 0.2 + (0.8 * Math.Max(pulse.Loudness, Math.Max(pulse.Punch, pulse.Bass)));
        var pull = context.Number("tightness") * (1.2 + (6 * pulse.Punch));
        var scatter = size * (0.05 + (0.6 * pulse.Treble));
        // Against the sky a bird is a dark shape: the birds are drawn as a shadow, and the sky darkened by it.
        var dusk = context.Toggle("sky");
        if (dusk && (_shadow is null || _shadow.Width != w || _shadow.Height != h))
        {
            _shadow = new Raster(w, h);
        }

        _shadow?.Clear(Rgb.Black);
        var target = dusk ? _shadow! : canvas;
        var bird = dusk ? Rgb.White : context.Paint(0.5);
        for (var b = 0; b < count; b++)
        {
            var (x, y, vx, vy) = _birds[b];

            // The turning air: a flow that curls and drifts, the same for neighbours, so they turn together.
            var (fx, fy) = (x / size * 3, y / size * 3);
            var flowX = Math.Sin((fy * 1.7) + _time) + Math.Cos((fx * 0.9) - (_time * 1.3));
            var flowY = Math.Cos((fx * 1.3) - _time) - Math.Sin((fy * 1.1) + (_time * 0.7));
            var (ax, ay) = (((flowX * size * 1.5) + ((_lead.X - x) * pull)) * energy, ((flowY * size * 1.5) + ((_lead.Y - y) * pull)) * energy);
            // The edges of the sky turn the flock back, softly.
            ay += (Math.Max(0, (h * 0.2) - y) - Math.Max(0, y - (h * 0.92))) * 6;
            ax += (Math.Max(0, (w * 0.05) - x) - Math.Max(0, x - (w * 0.95))) * 6;
            ax += (Noise.Hash((b * 2) + (long)(_time * 8)) - 0.5) * scatter * 4;
            ay += (Noise.Hash((b * 2) + 1 + (long)(_time * 8)) - 0.5) * scatter * 4;
            (vx, vy) = ((vx + (ax * dt)) * Math.Exp(-dt * 2), (vy + (ay * dt)) * Math.Exp(-dt * 2));
            (x, y) = (x + (vx * dt), y + (vy * dt));
            _birds[b] = (x, y, vx, vy);

            // A bird: a dash along its way, wings a little apart.
            var speed = Math.Max(1e-6, Math.Sqrt((vx * vx) + (vy * vy)));
            var (ux, uy) = (vx / speed * size * 0.012, vy / speed * size * 0.012);
            target.Line(x - ux, y - uy, x + ux, y + uy, Math.Max(1.5, size * 0.006), bird, 0.8f);
        }

        if (dusk)
        {
            var shadow = _shadow!;
            for (var i = 0; i < canvas.R.Length; i++)
            {
                var keep = 1 - Math.Min(0.92f, shadow.R[i]);
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (canvas.R[i] * keep, canvas.G[i] * keep, canvas.B[i] * keep);
            }
        }
        else
        {
            canvas.Bloom(0.3f, 0.7f, 3);
        }
    }
}
