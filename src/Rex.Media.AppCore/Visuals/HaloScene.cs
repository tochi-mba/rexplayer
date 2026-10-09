namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The spectrum as rays of light round a ring that swells with the bass (AU-18), mirrored left and
/// right with the bass at the top, leaving trails that drift outward like a tunnel; every beat
/// throws sparks off the ring.
/// </summary>
public sealed class HaloScene : VisualScene
{
    private readonly Particles _sparks = new(240);
    private double _spin;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var size = Math.Min(canvas.Width, canvas.Height);
        var (cx, cy) = (canvas.Width / 2.0, canvas.Height / 2.0);
        if (context.Toggle("spin"))
        {
            _spin += context.Dt * (0.15 + (pulse.Bass * 0.6));
        }

        canvas.Feedback((float)context.Number("trails"), 1.012 + (pulse.Bass * 0.025), context.Toggle("spin") ? 0.004 : 0, 0, 0);
        var inner = size * (0.16 + (pulse.Bass * 0.07));
        var reach = size * 0.3;
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
            canvas.Line(cx + (inner * cos), cy + (inner * sin), cx + ((inner + length) * cos), cy + ((inner + length) * sin), thickness, context.Paint((double)k / half), 0.55f + level, 1.2);
        }

        canvas.Ring(cx, cy, inner * 0.94, 1.5 + (pulse.Bass * 5), context.Paint(pulse.Pitch), 0.5f + pulse.Bass);
        if (pulse.Beat && context.Toggle("burst"))
        {
            var count = (int)(12 + (28 * pulse.BeatStrength));
            for (var n = 0; n < count; n++)
            {
                var angle = context.Random.NextDouble() * Math.Tau;
                var speed = size * (0.4 + (context.Random.NextDouble() * 0.8));
                _sparks.Add(cx + (inner * Math.Cos(angle)), cy + (inner * Math.Sin(angle)), speed * Math.Cos(angle), speed * Math.Sin(angle), size * 0.018, context.Random.NextDouble(), 0.9);
            }
        }

        _sparks.Step(context.Dt, 0, 0, 1.6);
        _sparks.Draw(context, 0.9f);
        canvas.Bloom(0.45f, 0.8f, 5);
    }
}

