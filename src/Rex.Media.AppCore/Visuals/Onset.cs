namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// One drum's hits (AU-18): the rise of its band, taken as a hit when it jumps well above its own
/// recent run (the median of the last second and a half, plus three times its spread, and never
/// less than <c>minimum</c>), no sooner than <c>gap</c> after the last. The median, not the mean,
/// because busy percussion makes a few large rises that would otherwise lift the bar over the very
/// hits it is for. A hit's strength is its energy against the hardest hits of late, so an accented
/// downbeat is strong and the beats between are softer. Each hit sets <see cref="Level"/> to its
/// strength, which then falls away over <c>decay</c> seconds: the envelope effects ride on, so they
/// move exactly with the drum.
/// </summary>
public sealed class Onset(double gap, double decay, double minimum = 0.003)
{
    private const int History = 45;

    private readonly Queue<double> _rises = new();
    private readonly double[] _sorted = new double[History];
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
        rise = double.IsFinite(rise) ? rise : 0;

        // The hardest of late fades over some seconds, so a song that gets quieter keeps its accents.
        _hardest *= Math.Exp(-dt / 4);
        Level = (float)Math.Max(0, Level - (Strength * dt / decay));
        if (listening && _rises.Count >= 4)
        {
            var (median, spread) = Robust(_rises, _sorted);
            if (rise > median + Math.Max(3 * spread, minimum) && _since >= gap)
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

    /// <summary>
    /// The median of <paramref name="values"/> and their spread about it (the median absolute
    /// difference, scaled to match a standard deviation), using <paramref name="scratch"/> (at least
    /// as long as there are values) to sort in; (0, 0) when there are none.
    /// </summary>
    public static (double Median, double Spread) Robust(IReadOnlyCollection<double> values, double[] scratch)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(scratch);
        var count = values.Count;
        if (count == 0)
        {
            return (0, 0);
        }

        var at = 0;
        foreach (var value in values)
        {
            scratch[at++] = value;
        }

        var median = Median(scratch.AsSpan(0, count));
        for (var i = 0; i < count; i++)
        {
            scratch[i] = Math.Abs(scratch[i] - median);
        }

        return (median, 1.4826 * Median(scratch.AsSpan(0, count)));
    }

    private static double Median(Span<double> values)
    {
        values.Sort();
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
