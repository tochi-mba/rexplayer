namespace Rex.Media.Vision;

/// <summary>
/// Smooths a value that arrives frame by frame, the way a tracked point should feel: steady when it
/// is still (the finder's jitter is filtered out) and immediate when it moves (the cutoff rises with
/// speed, so a fast hand is never dragged behind). Casiez, Roussel and Vogel, "1 euro filter", CHI 2012.
/// </summary>
public sealed class OneEuroFilter
{
    private float _value;
    private float _speed;
    private bool _started;

    /// <param name="minCutoff">The cutoff in hertz at rest: lower is steadier but lags more.</param>
    /// <param name="beta">How much the cutoff rises with speed: higher follows fast motion more closely.</param>
    /// <param name="speedCutoff">The cutoff of the speed estimate itself, in hertz.</param>
    public OneEuroFilter(float minCutoff = 1f, float beta = 0.5f, float speedCutoff = 1f)
    {
        if (!(minCutoff > 0) || !(beta >= 0) || !(speedCutoff > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(minCutoff), "The cutoffs are positive and beta is not negative.");
        }

        (MinCutoff, Beta, SpeedCutoff) = (minCutoff, beta, speedCutoff);
    }

    public float MinCutoff { get; }

    public float Beta { get; }

    public float SpeedCutoff { get; }

    /// <summary>The smoothed value after the last <see cref="Filter"/>; zero before any.</summary>
    public float Value => _value;

    /// <summary>Takes the next raw <paramref name="value"/>, <paramref name="dt"/> seconds after the last, and gives the smoothed one.</summary>
    public float Filter(float value, float dt)
    {
        if (!float.IsFinite(value))
        {
            return _value;
        }

        if (!_started || !(dt > 0) || !float.IsFinite(dt))
        {
            (_value, _speed, _started) = (value, 0, true);
            return value;
        }

        var rawSpeed = (value - _value) / dt;
        _speed += Alpha(SpeedCutoff, dt) * (rawSpeed - _speed);
        var cutoff = MinCutoff + (Beta * MathF.Abs(_speed));
        _value += Alpha(cutoff, dt) * (value - _value);
        return _value;
    }

    /// <summary>Starts again from the next value, as when a hand is lost and found.</summary>
    public void Reset() => (_value, _speed, _started) = (0, 0, false);

    /// <summary>How much of a new sample a first-order low-pass at <paramref name="cutoff"/> hertz takes in <paramref name="dt"/> seconds.</summary>
    public static float Alpha(float cutoff, float dt)
    {
        var tau = 1 / (2 * MathF.PI * cutoff);
        return 1 / (1 + (tau / dt));
    }
}

/// <summary>
/// Smooths every point of a hand or a body with a <see cref="OneEuroFilter"/> per coordinate, and
/// starts afresh when the part is lost and found, so an old position never bleeds into a new one.
/// </summary>
public sealed class LandmarkSmoother
{
    private readonly OneEuroFilter[] _x;
    private readonly OneEuroFilter[] _y;
    private readonly OneEuroFilter[] _z;
    private bool _tracking;

    public LandmarkSmoother(int points, float minCutoff = 1.2f, float beta = 20f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);
        _x = new OneEuroFilter[points];
        _y = new OneEuroFilter[points];
        _z = new OneEuroFilter[points];
        for (var i = 0; i < points; i++)
        {
            (_x[i], _y[i], _z[i]) = (new OneEuroFilter(minCutoff, beta), new OneEuroFilter(minCutoff, beta), new OneEuroFilter(minCutoff, beta));
        }
    }

    public int Points => _x.Length;

    /// <summary>
    /// Smooths <paramref name="landmarks"/> in place, <paramref name="dt"/> seconds after the last
    /// frame; <paramref name="present"/> false forgets the part, and the next present frame starts clean.
    /// </summary>
    public void Smooth(Landmark[] landmarks, bool present, float dt)
    {
        ArgumentNullException.ThrowIfNull(landmarks);
        if (landmarks.Length != Points)
        {
            throw new ArgumentException("The smoother was made for a part with a different number of points.", nameof(landmarks));
        }

        if (!present)
        {
            if (_tracking)
            {
                Reset();
            }

            return;
        }

        _tracking = true;
        for (var i = 0; i < landmarks.Length; i++)
        {
            var point = landmarks[i];
            landmarks[i] = point with { X = _x[i].Filter(point.X, dt), Y = _y[i].Filter(point.Y, dt), Z = _z[i].Filter(point.Z, dt) };
        }
    }

    public void Reset()
    {
        _tracking = false;
        for (var i = 0; i < _x.Length; i++)
        {
            _x[i].Reset();
            _y[i].Reset();
            _z[i].Reset();
        }
    }
}
