namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The music's beat as a running clock (AU-18): its tempo and where in the beat the music is, so
/// the visualisations can land on the beat even where a busy mix hides a kick, and keep time
/// through it.
/// <para>
/// The tempo is the one at which the last eight seconds of onsets cluster most tightly in phase
/// (see <see cref="EstimatePeriod"/>), weighed by a log-normal window around 120 beats a minute (a
/// standard deviation of one octave, as in D. P. W. Ellis, "Beat tracking by dynamic programming",
/// Journal of New Music Research 36(1), 2007) so a song is not counted at half or double its speed.
/// Unlike the onsets' autocorrelation, phase clustering is not fooled by a 3-3-2 bass, which repeats
/// at four-thirds and two-thirds of the beat. The envelope is kept at its own fixed rate whatever
/// the frame rate: each sample holds the strongest onset strength heard in its time.
/// </para>
/// <para>
/// The clock then runs a beat grid at that tempo and pulls it toward every onset near a beat, a
/// little in phase and less in tempo, as a phase-locked loop does. Each beat the music confirms
/// makes it surer; each it passes in silence, less. Sure enough, it is <see cref="Locked"/>.
/// </para>
/// </summary>
public sealed class BeatClock
{
    /// <summary>Samples a second of the onset envelope.</summary>
    public const int EnvelopeRate = 50;

    private const int EnvelopeSeconds = 8;
    private const double Slowest = 70;
    private const double Fastest = 180;

    /// <summary>The tempos tried apart, in beats a minute.</summary>
    private const double Step = 0.25;
    private const double Preferred = 120;

    /// <summary>How far from a beat (in beats) an onset may land and still be that beat.</summary>
    private const double Window = 0.2;

    private readonly float[] _envelope = new float[EnvelopeRate * EnvelopeSeconds];
    private readonly double[] _scores = new double[(int)((Fastest - Slowest) / Step) + 1];
    private readonly int[] _onsetAt = new int[EnvelopeRate * EnvelopeSeconds];
    private readonly float[] _onsetWeight = new float[EnvelopeRate * EnvelopeSeconds];
    private readonly float[] _sorted = new float[EnvelopeRate * EnvelopeSeconds];
    private int _onsets;
    private double _bestScore;
    private double _envelopeTime;
    private int _envelopeAt;
    private double _position;

    /// <summary>The newest beat of the grid an onset confirmed, and the one confirmed before it.</summary>
    private long _matched = -1;
    private long _matchedBefore = -1;
    // Seconds of sustained contradictory tempo, independent of how often frames arrive.
    private double _disagree;
    private double _frameSeconds;
    private int _halfVotes;

    /// <summary>How hard the kicks on the grid's beats have been of late, and those halfway between.</summary>
    private double _onAccent, _halfAccent;

    /// <summary>
    /// Seconds since the hardest kick of late, and how hard it was, fading over some seconds as the
    /// drums' own hardest do, so the softer kicks between the accents do not take its place.
    /// </summary>
    private double _sinceHardest = 10, _hardest;

    /// <summary>Seconds a beat, or 0 before the tempo is known.</summary>
    public double Period { get; private set; }

    /// <summary>Beats a minute, or 0 before the tempo is known.</summary>
    public double Tempo => Period > 0 ? 60 / Period : 0;

    /// <summary>How far through the beat the grid is, 0 at a beat to 1 at the next.</summary>
    public double Phase => _position - Math.Floor(_position);

    /// <summary>How sure the clock is of the grid, 0 to 1.</summary>
    public double Confidence { get; private set; }

    /// <summary>Whether the grid can be trusted to stand in for a beat the music does not show.</summary>
    public bool Locked => Period > 0 && Confidence >= 0.5;

    /// <summary>Seconds since the last onset (large before any).</summary>
    public double SinceOnset { get; private set; } = 10;

    /// <summary>Seconds since the last strong onset, a kick (large before any).</summary>
    public double SinceStrong { get; private set; } = 10;

    /// <summary>Whether the beat the grid last passed was confirmed by an onset, early or late.</summary>
    public bool Heard => Period > 0 && IsMatched((long)Math.Floor(_position));

    /// <summary>
    /// Moves the clock on by <paramref name="dt"/>, with the onset strength heard in that time; true
    /// when the grid passed a beat.
    /// </summary>
    public bool Advance(double dt, float strength)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.5) : 0;
        _frameSeconds = dt;
        Record(float.IsFinite(strength) ? Math.Max(0, strength) : 0, dt);
        SinceOnset += dt;
        SinceStrong += dt;
        _sinceHardest += dt;
        _hardest *= Math.Exp(-dt / 4);
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
        if (!IsMatched((long)before))
        {
            Confidence = Math.Max(0, Confidence - 0.15);
        }

        // The kicks of a few beats ago count for less, so the kind that has stopped fades.
        (_onAccent, _halfAccent) = (_onAccent * 0.8, _halfAccent * 0.8);

        return true;
    }

    /// <summary>
    /// Whether an onset heard <paramref name="age"/> seconds ago would be a beat: anywhere until the
    /// clock is locked, after that only near a beat of its grid.
    /// </summary>
    public bool Near(double age)
    {
        if (!Locked)
        {
            return true;
        }

        var at = _position - ((double.IsFinite(age) ? Math.Clamp(age, 0, 0.25) : 0) / Period);
        return Math.Abs(at - Math.Round(at)) <= Window;
    }

    /// <summary>
    /// An onset heard <paramref name="age"/> seconds ago, <paramref name="strong"/> when it is a
    /// kick, <paramref name="accent"/> how hard (0 to 1): near a beat of the grid, it confirms that
    /// beat and pulls the grid toward it. Kicks that keep landing halfway between the grid's beats,
    /// harder than any on them, mean the grid is on the off-beat, and it moves half a beat: the beat
    /// is where the hardest low drum falls, so a groove with a pitched drum on every off-beat (heard
    /// as a kick too when the frames come fast) keeps its beat on the kick.
    /// </summary>
    public void Onset(bool strong = false, double age = 0, double accent = 1)
    {
        accent = double.IsFinite(accent) ? Math.Clamp(accent, 0, 1) : 0;
        age = double.IsFinite(age) ? Math.Clamp(age, 0, 0.25) : 0;
        SinceOnset = age;
        SinceStrong = strong ? age : SinceStrong;
        if (strong && accent >= _hardest)
        {
            (_sinceHardest, _hardest) = (age, accent);
        }

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
            if (strong && Math.Abs(Math.Abs(error) - 0.5) < Window)
            {
                _halfAccent = Math.Max(_halfAccent, accent);
                if (++_halfVotes >= 3 && _halfAccent > 1.25 * _onAccent)
                {
                    _position += 0.5;
                    // Three consistent strong off-beats establish the corrected grid.
                    (_halfVotes, Confidence) = (0, Math.Max(0.6, Confidence * 0.5));
                    (_matched, _matchedBefore) = ((long)Math.Round(_position), -1);
                    (_onAccent, _halfAccent) = (_halfAccent, _onAccent);
                }
            }

            return;
        }

        if (strong)
        {
            _onAccent = Math.Max(_onAccent, accent);
            _halfVotes = _onAccent >= _halfAccent ? 0 : _halfVotes;
        }

        // Late onsets (a positive error) move the grid later and lengthen the beat; early ones the opposite.
        _position -= 0.5 * error;
        Period *= 1 + (0.15 * error);
        Confidence = Math.Min(1, Confidence + 0.2);
        if ((long)nearest != _matched)
        {
            // An onset just before a beat confirms that beat; the one before it stays confirmed.
            (_matchedBefore, _matched) = (_matched, (long)nearest);
        }
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
            Adopt(period, 0.35);
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
        else if (Math.Abs(ratio - 2) < 0.12 || Math.Abs(ratio - 0.5) < 0.03 || (Locked && Coherence(Tempo) >= 0.8 * _bestScore))
        {
            // Twice or half the beat: the same music counted another way, not a new tempo. Or the
            // locked tempo still fits the onsets nearly as well as the best: a passage (the kick out,
            // the log drums on) that lines up with itself elsewhere, not a new tempo either.
            _disagree = 0;
        }
        else if ((_disagree += _frameSeconds) >= (Locked ? 3.0 : 0.40))
        {
            Adopt(period, Confidence * 0.5);
        }
    }

    /// <summary>Forgets the beat, as when another song starts.</summary>
    public void Reset()
    {
        Array.Clear(_envelope);
        (_envelopeTime, _envelopeAt, _position, _matched, _matchedBefore, _disagree, _halfVotes) = (0, 0, 0, -1, -1, 0, 0);
        (_onAccent, _halfAccent, _sinceHardest, _hardest) = (0, 0, 10, 0);
        (Period, Confidence, SinceOnset, SinceStrong, _frameSeconds) = (0, 0, 10, 10, 0);
    }

    /// <summary>
    /// A new tempo, with the grid's beat on the hardest kick of late (not a softer drum between the
    /// beats), or the last onset when there has been no kick of late.
    /// </summary>
    private void Adopt(double period, double confidence)
    {
        (Period, Confidence, _disagree, _halfVotes, _onAccent, _halfAccent) = (period, confidence, 0, 0, 0, 0);
        var since = _sinceHardest < period * 4 ? _sinceHardest : SinceOnset;
        _position = since / period;
        (_matched, _matchedBefore) = (since < period * Window ? 0 : -1, -1);
    }

    private bool IsMatched(long beat) => _matched == beat || _matchedBefore == beat;

    /// <summary>
    /// Lays the strength into the envelope at its own fixed rate: every sample the frame's time
    /// reaches holds it, and a sample several frames share holds the strongest of them. Summing
    /// instead would make a steady strength rise and fall with the beat between the frame rate and
    /// the envelope's, a false tempo (sixty frames a second against fifty samples repeats every
    /// tenth of a second, so at every multiple of it: 120 beats a minute among them).
    /// </summary>
    private void Record(float strength, double dt)
    {
        var n = _envelope.Length;
        _envelopeTime += dt * EnvelopeRate;
        if (_envelopeTime < 1)
        {
            var newest = (_envelopeAt + n - 1) % n;
            _envelope[newest] = Math.Max(_envelope[newest], strength);
            return;
        }

        while (_envelopeTime >= 1)
        {
            _envelopeTime--;
            _envelope[_envelopeAt] = strength;
            _envelopeAt = (_envelopeAt + 1) % n;
        }
    }

    /// <summary>
    /// The seconds a beat at which the onsets of late line up best: for each tempo from the slowest
    /// to the fastest, in steps of a quarter beat a minute, how tightly the onsets cluster at one
    /// place in the beat and at one place in each half of it (the lengths of the means of their
    /// phases at the tempo and at twice it, each onset an arrow as long as its strength), weighed
    /// toward the preferred tempo. Onsets on every beat cancel at half that tempo; a bass hit halfway
    /// through every beat cancels the first measure but lines up in the second; and rhythms of three
    /// against two (a dotted beat, a 3-3-2 bass) cluster at neither four-thirds nor two-thirds of
    /// the tempo, where they line up with themselves. 0 when nothing clusters.
    /// </summary>
    internal double EstimatePeriod()
    {
        // The onsets: the samples of the envelope that stand out from its floor (its median, as a
        // busy mix keeps it up between the beats), oldest first, each weighed by how far.
        var n = _envelope.Length;
        Array.Copy(_envelope, _sorted, n);
        Array.Sort(_sorted);
        var (floor, loudest) = (_sorted[n / 2], _sorted[n - 1]);
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            var value = _envelope[(_envelopeAt + i) % n] - floor;
            if (loudest > floor && value >= (loudest - floor) * 0.2f)
            {
                (_onsetAt[count], _onsetWeight[count]) = (i, value);
                count++;
            }
        }

        // Too few, or too close together, and many tempos fit them.
        if (count < 4 || _onsetAt[count - 1] - _onsetAt[0] < EnvelopeRate * 1.5)
        {
            return 0;
        }

        _onsets = count;
        var (best, bestWeighted) = (0, 0.0);
        for (var step = 0; step < _scores.Length; step++)
        {
            var bpm = Slowest + (step * Step);
            _scores[step] = Coherence(bpm);
            var octaves = Math.Log2(bpm / Preferred);
            var weighted = _scores[step] * Math.Exp(-0.5 * octaves * octaves);
            if (weighted > bestWeighted)
            {
                (best, bestWeighted) = (step, weighted);
            }
        }

        // A clear cluster, or nothing is sure.
        _bestScore = _scores[best];
        if (_bestScore < 0.6)
        {
            return 0;
        }

        // Onsets on every beat line up just as well at twice the tempo, every other beat of it
        // empty; when they line up as well at half the tempo found, that is the beat.
        var half = (Slowest + (best * Step)) / 2;
        if (half >= Slowest * 0.9 && Coherence(half) >= 0.9 * _bestScore)
        {
            return 60 / half;
        }

        // Between steps: the top of the parabola through the best and its neighbours.
        var (a, b, c) = (_scores[Math.Max(0, best - 1)], _scores[best], _scores[Math.Min(_scores.Length - 1, best + 1)]);
        var bend = a - (2 * b) + c;
        var offset = bend < 0 ? Math.Clamp(0.5 * (a - c) / bend, -0.5, 0.5) : 0;
        return 60 / (Slowest + ((best + offset) * Step));
    }

    /// <summary>How tightly the onsets <see cref="EstimatePeriod"/> last gathered cluster at <paramref name="bpm"/>, 0 to 2.</summary>
    private double Coherence(double bpm)
    {
        var turn = Math.Tau * bpm / 60 / EnvelopeRate;
        var (x, y, x2, y2, total) = (0.0, 0.0, 0.0, 0.0, 0.0);
        for (var k = 0; k < _onsets; k++)
        {
            var (angle, weight) = (turn * _onsetAt[k], _onsetWeight[k]);
            (x, y) = (x + (weight * Math.Cos(angle)), y + (weight * Math.Sin(angle)));
            (x2, y2) = (x2 + (weight * Math.Cos(2 * angle)), y2 + (weight * Math.Sin(2 * angle)));
            total += weight;
        }

        return (Math.Sqrt((x * x) + (y * y)) + Math.Sqrt((x2 * x2) + (y2 * y2))) / total;
    }
}
