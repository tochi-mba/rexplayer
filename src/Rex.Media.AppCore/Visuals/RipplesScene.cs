namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Drops on still water (AU-18): every beat lands a drop, harder beats bigger, from the middle or
/// anywhere; each spreads in rings that fade as they grow, crossing the rings of the drops before,
/// the colour following the pitch of the moment it fell.
/// </summary>
public sealed class RipplesScene : VisualScene
{
    private readonly List<Drop> _drops = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var pulse = context.Pulse;
        var size = Math.Min(w, h);
        canvas.Feedback(0.6f, 1.0, 0, 0, 0);
        var lifetime = context.Number("lifetime");
        var falls = context.Pick("drops") switch
        {
            1 => pulse.Kick.Hit && pulse.Kick.Strength >= 0.6f,
            2 => pulse.Drop,
            _ => pulse.Kick.Hit,
        };
        if (falls)
        {
            // In the middle, anywhere, or across the stage by the pitch: bass to the left, treble to the right.
            var (x, y) = context.Pick("where") switch
            {
                1 => (w * (0.15 + (0.7 * context.Random.NextDouble())), h * (0.15 + (0.7 * context.Random.NextDouble()))),
                2 => (w * (0.1 + (0.8 * pulse.Pitch)), h * (0.35 + (0.3 * context.Random.NextDouble()))),
                _ => (w / 2.0, h / 2.0),
            };
            _drops.Add(new Drop(x, y, pulse.Pitch, 0.4 + (0.6 * pulse.Kick.Strength)));
        }

        var rings = (int)context.Number("rings");
        var thickness = context.Number("thickness") * size / 360;
        foreach (var drop in _drops)
        {
            drop.Age += context.Dt;
            var share = drop.Age / lifetime;
            var color = context.Paint(drop.Pitch);
            for (var ring = 0; ring < rings; ring++)
            {
                var radius = (drop.Age * size * 0.42) - (ring * size * 0.05);
                if (radius > 0)
                {
                    var light = (float)(drop.Strength * Math.Pow(1 - share, 1.5) * (1 - (ring * 0.25)));
                    canvas.Ring(drop.X, drop.Y, radius, thickness * (1 + (ring * 0.4)), color, Math.Max(0, light));
                }
            }

            canvas.Glow(drop.X, drop.Y, size * 0.06 * (1 - Math.Min(1, share * 3)), color, (float)drop.Strength);
        }

        _drops.RemoveAll(drop => drop.Age >= lifetime);
        canvas.Bloom(0.35f, 0.8f, 4);
    }

    private sealed class Drop(double x, double y, double pitch, double strength)
    {
        public double X { get; } = x;

        public double Y { get; } = y;

        public double Pitch { get; } = pitch;

        public double Strength { get; } = strength;

        public double Age { get; set; }
    }
}

