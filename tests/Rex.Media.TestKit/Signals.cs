namespace Rex.Media.TestKit;

/// <summary>Test signals and the measurements that check them.</summary>
public static class Signals
{
    /// <summary>A sine wave of <paramref name="count"/> samples.</summary>
    public static float[] Sine(double frequency, int sampleRate, int count, double amplitude = 0.5, double phase = 0)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin((2 * Math.PI * frequency * i / sampleRate) + phase));
        }

        return samples;
    }

    /// <summary>
    /// The amplitude of one frequency component, measured with the Goertzel algorithm over a Hann
    /// window. A full-scale sine at <paramref name="frequency"/> reads about its amplitude.
    /// </summary>
    public static double Amplitude(ReadOnlySpan<float> samples, int sampleRate, double frequency)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
        double previous = 0;
        double beforePrevious = 0;
        double windowSum = 0;
        var n = samples.Length;
        for (var i = 0; i < n; i++)
        {
            var window = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, n - 1)));
            windowSum += window;
            var current = (samples[i] * window) + (coefficient * previous) - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = (previous * previous) + (beforePrevious * beforePrevious) - (coefficient * previous * beforePrevious);
        return 2 * Math.Sqrt(Math.Max(0, power)) / windowSum;
    }

    /// <summary>Root-mean-square level.</summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * (double)sample;
        }

        return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
    }

    /// <summary>The largest absolute sample.</summary>
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }
}
