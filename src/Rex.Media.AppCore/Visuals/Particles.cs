namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Sparks for the visualisations (AU-18): each has a place, a speed, a life that runs from 1 down
/// to 0, a size and a colour share. Spent sparks are reused, so a burst never allocates.
/// </summary>
public sealed class Particles
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _vx;
    private readonly double[] _vy;
    private readonly double[] _life;
    private readonly double[] _size;
    private readonly double[] _share;
    private readonly double[] _fade;
    private int _next;

    public Particles(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        (_x, _y, _vx, _vy) = (new double[capacity], new double[capacity], new double[capacity], new double[capacity]);
        (_life, _size, _share, _fade) = (new double[capacity], new double[capacity], new double[capacity], new double[capacity]);
    }

    public int Capacity => _x.Length;

    /// <summary>How many are alive.</summary>
    public int Alive => _life.Count(life => life > 0);

    /// <summary>Starts a spark that lives <paramref name="seconds"/>, taking the place of the oldest when all are alive.</summary>
    public void Add(double x, double y, double vx, double vy, double size, double share, double seconds)
    {
        var i = _next;
        _next = (_next + 1) % _x.Length;
        (_x[i], _y[i], _vx[i], _vy[i], _size[i], _share[i]) = (x, y, vx, vy, size, share);
        _life[i] = 1;
        _fade[i] = 1 / Math.Max(0.05, seconds);
    }

    /// <summary>
    /// Moves every spark on by <paramref name="dt"/>: pulled by (<paramref name="ax"/>, <paramref name="ay"/>),
    /// slowed by <paramref name="drag"/> a second, and pushed sideways by <paramref name="swirl"/>.
    /// </summary>
    public void Step(double dt, double ax, double ay, double drag, Func<double, double, double>? swirl = null)
    {
        var keep = Math.Exp(-drag * dt);
        for (var i = 0; i < _x.Length; i++)
        {
            if (_life[i] <= 0)
            {
                continue;
            }

            _vx[i] = (_vx[i] + (ax + (swirl?.Invoke(_x[i], _y[i]) ?? 0)) * dt) * keep;
            _vy[i] = (_vy[i] + (ay * dt)) * keep;
            _x[i] += _vx[i] * dt;
            _y[i] += _vy[i] * dt;
            _life[i] -= _fade[i] * dt;
        }
    }

    /// <summary>Draws every live spark as a glow, its size and light following its life.</summary>
    public void Draw(VisualContext context, float strength = 1)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (var i = 0; i < _x.Length; i++)
        {
            if (_life[i] > 0)
            {
                var life = Math.Min(1, _life[i]);
                context.Canvas.Glow(_x[i], _y[i], _size[i] * (0.4 + (0.6 * life)), context.Paint(_share[i]), (float)life * strength);
            }
        }
    }

    public void Clear() => Array.Clear(_life);
}

