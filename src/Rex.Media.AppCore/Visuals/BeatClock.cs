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
    private const double Slowest = 70;
    private const double Fastest = 160;
    private const double Preferred = 120;

    /// <summary>How far from a beat (in beats) an onset may land and still be that beat.</summary>
    private const double Window = 0.2;

    private readonly float[] _envelope = new float[EnvelopeRate * EnvelopeSeconds];
    /// <summary>The tempos tried apart, in beats a minute.</summary>
    private const double Step = 0.25;

    private readonly double[] _scores = new double[(int)((Fastest - Slowest) / Step) + 1];
    private readonly int[] _onsetAt = new int[EnvelopeRate * EnvelopeSeconds];
    private readonly float[] _onsetWeight = new float[EnvelopeRate * EnvelopeSeconds];
    private double _envelopeTime;
    private int _envelopeAt;
    private double _position;
    private long _matched = -1;
    private int _disagree;
    private int _halfVotes;
    private int _doubleVotes;

    /// <summary>Seconds a beat, or 0 before the tempo is known.</summary>
    public double Period { get; private set; }

    /// <summary>Beats a minute, or 0 before the tempo is known.</summary>
    public double Tempo => Period > 0 ? 60 / Period : 0;

    /// <summary>How far through the beat the grid is, 0 at a beat to 1 at the next.</summary>
    public double Phase => _position - Math.Floor(_position);

    /// <summary>The grid's beat nearest now, counted from when the tempo was found.</summary>
    public long Nearest => (long)Math.Round(_position);

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
            // the kicks keep landing there. A slow grid with kicks both on and halfway between its
            // beats is counting the music at half its speed, and doubles; otherwise kicks only
            // halfway mean the grid is on the off-beat, and it moves half a beat.
            var halfway = strong && Math.Abs(Math.Abs(error) - 0.5) < Window;
            if (halfway && Tempo < 95 && Confidence >= 0.5 && ++_doubleVotes >= 4)
            {
                Period /= 2;
                _position *= 2;
                (_doubleVotes, _halfVotes, _matched) = (0, 0, (long)Math.Round(_position));
            }
            else if (halfway && (Tempo >= 95 || Confidence < 0.5) && ++_halfVotes >= 3)
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
        if (Tempo > 180)
        {
            // Drawn into counting the off-beats too: the beat is every other one of these.
            (Period, _position) = (Period * 2, _position / 2);
            nearest = Math.Round(_position);
        }

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
        else if (++_disagree >= (Locked ? 90 : 20))
        {
            Adopt(period, Confidence * 0.5);
        }
    }

    /// <summary>Forgets the beat, as when another song starts.</summary>
    public void Reset()
    {
        Array.Clear(_envelope);
        (_envelopeTime, _envelopeAt, _position, _matched, _disagree, _halfVotes, _doubleVotes) = (0, 0, 0, -1, 0, 0, 0);
        (Period, Confidence, SinceOnset, SinceStrong) = (0, 0, 10, 10);
    }

    /// <summary>A new tempo, with the grid's beat on the last kick, or the last onset when there has been no kick of late.</summary>
    private void Adopt(double period, double confidence)
    {
        (Period, Confidence, _disagree, _halfVotes, _doubleVotes) = (period, confidence, 0, 0, 0);
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

    /// <summary>
    /// The seconds a beat at which the onsets of late line up best: for each tempo from the slowest
    /// to the fastest, in steps of a quarter beat a minute, how tightly the onsets cluster at one
    /// place in the beat and at one place in each half of it (the lengths of the means of their
    /// phases at the tempo and at twice it, each onset a unit arrow weighed by its strength),
    /// weighed a little toward the preferred tempo. Onsets on every beat cancel at half that tempo,
    /// and rhythms of three against two (dotted beats, a 3-3-2 bass) never cluster at four-thirds
    /// or two-thirds of it, as they would line up with themselves there. 0 when nothing lines up.
    /// </summary>
    internal double EstimatePeriod()
    {
        var n = _envelope.Length;
        var loudest = 0f;
        foreach (var value in _envelope)
        {
            loudest = Math.Max(loudest, value);
        }

        // The onsets: the samples of the envelope that stand out, oldest first.
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            var value = _envelope[(_envelopeAt + i) % n];
            if (loudest > 0 && value >= loudest * 0.2f)
            {
                (_onsetAt[count], _onsetWeight[count]) = (i, value);
                count++;
            }
        }

        if (count < 4)
        {
            return 0;
        }

        var (best, bestWeighted) = (-1, 0.0);
        for (var step = 0; step < _scores.Length; step++)
        {
            var bpm = Slowest + (step * Step);
            var turn = Math.Tau * bpm / 60 / EnvelopeRate;
            var (x, y, x2, y2, total) = (0.0, 0.0, 0.0, 0.0, 0.0);
            for (var k = 0; k < count; k++)
            {
                var (angle, weight) = (turn * _onsetAt[k], _onsetWeight[k]);
                (x, y) = (x + (weight * Math.Cos(angle)), y + (weight * Math.Sin(angle)));
                (x2, y2) = (x2 + (weight * Math.Cos(2 * angle)), y2 + (weight * Math.Sin(2 * angle)));
                total += weight;
            }

            // On the beat, and on the beat and its half together: a bass hit halfway through every
            // beat cancels the first, but lines up with the beat in the second.
            _scores[step] = (Math.Sqrt((x * x) + (y * y)) + Math.Sqrt((x2 * x2) + (y2 * y2))) / total;
            var octaves = Math.Log2(bpm / Preferred);
            var weighted = _scores[step] * Math.Exp(-0.5 * octaves * octaves);
            if (weighted > bestWeighted)
            {
                (best, bestWeighted) = (step, weighted);
            }
        }

        // A clear cluster, or nothing is sure.
        if (best < 0 || _scores[best] < 0.6)
        {
            return 0;
        }

        // Between steps: the top of the parabola through the best and its neighbours.
        var (a, b, c) = (_scores[Math.Max(0, best - 1)], _scores[best], _scores[Math.Min(_scores.Length - 1, best + 1)]);
        var bend = a - (2 * b) + c;
        var offset = bend < 0 ? Math.Clamp(0.5 * (a - c) / bend, -0.5, 0.5) : 0;
        return 60 / (Slowest + ((best + offset) * Step));
    }
}
