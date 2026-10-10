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

        // The water itself, lit from below, brightening with the bass.
        canvas.Glow(w / 2.0, h / 2.0, Math.Max(w, h) * 0.7, context.Paint(0.2), 0.08f + (0.35f * pulse.Bass));
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
                1 => (w * (0.15 + (0.7 * Noise.Hash(pulse.Kick.Count * 2))), h * (0.15 + (0.7 * Noise.Hash((pulse.Kick.Count * 2) + 1)))),
                2 => (w * (0.1 + (0.8 * pulse.Pitch)), h * (0.35 + (0.3 * Noise.Hash(pulse.Kick.Count)))),
                _ => (w / 2.0, h / 2.0),
            };
            _drops.Add(new Drop(x, y, pulse.Pitch, 0.4 + (0.6 * pulse.Kick.Strength)));
        }

        // Each hi-hat lands a droplet, small and faint, somewhere the count of hats says.
        if (pulse.Hat.Hit)
        {
            _drops.Add(new Drop(w * (0.1 + (0.8 * Noise.Hash(pulse.Hat.Count * 3))), h * (0.1 + (0.8 * Noise.Hash((pulse.Hat.Count * 3) + 1))), 0.8, 0.25));
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

