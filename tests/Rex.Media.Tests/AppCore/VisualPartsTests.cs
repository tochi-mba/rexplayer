using Rex.Media.AppCore.Visuals;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The drums the visualisations hear, the noise they fold with, and the stage keeping up (AU-18).</summary>
public sealed class VisualPartsTests
{
    [Fact]
    [Capability("AU-18")]
    public void TheKickSnareAndHatsAreEachHeardOnTheirOwnBeats()
    {
        // 120 beats a minute: kicks on every beat, snares on two and four, hats on every half beat.
        var music = new SyntheticMusic(120, 10);
        var pulse = new MusicPulse();
        var (kicks, snares, hats) = (new List<double>(), new List<double>(), new List<double>());
        for (var frame = 1; frame <= music.Frames; frame++)
        {
            var (left, right) = music.At(frame);
            pulse.Update([.. left.Zip(right, (l, r) => (l + r) / 2)], SyntheticMusic.Rate, SyntheticMusic.FrameTime);
            var t = (double)frame / SyntheticMusic.Fps;
            if (pulse.Kick.Hit)
            {
                kicks.Add(t);
            }

            if (pulse.Snare.Hit)
            {
                snares.Add(t);
            }

            if (pulse.Hat.Hit)
            {
                hats.Add(t);
            }
        }

        // Every kick after the first second is found, close to its beat.
        foreach (var beat in Enumerable.Range(2, 17).Select(n => n * 0.5))
        {
            Assert.Contains(kicks, kick => kick >= beat && kick - beat < 0.1);
        }

        Assert.InRange(kicks.Count, 17, 21);
        Assert.InRange(snares.Count, 6, 22);
        Assert.True(hats.Count >= snares.Count, $"{hats.Count} hats, {snares.Count} snares.");
        Assert.True(pulse.Kick.Count == kicks.Count && pulse.Kick.Since < 0.6 && pulse.Kick.Strength is >= 0.25f and <= 1);

        pulse.Reset();
        Assert.Equal((0L, 0f, false), (pulse.Kick.Count, pulse.Kick.Level, pulse.Kick.Hit));
    }

    [Fact]
    public void AnOnsetsLevelFallsAwayAfterEachHitAndAQuietOneIsNoHit()
    {
        var onset = new Onset(0.25, 0.2);
        for (var i = 0; i < 10; i++)
        {
            onset.Hear(0.01, 0.1, 0.033, listening: true);
        }

        Assert.False(onset.Hit);
        onset.Hear(1, 4, 0.033, listening: true);
        Assert.True(onset.Hit);
        Assert.Equal(1, onset.Strength);
        Assert.Equal(onset.Strength, onset.Level);
        onset.Hear(0.01, 0.1, 0.1, listening: true);
        Assert.InRange(onset.Level, 0.45f, 0.55f);

        // Too soon after the last, or not listening: no hit however loud.
        onset.Hear(1, 4, 0.001, listening: true);
        Assert.False(onset.Hit);
        onset.Hear(1, 4, 1, listening: false);
        Assert.False(onset.Hit);
        Assert.Equal(1, onset.Count);
        Assert.Equal(0, onset.Level);

        // A softer hit than the hardest of late is weaker, by its energy.
        for (var i = 0; i < 10; i++)
        {
            onset.Hear(0.01, 0.1, 0.033, listening: true);
        }

        onset.Hear(1, 2, 0.033, listening: true);
        Assert.True(onset.Hit);
        Assert.InRange(onset.Strength, 0.5f, 0.75f);
    }

    [Fact]
    public void NoiseIsSmoothRepeatableAndInRange()
    {
        Assert.Equal(Noise.At(3.7), Noise.At(3.7));
        Assert.Equal(Noise.Hash(5), Noise.At(5));
        Assert.Equal(0, Noise.At(double.NaN));
        var values = Enumerable.Range(0, 1000).Select(i => Noise.Fractal(i * 0.013)).ToList();
        Assert.All(values, value => Assert.InRange(value, 0, 1));
        Assert.True(values.Zip(values.Skip(1), (a, b) => Math.Abs(a - b)).Max() < 0.1, "Nearby places have nearby values.");
        Assert.True(values.Max() - values.Min() > 0.3, "It wanders.");
        Assert.InRange(Noise.Hash(-7), 0, 1);
    }

    [Fact]
    public void TheStageDrawsSmallerWhenFramesAreSlowAndGrowsBackWhenFast()
    {
        var stage = new VisualStage(480, 270);
        Assert.Equal(1, stage.Quality);
        Assert.False(stage.WantsResize);

        stage.Took(80);
        Assert.True(stage.WantsResize);
        Assert.Equal(0.85, stage.Quality, 3);
        Assert.Equal((408, 230), VisualStage.SizeFor(1920, 1080, stage.Quality));

        for (var i = 0; i < 200; i++)
        {
            stage.Took(80);
        }

        Assert.Equal(VisualStage.LowestQuality, stage.Quality);
        stage.Resize(192, 108);
        Assert.False(stage.WantsResize);
        Assert.Equal((192, 108), (stage.Canvas.Width, stage.Canvas.Height));
        Assert.Same(stage.Canvas, stage.Context.Canvas);
        stage.Resize(192, 108);

        // Steady and comfortable: no change; quick: back up a step at a time, to the full size.
        stage.Took(17);
        Assert.False(stage.WantsResize);
        for (var i = 0; i < 400; i++)
        {
            stage.Took(2);
        }

        Assert.Equal(1, stage.Quality);
        Assert.InRange(stage.DrawMilliseconds, 1, 17);
    }
}
