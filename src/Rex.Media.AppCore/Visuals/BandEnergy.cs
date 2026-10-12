namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// How much sound there is between two frequencies in the newest few milliseconds (AU-18), worked
/// out in time rather than through a transform, so a drum hit shows the moment it is heard rather
/// than a transform's half-window later.
/// <para>
/// The sound runs through fourth-order Linkwitz-Riley filters (two second-order Butterworth
/// sections each side, from the Audio EQ Cookbook's low- and high-pass formulas), from silence, over
/// enough samples before the window that the filters have settled; the power is the mean square of
/// what comes out across the window.
/// </para>
/// </summary>
public sealed class BandEnergy
{
    /// <summary>Samples run before the window so the filters forget their silent start.</summary>
    public const int Settle = 2048;

    private readonly double _low;
    private readonly double _high;
    private readonly List<Section> _sections = [];
    private float[] _kept = [];
    private int _keptCount;
    private int _rate;

    /// <param name="lowHz">Below this is cut, or 0 for nothing cut below.</param>
    /// <param name="highHz">Above this is cut, or 0 for nothing cut above.</param>
    public BandEnergy(double lowHz, double highHz)
    {
        if (!(lowHz >= 0 && highHz >= 0 && (highHz == 0 || highHz > lowHz)))
        {
            throw new ArgumentOutOfRangeException(nameof(lowHz), "A band runs from a low frequency to a higher one.");
        }

        (_low, _high) = (lowHz, highHz);
    }

    /// <summary>
    /// The mean power, within the band, of the window <see cref="Power"/> was asked to look back
    /// to: the same length, ending <c>lag</c> samples before the newest; 0 when it was not asked.
    /// </summary>
    public double Earlier { get; private set; }

    /// <summary>
    /// The mean power of the newest <paramref name="window"/> samples of <paramref name="samples"/>
    /// at <paramref name="sampleRate"/>, within the band; up to <see cref="Settle"/> samples before
    /// them, when there are, settle the filters. With a <paramref name="lag"/>, the power of as
    /// many samples ending that many before the newest is <see cref="Earlier"/>. The newest
    /// <paramref name="keep"/> samples of the band (or as many as the windows cover, if more) are
    /// kept for <see cref="RiseAge"/>.
    /// </summary>
    public double Power(ReadOnlySpan<float> samples, int sampleRate, int window, int keep = 0, int lag = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        window = Math.Clamp(window, 1, samples.Length);
        lag = Math.Clamp(lag, 0, samples.Length - window);
        keep = Math.Clamp(Math.Max(keep, window + lag), 1, samples.Length);
        if (_kept.Length < keep)
        {
            _kept = new float[keep];
        }

        _keptCount = keep;
        if (sampleRate != _rate)
        {
            Design(sampleRate);
        }

        foreach (var section in _sections)
        {
            section.Clear();
        }

        var start = Math.Max(0, samples.Length - keep - Settle);
        double power = 0, earlier = 0;
        for (var i = start; i < samples.Length; i++)
        {
            double value = samples[i];
            foreach (var section in _sections)
            {
                value = section.Run(value);
            }

            if (i >= samples.Length - window)
            {
                power += value * value;
            }

            if (lag > 0 && i >= samples.Length - lag - window && i < samples.Length - lag)
            {
                earlier += value * value;
            }

            if (i >= samples.Length - keep)
            {
                _kept[i - (samples.Length - keep)] = (float)value;
            }
        }

        Earlier = earlier / window;
        return power / window;
    }

    /// <summary>
    /// How long before the newest sample the band's sound rose most steeply, in seconds: the samples
    /// kept by the last <see cref="Power"/>, in blocks of <paramref name="blockSeconds"/>, each
    /// compared with the one before. Where in a frame a drum struck, to well under a frame.
    /// </summary>
    public double RiseAge(double blockSeconds = 0.005)
    {
        var block = Math.Max(1, (int)(blockSeconds * _rate));
        var blocks = _keptCount / Math.Max(1, block);
        if (blocks < 2)
        {
            return 0;
        }

        var (best, bestRise, last) = (blocks - 1, double.NegativeInfinity, 0.0);
        for (var b = 0; b < blocks; b++)
        {
            double energy = 0;
            var from = _keptCount - ((blocks - b) * block);
            for (var i = from; i < from + block; i++)
            {
                energy += _kept[i] * (double)_kept[i];
            }

            var rise = Math.Log10((energy + 1e-12) / (last + 1e-12));
            if (b > 0 && rise > bestRise)
            {
                (best, bestRise) = (b, rise);
            }

            last = energy;
        }

        return (double)(blocks - best) * block / _rate;
    }

    private void Design(int sampleRate)
    {
        _rate = sampleRate;
        _sections.Clear();
        var nyquist = sampleRate / 2.0;
        for (var twice = 0; twice < 2; twice++)
        {
            if (_low > 0 && _low < nyquist * 0.95)
            {
                _sections.Add(Section.HighPass(_low, sampleRate));
            }

            if (_high > 0 && _high < nyquist * 0.95)
            {
                _sections.Add(Section.LowPass(_high, sampleRate));
            }
        }
    }

    /// <summary>One second-order section, direct form I.</summary>
    private sealed class Section(double b0, double b1, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public static Section LowPass(double hertz, int sampleRate)
        {
            var (cos, alpha) = Shape(hertz, sampleRate);
            var a0 = 1 + alpha;
            return new Section((1 - cos) / 2 / a0, (1 - cos) / a0, (1 - cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public static Section HighPass(double hertz, int sampleRate)
        {
            var (cos, alpha) = Shape(hertz, sampleRate);
            var a0 = 1 + alpha;
            return new Section((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        /// <summary>The cosine of the corner and the cookbook's alpha for a Butterworth Q (one over the root of two).</summary>
        private static (double Cos, double Alpha) Shape(double hertz, int sampleRate)
        {
            var w = 2 * Math.PI * hertz / sampleRate;
            return (Math.Cos(w), Math.Sin(w) / (2 * Math.Sqrt(0.5)));
        }

        public double Run(double x)
        {
            var y = (b0 * x) + (b1 * _x1) + (b2 * _x2) - (a1 * _y1) - (a2 * _y2);
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return y;
        }

        public void Clear() => (_x1, _x2, _y1, _y2) = (0, 0, 0, 0);
    }
}
