namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The spectrum as rays of light round a ring that swells with the bass (AU-18), mirrored left and
/// right with the bass at the top, leaving trails that drift outward like a tunnel. Every kick
/// thumps the whole halo outward and throws sparks off the ring; the ring burns with the punch, and
/// turning, it steps round on each beat.
/// </summary>
public sealed class HaloScene : VisualScene
{
    private readonly Particles _sparks = new(240);
    private double _spin;
    private double _spinTo;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var size = Math.Min(canvas.Width, canvas.Height);
        var (cx, cy) = (canvas.Width / 2.0, canvas.Height / 2.0);
        if (context.Toggle("spin"))
        {
            // A step round on each beat, eased in, over a slow drift.
            _spinTo += (context.Dt * 0.04) + (pulse.Beat ? 0.09 * (pulse.Filled ? 0.5 : 1) : 0);
            _spin += (_spinTo - _spin) * (1 - Math.Exp(-context.Dt / 0.08));
        }

        // The kick thumps it all outward; the tunnel of trails rushes with it.
        var thump = 1 + (0.1 * pulse.Punch);
        context.Trail(context.Number("trails"), 1.003 + (pulse.Punch * 0.025), context.Toggle("spin") ? 0.002 : 0, 0, 0);
        var inner = size * (0.13 + (pulse.Bass * 0.1)) * thump;
        var reach = size * 0.32 * thump;
        var rays = (int)context.Number("rays");
        var thickness = context.Number("thickness") * size / 360;
        var half = rays / 2;
        for (var i = 0; i < rays; i++)
        {
            // Mirrored: both halves run from the bass at the top to the treble at the bottom.
            var k = i < half ? i : rays - 1 - i;
            var band = Math.Min(pulse.Bands.Count - 1, k * pulse.Bands.Count / Math.Max(1, half));
            var level = pulse.Bands[band];
            var angle = ((double)i / rays * Math.Tau) - (Math.PI / 2) + _spin;
            var length = Math.Max(1.5, level * reach);
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            canvas.Line(cx + (inner * cos), cy + (inner * sin), cx + ((inner + length) * cos), cy + ((inner + length) * sin), thickness, context.Paint((double)k / half), (0.35f + (1.1f * level)) * (1 + (0.5f * pulse.Build)), 1.2);
        }

        canvas.Ring(cx, cy, inner * 0.94, (1.5 + (pulse.Punch * 9)) * size / 360, context.Paint(pulse.Pitch), 0.3f + (0.4f * pulse.Bass) + (1.3f * pulse.Punch));
        // The kick throws sparks off the ring, as many as it is hard.
        if ((pulse.Kick.Hit || pulse.Drop) && context.Toggle("burst"))
        {
            var count = pulse.Drop ? 120 : (int)(16 + (40 * pulse.Kick.Strength));
            for (var n = 0; n < count; n++)
            {
                var angle = context.Random.NextDouble() * Math.Tau;
                var speed = size * (0.4 + (context.Random.NextDouble() * 0.8));
                _sparks.Add(cx + (inner * Math.Cos(angle)), cy + (inner * Math.Sin(angle)), speed * Math.Cos(angle), speed * Math.Sin(angle), size * 0.018, context.Random.NextDouble(), 0.9);
            }
        }

        _sparks.Step(context.Dt, 0, 0, 1.6);
        _sparks.Draw(context, 0.9f, 0.035);
        canvas.Bloom(0.45f, 0.8f, 5);
    }
}

