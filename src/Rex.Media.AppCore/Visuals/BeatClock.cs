namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The music's beat as a running clock (AU-18): its tempo, where in the beat the music is, and
/// when the next beat is due, so the visualisations can land on the beat even where a busy mix
/// hides a kick, and keep time through it.
/// <para>
/// The tempo is the spacing at which the last eight seconds of onset strength best repeat (their
/// autocorrelation, with the repeat at twice the spacing counted in, so the beat is preferred to
/// the bar), weighed toward 120 beats a minute so a song is not counted at half or double its speed,
/// and refined between lags. The clock then runs a beat grid at that tempo and pulls it toward
/// every onset near a beat, a little in phase and less in tempo, as a phase-locked loop does. Each
/// beat the music confirms makes it surer; each it passes in silence, less. Sure enough, it is
/// <see cref="Locked"/>.
/// </para>
/// </summary>
public sealed class BeatClock
{
    /// <summary>Samples a second of the onset envelope.</summary>
    public const int EnvelopeRate = 50;

    private const int EnvelopeSeconds = 8;
    private const double Slowest = 60;
    private const double Fastest = 200;
    private const double Preferred = 120;

    /// <summary>How far from a beat (in beats) an onset may land and still be that beat.</summary>
    private const double Window = 0.2;

    private readonly float[] _envelope = new float[EnvelopeRate * EnvelopeSeconds];
    private readonly double[] _scores = new double[(EnvelopeRate * 60 / (int)Slowest) + 2];
    private double _envelopeTime;
    private int _envelopeAt;
    private double _position;
    private long _matched = -1;
    private int _disagree;
    private int _halfVotes;

    /// <summary>Seconds a beat, or 0 before the tempo is known.</summary>
    public double Period { get; private set; }

    /// <summary>Beats a minute, or 0 before the tempo is known.</summary>
    public double Tempo => Period > 0 ? 60 / Period : 0;

    /// <summary>How far through the beat the grid is, 0 at a beat to 1 at the next.</summary>
    public double Phase => _position - Math.Floor(_position);

    /// <summary>Seconds until the grid's next beat, or 0 before the tempo is known.</summary>
    public double NextBeatIn => (1 - Phase) * Period;

    /// <summary>How sure the clock is of the grid, 0 to 1.</summary>
    public double Confidence { get; private set; }

    /// <summary>Whether the grid can be trusted to stand in for a beat the music does not show.</summary>
    public bool Locked => Period > 0 && Confidence >= 0.5;

    /// <summary>Seconds since the last onset (large before any).</summary>
    public double SinceOnset { get; private set; } = 10;

    /// <summary>Seconds since the last strong onset, a kick (large before any).</summary>
    public double SinceStrong { get; private set; } = 10;

    /// <summary>
    /// Moves the clock on by <paramref name="dt"/>, with the onset strength heard in that time,
    /// <paramref name="age"/> seconds before now; true when the grid passed a beat.
    /// </summary>
    public bool Advance(double dt, float strength, double age = 0)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.5) : 0;
        Record(float.IsFinite(strength) ? Math.Max(0, strength) : 0, dt, double.IsFinite(age) ? Math.Clamp(age, 0, dt) : 0);
        SinceOnset += dt;
        SinceStrong += dt;
        if (Period <= 0)
        {
            return false;
        }

        var before = Math.Floor(_position);
        _position += dt / Period;
        if (Math.Floor(_position) == before)
        {
            return false;
        }

        // The beat just behind is settled: any onset for it has come by now.
        if (_matched != (long)before)
        {
            Confidence = Math.Max(0, Confidence - 0.15);
        }

        return true;
    }

    /// <summary>
    /// An onset heard <paramref name="age"/> seconds ago, <paramref name="strong"/> when it is a
    /// kick: near a beat of the grid, it confirms that beat and pulls the grid toward it. Kicks that
    /// keep landing halfway between the grid's beats mean the grid is on the off-beat, and it moves
    /// half a beat.
    /// </summary>
    public void Onset(bool strong = false, double age = 0)
    {
        age = double.IsFinite(age) ? Math.Clamp(age, 0, 0.25) : 0;
        SinceOnset = age;
        SinceStrong = strong ? age : SinceStrong;
        if (Period <= 0)
        {
            return;
        }

        var at = _position - (age / Period);
        var nearest = Math.Round(at);
        var error = at - nearest;
        if (Math.Abs(error) > Window)
        {
            // Between beats (an off-beat, a syncopation): neither for nor against the grid, unless
            // the kicks keep landing there.
            if (strong && Math.Abs(Math.Abs(error) - 0.5) < Window && ++_halfVotes >= 3)
            {
                _position += 0.5;
                // Three consistent strong off-beats establish the corrected grid.
                (_halfVotes, Confidence, _matched) = (0, Math.Max(0.6, Confidence * 0.5), (long)Math.Round(_position));
            }

            return;
        }

        _halfVotes = strong ? 0 : _halfVotes;

        // Late onsets (a positive error) move the grid later and lengthen the beat; early ones the opposite.
        _position -= 0.5 * error;
        Period *= 1 + (0.15 * error);
        Confidence = Math.Min(1, Confidence + 0.2);
        _matched = (long)nearest;
    }

    /// <summary>
    /// Works the tempo out again from the onsets of late: the first one sets the grid on the last
    /// onset; later ones that agree draw it gently along, and one that keeps disagreeing replaces it.
    /// </summary>
    public void Estimate()
    {
        var period = EstimatePeriod();
        if (period <= 0)
        {
            return;
        }

        if (Period <= 0)
        {
            Adopt(period, 0.25);
            return;
        }

        var ratio = period / Period;
        if (Math.Abs(ratio - 1) < 0.06)
        {
            // Agreed. Until the grid is locked the estimate draws it along; after, the onsets it
            // locks to keep finer time than the envelope's samples can, and it is left to them.
            Period += Locked ? 0 : (period - Period) * 0.05;
            _disagree = 0;
        }
        else if (Math.Abs(ratio - 2) < 0.12 || Math.Abs(ratio - 0.5) < 0.03)
        {
            // Twice or half the beat: the same music counted another way, not a new tempo.
            _disagree = 0;
        }
        else if (++_disagree >= 8)
        {
            Adopt(period, Confidence * 0.5);
        }
    }

    /// <summary>Forgets the beat, as when another song starts.</summary>
    public void Reset()
    {
        Array.Clear(_envelope);
        (_envelopeTime, _envelopeAt, _position, _matched, _disagree, _halfVotes) = (0, 0, 0, -1, 0, 0);
        (Period, Confidence, SinceOnset, SinceStrong) = (0, 0, 10, 10);
    }

    /// <summary>A new tempo, with the grid's beat on the last kick, or the last onset when there has been no kick of late.</summary>
    private void Adopt(double period, double confidence)
    {
        (Period, Confidence, _disagree, _halfVotes) = (period, confidence, 0, 0);
        var since = SinceStrong < period * 4 ? SinceStrong : SinceOnset;
        _position = since / period;
        _matched = since < period * Window ? 0 : -1;
    }

    /// <summary>
    /// Lays the strength into the envelope at its own fixed rate: an impulse where the onset was
    /// (<paramref name="age"/> before now), nothing in the frame's other samples.
    /// </summary>
    private void Record(float strength, double dt, double age)
    {
        _envelopeTime += dt * EnvelopeRate;
        var added = 0;
        while (_envelopeTime >= 1)
        {
            _envelopeTime--;
            _envelope[_envelopeAt] = 0;
            _envelopeAt = (_envelopeAt + 1) % _envelope.Length;
            added++;
        }

        // The impulse goes where the onset was, among the samples this frame added.
        var back = Math.Min(Math.Max(0, added - 1), (int)Math.Round(age * EnvelopeRate));
        var n = _envelope.Length;
        _envelope[(((_envelopeAt - 1 - back) % n) + n) % n] += strength;
    }

    /// <summary>The seconds a beat whose spacing best repeats the envelope, weighed toward the preferred tempo; 0 when nothing stands out.</summary>
    internal double EstimatePeriod()
    {
        var n = _envelope.Length;
        double mean = 0;
        foreach (var value in _envelope)
        {
            mean += value;
        }

        mean /= n;
        var (shortest, longest) = ((int)Math.Ceiling(EnvelopeRate * 60 / Fastest), (int)Math.Floor(EnvelopeRate * 60 / Slowest));
        for (var lag = shortest - 1; lag <= longest + 1; lag++)
        {
            _scores[lag - shortest + 1] = Correlation(lag, mean);
        }

        var (best, bestWeighted, total) = (0, 0.0, 0.0);
        for (var lag = shortest; lag <= longest; lag++)
        {
            var score = _scores[lag - shortest + 1] + (0.5 * Correlation(lag * 2, mean));
            total += Math.Max(0, score);
            var octaves = Math.Log2(EnvelopeRate * 60.0 / lag / Preferred);
            var weighted = score * Math.Exp(-0.5 * octaves * octaves);
            if (weighted > bestWeighted)
            {
                (best, bestWeighted) = (lag, weighted);
            }
        }

        // A clear peak, well above the average lag: otherwise nothing is sure.
        if (best == 0 || bestWeighted <= 1.5 * total / (longest - shortest + 1))
        {
            return 0;
        }

        // Between lags: the top of the parabola through the best and its neighbours.
        var (a, b, c) = (_scores[best - shortest], _scores[best - shortest + 1], _scores[best - shortest + 2]);
        var bend = a - (2 * b) + c;
        var offset = bend < 0 ? Math.Clamp(0.5 * (a - c) / bend, -0.5, 0.5) : 0;
        return (best + offset) / EnvelopeRate;
    }

    /// <summary>How well the envelope lines up with itself <paramref name="lag"/> samples on, its mean taken away.</summary>
    private double Correlation(int lag, double mean)
    {
        var n = _envelope.Length;
        // EstimatePeriod calls this only with lags shorter than the envelope.
        double score = 0;
        for (var i = 0; i + lag < n; i++)
        {
            score += (_envelope[(_envelopeAt + i) % n] - mean) * (_envelope[(_envelopeAt + i + lag) % n] - mean);
        }

        return score / (n - lag);
    }
}
