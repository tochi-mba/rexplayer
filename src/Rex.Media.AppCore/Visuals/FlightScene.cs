namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A flight down a tunnel of light, in time with the music (AU-18). Every beat sends a ring out
/// far ahead, shaped by the spectrum of that moment, and it reaches the viewer some beats later,
/// on a beat, so the tunnel goes by at the tempo: every bar's ring brighter and wider. Each kick
/// lunges forward, widening the view and smearing it outward; the bass breathes light at the end
/// of the tunnel; stars stream past and glint with the hi-hats. When the music stops, nothing new
/// sets out and the flight comes to rest.
/// </summary>
public sealed class FlightScene : VisualScene
{
    /// <summary>How far ahead a ring sets out, in tunnel widths.</summary>
    private const double Far = 8;

    /// <summary>Where a ring has passed the viewer.</summary>
    private const double Near = 0.12;

    private const int StarCount = 160;
    private const int Shape = 16;

    private readonly List<Ring> _rings = [];
    private readonly (double X, double Y, double Z)[] _stars = new (double, double, double)[StarCount];
    private readonly List<(double, double)> _points = [];
    private double _speed;
    private float _glint;
    private bool _placed;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var dt = context.Dt;
        var (cx, cy) = (w / 2.0, h / 2.0);

        // A ring set out on a beat arrives the chosen number of beats later, on a beat: the
        // tunnel's speed is the tempo's. Without a tempo, a walking pace; in silence, slowing to rest.
        var ahead = context.Number("ahead");
        var beat = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        var cruise = pulse.Loudness > 0 || pulse.Beat ? Far / (ahead * beat) : 0;
        _speed += (cruise - _speed) * (1 - Math.Exp(-dt / 0.6));
        var travel = _speed * dt;
        if (pulse.Beat)
        {
            _rings.Add(new Ring(Far, Snapshot(pulse), pulse.Filled ? 0.5f : Math.Max(0.4f, pulse.BeatStrength), pulse.Beats % 4 == 0, pulse.Pitch));
        }

        foreach (var ring in _rings)
        {
            ring.Z -= travel;
        }

        _rings.RemoveAll(ring => ring.Z < Near);

        // The kick lunges: the view widens, and what was drawn smears outward behind.
        var lens = size * 0.5 * (1 + (0.18 * pulse.Punch));
        canvas.Feedback((float)context.Number("trails"), 1.01 + (0.05 * pulse.Punch) + (0.004 * _speed), 0, 0, 0);
        canvas.Glow(cx, cy, size * (0.08 + (0.22 * pulse.Bass)), context.Paint(pulse.Pitch), 0.15f + (0.7f * pulse.Bass) + (0.5f * pulse.Punch));

        if (context.Toggle("stars"))
        {
            Stars(context, cx, cy, lens, travel);
        }

        // The rings, farthest first, so the nearer cover them.
        var spectrum = context.Toggle("spectrum");
        foreach (var ring in _rings.OrderByDescending(ring => ring.Z))
        {
            var radius = lens / ring.Z;
            var near = (float)Math.Clamp((Far - ring.Z) / 2, 0, 1);
            var light = ring.Strength * near * (ring.Bar ? 1.4f : 1) * (1 + (0.6f * pulse.Punch));
            var width = Math.Max(1, size * (ring.Bar ? 0.02 : 0.012) / ring.Z);
            var color = context.Paint(ring.Pitch);
            if (!spectrum)
            {
                canvas.Ring(cx, cy, radius, width, color, light);
                continue;
            }

            // The spectrum it set out with, round the ring: the bass at the top, mirrored.
            _points.Clear();
            for (var k = 0; k <= Shape * 2; k++)
            {
                var band = ring.Bands[Math.Min(Shape - 1, k <= Shape ? k : (Shape * 2) - k)];
                var angle = ((double)k / (Shape * 2) * Math.Tau) - (Math.PI / 2);
                var reach = radius * (1 + (0.3 * band));
                _points.Add((cx + (reach * Math.Cos(angle)), cy + (reach * Math.Sin(angle))));
            }

            canvas.Path(_points, width, color, light, 2, closed: true);
        }

        canvas.Bloom(0.4f, 0.8f, 5);
    }

    /// <summary>The spectrum now, in a few broad bands.</summary>
    private static float[] Snapshot(MusicPulse pulse)
    {
        var bands = new float[Shape];
        for (var k = 0; k < Shape; k++)
        {
            var (from, to) = (k * pulse.Bands.Count / Shape, (k + 1) * pulse.Bands.Count / Shape);
            for (var b = from; b < to; b++)
            {
                bands[k] = Math.Max(bands[k], pulse.Bands[b]);
            }
        }

        return bands;
    }

    /// <summary>Stars streaming past, each a streak along the way it has come, glinting with the hi-hats.</summary>
    private void Stars(VisualContext context, double cx, double cy, double lens, double travel)
    {
        var random = context.Random;
        if (!_placed)
        {
            for (var i = 0; i < StarCount; i++)
            {
                _stars[i] = ((random.NextDouble() - 0.5) * 3, (random.NextDouble() - 0.5) * 3, Near + (random.NextDouble() * Far));
            }

            _placed = true;
        }

        _glint = context.Pulse.Hat.Hit ? 1 : Math.Max(0, _glint - (float)(context.Dt * 6));
        var color = context.Paint(0.85);
        for (var i = 0; i < StarCount; i++)
        {
            var (x, y, z) = _stars[i];
            var was = z;
            z -= travel * 2.5;
            if (z < Near)
            {
                (x, y, z, was) = ((random.NextDouble() - 0.5) * 3, (random.NextDouble() - 0.5) * 3, Far, Far);
            }

            _stars[i] = (x, y, z);
            var light = (float)Math.Clamp((Far - z) / Far, 0, 1) * (0.35f + (0.65f * _glint * (i % 3 == 0 ? 1 : 0.3f)));
            context.Canvas.Line(cx + (x * lens / was), cy + (y * lens / was), cx + (x * lens / z), cy + (y * lens / z), Math.Max(1, 2.5 / z), color, light, 1);
        }
    }

    private sealed class Ring(double z, float[] bands, float strength, bool bar, float pitch)
    {
        public double Z { get; set; } = z;

        public float[] Bands { get; } = bands;

        public float Strength { get; } = strength;

        public bool Bar { get; } = bar;

        public float Pitch { get; } = pitch;
    }
}
