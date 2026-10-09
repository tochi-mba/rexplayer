using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class VisualStageTests
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>();

    [Fact]
    public void EveryCreativeSceneDrawsARealFrameFromMusic()
    {
        var left = Sound(110);
        var right = Sound(330);
        var pictures = new List<byte[]>();
        foreach (var choice in new[]
        {
            VisualizerChoice.Vinyl,
            VisualizerChoice.Halo,
            VisualizerChoice.Mirror,
            VisualizerChoice.Aurora,
            VisualizerChoice.Embers,
            VisualizerChoice.Ripples,
            VisualizerChoice.Strobe,
            VisualizerChoice.Silhouette,
            VisualizerChoice.BeatEdit,
        })
        {
            var stage = new VisualStage(160, 90, new Random(42));
            stage.Context.Seconds = 12;
            stage.Context.Progress = 0.4;
            stage.Context.Cover = Picture(24, 16);
            stage.Context.Camera = Picture(32, 24);
            stage.Context.Mask = PersonMask(32, 24);
            stage.Context.MaskWidth = 32;
            stage.Context.MaskHeight = 24;
            for (var frame = 0; frame < 12; frame++)
            {
                stage.Context.Seconds += 1.0 / 30;
                Assert.True(stage.Draw(choice, Defaults, left, right, 48_000, TimeSpan.FromSeconds(1.0 / 30)));
            }

            Assert.Contains(stage.Pixels, value => value != 0);
            pictures.Add([.. stage.Pixels]);
        }

        Assert.True(pictures.DistinctBy(Convert.ToBase64String).Count() >= 7);
    }

    [Fact]
    public void StageSizingSceneSelectionAndInputsAreSafe()
    {
        Assert.Equal((16, 9), VisualStage.SizeFor(0, double.NaN));
        Assert.Equal((560, 315), VisualStage.SizeFor(1920, 1080));
        Assert.Equal((320, 180), VisualStage.SizeFor(320, 180));
        Assert.False(VisualStage.Draws(VisualizerChoice.Spectrum));
        Assert.True(VisualStage.Draws(VisualizerChoice.BeatEdit));

        var stage = new VisualStage(64, 36, new Random(1));
        var sound = Sound(220);
        Assert.False(stage.Draw(VisualizerChoice.Spectrum, Defaults, sound, sound, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => stage.Draw(VisualizerChoice.Vinyl, null!, sound, sound, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => stage.Draw(VisualizerChoice.Vinyl, Defaults, [0], sound, 48_000, TimeSpan.Zero));
        stage.Reset();
    }

    [Fact]
    public void PicturesInterpolateAndBeatEditGradesDiffer()
    {
        var picture = new VisualPicture([0, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 255, 255], 2, 2);
        Assert.InRange(picture.Sample(0.5, 0.5, 2), 0.49f, 0.51f);
        Assert.Equal(0, picture.Sample(-10, -10, 0));

        var source = new Rgb(0.2f, 0.5f, 0.8f);
        var dark = new Rgb(0.05f, 0.1f, 0.2f);
        var bright = new Rgb(1, 0.7f, 0.3f);
        var grades = Enumerable.Range(0, 7).Select(i => BeatEditScene.Graded(i, source, dark, bright)).ToList();
        Assert.True(grades.Distinct().Count() >= 6);
    }

    private static float[] Sound(double frequency) =>
        [.. Enumerable.Range(0, SpectrumAnalyzer.Size).Select(i => (float)(Math.Sin(Math.Tau * frequency * i / 48_000) * (i % 900 < 120 ? 1 : 0.2)))];

    private static VisualPicture Picture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i * 7);
            pixels[i + 1] = (byte)(i * 3);
            pixels[i + 2] = (byte)i;
            pixels[i + 3] = 255;
        }

        return new VisualPicture(pixels, width, height);
    }

    private static byte[] PersonMask(int width, int height)
    {
        var mask = new byte[width * height];
        for (var y = 3; y < height - 2; y++)
        {
            for (var x = width / 4; x < width * 3 / 4; x++)
            {
                mask[(y * width) + x] = 255;
            }
        }

        return mask;
    }
}
