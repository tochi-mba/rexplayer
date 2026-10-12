namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A level in decibels mapped onto 0 to 1 between the song's own recent quiet and its recent loud
/// (AU-18), so a quiet recording and a loud one both move a visualisation over its whole range.
/// <para>
/// The floor falls at once to anything quieter and creeps back up toward the level over about
/// <c>floorSeconds</c>; the ceiling rises at once to anything louder and sinks back over about
/// <c>ceilingSeconds</c>. The two are never closer than <c>span</c> decibels, so a steady sound
/// does not swing from nothing to everything. Below <see cref="Gate"/> is silence: 0, whatever came before.
/// </para>
/// </summary>
public sealed class AdaptiveRange(double span = 10, double floorSeconds = 1.5, double ceilingSeconds = 3, double gamma = 0.8)
{
    /// <summary>The level, in decibels below full scale, under which everything is silence.</summary>
    public const double Gate = -75;

    private double _floor = double.NaN;
    private double _ceiling = double.NaN;

    /// <summary>The latest value, 0 to 1.</summary>
    public float Value { get; private set; }

    /// <summary>The recent quiet, in decibels (not a number before any sound).</summary>
    public double Floor => _floor;

    /// <summary>The recent loud, in decibels (not a number before any sound).</summary>
    public double Ceiling => _ceiling;

    /// <summary>Takes the level now, <paramref name="decibels"/>, <paramref name="dt"/> seconds after the last, and gives its place in the range.</summary>
    public float Map(double decibels, double dt)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.5) : 0;
        if (!double.IsFinite(decibels) || decibels < Gate)
        {
            // Silence: the ceiling sinks as it would, and the floor waits for the music to come back.
            if (!double.IsNaN(_ceiling))
            {
                _ceiling = Math.Max(Gate + span, _ceiling - ((_ceiling - Gate) * (1 - Math.Exp(-dt / ceilingSeconds))));
            }

            return Value = 0;
        }

        if (double.IsNaN(_floor))
        {
            (_floor, _ceiling) = (decibels - span, decibels);
        }

        // Track new quiet immediately, but do not creep the floor into the quietest
        // observed level: a steady note still needs its full span below it.
        _floor = decibels < _floor ? decibels : _floor + (Math.Max(0, decibels - span - _floor) * (1 - Math.Exp(-dt / floorSeconds)));
        _ceiling = decibels > _ceiling ? decibels : _ceiling + ((decibels - _ceiling) * (1 - Math.Exp(-dt / ceilingSeconds)));
        _floor = Math.Max(Gate, _floor);
        _ceiling = Math.Max(_ceiling, _floor + span);
        var share = Math.Clamp((decibels - _floor) / (_ceiling - _floor), 0, 1);
        return Value = (float)Math.Pow(share, gamma);
    }

    /// <summary>Forgets the range, as when another song starts.</summary>
    public void Reset() => (_floor, _ceiling, Value) = (double.NaN, double.NaN, 0);

    /// <summary>Power (a squared amplitude) in decibels; silence is far below <see cref="Gate"/>.</summary>
    public static double Decibels(double power) => 10 * Math.Log10(Math.Max(power, 1e-12));
}
