using Rex.Media.Audio;

namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// What the visualisations move to (AU-18), worked out from the sound as it is heard, a frame at a
/// time: the spectrum, the bass, middle and treble, how loud it is, the punch of each kick, each
/// beat as it lands (or, where the mix hides it, as the beat clock expects it), the tempo, where the
/// music is in the beat, and the drop.
/// <para>
/// Everything a visualisation moves by is measured against the song's own recent quiet and loud
/// (<see cref="AdaptiveRange"/>), so a quiet recording moves as much as a loud one, and silence is
/// still. The bass, middle, treble, loudness and the drums are measured in time, over the newest
/// <see cref="Window"/> seconds, through band filters (<see cref="BandEnergy"/>): a kick shows the
/// moment it is heard. The spectrum is two transforms: a long one for the bass notes, which need
/// it to be told apart, and a short one for the rest.
/// </para>
/// <para>
/// Beats are onsets: a drum's band rising sharply above its own run, or the spectrum as a whole
/// rising well above the median of the last second and a half plus a multiple of its spread (a
/// moving-median threshold, as in J. P. Bello et al., "A tutorial on onset detection in music
/// signals", IEEE Transactions on Speech and Audio Processing 13(5), 2005). The spectrum's rise is its log-compressed spectral flux, each bin against the strongest of itself and its
/// neighbours a frame ago so a sliding note is no onset (S. Böck and G. Widmer, "Maximum filter
/// vibrato suppression for onset detection", DAFx 2013). Once the <see cref="BeatClock"/> is
/// locked, onsets between its beats are not beats (a kick there still counts toward moving its
/// grid half a beat), and a beat it expects that the music does not show is stood in for.
/// </para>
/// </summary>
public sealed class MusicPulse
{
    /// <summary>Spectrum bands for drawing.</summary>
    public const int BandCount = 64;

    /// <summary>Samples taken each frame: the long transform's length, about 85 ms at 48 kHz.</summary>
    public const int AnalysisSize = 4096;

    /// <summary>Samples in the short transform and the <see cref="Wave"/>.</summary>
    public const int ShortSize = SpectrumAnalyzer.Size;

    /// <summary>Seconds of the newest sound the levels are measured over.</summary>
    public const double Window = 0.025;

    /// <summary>
    /// How far back a drum's rise is measured from: its level this long before, from the same
    /// samples, so the rise means the same at any frame rate. Measured from the frame before, a
    /// rise was smaller the faster the frames came, and at sixty a second a bass drum between the
    /// kicks passed for one.
    /// </summary>
    public const double RiseSpan = 1.0 / 30;

    private const double LowHz = 40;
    private const double HighHz = 16000;

    /// <summary>Bands wholly below this come from the long transform.</summary>
    private const double LongBelowHz = 320;

    private const double MinimumGap = 1.0 / 6;

    /// <summary>How long the drums must be away for their return to be the drop.</summary>
    private const double DropGap = 2.5;

    /// <summary>How long past the beat the clock expected a stand-in waits for the music to show it.</summary>
    private const double FillWait = 0.006;

    /// <summary>The raw loudness under which nothing is a beat.</summary>
    private const float Quiet = 0.15f;

    /// <summary>
    /// How much of their range the bass and the loudness reach between kicks: the rest is kept for
    /// the kick, which takes them to 1, so a held bass line or a busy mix never hides it.
    /// </summary>
    private const float BetweenKicks = 0.38f;

    private readonly float[] _wave = new float[ShortSize];
    private readonly float[] _shortMagnitudes = new float[ShortSize / 2];
    private readonly float[] _longMagnitudes = new float[AnalysisSize / 2];
    private readonly float[] _shortReal = new float[ShortSize];
    private readonly float[] _shortImaginary = new float[ShortSize];
    private readonly float[] _longReal = new float[AnalysisSize];
    private readonly float[] _longImaginary = new float[AnalysisSize];
    private readonly float[] _previous = new float[ShortSize / 2];
    private readonly float[] _now = new float[ShortSize / 2];
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _rawBands = new float[BandCount];
    private readonly AdaptiveRange[] _bandRanges = [.. Enumerable.Range(0, BandCount).Select(_ => new AdaptiveRange(15, 2, 4))];
    // Restrict kick onset timing to sub-bass; off-beat pitched percussion around 110 Hz
    // belongs to the groove but must not move the beat grid away from the kicks.
    private readonly BandEnergy _bassBand = new(30, 95);
    private readonly BandEnergy _midBand = new(150, 2000);
    private readonly BandEnergy _trebleBand = new(2000, 0);
    private readonly BandEnergy _wholeBand = new(0, 0);
    private readonly AdaptiveRange _bassRange = new(gamma: 1.8);
    private readonly AdaptiveRange _midRange = new();
    private readonly AdaptiveRange _trebleRange = new();
    private readonly AdaptiveRange _loudnessRange = new(12, 2, 4);
    private readonly MovingMedian _fluxes = new(Onset.History);
    private readonly Onset _kick = new(0.18, 0.15, 2.5);
    private readonly Onset _snare = new(0.15, 0.2, 2.5);
    private readonly Onset _hat = new(0.09, 0.08, 2.5);
    private readonly BeatClock _clock = new();
    private double _slowBass = double.NaN;
    private double _lastFlux;
    private double _peakFlux;
    private double _sinceBeat = 10;
    private double _sinceDrop = 10;
    private double _musicFor;
    private double _busy;
    private long _detected;
    private bool _primed;
    private float _bass, _mid, _treble, _loudness, _punch;
    private double _bassRaw, _midRaw, _trebleRaw, _bassPower, _midPower, _treblePower;
    private double _bassBefore, _midBefore, _trebleBefore;

    /// <summary>Each band's level, 0 to 1, lowest first: against its own recent range, and shaped by how loud the band is beside the rest.</summary>
    public IReadOnlyList<float> Bands => _bands;

    /// <summary>The newest <see cref="ShortSize"/> samples, mono, oldest first.</summary>
    public IReadOnlyList<float> Wave => _wave;

    /// <summary>
    /// How strongly everything moves: 1 as measured, more lifts each level toward 1, less holds it
    /// down (a level v is shown as 1 - (1 - v) to this power).
    /// </summary>
    public double Sensitivity { get; set; } = 1;

    /// <summary>
    /// The bass (30 to 150 Hz) within the song's range: up at once, down over a tenth of a second;
    /// up to about 0.4 as it plays, and 1 on each kick.
    /// </summary>
    public float Bass => Shape(_bass);

    /// <summary>The middle (150 Hz to 2 kHz).</summary>
    public float Mid => Shape(_mid);

    /// <summary>The treble (above 2 kHz).</summary>
    public float Treble => Shape(_treble);

    /// <summary>
    /// How loud within the song's range: 0 at silence or its quietest, up to about 0.4 at its
    /// loudest of late, and 1 on each kick.
    /// </summary>
    public float Loudness => Shape(_loudness);

    /// <summary>
    /// The kick's punch: 1 the moment the bass jumps well above where it has been, falling away over
    /// about a tenth of a second; 0 for a bass note that holds. What a visualisation thumps with.
    /// </summary>
    public float Punch => Shape(_punch);

    /// <summary>How loud on a fixed scale: 0 at 60 dB below full scale, 1 at full scale.</summary>
    public float RawLoudness { get; private set; }

    /// <summary>Whether a beat landed in this frame, heard or (with <see cref="Filled"/>) expected.</summary>
    public bool Beat { get; private set; }

    /// <summary>Whether this frame's beat is one the clock stood in for, the music not showing it.</summary>
    public bool Filled { get; private set; }

    /// <summary>How hard the last beat was, 0 to 1, falling away after it.</summary>
    public float BeatStrength { get; private set; }

    /// <summary>Beats so far, heard and stood in for: the count lets effects change every so many.</summary>
    public long Beats { get; private set; }

    /// <summary>Whether the drop came in this frame: the kick back after the drums were away.</summary>
    public bool Drop { get; private set; }

    /// <summary>
    /// The build before a drop, 0 to 1: rising while the music plays on, its snares and hats busy,
    /// with no kick; gone at the drop or when the kick comes back.
    /// </summary>
    public float Build { get; private set; }

    /// <summary>Seconds since the last beat (large before any).</summary>
    public double SinceBeat => _sinceBeat;

    /// <summary>The tempo in beats a minute, or 0 while it is not yet clear.</summary>
    public double Tempo => _clock.Tempo;

    /// <summary>Whether the beat clock is sure of the beat.</summary>
    public bool Locked => _clock.Locked;

    /// <summary>How far through the beat the music is, 0 at a beat to 1 at the next (by the clock, else by the tempo, else since the last beat).</summary>
    public double Phase => _clock.Locked ? _clock.Phase
        : Tempo > 0 ? Math.Min(1, _sinceBeat * Tempo / 60) : Math.Min(1, _sinceBeat * 2);

    /// <summary>Where the strongest band is, 0 (bass) to 1 (treble), glided: what colours that follow the pitch use.</summary>
    public float Pitch { get; private set; }

    /// <summary>The kick drum (and the bass that hits with it): a hit now, how hard, and a level that falls away after each.</summary>
    public Onset Kick => _kick;

    /// <summary>The snare and claps, in the middle of the spectrum.</summary>
    public Onset Snare => _snare;

    /// <summary>The hi-hats and cymbals, at the top.</summary>
    public Onset Hat => _hat;

    /// <summary>Takes the newest <see cref="AnalysisSize"/> samples, heard <paramref name="elapsed"/> after the last.</summary>
    public void Update(ReadOnlySpan<float> mono, int sampleRate, TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        if (mono.Length != AnalysisSize)
        {
            throw new ArgumentException($"Analyse {AnalysisSize} samples at a time.", nameof(mono));
        }

        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.5);
        var newest = mono[^ShortSize..];
        newest.CopyTo(_wave);
        Fft.Magnitudes(newest, _shortMagnitudes, _shortReal, _shortImaginary);
        Fft.Magnitudes(mono, _longMagnitudes, _longReal, _longImaginary);
        Levels(mono, sampleRate, dt);
        Spectrum(sampleRate, dt);

        var flux = Flux(mono, sampleRate);
        var listening = _primed && RawLoudness > Quiet;
        var kickGap = _kick.Since;
        _kick.Hear(Rise(_bassBefore, _bassRaw), _bassPower, dt, listening);
        if (_kick.Hit)
        {
            (_bass, _loudness) = (1, 1);
        }

        Punching(dt);
        _snare.Hear(Rise(_midBefore, _midRaw), _midPower, dt, listening);
        _hat.Hear(Rise(_trebleBefore, _trebleRaw), _treblePower, dt, _primed && RawLoudness > 0.1f);

        (Beat, Filled, Drop) = (false, false, false);
        BeatStrength = Math.Max(0, BeatStrength - (float)(dt * 4));
        _sinceBeat += dt;
        _sinceDrop += dt;
        // Where in the frame the kick struck, so the clock keeps finer time than the frames do.
        var age = _kick.Hit ? Math.Min(Math.Max(dt, RiseSpan), _bassBand.RiseAge()) : dt / 2;
        _clock.Advance(dt, (float)flux);
        if (!_primed)
        {
            // The first frame rises from nothing: it is no beat.
            _primed = true;
            Remember(0, dt);
            return;
        }

        // An onset of the whole spectrum, or a kick; once the clock is sure, only near its beats.
        var (median, spread) = _fluxes.Measure();
        // The surge must also stand out against the strongest of late, so the hats between the beats are not beats.
        _peakFlux = Math.Max(flux, _peakFlux * Math.Exp(-dt / 3));
        var surge = flux > median + Math.Max(Math.Max(3 * spread, 0.004), 0.4 * (_peakFlux - median)) && flux >= _lastFlux;
        var onBeat = _clock.Near(age);
        var onset = (_kick.Hit || (surge && onBeat)) && RawLoudness > Quiet;
        var sinceGrid = _clock.Phase * _clock.Period;
        if (onset)
        {
            var strength = _kick.Hit ? _kick.Strength : (float)Math.Clamp((flux - median) / Math.Max(spread * 8, 1e-4), 0.25, 1);
            _detected++;
            _clock.Onset(_kick.Hit, age);

            // A kick between the locked clock's beats (a syncopation, an off-beat drum) counts
            // toward moving the grid half a beat, but it is no beat.
            if (onBeat && _sinceBeat >= MinimumGap)
            {
                Land(strength, filled: false);
            }
            else if (onBeat)
            {
                // A second onset of the same beat: one beat, as hard as the harder.
                BeatStrength = Math.Max(BeatStrength, strength);
            }
        }
        else if (_clock.Locked && _detected >= 8 && sinceGrid >= FillWait && sinceGrid - dt < FillWait && !_clock.Heard && _sinceBeat >= MinimumGap && RawLoudness > Quiet)
        {
            // The clock's beat came and went with nothing heard near it, early or late: the mix
            // hid it, so it is stood in for.
            Land(0.5f, filled: true);
        }

        // The drop: the kick coming back after the drums dropped out for a while (a breakdown, a
        // build), in a song that had them before, and the music never stopped meanwhile.
        if (_kick.Hit && kickGap >= DropGap && _kick.Count > 4 && _musicFor >= DropGap && _sinceDrop > 8)
        {
            (Drop, _sinceDrop) = (true, 0);
        }

        // The build: music without the kick, busy with snares and hats, for a while.
        _busy = (_busy * Math.Exp(-dt / 1.0)) + (_snare.Hit || _hat.Hit ? 1 : 0);
        // One recent snare or hat can establish a build when kicks have stayed away; requiring
        // several simultaneous hits missed sparse rhythmic fills at slower frame rates.
        var building = RawLoudness > Quiet && _kick.Count > 0 && _kick.Since > 1.5 && _busy > 0.8;
        Build = Drop || _kick.Hit ? 0 : (float)Math.Clamp(Build + (building ? dt / 6 : -dt), 0, 1);

        _musicFor = RawLoudness > Quiet ? _musicFor + dt : 0;
        Remember(flux, dt);
        if (_detected > 3 && _clock.SinceOnset < 2)
        {
            _clock.Estimate();
        }
    }

    /// <summary>Forgets everything heard, as when another song starts.</summary>
    public void Reset()
    {
        _kick.Reset();
        _snare.Reset();
        _hat.Reset();
        _clock.Reset();
        foreach (var range in _bandRanges)
        {
            range.Reset();
        }

        foreach (var range in new[] { _bassRange, _midRange, _trebleRange, _loudnessRange })
        {
            range.Reset();
        }

        Array.Clear(_previous);
        Array.Clear(_wave);
        Array.Clear(_bands);
        Array.Clear(_rawBands);
        _fluxes.Clear();
        (_slowBass, _lastFlux, _peakFlux) = (double.NaN, 0, 0);
        (_bassRaw, _midRaw, _trebleRaw, _bassPower, _midPower, _treblePower) = (0, 0, 0, 0, 0, 0);
        (_bassBefore, _midBefore, _trebleBefore) = (0, 0, 0);
        (_sinceBeat, _sinceDrop, _musicFor, _busy, _detected, _primed) = (10, 10, 0, 0, 0, false);
        (_bass, _mid, _treble, _loudness, _punch) = (0, 0, 0, 0, 0);
        (RawLoudness, BeatStrength, Pitch, Build) = (0, 0, 0, 0);
        (Beat, Filled, Drop, Beats) = (false, false, false, 0);
    }

    private void Land(float strength, bool filled)
    {
        (Beat, Filled, BeatStrength, _sinceBeat) = (true, filled, strength, 0);
        Beats++;
    }

    /// <summary>A level after <see cref="Sensitivity"/>.</summary>
    private float Shape(float value) => Sensitivity == 1 || !double.IsFinite(Sensitivity) || Sensitivity <= 0
        ? value
        : (float)(1 - Math.Pow(1 - Math.Clamp(value, 0, 1), Sensitivity));

    /// <summary>The 64 bands: the bass ones from the long transform, the rest from the short; each against its own range.</summary>
    private void Spectrum(int sampleRate, double dt)
    {
        var release = (float)Math.Exp(-dt / 0.08);
        var loudest = double.NegativeInfinity;
        foreach (var range in _bandRanges)
        {
            loudest = double.IsNaN(range.Ceiling) ? loudest : Math.Max(loudest, range.Ceiling);
        }

        var strongest = 0;
        for (var band = 0; band < BandCount; band++)
        {
            var useLong = BandLayout.Edges(band, BandCount, LowHz, HighHz).High <= LongBelowHz;
            var (from, to) = BandLayout.Bins(band, BandCount, LowHz, HighHz, sampleRate, useLong ? AnalysisSize : ShortSize);
            var decibels = AdaptiveRange.Decibels(BandLayout.Power(useLong ? _longMagnitudes : _shortMagnitudes, from, to));
            _rawBands[band] = (float)Math.Clamp((decibels + 60) / 60, 0, 1);
            var range = _bandRanges[band];
            var own = range.Map(decibels, dt);
            own = own < 0.002f ? 0 : own;

            // A band that is always far quieter than the loudest is drawn smaller, so the spectrum keeps its shape.
            var beside = double.IsFinite(loudest) && !double.IsNaN(range.Ceiling) ? Math.Clamp(1 + ((range.Ceiling - loudest) / 36), 0, 1) : 1;
            _bands[band] = Math.Max(Shape(own) * (float)(0.35 + (0.65 * beside)), _bands[band] * release);
            if (_rawBands[band] > _rawBands[strongest])
            {
                strongest = band;
            }
        }

        // The pitch glides to the strongest band rather than jumping between bands.
        Pitch += (((float)strongest / (BandCount - 1)) - Pitch) * (float)(1 - Math.Exp(-dt / 0.15));
    }

    /// <summary>The bass, middle, treble and loudness of the newest few milliseconds, against their ranges.</summary>
    private void Levels(ReadOnlySpan<float> mono, int sampleRate, double dt)
    {
        var window = Math.Max(64, (int)(sampleRate * Window));
        var lag = (int)(sampleRate * Math.Max(RiseSpan, dt));
        _bassPower = _bassBand.Power(mono, sampleRate, window, (int)(sampleRate * 0.06), lag);
        _midPower = _midBand.Power(mono, sampleRate, window, lag: lag);
        _treblePower = _trebleBand.Power(mono, sampleRate, window, lag: lag);
        var whole = AdaptiveRange.Decibels(_wholeBand.Power(mono, sampleRate, window));
        (_bassRaw, _midRaw, _trebleRaw) = (AdaptiveRange.Decibels(_bassPower), AdaptiveRange.Decibels(_midPower), AdaptiveRange.Decibels(_treblePower));
        (_bassBefore, _midBefore, _trebleBefore) = (AdaptiveRange.Decibels(_bassBand.Earlier), AdaptiveRange.Decibels(_midBand.Earlier), AdaptiveRange.Decibels(_trebleBand.Earlier));

        RawLoudness = (float)Math.Clamp((whole + 60) / 60, 0, 1);
        _bass = Envelope(_bass, BetweenKicks * _bassRange.Map(_bassRaw, dt), dt, 0.065);
        _mid = Envelope(_mid, _midRange.Map(_midRaw, dt), dt, 0.12);
        _treble = Envelope(_treble, _trebleRange.Map(_trebleRaw, dt), dt, 0.06);
        _loudness = Envelope(_loudness, BetweenKicks * _loudnessRange.Map(whole, dt), dt, 0.09);
    }

    /// <summary>
    /// The punch: while the bass is rising, how far it stands above where it has been over the last
    /// half second; a kick heard sets it at least to its strength. Either way it then falls away.
    /// </summary>
    private void Punching(double dt)
    {
        if (_bassRaw < AdaptiveRange.Gate)
        {
            _slowBass = double.NaN;
            _punch = Envelope(_punch, 0, dt, 0.09);
            return;
        }

        var rising = _bassRaw > _bassBefore;
        var above = double.IsNaN(_slowBass) ? 0 : _bassRaw - _slowBass;
        var hit = _kick.Hit ? 0.75f + (0.25f * _kick.Strength) : 0;
        _punch = Envelope(_punch, Math.Max(hit, rising ? (float)Math.Clamp((above - 2) / 8, 0, 1) : 0), dt, 0.09);
        _slowBass = double.IsNaN(_slowBass) ? _bassRaw : _slowBass + ((_bassRaw - _slowBass) * (1 - Math.Exp(-dt / 0.4)));
    }

    /// <summary>Up at once to <paramref name="value"/>, else down from <paramref name="last"/> over <paramref name="release"/> seconds, to nothing once it is too faint to see.</summary>
    private static float Envelope(float last, float value, double dt, double release)
    {
        var level = Math.Max(value, last * (float)Math.Exp(-dt / release));
        return level < 0.002f ? 0 : level;
    }

    /// <summary>How far a band's level rose over <see cref="RiseSpan"/>, in decibels; silence counts as the gate.</summary>
    private static double Rise(double before, double now) =>
        Math.Max(0, Math.Max(now, AdaptiveRange.Gate) - Math.Max(before, AdaptiveRange.Gate));

    /// <summary>
    /// The onset strength: how much the spectrum below 5 kHz rose since the last frame, each bin on a
    /// log scale against the strongest of itself and its neighbours a frame ago, the bass weighing more.
    /// </summary>
    private double Flux(ReadOnlySpan<float> mono, int sampleRate)
    {
        // Compare two windows separated by a fixed time, rather than consecutive draw frames.
        var lag = Math.Clamp((int)(sampleRate * RiseSpan), 1, mono.Length - ShortSize);
        Fft.Magnitudes(mono.Slice(mono.Length - ShortSize - lag, ShortSize), _previous, _shortReal, _shortImaginary);
        var top = Math.Clamp(5000 * ShortSize / sampleRate, 2, _shortMagnitudes.Length - 1);
        for (var bin = 0; bin < _now.Length; bin++)
        {
            _now[bin] = MathF.Log(1 + (_shortMagnitudes[bin] * 100));
            _previous[bin] = MathF.Log(1 + (_previous[bin] * 100));
        }

        double flux = 0;
        for (var bin = 1; bin < top; bin++)
        {
            var before = Math.Max(_previous[bin - 1], Math.Max(_previous[bin], _previous[bin + 1]));
            flux += Math.Max(0, _now[bin] - before) * (bin < top / 8 ? 3 : 1);
        }

        return flux / top;
    }

    private void Remember(double flux, double dt)
    {
        _lastFlux = flux;
        _fluxes.Add(flux, dt);
    }
}
