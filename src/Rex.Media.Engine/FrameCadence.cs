namespace Rex.Media.Engine;

/// <summary>
/// How steadily the newest pictures reached the screen while playing in real time: the time between
/// one picture being presented and the next, and how late each was against its own time. Over the
/// newest <see cref="Window"/> pictures it gives the mean, the spread (jitter, the standard
/// deviation) and the worst of each. The window lives in arrays made once, so recording a picture
/// allocates nothing; a gap of more than four pictures (a pause, a seek, a stall) is not counted as
/// an interval, only the lateness of the picture after it.
/// </summary>
public sealed class FrameCadence
{
    /// <summary>How many of the newest pictures the figures cover: ten seconds at sixty a second.</summary>
    public const int Window = 600;

    private readonly double[] _intervals = new double[Window];
    private readonly double[] _lateness = new double[Window];
    private readonly object _gate = new();
    private int _next;
    private int _count;
    private double? _lastPresented;

    /// <summary>
    /// A picture was presented at <paramref name="presentedMs"/> (any steady clock, in
    /// milliseconds), <paramref name="latenessMs"/> after its own time, and is
    /// <paramref name="lengthMs"/> long.
    /// </summary>
    public void Presented(double presentedMs, double latenessMs, double lengthMs)
    {
        lock (_gate)
        {
            var interval = _lastPresented is { } last && presentedMs - last is var gap && gap >= 0 && gap <= 4 * Math.Max(lengthMs, 1)
                ? gap
                : double.NaN;
            _lastPresented = presentedMs;
            _intervals[_next] = interval;
            _lateness[_next] = latenessMs;
            _next = (_next + 1) % Window;
            _count = Math.Min(Window, _count + 1);
        }
    }

    /// <summary>The next picture starts afresh: shown at once (paused, first after a seek) or after a jump in time.</summary>
    public void Restart()
    {
        lock (_gate)
        {
            _lastPresented = null;
        }
    }

    /// <summary>Forgets every picture recorded, as when a measurement starts.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            (_next, _count, _lastPresented) = (0, 0, null);
        }
    }

    /// <summary>The figures for the pictures in the window now.</summary>
    public FrameCadenceStats Snapshot()
    {
        lock (_gate)
        {
            var (intervalMean, intervalJitter, intervalWorst, intervals) = Spread(_intervals, _count);
            var (latenessMean, latenessJitter, latenessWorst, _) = Spread(_lateness, _count);
            return new FrameCadenceStats(_count, intervals, intervalMean, intervalJitter, intervalWorst, latenessMean, latenessJitter, latenessWorst);
        }
    }

    /// <summary>The mean, standard deviation and largest of the first <paramref name="count"/> values that are numbers, and how many there were.</summary>
    private static (double Mean, double Deviation, double Worst, int Count) Spread(double[] values, int count)
    {
        double sum = 0, squares = 0, worst = 0;
        var counted = 0;
        for (var i = 0; i < count; i++)
        {
            var value = values[i];
            if (double.IsNaN(value))
            {
                continue;
            }

            counted++;
            sum += value;
            squares += value * value;
            worst = counted == 1 ? value : Math.Max(worst, value);
        }

        if (counted == 0)
        {
            return (0, 0, 0, 0);
        }

        var mean = sum / counted;
        return (mean, Math.Sqrt(Math.Max(0, (squares / counted) - (mean * mean))), worst, counted);
    }
}

/// <summary>
/// How steadily pictures were presented (see <see cref="FrameCadence"/>): over
/// <paramref name="Pictures"/> pictures, of which <paramref name="Intervals"/> followed the one
/// before closely enough to time the gap, each figure in milliseconds.
/// </summary>
public readonly record struct FrameCadenceStats(
    int Pictures,
    int Intervals,
    double IntervalMeanMs,
    double IntervalJitterMs,
    double IntervalWorstMs,
    double LatenessMeanMs,
    double LatenessJitterMs,
    double LatenessWorstMs);
