namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// One drum's hits (AU-18): the rise of its part of the spectrum, taken as a hit when it jumps well
/// above its own recent run (the mean and spread of the last second and a half), no sooner than
/// <c>gap</c> after the last. A hit's strength is its energy against the hardest hits of late, so an
/// accented downbeat is strong and the beats between are softer. Each hit sets <see cref="Level"/>
/// to its strength, which then falls away over <c>decay</c> seconds: the envelope effects ride on,
/// so they move exactly with the drum.
/// </summary>
public sealed class Onset(double gap, double decay)
{
    private const int History = 45;

    private readonly Queue<double> _rises = new();
    private double _since = 10;
    private double _hardest;

    /// <summary>Whether it hit in this frame.</summary>
    public bool Hit { get; private set; }

    /// <summary>How hard the last hit was, 0.25 to 1.</summary>
    public float Strength { get; private set; }

    /// <summary>The hit's envelope: its strength at the hit, falling to 0 over the decay.</summary>
    public float Level { get; private set; }

    /// <summary>Hits so far.</summary>
    public long Count { get; private set; }

    /// <summary>Seconds since the last hit (large before any).</summary>
    public double Since => _since;

    /// <summary>
    /// Takes this frame's <paramref name="rise"/> and <paramref name="energy"/>, <paramref name="dt"/>
    /// after the last; only listens for a hit when <paramref name="listening"/>.
    /// </summary>
    public void Hear(double rise, double energy, double dt, bool listening)
    {
        Hit = false;
        _since += dt;

        // The hardest of late fades over some seconds, so a song that gets quieter keeps its accents.
        _hardest *= Math.Exp(-dt / 6);
        Level = (float)Math.Max(0, Level - (Strength * dt / decay));
        if (listening && _rises.Count >= 4)
        {
            var mean = _rises.Average();
            var spread = Math.Sqrt(_rises.Sum(value => (value - mean) * (value - mean)) / _rises.Count);
            if (rise > mean + Math.Max(1.8 * spread, 0.003) && _since >= gap)
            {
                Hit = true;
                Count++;
                _since = 0;
                _hardest = Math.Max(_hardest, energy);
                Strength = (float)Math.Clamp(_hardest > 0 ? energy / _hardest : 1, 0.25, 1);
                Level = Strength;
            }
        }

        _rises.Enqueue(rise);
        while (_rises.Count > History)
        {
            _rises.Dequeue();
        }
    }

    public void Reset()
    {
        _rises.Clear();
        (Hit, Strength, Level, Count, _since, _hardest) = (false, 0, 0, 0, 10, 0);
    }
}
