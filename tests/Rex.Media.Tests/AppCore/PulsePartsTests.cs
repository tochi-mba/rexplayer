using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The parts the beat is heard with: the adaptive range, the band filters, the band layout and the beat clock (AU-18).</summary>
public sealed class PulsePartsTests
{
    [Fact]
    [Capability("AU-18")]
    public void TheRangeFollowsTheSongsOwnQuietAndLoud()
    {
        var range = new AdaptiveRange();
        Assert.True(double.IsNaN(range.Floor) && double.IsNaN(range.Ceiling));

        // A quiet song swinging between -50 and -40 dB uses the whole range, as a loud one between -15 and -5 does.
        foreach (var top in new[] { -40.0, -5.0 })
        {
            range.Reset();
            float low = 1, high = 0;
            for (var frame = 0; frame < 300; frame++)
            {
                var value = range.Map(frame % 15 == 0 ? top : top - 10, 1.0 / 30);
                if (frame > 60)
                {
                    (low, high) = (Math.Min(low, value), Math.Max(high, value));
                }
            }

            Assert.True(high > 0.95f && low < 0.05f, $"At {top} dB the range runs {low:0.00} to {high:0.00}.");
            Assert.InRange(range.Ceiling - range.Floor, 10, 11);
        }

        // A level held steady sits at the top of a range the span wide, never swinging from nothing to everything.
        range.Reset();
        for (var frame = 0; frame < 300; frame++)
        {
            range.Map(-20, 1.0 / 30);
        }

        Assert.InRange(range.Value, 0.95f, 1f);
        Assert.InRange(range.Ceiling - range.Floor, 9.99, 10.01);

        // Silence is nothing at once, and the ceiling sinks while it lasts.
        var ceiling = range.Ceiling;
        Assert.Equal(0, range.Map(double.NegativeInfinity, 1));
        Assert.Equal(0, range.Map(-90, 0.5));
        Assert.True(range.Ceiling < ceiling);
        Assert.Equal(0, range.Map(double.NaN, double.NaN));
        Assert.True(range.Map(-20, double.PositiveInfinity) > 0);
        Assert.True(AdaptiveRange.Decibels(0) < AdaptiveRange.Gate);
        Assert.Equal(-20, AdaptiveRange.Decibels(0.01), 6);

        // Silence before anything was heard leaves the range unknown.
        var fresh = new AdaptiveRange();
        Assert.Equal(0, fresh.Map(-120, 0.1));
        Assert.True(double.IsNaN(fresh.Ceiling));
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(60, 30, 150, true)]
    [InlineData(800, 150, 2000, true)]
    [InlineData(6000, 2000, 0, true)]
    [InlineData(1000, 30, 150, false)]
    [InlineData(60, 2000, 0, false)]
    [InlineData(6000, 150, 2000, false)]
    public void ABandFilterHearsItsOwnFrequenciesAndNotOthers(double hertz, double low, double high, bool inside)
    {
        var band = new BandEnergy(low, high);
        var sine = Enumerable.Range(0, MusicPulse.AnalysisSize).Select(i => (float)Math.Sin(Math.Tau * hertz * i / 48_000)).ToArray();
        var power = band.Power(sine, 48_000, 1200);

        // A full-scale sine has a mean power of a half.
        if (inside)
        {
            Assert.InRange(power, 0.35, 0.55);
        }
        else
        {
            Assert.True(power < 0.02, $"{hertz} Hz leaks {power:0.000} through {low} to {high} Hz.");
        }
    }

    [Fact]
    public void ABandFilterSettlesRedesignsForANewRateAndRefusesNonsense()
    {
        var whole = new BandEnergy(0, 0);
        Assert.Equal(0, whole.RiseAge()); // No analysed samples yet.
        Assert.Equal(1, whole.Power(Enumerable.Repeat(1f, 100).ToArray(), 48_000, 1000), 6);
        Assert.Equal(0.25, whole.Power([0, 0, 0.5f, 0.5f], 48_000, 2), 6);
        Assert.Equal(0, whole.Earlier);

        // Looking back: the window as long, ending that many samples before the newest.
        Assert.Equal(0, whole.Power([0.5f, 0.5f, 0, 0], 48_000, 2, lag: 2), 6);
        Assert.Equal(0.25, whole.Earlier, 6);
        Assert.Equal(0.25, whole.Power([0.5f, 0.5f, 0, 0, 0.5f], 48_000, 1, lag: 9), 6);
        Assert.Equal(0.25, whole.Earlier, 6);

        // A corner above the rate's limit is left out rather than made unstable.
        var high = new BandEnergy(2000, 20000);
        var noise = Enumerable.Range(0, 4096).Select(i => (float)Math.Sin(i * 1.3) * 0.5f).ToArray();
        Assert.True(double.IsFinite(high.Power(noise, 48_000, 1200)));
        Assert.True(double.IsFinite(high.Power(noise, 32_000, 1200)));
        Assert.True(double.IsFinite(high.Power(noise, 32_000, 0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BandEnergy(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BandEnergy(200, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => whole.Power(noise, 0, 10));
    }

    [Fact]
    public void BandsShareTheRangeByRatioEachAtLeastABin()
    {
        var (from, to) = BandLayout.Bins(0, 64, 40, 16000, 48_000, 2048);
        Assert.Equal((2, 2), (from, to));
        var last = BandLayout.Bins(63, 64, 40, 16000, 48_000, 2048);
        Assert.True(last.From <= last.To && last.To <= 1023);
        Assert.Equal((40.0, 16000.0), (BandLayout.Edges(0, 1, 40, 16000).Low, BandLayout.Edges(0, 1, 40, 16000).High));
        Assert.Equal(25, BandLayout.Power([3, 4], 0, 1), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => BandLayout.Bins(0, 0, 40, 16000, 48_000, 2048));
        Assert.Throws<ArgumentOutOfRangeException>(() => BandLayout.Bins(0, 4, 40, 16000, 0, 2048));
        Assert.Throws<ArgumentOutOfRangeException>(() => BandLayout.Bins(0, 4, 40, 16000, 48_000, 2));

        // A transform's working space can be the caller's; the wrong sizes are refused.
        var samples = Enumerable.Range(0, 8).Select(i => (float)Math.Sin(i)).ToArray();
        var (a, b) = (new float[4], new float[4]);
        Fft.Magnitudes(samples, a);
        Fft.Magnitudes(samples, b, new float[8], new float[8]);
        Assert.Equal(a, b);
        Assert.Throws<ArgumentException>(() => Fft.Magnitudes(samples, b, new float[4], new float[8]));
        Assert.Throws<ArgumentException>(() => Fft.Magnitudes(samples, b, new float[8], new float[4]));
    }

    /// <summary>Runs a clock at 30 frames a second over <paramref name="seconds"/>, with an onset at each of <paramref name="onsets"/>.</summary>
    private static List<(double T, bool Crossed)> Run(BeatClock clock, double seconds, IEnumerable<double> onsets, double start = 0, bool strong = true)
    {
        var due = new Queue<double>(onsets.Where(t => t >= start).Order());
        var frames = new List<(double, bool)>();
        for (var t = start + (1.0 / 30); t <= start + seconds; t += 1.0 / 30)
        {
            var hit = due.Count > 0 && due.Peek() <= t;
            while (due.Count > 0 && due.Peek() <= t)
            {
                due.Dequeue();
            }

            var crossed = clock.Advance(1.0 / 30, hit ? 1 : 0);
            if (hit)
            {
                clock.Onset(strong);
            }

            if (hit && clock.SinceOnset < 2)
            {
                clock.Estimate();
            }

            frames.Add((t, crossed));
        }

        return frames;
    }

    private static IEnumerable<double> Beats(double bpm, double from, double until) =>
        Enumerable.Range(0, (int)((until - from) * bpm / 60)).Select(n => from + (n * 60 / bpm));

    [Theory]
    [Capability("AU-18")]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(128)]
    [InlineData(174)]
    public void TheClockFindsTheTempoLocksAndKeepsTimeThroughAGap(double bpm)
    {
        var clock = new BeatClock();
        Assert.Equal((0.0, 0.0, false), (clock.Tempo, clock.Period, clock.Locked));

        // Ten seconds of beats, then three beats with none heard: the grid crosses them on time.
        var onsets = Beats(bpm, 0, 10).ToList();
        Run(clock, 10, onsets);
        Assert.InRange(clock.Tempo, bpm * 0.98, bpm * 1.02);
        Assert.True(clock.Locked);
        var gap = Run(clock, 60 * 3.2 / bpm, [], start: 10);
        var crossings = gap.Where(frame => frame.Crossed).Select(frame => frame.T).ToList();
        // A beat at exactly the boundary may cross in the first frame of the gap;
        // include it if it lands within one frame of the starting time.
        var gapEnd = 10 + (60 * 3.2 / bpm);
        var expected = Beats(bpm, 0, gapEnd + (60 / bpm))
            .Where(t => t >= 10 - 0.04 && t <= gapEnd + 0.05).ToList();
        Assert.Equal(expected.Count, crossings.Count);
        foreach (var (beat, at) in expected.Zip(crossings))
        {
            Assert.InRange(at - beat, -0.04, 0.07);
        }

        Assert.InRange(clock.Phase, 0, 1);
    }

    [Fact]
    public void TheClockMovesHalfABeatWhenTheKicksKeepLandingBetweenItsBeats()
    {
        var clock = new BeatClock();
        Run(clock, 8, Beats(120, 0, 8));
        Assert.True(clock.Locked);

        // The music turns round half a beat: weak onsets there are ignored, three kicks move the grid.
        Run(clock, 2, Beats(120, 8.25, 10), start: 8, strong: false);
        Run(clock, 4, Beats(120, 10.25, 14), start: 10);
        Assert.True(clock.Confidence > 0.5);
        var crossings = Run(clock, 2, Beats(120, 14.25, 16), start: 14).Where(frame => frame.Crossed).Select(frame => frame.T % 0.5).ToList();
        Assert.All(crossings, at => Assert.InRange(at, 0.2, 0.33));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(48)]
    [InlineData(60)]
    [InlineData(144)]
    public void ASteadyStrengthHasNoTempoWhateverTheFrameRate(int fps)
    {
        // The envelope used to add up the frames that fell in each of its samples: at sixty frames
        // a second against fifty samples every fifth sample held two, and a steady strength read
        // as a beat at 120 a minute.
        var clock = new BeatClock();
        for (var frame = 0; frame < fps * 10; frame++)
        {
            clock.Advance(1.0 / fps, 1);
        }

        Assert.Equal(0, clock.EstimatePeriod());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void TheClockFindsTheTempoOfOnsetsHeardAtTheWindowsFrameRates(int fps)
    {
        // Onsets on a busy floor of steady strength, as the spectral flux of a full mix is.
        var clock = new BeatClock();
        var next = 0.0;
        for (var frame = 1; frame <= fps * 8; frame++)
        {
            var t = (double)frame / fps;
            var hit = t >= next;
            next = hit ? next + 0.56 : next;
            clock.Advance(1.0 / fps, hit ? 3 : 1);
        }

        Assert.InRange(clock.EstimatePeriod(), 0.55, 0.57);
    }

    [Fact]
    public void AnOnsetJustBeforeTheGridsBeatConfirmsItAndKeepsTheBeatBefore()
    {
        var clock = new BeatClock();
        Run(clock, 8, Beats(120, 0, 8));
        Assert.True(clock.Locked);

        // The next beat is heard as it comes, so the one under test has a confirmed beat before it.
        while (!clock.Advance(0.005, 0))
        {
        }

        clock.Onset(strong: true);
        Assert.True(clock.Heard);
        Assert.Equal(1, clock.Confidence, 6);

        // A kick a tenth of a beat before the grid's next beat.
        while (clock.Phase < 0.9)
        {
            clock.Advance(0.005, 0);
        }

        Assert.False(clock.Near(0.25 * 0.5));
        Assert.True(clock.Near(0));
        clock.Onset(strong: true);
        var crossed = false;
        while (!crossed)
        {
            crossed = clock.Advance(0.005, 0);
        }

        // The beat before was heard and so was this one: the clock is as sure as it was, where it
        // used to forget the beat before and lose a step of confidence.
        Assert.True(clock.Heard);
        Assert.Equal(1, clock.Confidence, 6);
        Assert.True(new BeatClock().Near(0.1));
    }

    [Fact]
    public void TheClockLetsGoInSilenceIgnoresOctavesAndForgetsOnReset()
    {
        var clock = new BeatClock();
        Run(clock, 8, Beats(120, 0, 8));
        Assert.True(clock.Locked);

        // Silence: each beat passed unheard makes it less sure, until it is not locked.
        Run(clock, 3, [], start: 8);
        Assert.False(clock.Locked);
        Assert.InRange(clock.Tempo, 118, 122);

        // Onsets at twice the beat are the same music counted another way.
        Run(clock, 6, Beats(240, 11, 17), start: 11);
        Assert.InRange(clock.Tempo, 115, 125);

        // A new tempo, held long enough while the clock is unsure, replaces the old.
        Run(clock, 3, [], start: 17);
        Run(clock, 12, Beats(90, 20, 32), start: 20);
        Assert.InRange(clock.Tempo, 88, 92);

        // Nothing but steady noise: no tempo stands out.
        var flat = new BeatClock();
        for (var frame = 0; frame < 400; frame++)
        {
            flat.Advance(0.02, 1);
        }

        Assert.Equal(0, flat.EstimatePeriod());
        flat.Onset();
        flat.Estimate();
        Assert.Equal(0, flat.Tempo);

        Assert.False(clock.Advance(double.NaN, float.NaN));
        clock.Reset();
        Assert.Equal((0.0, 0.0, false, 10.0, 10.0), (clock.Tempo, clock.Confidence, clock.Locked, clock.SinceOnset, clock.SinceStrong));
    }

    [Fact]
    public void TheMedianAndSpreadIgnoreAFewLargeValues()
    {
        var scratch = new double[8];
        Assert.Equal((0.0, 0.0), Onset.Robust([], scratch));
        var (median, spread) = Onset.Robust([1, 1, 2, 2, 100], scratch);
        Assert.Equal(2, median);
        Assert.Equal(1.4826, spread, 6);
        Assert.Equal(1.5, Onset.Robust([1, 2], scratch).Median);
        Assert.Throws<ArgumentNullException>(() => Onset.Robust(null!, scratch));
        Assert.Throws<ArgumentNullException>(() => Onset.Robust([1], null!));
    }
}
