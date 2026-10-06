using System.Buffers.Binary;

namespace Rex.Media.TestKit;

/// <summary>Reference decodes from an independent decoder, and how far a decode is from one.</summary>
public static class ReferenceAudio
{
    /// <summary>The conformance standard's "full accuracy" RMS limit: 2^-15 / sqrt(12).</summary>
    public static readonly double FullAccuracyRms = Math.Pow(2, -15) / Math.Sqrt(12);

    /// <summary>The conformance standard's "limited accuracy" RMS limit: 2^-11 / sqrt(12).</summary>
    public static readonly double LimitedAccuracyRms = Math.Pow(2, -11) / Math.Sqrt(12);

    /// <summary>Reads a 24-bit PCM WAV into float planes.</summary>
    public static float[][] Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var span = bytes.AsSpan();
        var fmt = span.IndexOf("fmt "u8);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(span[(fmt + 10)..]);
        var data = span.IndexOf("data"u8);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(data + 4)..]);
        var samples = length / 3 / channels;
        var planes = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            planes[c] = new float[samples];
        }

        var at = data + 8;
        for (var i = 0; i < samples; i++)
        {
            for (var c = 0; c < channels; c++, at += 3)
            {
                var value = (bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16)) << 8 >> 8;
                planes[c][i] = value / 8_388_608f;
            }
        }

        return planes;
    }

    /// <summary>
    /// The shift, in samples, that best lines <paramref name="actual"/> up with <paramref name="expected"/>
    /// (positive when actual is late), for saying why two decodes differ.
    /// </summary>
    public static int BestLag(float[] actual, float[] expected, int maxLag)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        var best = 0;
        var bestError = double.MaxValue;
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            double error = 0;
            var count = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                var j = i + lag;
                if (j >= 0 && j < actual.Length)
                {
                    var difference = actual[j] - expected[i];
                    error += difference * difference;
                    count++;
                }
            }

            if (count > expected.Length / 2 && error / count < bestError)
            {
                (best, bestError) = (lag, error / count);
            }
        }

        return best;
    }

    /// <summary>The RMS and peak difference between two planes of the same length.</summary>
    public static (double Rms, double Peak) Difference(float[] actual, float[] expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        double sum = 0;
        double peak = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs((double)actual[i] - expected[i]);
            sum += difference * difference;
            peak = Math.Max(peak, difference);
        }

        return (Math.Sqrt(sum / expected.Length), peak);
    }
}
