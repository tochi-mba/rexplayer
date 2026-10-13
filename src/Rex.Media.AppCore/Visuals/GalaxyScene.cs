namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A spiral galaxy turning to the music (AU-18): its stars lie along log-spiral arms, the core
/// bright with the bass and the rim with the treble, each ring of stars lit by its part of the
/// spectrum; the galaxy turns a step on each beat, the inner stars faster, as galaxies do; a hard
/// kick sets off a supernova somewhere in the arms.
/// </summary>
public sealed class GalaxyScene : VisualScene
{
    private readonly List<(double Radius, double Angle, double Age)> _novas = [];
    private double _turn;
    private double _turnTo;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var (cx, cy) = (w / 2.0, h / 2.0);
        var arms = (int)context.Number("arms");
        var stars = (int)context.Number("stars");
        var spin = context.Number("spin");

        // A step round on each beat, eased in, and a slow drift between.
        _turnTo += (context.Dt * 0.03 * spin) + (pulse.Beat ? 0.06 * spin : 0);
        _turn += (_turnTo - _turn) * (1 - Math.Exp(-context.Dt / 0.12));
        canvas.Feedback(0.4f, 1, 0, 0, 0);
        canvas.Glow(cx, cy, size * (0.06 + (0.06 * pulse.Bass)), context.Paint(0.1), 0.25f + (0.5f * pulse.Bass) + (0.5f * pulse.Punch));

        for (var s = 0; s < stars; s++)
        {
            var reach = Math.Pow(Noise.Hash(s * 5), 0.6);
            var arm = s % arms;
            var scatter = (Noise.Hash((s * 5) + 1) - 0.5) * (0.5 + reach);
            var angle = (arm * Math.Tau / arms) + (Math.Log(1 + (reach * 8)) * 1.8) + scatter - (_turn * (1.6 - reach));
            var radius = reach * size * 0.46;
            var tilt = 0.55;
            var (x, y) = (cx + (Math.Cos(angle) * radius), cy + (Math.Sin(angle) * radius * tilt));
            var band = pulse.Bands[(int)(reach * (pulse.Bands.Count - 1))];
            var light = (float)((0.15 + (0.85 * band)) * (0.4 + (0.6 * Noise.Hash((s * 5) + 2))));
            canvas.Disc(x, y, Math.Max(0.9, size * 0.004), context.Paint(reach), light);
        }

        // A hard kick: a supernova, swelling and fading over a second.
        if (pulse.Kick.Hit && pulse.Kick.Strength > 0.5f)
        {
            _novas.Add((Math.Pow(random.NextDouble(), 0.6) * size * 0.42, random.NextDouble() * Math.Tau, 0));
        }

        for (var n = 0; n < _novas.Count; n++)
        {
            var (radius, angle, age) = _novas[n];
            _novas[n] = (radius, angle, age + context.Dt);
            var a = angle - _turn;
            var swell = Math.Sin(Math.Min(1, age) * Math.PI);
            canvas.Glow(cx + (Math.Cos(a) * radius), cy + (Math.Sin(a) * radius * 0.55), size * (0.02 + (0.06 * swell)), context.Paint(0.9), (float)(1.5 * swell));
        }

        _novas.RemoveAll(nova => nova.Age > 1);
        canvas.Bloom(0.35f, 0.9f, 5);
    }
}
