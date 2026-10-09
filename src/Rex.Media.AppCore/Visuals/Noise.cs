namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Smooth noise for the visualisations (AU-18): values that wander between 0 and 1 without
/// repeating, the same for the same place every time, for curtains, ridges and flicker that look
/// natural rather than regular.
/// </summary>
public static class Noise
{
    /// <summary>Value noise along a line: a random level at each whole number, eased between them.</summary>
    public static double At(double x)
    {
        if (!double.IsFinite(x))
        {
            return 0;
        }

        var cell = Math.Floor(x);
        var t = x - cell;
        var ease = t * t * (3 - (2 * t));
        return Lerp(Hash((long)cell), Hash((long)cell + 1), ease);
    }

    /// <summary>Layers of <see cref="At"/>, each twice as fine and half as strong: detail on detail, still 0 to 1.</summary>
    public static double Fractal(double x, int octaves = 4)
    {
        double sum = 0, weight = 1, total = 0, scale = 1;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += At((x * scale) + (octave * 17.3)) * weight;
            total += weight;
            weight *= 0.5;
            scale *= 2;
        }

        return sum / total;
    }

    /// <summary>A repeatable random number from 0 to 1 for a whole number.</summary>
    public static double Hash(long n)
    {
        unchecked
        {
            var h = (ulong)n * 0x9E3779B97F4A7C15UL;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 29;
            return (h >> 11) * (1.0 / (1UL << 53));
        }
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
