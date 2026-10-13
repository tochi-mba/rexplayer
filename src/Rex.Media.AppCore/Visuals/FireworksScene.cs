namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A fireworks show fired to the music (AU-18): each kick launches a shell, which bursts at the top
/// of its climb or, sooner, on a snare; the burst is as big as the kick was hard and coloured by the
/// pitch of the moment; hi-hats set the falling stars crackling; the drop fires a volley.
/// </summary>
public sealed class FireworksScene : VisualScene
{
    private readonly Particles _stars = new(1600);
    private readonly List<Shell> _shells = [];
    private float _crackle;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var gravity = context.Number("gravity") * size * 0.35;
        canvas.Feedback((float)context.Number("trails"), 1, 0, 0, size * 0.002);

        var launches = pulse.Drop ? 6 : pulse.Kick.Hit ? 1 : 0;
        for (var n = 0; n < launches; n++)
        {
            // Up fast enough to burst in the upper third.
            var climb = Math.Sqrt(2 * gravity * h * (0.55 + (0.25 * random.NextDouble())));
            _shells.Add(new Shell(w * (0.15 + (0.7 * random.NextDouble())), h, (random.NextDouble() - 0.5) * size * 0.1, -climb, 0.5 + (0.5 * pulse.Kick.Strength), pulse.Pitch));
        }

        var burst = pulse.Snare.Hit;
        foreach (var shell in _shells)
        {
            shell.Vy += gravity * context.Dt;
            (shell.X, shell.Y) = (shell.X + (shell.Vx * context.Dt), shell.Y + (shell.Vy * context.Dt));
            canvas.Glow(shell.X, shell.Y, size * 0.012, context.Paint(shell.Pitch), 0.9f);
            if (shell.Vy >= 0 || (burst && shell.Y < h * 0.6))
            {
                Burst(shell, size, random);
                shell.Done = true;
            }
        }

        _shells.RemoveAll(shell => shell.Done);
        _crackle = pulse.Hat.Hit ? 1 : Math.Max(0, _crackle - (float)(context.Dt * 5));
        _stars.Step(context.Dt, 0, gravity * 0.25, 1.4);
        _stars.Draw(context, 0.7f + (0.6f * _crackle) + (0.4f * pulse.Punch), 0.03);
        canvas.Bloom(0.35f, 0.9f, 5);
    }

    /// <summary>A shell's burst: a sphere of stars, as many and as fast as the kick that sent it was hard.</summary>
    private void Burst(Shell shell, double size, Random random)
    {
        var count = (int)(60 + (90 * shell.Power));
        for (var n = 0; n < count; n++)
        {
            var angle = random.NextDouble() * Math.Tau;
            var speed = size * shell.Power * (0.35 + (0.25 * random.NextDouble()));
            _stars.Add(shell.X, shell.Y, (Math.Cos(angle) * speed) + shell.Vx, Math.Sin(angle) * speed, size * 0.006, Math.Clamp(shell.Pitch + ((random.NextDouble() - 0.5) * 0.15), 0, 1), 1.3 + random.NextDouble());
        }
    }

    private sealed class Shell(double x, double y, double vx, double vy, double power, float pitch)
    {
        public double X { get; set; } = x;

        public double Y { get; set; } = y;

        public double Vx { get; } = vx;

        public double Vy { get; set; } = vy;

        public double Power { get; } = power;

        public float Pitch { get; } = pitch;

        public bool Done { get; set; }
    }
}
