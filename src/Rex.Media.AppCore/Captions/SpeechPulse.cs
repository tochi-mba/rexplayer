using Rex.Media.AppCore.Visuals;

namespace Rex.Media.AppCore.Captions;

/// <summary>How a word was said, against the speaker's own recent run: a whisper, plainly, a stressed word, or a shout.</summary>
public enum Stress
{
    Hush,
    Plain,
    Shout,
    Peak,
}

/// <summary>
/// What the voice is doing in one frame (OSD-11): how loud against the speaker's own recent quiet
/// and loud, how high against their own usual pitch (0.5 is usual), whether a syllable began,
/// how many have begun a second lately, whether it is silent, for how long, and the level itself
/// in decibels below full scale, which <see cref="SpeechPulse.StressOf"/> ranks words by.
/// </summary>
public readonly record struct SpeechFrame(float Loudness, float Pitch, bool Onset, float Pace, bool Silent, double SilentFor, double Decibels = double.NegativeInfinity);

/// <summary>
/// Reads the voice from the sound as it is heard, a frame at a time (OSD-11), for captions that
/// move the way the words were said.
/// <para>
/// Loudness is the frame's power mapped between the speaker's recent quiet and loud
/// (<see cref="AdaptiveRange"/>), so a soft talker and a loud one both use the whole range. A
/// syllable's onset is the power rising sharply above its own recent rises (<see cref="Onset"/>),
/// no sooner than 90 ms after the last, as syllables come. Pitch is the strongest repetition in
/// the newest 40 ms (autocorrelation over 70 to 400 Hz), kept only when the sound repeats clearly
/// (voiced), and told against the speaker's own running usual pitch, so a lift at the end of a
/// question reads as a lift whoever is speaking.
/// </para>
/// <para>
/// Every onset's level is remembered in decibels, not against the recent range (which tops out at
/// 1 for every loud word), so <see cref="StressOf"/> can say whether a word was quieter than most
/// of the speaker's last words (a hush), louder than three in four (a shout) or louder than nearly
/// all (a peak).
/// </para>
/// </summary>
public sealed class SpeechPulse
{
    /// <summary>Samples taken each frame: about 43 ms at 48 kHz, enough for two periods of a low voice.</summary>
    public const int Size = 2048;

    /// <summary>Onsets closer than this are one syllable.</summary>
    public const double OnsetGap = 0.09;

    /// <summary>Seconds the pace is counted over.</summary>
    public const double PaceWindow = 1.5;

    private const double LowHz = 70;
    private const double HighHz = 400;
    private const double VoicedClarity = 0.45;
    private const int Remembered = 40;
    private const float Quiet = 0.04f;

    private readonly AdaptiveRange _range = new(span: 18, floorSeconds: 2.5, ceilingSeconds: 4, gamma: 1);
    private readonly Onset _onset = new(OnsetGap, 0.15, minimum: 0.001);
    private readonly Queue<double> _onsetTimes = new();
    private readonly List<double> _onsetLevels = [];
    private readonly double[] _sorted = new double[Remembered];
    private double _seconds;
    private double _lastPower;
    private double _usualHz;
    private double _silentFor;
    private float _pitch = 0.5f;

    /// <summary>The frame last heard.</summary>
    public SpeechFrame Frame { get; private set; }

    /// <summary>The voice's pitch in hertz where the last frame was voiced; 0 where it was not.</summary>
    public double Hertz { get; private set; }

    /// <summary>The speaker's running usual pitch in hertz; 0 before any voice.</summary>
    public double UsualHertz => _usualHz;

    /// <summary>Syllable onsets heard so far.</summary>
    public long Onsets { get; private set; }

    /// <summary>Hears <paramref name="mono"/> (<see cref="Size"/> samples, the newest last), <paramref name="elapsed"/> after the last frame.</summary>
    public SpeechFrame Update(ReadOnlySpan<float> mono, int sampleRate, TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        if (mono.Length != Size)
        {
            throw new ArgumentException($"Hear {Size} samples at a time.", nameof(mono));
        }

        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.5);
        _seconds += dt;
        var power = Power(mono);
        var decibels = AdaptiveRange.Decibels(power);
        var loudness = _range.Map(decibels, dt);
        var silent = loudness < Quiet;
        _silentFor = silent ? _silentFor + dt : 0;

        // A syllable: the power leaping above the frame before, while there is a voice to hear.
        _onset.Hear(Math.Max(0, power - _lastPower), power, dt, !silent);
        _lastPower = power;
        if (_onset.Hit)
        {
            Onsets++;
            _onsetTimes.Enqueue(_seconds);
            _onsetLevels.Add(decibels);
            if (_onsetLevels.Count > Remembered)
            {
                _onsetLevels.RemoveAt(0);
            }
        }

        while (_onsetTimes.Count > 0 && _onsetTimes.Peek() < _seconds - PaceWindow)
        {
            _onsetTimes.Dequeue();
        }

        Hertz = silent ? 0 : Fundamental(mono, sampleRate);
        if (Hertz > 0)
        {
            // The usual pitch follows what is heard, slowly, so one lifted word does not move it.
            _usualHz = _usualHz == 0 ? Hertz : _usualHz + ((Hertz - _usualHz) * (1 - Math.Exp(-dt / 4)));
            _pitch = (float)Math.Clamp(0.5 + (0.7 * Math.Log2(Hertz / _usualHz)), 0, 1);
        }
        else
        {
            // Between voiced frames the pitch settles back toward usual.
            _pitch = (float)(0.5 + ((_pitch - 0.5) * Math.Exp(-dt / 0.25)));
        }

        Frame = new SpeechFrame(loudness, _pitch, _onset.Hit, (float)(_onsetTimes.Count / PaceWindow), silent, _silentFor, decibels);
        return Frame;
    }

    /// <summary>
    /// How a word said as in <paramref name="frame"/> stands against the speaker's recent onsets: no
    /// louder than one in four is a hush, at least as loud as five in six a shout, louder than all
    /// but one in twelve, and clear of the shouts, a peak. Until enough has been heard to compare,
    /// every word is plain.
    /// </summary>
    public Stress StressOf(SpeechFrame frame)
    {
        // Until a few words have been heard there is nothing to compare with: the speaker's first words are plain.
        if (_onsetLevels.Count < 6)
        {
            return Stress.Plain;
        }

        var count = _onsetLevels.Count;
        _onsetLevels.CopyTo(_sorted);
        Array.Sort(_sorted, 0, count);
        var level = frame.Decibels;
        var (hush, shout, peak) = (_sorted[count / 4], _sorted[Math.Min(count - 1, count * 5 / 6)], _sorted[Math.Min(count - 1, count * 11 / 12)]);
        // A shout stands clear of the quiet words, and a peak clear of the shouts: three decibels,
        // about half as loud again; even speech, all one level, has no shouts in it.
        return level >= peak && level >= shout + 3 && level >= hush + 3 ? Stress.Peak
            : level >= shout && level >= hush + 3 ? Stress.Shout
            : level <= hush ? Stress.Hush
            : Stress.Plain;
    }

    /// <summary>Forgets the speaker, as when another file starts.</summary>
    public void Reset()
    {
        _range.Reset();
        _onset.Reset();
        _onsetTimes.Clear();
        _onsetLevels.Clear();
        (_seconds, _lastPower, _usualHz, _silentFor, Hertz, Onsets, _pitch) = (0, 0, 0, 0, 0, 0, 0.5f);
        Frame = default;
    }

    private static double Power(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return sum / samples.Length;
    }

    /// <summary>
    /// The voice's fundamental: the lag between <see cref="LowHz"/> and <see cref="HighHz"/> whose
    /// normalised autocorrelation is highest, when it is clear enough to be a voice; 0 otherwise.
    /// </summary>
    private static double Fundamental(ReadOnlySpan<float> samples, int sampleRate)
    {
        var low = Math.Max(1, (int)(sampleRate / HighHz));
        var high = Math.Min(samples.Length / 2, (int)(sampleRate / LowHz));
        if (high <= low)
        {
            return 0;
        }

        double energy = 0;
        for (var i = 0; i < samples.Length - high; i++)
        {
            energy += samples[i] * samples[i];
        }

        if (energy <= 0)
        {
            return 0;
        }

        var bestLag = 0;
        double best = 0;
        for (var lag = low; lag <= high; lag++)
        {
            double sum = 0;
            double shifted = 0;
            for (var i = 0; i < samples.Length - high; i++)
            {
                sum += samples[i] * samples[i + lag];
                shifted += samples[i + lag] * samples[i + lag];
            }

            var clarity = sum / Math.Sqrt(energy * Math.Max(shifted, 1e-12));
            if (clarity > best)
            {
                (best, bestLag) = (clarity, lag);
            }
        }

        return best >= VoicedClarity ? (double)sampleRate / bestLag : 0;
    }
}
