namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The values of the last few seconds (AU-18), however many frames a second bring them, and their
/// median and spread: the moving-median threshold an onset must rise above, as in J. P. Bello et
/// al., "A tutorial on onset detection in music signals", IEEE Transactions on Speech and Audio
/// Processing 13(5), 2005, section IV-B. Kept by time rather than by count, so the threshold means
/// the same at twenty-four frames a second as at a hundred and forty-four.
/// </summary>
public sealed class MovingMedian(double seconds)
{
    /// <summary>The most values kept, however fast they come, so measuring them stays cheap.</summary>
    public const int Most = 1024;

    private readonly Queue<(double At, double Value)> _values = new();
    private double[] _scratch = new double[64];
    private double _now;

    /// <summary>Values kept.</summary>
    public int Count => _values.Count;

    /// <summary>The seconds from the oldest value kept to the newest.</summary>
    public double Span => _values.Count == 0 ? 0 : _now - _values.Peek().At;

    /// <summary>Takes <paramref name="value"/>, <paramref name="dt"/> seconds after the last, and lets go of those older than the span.</summary>
    public void Add(double value, double dt)
    {
        _now += double.IsFinite(dt) ? Math.Max(0, dt) : 0;
        _values.Enqueue((_now, value));
        while (_values.Count > Most || _now - _values.Peek().At > seconds + 1e-9)
        {
            _values.Dequeue();
        }
    }

    /// <summary>
    /// The median of the values kept and their spread about it (the median absolute difference,
    /// scaled to match a standard deviation); (0, 0) when there are none.
    /// </summary>
    public (double Median, double Spread) Measure()
    {
        var count = _values.Count;
        if (count == 0)
        {
            return (0, 0);
        }

        if (_scratch.Length < count)
        {
            _scratch = new double[Math.Max(count, _scratch.Length * 2)];
        }

        var at = 0;
        foreach (var (_, value) in _values)
        {
            _scratch[at++] = value;
        }

        var values = _scratch.AsSpan(0, count);
        var median = Median(values);
        for (var i = 0; i < count; i++)
        {
            values[i] = Math.Abs(values[i] - median);
        }

        return (median, 1.4826 * Median(values));
    }

    /// <summary>Forgets every value.</summary>
    public void Clear()
    {
        _values.Clear();
        _now = 0;
    }

    private static double Median(Span<double> values)
    {
        values.Sort();
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
