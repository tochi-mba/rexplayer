using System.Globalization;

namespace Rex.Media.Primitives;

/// <summary>
/// An exact fraction, used for container timebases (1/90000, 1001/30000) and aspect ratios.
/// Timestamps are converted between timebases through <see cref="Rescale"/>, which works in 128-bit
/// arithmetic so a 90 kHz clock running for days never overflows on its way to ticks.
/// </summary>
public readonly record struct Rational
{
    public Rational(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        // The denominator is positive here, so the divisor is at least 1.
        var divisor = GreatestCommonDivisor(Math.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public long Numerator { get; }

    public long Denominator { get; }

    /// <summary>The value as a double, for display and for code that measures rather than counts.</summary>
    public double Value => Numerator / (double)Denominator;

    /// <summary>
    /// <paramref name="value"/> counted in units of <paramref name="from"/>, expressed in units of
    /// <paramref name="to"/>, rounded to the nearest unit with halves away from zero.
    /// </summary>
    public static long Rescale(long value, Rational from, Rational to)
    {
        ArgumentOutOfRangeException.ThrowIfZero(from.Denominator);
        ArgumentOutOfRangeException.ThrowIfZero(to.Numerator);
        var numerator = (Int128)value * from.Numerator * to.Denominator;
        var denominator = (Int128)from.Denominator * to.Numerator;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        if (Int128.Abs(remainder) * 2 >= denominator)
        {
            quotient += numerator < 0 ? -1 : 1;
        }

        return checked((long)quotient);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
