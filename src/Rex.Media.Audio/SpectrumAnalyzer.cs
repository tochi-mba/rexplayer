namespace Rex.Media.Audio;

/// <summary>
/// Turns sound into bars for a spectrum visualisation (AU-18): bands spaced as hearing spaces
/// pitch, each from silence (0) to full scale (1) on a decibel scale. Bars rise at once and fall
/// smoothly; a peak mark above each holds the recent highest and drifts down.
/// </summary>
public sealed class SpectrumAnalyzer
{
    /// <summary>Samples analysed at a time: about 43 ms at 48 kHz, fine enough to tell bass notes apart.</summary>
    public const int Size = 2048;

    /// <summary>How fast a bar falls, in heights per second.</summary>
    private const float BarFall = 1.5f;

    /// <summary>How fast a peak mark falls, in heights per second.</summary>
    private const float PeakFall = 0.4f;

    private readonly float[] _levels;
    private readonly float[] _peaks;
    private readonly float[] _magnitudes = new float[Size / 2];
    private readonly double _low;
    private readonly double _high;
    private readonly double _floor;

    /// <param name="bands">How many bars.</param>
    /// <param name="lowHz">The lowest frequency shown.</param>
    /// <param name="highHz">The highest frequency shown.</param>
    /// <param name="floorDb">The level, in decibels below full scale, drawn as nothing.</param>
    public SpectrumAnalyzer(int bands, double lowHz = 40, double highHz = 16000, double floorDb = -60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bands, 1);
        if (!(lowHz > 0 && highHz > lowHz && floorDb < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(lowHz), "The range runs from a low frequency to a higher one, down to a floor below 0 dB.");
        }

        (_levels, _peaks, _low, _high, _floor) = (new float[bands], new float[bands], lowHz, highHz, floorDb);
    }

    /// <summary>Each bar's height, lowest band first.</summary>
    public IReadOnlyList<float> Levels => _levels;

    /// <summary>Each bar's peak mark.</summary>
    public IReadOnlyList<float> Peaks => _peaks;

    /// <summary>
    /// Analyses the newest <see cref="Size"/> samples of the sound at <paramref name="sampleRate"/>,
    /// <paramref name="elapsed"/> after the last time, which sets how far bars and peaks have fallen.
    /// </summary>
    public void Update(ReadOnlySpan<float> samples, int sampleRate, TimeSpan elapsed)
    {
        if (samples.Length != Size)
        {
            throw new ArgumentException($"Analyse {Size} samples at a time.", nameof(samples));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        Fft.Magnitudes(samples, _magnitudes);
        var seconds = (float)Math.Max(0, elapsed.TotalSeconds);
        var lastBin = _magnitudes.Length - 1;
        for (var band = 0; band < _levels.Length; band++)
        {
            // Bands share the range by ratio, as octaves do; each covers at least one bin.
            var from = Bin(_low * Math.Pow(_high / _low, (double)band / _levels.Length), sampleRate, lastBin);
            var to = Math.Max(from, Bin(_low * Math.Pow(_high / _low, (double)(band + 1) / _levels.Length), sampleRate, lastBin) - 1);
            var strongest = 0f;
            for (var bin = from; bin <= to; bin++)
            {
                strongest = Math.Max(strongest, _magnitudes[bin]);
            }

            var level = Level(strongest);
            _levels[band] = Math.Max(level, _levels[band] - (BarFall * seconds));
            _peaks[band] = level >= _peaks[band] ? level : Math.Max(0, _peaks[band] - (PeakFall * seconds));
        }
    }

    /// <summary>Drops every bar and peak, as when playback stops.</summary>
    public void Reset()
    {
        Array.Clear(_levels);
        Array.Clear(_peaks);
    }

    private static int Bin(double hertz, int sampleRate, int lastBin) => (int)Math.Clamp(Math.Round(hertz * Size / sampleRate), 1, lastBin);

    private float Level(float magnitude)
    {
        var decibels = 20 * Math.Log10(Math.Max(magnitude, 1e-9));
        return (float)Math.Clamp((decibels - _floor) / -_floor, 0, 1);
    }
}
