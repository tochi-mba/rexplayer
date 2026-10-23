namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A storm the music conducts (AU-18): clouds along the top glow with the bass; every kick strikes
/// a forked bolt to the ground, where the pitch says, lighting the whole sky as hard as the kick
/// was; snares crack bolts from cloud to cloud; hi-hats flicker inside the clouds; rain falls in
/// time with the beat.
/// </summary>
public sealed class LightningScene : VisualScene
{
    private readonly List<Bolt> _bolts = [];
    private readonly List<(double, double)> _path = [];
    private double _flash;
    private float _flicker;
    private double _rain;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        canvas.Clear(Rgb.Black);

        if (pulse.Kick.Hit)
        {
            var x = w * (0.1 + (0.8 * pulse.Pitch));
            _bolts.Add(new Bolt(Fork(random, x, h * 0.12, x + ((random.NextDouble() - 0.5) * w * 0.3), h, size, (int)context.Number("branches")), pulse.Kick.Strength));
            _flash = Math.Max(_flash, pulse.Kick.Strength * context.Number("flash"));
        }

        if (pulse.Snare.Hit)
        {
            var y = h * (0.08 + (0.1 * random.NextDouble()));
            _bolts.Add(new Bolt(Fork(random, w * random.NextDouble() * 0.4, y, w * (0.6 + (random.NextDouble() * 0.4)), y + (h * 0.05), size * 0.5, 0), pulse.Snare.Strength * 0.6f));
        }

        _flicker = pulse.Hat.Hit ? 1 : Math.Max(0, _flicker - (float)(context.Dt * 8));
        _flash = Math.Max(0, _flash - (context.Dt * 4));

        // The sky lit by the last strike, and the clouds, lit from within.
        var sky = context.Paint(0.6).Times((float)(0.05 + (0.5 * _flash)));
        canvas.Fill(0, 0, w, h, sky);
        for (var c = 0; c < 9; c++)
        {
            var cx = w * ((c + 0.5) / 9) + (Math.Sin((c * 1.7) + (context.Seconds * 0.65)) * w * 0.03);
            // A low rolling glow and moving cloud bank keep the storm alive between
            // percussion hits, without substituting an artificial lightning strike.
            var rolling = 0.035 * (1 + Math.Sin((context.Seconds * 2.2) + (c * 1.1)));
            var inside = (float)((0.12 * pulse.Bass) + (0.25 * _flicker * Noise.Hash((c * 13) + pulse.Hat.Count)) + (0.4 * _flash) + 0.09 + rolling);
            canvas.Glow(cx, h * 0.06, w * 0.12, context.Paint(0.45 + (0.1 * Noise.Hash(c))), inside);
        }

        // The bolts: brilliant at the strike, flickering out over a fifth of a second.
        foreach (var bolt in _bolts)
        {
            bolt.Age += context.Dt;
            var light = bolt.Strength * (float)Math.Max(0, 1 - (bolt.Age / 0.22)) * (Noise.Hash((long)(bolt.Age * 60)) > 0.3 ? 1 : 0.4f);
            foreach (var branch in bolt.Branches)
            {
                canvas.Path(branch.Points, branch.Width, context.Paint(0.85), light * 1.6f, 3);
            }
        }

        _bolts.RemoveAll(bolt => bolt.Age > 0.25);

        if (context.Toggle("rain"))
        {
            Rain(context, w, h, size);
        }

        canvas.Bloom(0.3f, 1.0f, 6);
    }

    /// <summary>Rain in slanting streaks, falling a stage's height every two beats.</summary>
    private void Rain(VisualContext context, int w, int h, double size)
    {
        var pulse = context.Pulse;
        var beat = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        _rain += context.Dt / (beat * 2);
        var (canvas, color) = (context.Canvas, context.Paint(0.5));
        for (var d = 0; d < 140; d++)
        {
            // Slight crosswind gives the rain visible lateral motion between beats.
            var x = ((Noise.Hash(d * 7) + (context.Seconds * 0.015)) % 1) * w;
            var y = ((Noise.Hash((d * 7) + 1) + _rain) % 1) * h * 1.1;
            canvas.Line(x, y, x - (size * 0.01), y + (size * 0.04), 1, color, 0.12f + (0.2f * pulse.Loudness));
        }
    }

    /// <summary>A jagged bolt from one point to another, by halving each stretch and nudging the middle aside, with forks off it.</summary>
    private List<Branch> Fork(Random random, double x0, double y0, double x1, double y1, double size, int branches)
    {
        var all = new List<Branch>();
        var trunk = Jagged(random, x0, y0, x1, y1, size * 0.12);
        all.Add(new Branch(trunk, Math.Max(1.5, size * 0.006)));
        for (var b = 0; b < branches; b++)
        {
            var (fx, fy) = trunk[random.Next(1, trunk.Count - 1)];
            var angle = (Math.PI / 2) + ((random.NextDouble() - 0.5) * 1.6);
            var reach = size * (0.1 + (0.2 * random.NextDouble()));
            all.Add(new Branch(Jagged(random, fx, fy, fx + (Math.Cos(angle) * reach), fy + (Math.Sin(angle) * reach), size * 0.05), Math.Max(1, size * 0.003)));
        }

        return all;
    }

    private List<(double, double)> Jagged(Random random, double x0, double y0, double x1, double y1, double roughness)
    {
        _path.Clear();
        _path.Add((x0, y0));
        _path.Add((x1, y1));
        for (var pass = 0; pass < 6; pass++)
        {
            for (var i = _path.Count - 1; i > 0; i--)
            {
                var ((ax, ay), (bx, by)) = (_path[i - 1], _path[i]);
                var nudge = (random.NextDouble() - 0.5) * roughness;
                _path.Insert(i, (((ax + bx) / 2) + nudge, ((ay + by) / 2) + (nudge * 0.3)));
            }

            roughness *= 0.55;
        }

        return [.. _path];
    }

    private sealed record Branch(List<(double, double)> Points, double Width);

    private sealed class Bolt(List<Branch> branches, float strength)
    {
        public List<Branch> Branches { get; } = branches;

        public float Strength { get; } = strength;

        public double Age { get; set; }
    }
}
