using Rex.Media.Audio;

namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// What the visualisations move to (AU-18), worked out from the sound as it is heard, a frame at a
/// time: the spectrum, the bass, middle and treble, how loud it is now and over the last seconds,
/// each beat as it lands, the tempo, where the next beat is due, and the drop (the music coming
/// back in hard after a quieter stretch).
/// <para>
/// Beats are onsets: the rise in the low and middle spectrum from one frame to the next (spectral
/// flux) jumping well above its own recent run, no sooner than a sixth of a second after the last.
/// The tempo is the spacing that best lines the onsets of the last six seconds up with themselves
/// (their autocorrelation) between 70 and 180 beats a minute.
/// </para>
/// </summary>
public sealed class MusicPulse
{
    /// <summary>Spectrum bands for drawing.</summary>
    public const int BandCount = 64;

    private const int FluxHistory = 48;
    private const int EnvelopeRate = 50;
    private const int EnvelopeSeconds = 6;
    private const double MinimumGap = 1.0 / 6;

    /// <summary>How long the drums must be away for their return to be the drop.</summary>
    private const double DropGap = 2.5;

    private readonly SpectrumAnalyzer _bands = new(BandCount);
    private readonly float[] _magnitudes = new float[SpectrumAnalyzer.Size / 2];
    private readonly float[] _previous = new float[SpectrumAnalyzer.Size / 2];
    private readonly Queue<float> _fluxes = new();
    private readonly float[] _envelope = new float[EnvelopeRate * EnvelopeSeconds];
    private readonly float[] _wave = new float[SpectrumAnalyzer.Size];
    private readonly Onset _kick = new(0.18, 0.15);
    private readonly Onset _snare = new(0.15, 0.2);
    private readonly Onset _hat = new(0.09, 0.08);
    private double _envelopeTime;
    private int _envelopeAt;
    private double _sinceBeat = 10;
    private double _sinceDrop = 10;
    private double _musicFor;
    private bool _primed;

    /// <summary>Each band's level, 0 to 1, lowest first.</summary>
    public IReadOnlyList<float> Bands => _bands.Levels;

    /// <summary>The newest sound, mono, oldest sample first.</summary>
    public IReadOnlyList<float> Wave => _wave;

    /// <summary>Levels from 0 to 1 that rise at once and fall smoothly.</summary>
    public float Bass { get; private set; }

    public float Mid { get; private set; }

    public float Treble { get; private set; }

    /// <summary>How loud the newest sound is, 0 (silence) to 1 (full scale), on a decibel scale.</summary>
    public float Loudness { get; private set; }

    /// <summary>The loudness over about the last four seconds.</summary>
    public float LongLoudness { get; private set; }

    /// <summary>Whether a beat landed in this frame, and how hard (0 to 1).</summary>
    public bool Beat { get; private set; }

    public float BeatStrength { get; private set; }

    /// <summary>Beats so far: the count lets effects change every so many.</summary>
    public long Beats { get; private set; }

    /// <summary>Whether the drop came in this frame: a strong beat after a quieter stretch.</summary>
    public bool Drop { get; private set; }

    /// <summary>Seconds since the last drop (large before any).</summary>
    public double SinceDrop => _sinceDrop;

    /// <summary>Seconds since the last beat (large before any).</summary>
    public double SinceBeat => _sinceBeat;

    /// <summary>The tempo in beats a minute, or 0 while it is not yet clear.</summary>
    public double Tempo { get; private set; }

    /// <summary>How far through the beat the music is, 0 at a beat to 1 at the next (by the tempo, or since the last beat).</summary>
    public double Phase => Tempo > 0 ? Math.Min(1, _sinceBeat * Tempo / 60) : Math.Min(1, _sinceBeat * 2);

    /// <summary>Where the strongest band is, 0 (bass) to 1 (treble): what colours that follow the pitch use.</summary>
    public float Pitch { get; private set; }

    /// <summary>Takes the newest <see cref="SpectrumAnalyzer.Size"/> samples, heard <paramref name="elapsed"/> after the last.</summary>
    public void Update(ReadOnlySpan<float> mono, int sampleRate, TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        if (mono.Length != SpectrumAnalyzer.Size)
        {
            throw new ArgumentException($"Analyse {SpectrumAnalyzer.Size} samples at a time.", nameof(mono));
        }

        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.5);
        mono.CopyTo(_wave);
        _bands.Update(mono, sampleRate, elapsed);
        Fft.Magnitudes(mono, _magnitudes);

        // Bass, middle and treble from the bands, rising at once and falling over a few frames.
        var fall = (float)Math.Exp(-dt / 0.12);
        Bass = Math.Max(Average(0, 8), Bass * fall);
        Mid = Math.Max(Average(8, 36), Mid * fall);
        Treble = Math.Max(Average(36, BandCount), Treble * fall);
        Pitch = (float)Player.Visualizers.DominantBand(_bands.Levels) / (BandCount - 1);

        // How loud, as the root mean square of the newest thirtieth of a second.
        var recent = Math.Min(mono.Length, Math.Max(64, sampleRate / 30));
        double power = 0;
        foreach (var sample in mono[^recent..])
        {
            power += sample * sample;
        }

        var decibels = 10 * Math.Log10(Math.Max(power / recent, 1e-10));
        Loudness = (float)Math.Clamp((decibels + 60) / 60, 0, 1);
        LongLoudness += (Loudness - LongLoudness) * (float)Math.Min(1, dt / 4);

        // The onset strength: how much the spectrum below 5 kHz rose since the last frame. Each drum's
        // own part of the spectrum is watched too: the kick low, the snare in the middle, the hats high.
        var top = Math.Min(_magnitudes.Length, 5000 * SpectrumAnalyzer.Size / sampleRate);
        var (kickTop, snareTop, hatTop) = (Bin(160, sampleRate), Bin(4000, sampleRate), Bin(16000, sampleRate));
        double flux = 0, kick = 0, snare = 0, hat = 0, kickEnergy = 0, snareEnergy = 0, hatEnergy = 0;
        for (var bin = 1; bin < hatTop; bin++)
        {
            // Rises are judged on a log scale, as hearing does; how hard a hit is, by its energy.
            var energy = _magnitudes[bin] * _magnitudes[bin];
            var now = MathF.Log(1 + (_magnitudes[bin] * 100));
            var rise = Math.Max(0, now - _previous[bin]);
            _previous[bin] = now;
            if (bin < top)
            {
                // Low bins weigh more: the kick and the bass carry the beat.
                flux += rise * (bin < top / 8 ? 3 : 1);
            }

            if (bin < kickTop)
            {
                (kick, kickEnergy) = (kick + rise, kickEnergy + energy);
            }
            else if (bin < snareTop)
            {
                (snare, snareEnergy) = (snare + rise, snareEnergy + energy);
            }
            else
            {
                (hat, hatEnergy) = (hat + rise, hatEnergy + energy);
            }
        }

        flux /= top;
        var primed = _primed;
        var kickGap = _kick.Since;
        _kick.Hear(kick / kickTop, kickEnergy, dt, primed && Loudness > 0.15f);
        _snare.Hear(snare / (snareTop - kickTop), snareEnergy, dt, primed && Loudness > 0.15f);
        _hat.Hear(hat / Math.Max(1, hatTop - snareTop), hatEnergy, dt, primed && Loudness > 0.1f);
        Beat = false;
        Drop = false;
        BeatStrength = Math.Max(0, BeatStrength - (float)(dt * 4));
        _sinceBeat += dt;
        _sinceDrop += dt;
        if (!_primed)
        {
            // The first frame rises from nothing: it is no beat.
            _primed = true;
            Record(0, dt);
            return;
        }

        var (mean, spread) = Statistics();
        var threshold = mean + Math.Max(1.6 * spread, 0.004);
        // A beat is a jump in the whole spectrum's rise, or a kick: either way, no closer than the gap.
        if ((flux > threshold || _kick.Hit) && _sinceBeat >= MinimumGap && Loudness > 0.15f)
        {
            Beat = true;
            Beats++;
            BeatStrength = _kick.Hit ? _kick.Strength : (float)Math.Clamp((flux - mean) / Math.Max(spread * 4, 1e-4), 0.25, 1);

            _sinceBeat = 0;
        }

        // The drop: the kick coming back after the drums dropped out for a while (a breakdown, a
        // build), in a song that had them before, and the music never stopped meanwhile.
        if (_kick.Hit && kickGap >= DropGap && _kick.Count > 4 && _musicFor >= DropGap && _sinceDrop > 8)
        {
            Drop = true;
            _sinceDrop = 0;
        }

        _musicFor = Loudness > 0.15f ? _musicFor + dt : 0;
        Record((float)flux, dt);
        // The tempo is worked out again only while the beat goes on: silence lines up with nothing.
        if (Beats > 3 && _sinceBeat < 2)
        {
            Tempo = EstimateTempo();
        }
    }

    /// <summary>The kick drum (and the bass that hits with it): a hit now, how hard, and a level that falls away after each.</summary>
    public Onset Kick => _kick;

    /// <summary>The snare and claps, in the middle of the spectrum.</summary>
    public Onset Snare => _snare;

    /// <summary>The hi-hats and cymbals, at the top.</summary>
    public Onset Hat => _hat;

    private int Bin(double hertz, int sampleRate) => (int)Math.Clamp(Math.Round(hertz * SpectrumAnalyzer.Size / sampleRate), 2, _magnitudes.Length);

    /// <summary>Forgets everything heard, as when another song starts.</summary>
    public void Reset()
    {
        _kick.Reset();
        _snare.Reset();
        _hat.Reset();
        _bands.Reset();
        Array.Clear(_previous);
        Array.Clear(_envelope);
        Array.Clear(_wave);
        _fluxes.Clear();
        (_envelopeTime, _envelopeAt, _sinceBeat, _sinceDrop, _musicFor, _primed) = (0, 0, 10, 10, 0, false);
        (Bass, Mid, Treble, Loudness, LongLoudness, BeatStrength, Pitch) = (0, 0, 0, 0, 0, 0, 0);
        (Beat, Drop, Beats, Tempo) = (false, false, 0, 0);
    }

    private float Average(int from, int to)
    {
        float sum = 0;
        for (var i = from; i < to; i++)
        {
            sum += _bands.Levels[i];
        }

        return sum / (to - from);
    }

    private (double Mean, double Spread) Statistics()
    {
        if (_fluxes.Count == 0)
        {
            return (0, 0);
        }

        var mean = _fluxes.Average();
        var variance = _fluxes.Sum(value => (value - mean) * (value - mean)) / _fluxes.Count;
        return (mean, Math.Sqrt(variance));
    }

    /// <summary>Keeps the flux for the threshold, and lays it into the onset envelope at its own fixed rate.</summary>
    private void Record(float flux, double dt)
    {
        _fluxes.Enqueue(flux);
        while (_fluxes.Count > FluxHistory)
        {
            _fluxes.Dequeue();
        }

        _envelopeTime += dt * EnvelopeRate;
        while (_envelopeTime >= 1)
        {
            _envelopeTime--;
            _envelope[_envelopeAt] = flux;
            _envelopeAt = (_envelopeAt + 1) % _envelope.Length;
        }
    }

    /// <summary>The beats a minute whose spacing best repeats the onset envelope, or the last estimate when nothing stands out.</summary>
    private double EstimateTempo()
    {
        var n = _envelope.Length;
        var mean = _envelope.Average();
        var (bestLag, bestScore, total) = (0, 0.0, 0.0);
        var (shortest, longest) = ((EnvelopeRate * 60 / 180) + 1, EnvelopeRate * 60 / 70);
        for (var lag = shortest; lag <= longest; lag++)
        {
            double score = 0;
            for (var i = 0; i + lag < n; i++)
            {
                var a = _envelope[(_envelopeAt + i) % n] - mean;
                var b = _envelope[(_envelopeAt + i + lag) % n] - mean;
                score += a * b;
            }

            score /= n - lag;
            total += Math.Max(0, score);
            if (score > bestScore)
            {
                (bestLag, bestScore) = (lag, score);
            }
        }

        // A clear peak, well above the average lag: otherwise keep what was known.
        return bestLag > 0 && bestScore > 2 * total / (longest - shortest + 1) ? 60.0 * EnvelopeRate / bestLag : Tempo;
    }
}

