using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The beat, tempo, loudness and drop the visualisations move to (AU-18).</summary>
public sealed class MusicPulseTests
{
    private static List<(int Frame, MusicPulse Pulse, bool Beat, bool Drop)> Listen(SyntheticMusic music, MusicPulse? pulse = null)
    {
        pulse ??= new MusicPulse();
        var heard = new List<(int, MusicPulse, bool, bool)>();
        for (var frame = 1; frame <= music.Frames; frame++)
        {
            var (left, right) = music.At(frame);
            var mono = left.Zip(right, (l, r) => (l + r) / 2).ToArray();
            pulse.Update(mono, SyntheticMusic.Rate, SyntheticMusic.FrameTime);
            heard.Add((frame, pulse, pulse.Beat, pulse.Drop));
        }

        return heard;
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(140)]
    public void BeatsLandOnTheKicksAndTheTempoIsFound(double bpm)
    {
        var music = new SyntheticMusic(bpm, 10);
        var pulse = new MusicPulse();
        var beats = Listen(music, pulse).Where(frame => frame.Beat).Select(frame => (double)frame.Frame / SyntheticMusic.Fps).ToList();

        // Every beat after the first second is found, each within a frame or two of its kick.
        var period = 60 / bpm;
        var expected = Enumerable.Range(0, (int)(10 / period)).Select(n => n * period).Where(t => t > 1 && t < 9.9).ToList();
        foreach (var kick in expected)
        {
            Assert.Contains(beats, beat => beat >= kick && beat - kick < 0.1);
        }

        Assert.InRange(beats.Count, expected.Count, expected.Count + 3);
        Assert.InRange(pulse.Tempo, bpm * 0.97, bpm * 1.03);
        Assert.InRange(pulse.Phase, 0, 1);
        Assert.True(pulse.Bass > 0 && pulse.Mid > 0 && pulse.Loudness > 0.4f);
        Assert.Equal(MusicPulse.BandCount, pulse.Bands.Count);
        Assert.Equal(SpectrumAnalyzer.Size, pulse.Wave.Count);
    }

    [Fact]
    public void SilenceHasNoBeatNoTempoAndNoLoudness()
    {
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 4, silence: true), pulse);

        Assert.DoesNotContain(heard, frame => frame.Beat);
        Assert.Equal(0, pulse.Tempo);
        Assert.Equal(0, pulse.Loudness);
        Assert.Equal(0, pulse.Beats);
        Assert.Equal(1, pulse.Phase);
        Assert.True(pulse.SinceBeat > 4);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheDropIsTheMusicComingBackAfterAQuietStretch()
    {
        var pulse = new MusicPulse();
        var heard = Listen(new SyntheticMusic(120, 16, quietFrom: 6, quietUntil: 11), pulse);

        var drops = heard.Where(frame => frame.Drop).Select(frame => (double)frame.Frame / SyntheticMusic.Fps).ToList();
        var drop = Assert.Single(drops);
        Assert.InRange(drop, 11, 11.7);
        Assert.InRange(pulse.SinceDrop, 4, 5.1);
    }

    [Fact]
    public void ResetForgetsTheSongAndBadInputIsRefused()
    {
        var pulse = new MusicPulse();
        Listen(new SyntheticMusic(120, 6), pulse);
        Assert.True(pulse.Beats > 0);

        pulse.Reset();

        Assert.Equal((0, 0L, 0f, 0f), (pulse.Tempo, pulse.Beats, pulse.Loudness, pulse.Bass));
        Assert.All(pulse.Wave, sample => Assert.Equal(0, sample));
        Assert.Throws<ArgumentOutOfRangeException>(() => pulse.Update(new float[SpectrumAnalyzer.Size], 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => pulse.Update(new float[3], 48_000, TimeSpan.Zero));
    }

    [Fact]
    public void AnUnsteadyRhythmKeepsTheLastTempoItWasSureOf()
    {
        var pulse = new MusicPulse();
        Listen(new SyntheticMusic(120, 8), pulse);
        var sure = pulse.Tempo;

        // Kicks at random spacing: no spacing stands out, so the tempo stays as it was.
        var random = new Random(3);
        var samples = new float[SyntheticMusic.Rate * 6];
        var next = 0.0;
        while (next < 5.8)
        {
            for (var i = 0; i < 2400; i++)
            {
                samples[(int)(next * SyntheticMusic.Rate) + i] = (float)(0.8 * Math.Sin(Math.Tau * 60 * i / SyntheticMusic.Rate) * Math.Exp(-i / 600.0));
            }

            next += 0.2 + (random.NextDouble() * 0.6);
        }

        for (var frame = 1; frame < 6 * SyntheticMusic.Fps; frame++)
        {
            var end = frame * SyntheticMusic.Rate / SyntheticMusic.Fps;
            var window = new float[SpectrumAnalyzer.Size];
            Array.Copy(samples, Math.Max(0, end - window.Length), window, Math.Max(0, window.Length - end), Math.Min(end, window.Length));
            pulse.Update(window, SyntheticMusic.Rate, SyntheticMusic.FrameTime);
        }

        Assert.Equal(sure, pulse.Tempo);
    }
}
