namespace Rex.Media.Audio;

/// <summary>The fast Fourier transform, for the spectrum visualisations (AU-18).</summary>
public static class Fft
{
    /// <summary>Transforms the complex signal in <paramref name="real"/> and <paramref name="imaginary"/> in place; the length must be a power of two.</summary>
    public static void Transform(Span<float> real, Span<float> imaginary)
    {
        var n = real.Length;
        if (n != imaginary.Length || n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Both parts must have the same power-of-two length.");
        }

        // Put the samples in bit-reversed order, so each pass combines neighbours.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var (stepReal, stepImaginary) = (Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < n; start += length)
            {
                var (turnReal, turnImaginary) = (1.0, 0.0);
                for (var k = 0; k < length / 2; k++)
                {
                    var (a, b) = (start + k, start + k + (length / 2));
                    var productReal = (real[b] * turnReal) - (imaginary[b] * turnImaginary);
                    var productImaginary = (real[b] * turnImaginary) + (imaginary[b] * turnReal);
                    real[b] = (float)(real[a] - productReal);
                    imaginary[b] = (float)(imaginary[a] - productImaginary);
                    real[a] = (float)(real[a] + productReal);
                    imaginary[a] = (float)(imaginary[a] + productImaginary);
                    (turnReal, turnImaginary) = ((turnReal * stepReal) - (turnImaginary * stepImaginary), (turnReal * stepImaginary) + (turnImaginary * stepReal));
                }
            }
        }
    }

    /// <summary>
    /// The strength of each frequency in <paramref name="samples"/> (a power-of-two count), under a
    /// Hann window and scaled so a full-scale sine wave reads 1: bin k is k × rate / count hertz.
    /// <paramref name="magnitudes"/> takes the first half of the bins.
    /// </summary>
    public static void Magnitudes(ReadOnlySpan<float> samples, Span<float> magnitudes)
    {
        var n = samples.Length;
        if (magnitudes.Length != n / 2)
        {
            throw new ArgumentException("There is one magnitude for each of the first half of the bins.");
        }

        var real = new float[n];
        var imaginary = new float[n];
        for (var i = 0; i < n; i++)
        {
            real[i] = samples[i] * (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / n)));
        }

        Transform(real, imaginary);

        // A Hann window halves a sine's strength, and the transform spreads it over both halves.
        var scale = 4f / n;
        for (var k = 0; k < magnitudes.Length; k++)
        {
            magnitudes[k] = MathF.Sqrt((real[k] * real[k]) + (imaginary[k] * imaginary[k])) * scale;
        }
    }
}
