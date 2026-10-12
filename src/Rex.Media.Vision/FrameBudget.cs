namespace Rex.Media.Vision;

/// <summary>How a model may be used, decided from how long it takes on this computer.</summary>
public enum ModelUse
{
    /// <summary>Fast enough to run on every picture without slowing playback.</summary>
    PerFrame,

    /// <summary>Only when the user asks (a click, a removal) or when the hand-written tracker needs help.</summary>
    OnDemand,

    /// <summary>So slow that waiting for it would feel broken; the hand-written algorithm is used instead.</summary>
    Unsuitable,
}

/// <summary>
/// Measured timings of one model on one device, and the decision they lead to. The first call is
/// left out because it includes loading and graph optimisation. A model runs per frame only when
/// its slow calls (the 90th percentile of the recent ones) take at most half a frame interval, so
/// decoding and presenting keep the other half.
/// </summary>
public sealed class FrameBudget
{
    /// <summary>How many recent calls the decision looks at.</summary>
    public const int Window = 16;

    /// <summary>The share of a frame interval a per-frame model may take.</summary>
    public const double FrameShare = 0.5;

    /// <summary>The longest wait for an on-demand answer that still feels like a response to a click.</summary>
    public static readonly TimeSpan Interactive = TimeSpan.FromSeconds(2);

    private readonly Queue<double> _milliseconds = new();
    private bool _warm;

    /// <summary>Recorded calls, not counting the first.</summary>
    public int Samples => _milliseconds.Count;

    /// <summary>The 90th percentile of the recent calls in milliseconds, or null before any was recorded.</summary>
    public double? Slow
    {
        get
        {
            if (_milliseconds.Count == 0)
            {
                return null;
            }

            var sorted = _milliseconds.Order().ToArray();
            return sorted[(int)Math.Ceiling(0.9 * sorted.Length) - 1];
        }
    }

    public void Record(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (!_warm)
        {
            _warm = true;
            return;
        }

        _milliseconds.Enqueue(elapsed.TotalMilliseconds);
        if (_milliseconds.Count > Window)
        {
            _milliseconds.Dequeue();
        }
    }

    /// <summary>How the model may be used for video at <paramref name="framesPerSecond"/>. Unmeasured models are on demand.</summary>
    public ModelUse Decide(double framesPerSecond)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), framesPerSecond, "A frame rate is positive.");
        }

        if (Slow is not { } slow)
        {
            return ModelUse.OnDemand;
        }

        if (slow <= 1000 / framesPerSecond * FrameShare)
        {
            return ModelUse.PerFrame;
        }

        return slow <= Interactive.TotalMilliseconds ? ModelUse.OnDemand : ModelUse.Unsuitable;
    }
}
