using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// What the equaliser does (AU-07): a preamp and a gain for each of the ten ISO octave bands, in
/// decibels from -20 to +20. Immutable, so the window can hand a new one to the audio thread at any
/// moment without a lock.
/// </summary>
public sealed record EqualizerSettings(bool Enabled, double Preamp, IReadOnlyList<double> Gains)
{
    /// <summary>The ten bands' centre frequencies, in hertz.</summary>
    public static IReadOnlyList<double> Bands { get; } = [31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public const double Limit = 20;

    public static EqualizerSettings Off { get; } = new(false, 0, new double[10]);

    /// <summary>The built-in presets, flat first. The curves are rexplayer's own.</summary>
    public static IReadOnlyList<(string Name, double[] Gains)> Presets { get; } =
    [
        ("Flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("Classical", [0, 0, 0, 0, 0, 0, -3, -3, -3, -5]),
        ("Club", [0, 0, 4, 3, 3, 3, 2, 0, 0, 0]),
        ("Dance", [6, 5, 2, 0, 0, -3, -4, -4, 0, 0]),
        ("Full bass", [6, 6, 6, 4, 1, -2, -4, -6, -7, -7]),
        ("Full bass and treble", [4, 4, 0, -4, -3, 1, 4, 7, 8, 8]),
        ("Full treble", [-6, -6, -6, -2, 1, 6, 9, 10, 10, 10]),
        ("Headphones", [3, 7, 3, -2, -1, 1, 3, 6, 8, 9]),
        ("Large hall", [6, 6, 4, 4, 0, -3, -3, -3, 0, 0]),
        ("Live", [-3, 0, 2, 3, 3, 3, 2, 1, 1, 1]),
        ("Party", [4, 4, 0, 0, 0, 0, 0, 0, 4, 4]),
        ("Pop", [-1, 3, 4, 5, 3, 0, -1, -1, -1, -1]),
        ("Reggae", [0, 0, 0, -3, 0, 4, 4, 0, 0, 0]),
        ("Rock", [5, 3, -3, -5, -2, 2, 5, 6, 6, 6]),
        ("Ska", [-1, -3, -2, 0, 2, 3, 5, 6, 6, 6]),
        ("Soft", [3, 1, 0, -1, 0, 2, 5, 6, 7, 8]),
        ("Soft rock", [2, 2, 1, 0, -2, -3, -2, 0, 1, 5]),
        ("Techno", [5, 4, 0, -3, -3, 0, 5, 6, 6, 5]),
    ];

    /// <summary>The preset with this name, switched on, or null.</summary>
    public static EqualizerSettings? Preset(string name) =>
        Presets.FirstOrDefault(preset => preset.Name == name) is { Gains: { } gains } ? new EqualizerSettings(true, 0, gains) : null;

    /// <summary>A copy with every value inside the limits and exactly ten gains.</summary>
    public EqualizerSettings Normalize() => this with
    {
        Preamp = Clamp(Preamp),
        Gains = [.. Enumerable.Range(0, 10).Select(i => Clamp(Gains is { } g && i < g.Count ? g[i] : 0))],
    };

    /// <summary>Whether it changes the sound at all.</summary>
    public bool IsAudible => Enabled && (Preamp != 0 || Gains.Any(gain => gain != 0));

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, -Limit, Limit) : 0;
}

/// <summary>
/// The ten-band graphic equaliser: a low shelf at 31 Hz, peaking filters an octave wide for the
/// bands between, and a high shelf at 16 kHz, each a biquad from the published Audio EQ Cookbook
/// formulas, applied to every channel in turn. A band at or above 45 % of the sample rate cannot be
/// shaped and is left out. Flat or off, it leaves the samples untouched.
/// </summary>
public sealed class Equalizer
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly (double B0, double B1, double B2, double A1, double A2)[] _filters;
    private readonly double[] _state;
    private readonly float _preamp;

    public Equalizer(EqualizerSettings settings, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        Settings = settings.Normalize();
        _sampleRate = sampleRate;
        _channels = channels;
        _preamp = (float)Math.Pow(10, Settings.Preamp / 20);
        _filters = !Settings.IsAudible ? [] :
        [
            .. Settings.Gains.Select((gain, band) => (Gain: gain, Band: band))
                .Where(entry => entry.Gain != 0 && EqualizerSettings.Bands[entry.Band] < sampleRate * 0.45)
                .Select(entry => Design(entry.Band, entry.Gain)),
        ];
        _state = new double[_filters.Length * channels * 4];
    }

    public EqualizerSettings Settings { get; }

    /// <summary>Whether this equaliser serves frames of <paramref name="sampleRate"/> and <paramref name="channels"/>.</summary>
    public bool Fits(int sampleRate, int channels) => sampleRate == _sampleRate && channels == _channels;

    /// <summary>Shapes the frame in place.</summary>
    public void Process(AudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Settings.IsAudible)
        {
            return;
        }

        for (var channel = 0; channel < Math.Min(frame.Channels, _channels); channel++)
        {
            var samples = frame.Channel(channel)[..frame.SampleCount];
            for (var i = 0; i < samples.Length; i++)
            {
                double x = samples[i] * _preamp;
                for (var f = 0; f < _filters.Length; f++)
                {
                    // Direct form I: the last two inputs and outputs of this filter on this channel.
                    var (b0, b1, b2, a1, a2) = _filters[f];
                    var s = ((f * _channels) + channel) * 4;
                    var y = (b0 * x) + (b1 * _state[s]) + (b2 * _state[s + 1]) - (a1 * _state[s + 2]) - (a2 * _state[s + 3]);
                    (_state[s + 1], _state[s], _state[s + 3], _state[s + 2]) = (_state[s], x, _state[s + 2], y);
                    x = y;
                }

                samples[i] = (float)x;
            }
        }
    }

    /// <summary>Forgets the filters' memory, as after a seek, so nothing from before rings on.</summary>
    public void Reset() => Array.Clear(_state);

    private (double, double, double, double, double) Design(int band, double gain)
    {
        var frequency = EqualizerSettings.Bands[band];
        var a = Math.Pow(10, gain / 40);
        var w = 2 * Math.PI * frequency / _sampleRate;
        var (cos, sin) = (Math.Cos(w), Math.Sin(w));
        double b0, b1, b2, a0, a1, a2;
        if (band == 0 || band == EqualizerSettings.Bands.Count - 1)
        {
            // Shelves with a slope of one.
            var alpha = sin / 2 * Math.Sqrt(2);
            var root = 2 * Math.Sqrt(a) * alpha;
            var sign = band == 0 ? 1 : -1;
            b0 = a * ((a + 1) - (sign * (a - 1) * cos) + root);
            b1 = 2 * sign * a * ((a - 1) - (sign * (a + 1) * cos));
            b2 = a * ((a + 1) - (sign * (a - 1) * cos) - root);
            a0 = (a + 1) + (sign * (a - 1) * cos) + root;
            a1 = -2 * sign * ((a - 1) + (sign * (a + 1) * cos));
            a2 = (a + 1) + (sign * (a - 1) * cos) - root;
        }
        else
        {
            // An octave wide: Q = sqrt(2).
            var alpha = sin / (2 * Math.Sqrt(2));
            b0 = 1 + (alpha * a);
            b1 = -2 * cos;
            b2 = 1 - (alpha * a);
            a0 = 1 + (alpha / a);
            a1 = -2 * cos;
            a2 = 1 - (alpha / a);
        }

        return (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }
}
