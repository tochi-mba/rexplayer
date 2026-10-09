namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Embers rising from a fire (AU-18): more of them and faster the louder the music, swirling as they
/// climb and leaving smoke behind, a glow at the base that breathes with the bass, and a burst
/// thrown up on every beat.
/// </summary>
public sealed class EmbersScene : VisualScene
{
    private readonly Particles _embers = new(600);
    private double _owed;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var pulse = context.Pulse;
        var speed = context.Number("speed");
        var wind = context.Number("wind");
        var size = Math.Min(w, h);
        canvas.Feedback((float)context.Number("smoke"), 1.0, 0, wind * 0.6, -1.2 * speed);

        // A steady stream that thickens with the loudness, and a burst on each beat.
        var amount = context.Number("amount");
        _owed += context.Dt * amount * (0.2 + (2.5 * pulse.Loudness * pulse.Loudness));
        while (_owed >= 1)
        {
            _owed--;
            Spawn(context, w, h, size, speed, 1);
        }

        if (pulse.Beat && context.Toggle("burst"))
        {
            for (var n = 0; n < (int)(amount * 0.25 * pulse.BeatStrength); n++)
            {
                Spawn(context, w, h, size, speed, 1.8);
            }
        }

        var t = context.Seconds;
        _embers.Step(context.Dt, wind * size * 0.2, -size * 0.15 * speed, 0.6, (x, y) => Math.Sin((y * 0.025) + (t * 1.7) + (x * 0.01)) * size * 0.5);
        canvas.Glow(w / 2.0, h + (size * 0.1), size * (0.55 + (0.35 * pulse.Bass)), context.Paint(0.05), 0.25f + pulse.Bass);
        _embers.Draw(context, 0.85f);
        canvas.Bloom(0.4f, 0.9f, 5);
    }

    private void Spawn(VisualContext context, int w, int h, double size, double speed, double kick)
    {
        var random = context.Random;
        var x = w * (0.5 + ((random.NextDouble() - 0.5) * (0.4 + (0.5 * random.NextDouble()))));
        var rise = size * (0.3 + (0.7 * random.NextDouble())) * speed * kick;
        _embers.Add(x, h + 2, (random.NextDouble() - 0.5) * size * 0.3, -rise, size * (0.008 + (0.012 * random.NextDouble())), random.NextDouble() * 0.35, 2.4 + random.NextDouble());
    }
}

