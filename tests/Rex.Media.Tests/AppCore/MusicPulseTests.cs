using System.Globalization;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The beat, tempo, levels, punch and drop the visualisations move to (AU-18).</summary>
public sealed class MusicPulseTests
{
    private sealed record Heard(double T, bool Beat, bool Filled, bool Drop, bool Locked, float Punch, float Bass, float Loudness, float Build, double Tempo);

    private static List<Heard> Listen(SyntheticMusic music, MusicPulse? pulse = null, int from = 1)
    {
        pulse ??= new MusicPulse();
        var heard = new List<Heard>();
        for (var frame = from; frame <= music.Frames; frame++)
        {
            pulse.Update(music.Mono(frame), SyntheticMusic.Rate, SyntheticMusic.FrameTime);
            heard.Add(new((double)frame / SyntheticMusic.Fps, pulse.Beat, pulse.Filled, pulse.Drop, pulse.Locked, pulse.Punch, pulse.Bass, pulse.Loudness, pulse.Build, pulse.Tempo));
        }

        return heard;
    }

    private static string Times(IEnumerable<double> times) => string.Join(", ", times.Select(t => t.ToString("0.000", CultureInfo.InvariantCulture)));

    /// <summary>Whether a beat at <paramref name="beat"/> is the kick at <paramref name="kick"/>: in the frame that first hears it, or the next.</summary>
    private static bool Matches(double beat, double kick) => beat >= kick - 0.035 && beat - kick < 0.075;

    public static readonly TheoryData<string> Kinds = new("90", "120", "140", "busy", "quiet", "loud");

    private static SyntheticMusic Make(string kind, double seconds = 12) => kind switch
    {
        "busy" => new SyntheticMusic(120, seconds, busy: true),
        "quiet" => new SyntheticMusic(124, seconds, gainDb: -30),
        "loud" => new SyntheticMusic(128, seconds, gainDb: 6),
        _ => new SyntheticMusic(double.Parse(kind, CultureInfo.InvariantCulture), seconds),
    };

    [Theory]
    [Capability("AU-18")]
    [MemberData(nameof(Kinds))]
    public void BeatsLandOnTheKicksTheTempoIsFoundAndTheClockLocks(string kind)
    {
        var music = Make(kind);
        var pulse = new MusicPulse();
        var heard = Listen(music, pulse);
        var beats = heard.Where(h => h.Beat).Select(h => h.T).ToList();

        // Every kick after the first second has its beat, at the frame that hears it or the next.
        var kicks = music.Kicks.Where(kick => kick > 1 && kick < (music.Frames / (double)SyntheticMusic.Fps) - 0.1).ToList();
        var missed = kicks.Where(kick => !beats.Any(beat => Matches(beat, kick))).ToList();
        Assert.True(missed.Count == 0, $"{kind}: no beat for the kicks at {Times(missed)}; beats at {Times(beats)}.");

        // And once the music has been heard a while, nearly every beat is a kick.
        var later = beats.Where(beat => beat > 1.5).ToList();
        var stray = later.Where(beat => !music.Kicks.Any(kick => Matches(beat, kick))).ToList();
        Assert.True(stray.Count <= later.Count / 20, $"{kind}: beats on no kick at {Times(stray)}.");

        Assert.InRange(pulse.Tempo, music.Bpm * 0.98, music.Bpm * 1.02);
        var locked = heard.First(h => h.Locked).T;
        Assert.True(locked <= 4, $"{kind}: the clock locked at {locked:0.00} s.");
        Assert.True(heard.Where(h => h.T > 4).All(h => h.Locked), $"{kind}: the clock let go.");
        Assert.InRange(pulse.Phase, 0, 1);
        Assert.InRange(pulse.NextBeatIn, 0, 60 / music.Bpm * 1.05);
        Assert.InRange(pulse.BeatInBar, 0, 3);
        Assert.Equal(MusicPulse.BandCount, pulse.Bands.Count);
        Assert.Equal(MusicPulse.BandCount, pulse.RawBands.Count);
        Assert.Equal(SpectrumAnalyzer.Size, pulse.Wave.Count);
    }

    [Theory]
    [Capability("AU-18")]
    [MemberData(nameof(Kinds))]
    public void EveryKickPunchesAndTheLevelsUseTheirWholeRangeHoweverLoudTheMaster(string kind)
    {
        var music = Make(kind, 8);
        var heard = Listen(music);
        var period = 60 / music.Bpm;
        foreach (var kick in music.Kicks.Where(kick => kick > 2 && kick < 7.5))
        {
            // The frame that hears the kick, or the next, punches; halfway to the next kick it is gone.
            var on = heard.Where(h => Matches(h.T, kick)).Max(h => h.Punch);
            var between = heard.Where(h => Math.Abs(h.T - (kick + (period / 2))) < 0.02).Max(h => h.Punch);
            Assert.True(on >= 0.8f, $"{kind}: the kick at {kick:0.000} punches only {on:0.00}.");
            Assert.True(between <= 0.25f, $"{kind}: still {between:0.00} punch halfway after the kick at {kick:0.000}.");

            // Over each beat the bass and the loudness swing across most of their range; less
            // where a held sub bass and a shaker on every sixteenth keep the mix full between kicks.
            var beat = heard.Where(h => h.T >= kick && h.T < kick + period).ToList();
            var swing = kind == "busy" ? 0.3f : 0.6f;
            Assert.True(beat.Max(h => h.Bass) - beat.Min(h => h.Bass) >= swing, $"{kind}: the bass swings only {beat.Max(h => h.Bass) - beat.Min(h => h.Bass):0.00}.");
            Assert.True(beat.Max(h => h.Loudness) - beat.Min(h => h.Loudness) >= swing, $"{kind}: the loudness swings only {beat.Max(h => h.Loudness) - beat.Min(h => h.Loudness):0.00}.");
        }
    }

    [Fact]
    [Capability("AU-18")]
    public void AKickTheMixHidesIsStoodInForOnTheBeat()
    {
        // The kick on beat 12 (six seconds in) is left out; the snare and hats play on.
        var music = new SyntheticMusic(120, 9, missing: [12]);
        var heard = Listen(music);
        var filled = heard.Where(h => h.Filled).ToList();
        var stood = Assert.Single(filled);
        Assert.True(stood.Beat);
        // It waits a moment past the beat for a late kick, then stands in, within a frame of that.
        Assert.InRange(stood.T, 6.07, 6.07 + (1.0 / SyntheticMusic.Fps) + 0.001);
        Assert.All(heard.Where(h => h.T > 5.6 && h.T < 6.4 && h.Beat), h => Assert.Same(stood, h));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheClockFollowsTheTempoWhenItChanges()
    {
        var music = new SyntheticMusic(120, 16, changeAt: 6, newBpm: 132);
        var pulse = new MusicPulse();
        var heard = Listen(music, pulse);
        Assert.InRange(heard.Last(h => h.T < 6).Tempo, 117.6, 122.4);
        Assert.InRange(pulse.Tempo, 132 * 0.98, 132 * 1.02);
        var beats = heard.Where(h => h.Beat && h.T > 12).Select(h => h.T).ToList();
        Assert.All(music.Kicks.Where(kick => kick > 12 && kick < 15.9), kick => Assert.Contains(beats, beat => Matches(beat, kick)));
    }

    [Fact]
    public void SilenceHasNoBeatNoTempoAndNoLevels()
    {
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 4, silence: true), pulse);

        Assert.DoesNotContain(heard, h => h.Beat);
        Assert.Equal(0, pulse.Tempo);
        Assert.Equal(0, pulse.Loudness);
        Assert.Equal(0, pulse.RawLoudness);
        Assert.Equal(0, pulse.Punch);
        Assert.Equal(0, pulse.Beats);
        Assert.Equal(1, pulse.Phase);
        Assert.False(pulse.Locked);
        Assert.True(pulse.SinceBeat > 4);
        Assert.All(pulse.Bands, band => Assert.Equal(0, band));
    }

    [Fact]
    [Capability("AU-18")]
    public void WhenTheMusicStopsEverythingSettlesAndTheTempoItHadIsKept()
    {
        var pulse = new MusicPulse();
        Listen(new SyntheticMusic(120, 8), pulse);
        var sure = pulse.Tempo;

        // Silence: nothing in it lines up with anything, so the tempo found stays; within two seconds all is still.
        var heard = Listen(new SyntheticMusic(120, 7, silence: true), pulse);

        Assert.InRange(pulse.Tempo, sure * 0.995, sure * 1.005);
        Assert.InRange(sure, 118, 122);
        Assert.All(heard.Where(h => h.T > 2), h => Assert.Equal((0f, 0f, 0f, false), (h.Punch, h.Bass, h.Loudness, h.Beat)));
        Assert.All(pulse.Bands, band => Assert.True(band < 0.01f));
        Assert.False(pulse.Locked);
    }

    [Fact]
    public void AHeldBassNoteHasNoPunchAndHoldsItsBands()
    {
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 6, tone: true), pulse);
        Assert.All(heard.Where(h => h.T > 1.5), h => Assert.Equal(0, h.Punch));
        Assert.DoesNotContain(heard.Where(h => h.T > 0.5), h => h.Beat);

        var before = pulse.Bands.ToArray();
        pulse.Update(new SyntheticMusic(120, 7, tone: true).Mono(200), SyntheticMusic.Rate, SyntheticMusic.FrameTime);
        Assert.All(before.Zip(pulse.Bands), pair => Assert.True(Math.Abs(pair.First - pair.Second) < 0.05f));
        Assert.True(pulse.Pitch < 0.15f, $"A 55 Hz note is low: the pitch is {pulse.Pitch:0.00}.");
    }

    [Fact]
    public void SensitivityLiftsOrHoldsDownEveryLevel()
    {
        var music = new SyntheticMusic(120, 3);
        float Sum(double sensitivity)
        {
            var pulse = new MusicPulse { Sensitivity = sensitivity };
            return Listen(music, pulse).Sum(h => h.Bass + h.Loudness + h.Punch) + pulse.Mid + pulse.Treble + pulse.Bands.Sum();
        }

        var (plain, more, less) = (Sum(1), Sum(3), Sum(0.4));
        Assert.True(more > plain * 1.2 && less < plain * 0.8, $"{less} < {plain} < {more}");
        Assert.Equal(plain, Sum(double.NaN));
        Assert.Equal(plain, Sum(-1));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheDropIsTheMusicComingBackAfterAQuietStretch()
    {
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 16, quietFrom: 6, quietUntil: 11), pulse);

        var drop = Assert.Single(heard, h => h.Drop);
        Assert.InRange(drop.T, 11, 11.2);
        Assert.InRange(pulse.SinceDrop, 4.7, 5.1);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheBuildRisesWhileTheKickIsAwayAndBreaksAtTheDrop()
    {
        // Twelve beats with no kick, the snare and hats going on, then the kick again.
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 12, missing: [.. Enumerable.Range(10, 12)]), pulse);
        Assert.All(heard.Where(h => h.T < 5), h => Assert.Equal(0, h.Build));
        Assert.True(heard.Where(h => h.T is > 10 and < 11).Max(h => h.Build) > 0.4f);
        var drop = Assert.Single(heard, h => h.Drop);
        Assert.InRange(drop.T, 11, 11.1);
        Assert.Equal(0, drop.Build);
    }

    [Fact]
    public void ResetForgetsTheSongAndBadInputIsRefused()
    {
        var pulse = new MusicPulse();
        Listen(new SyntheticMusic(120, 6), pulse);
        Assert.True(pulse.Beats > 0);

        pulse.Reset();

        Assert.Equal((0, 0L, 0f, 0f, 0f), (pulse.Tempo, pulse.Beats, pulse.Loudness, pulse.Bass, pulse.Punch));
        Assert.False(pulse.Locked);
        Assert.All(pulse.Wave, sample => Assert.Equal(0, sample));
        Assert.All(pulse.Bands, band => Assert.Equal(0, band));
        Assert.Throws<ArgumentOutOfRangeException>(() => pulse.Update(new float[MusicPulse.AnalysisSize], 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => pulse.Update(new float[SpectrumAnalyzer.Size], 48_000, TimeSpan.Zero));

        // After a reset it hears a new song as it heard the first.
        var again = Listen(new SyntheticMusic(120, 6), pulse);
        Assert.Contains(again, h => h.Beat);
        Assert.Same(pulse.Clock, pulse.Clock);
    }
}
