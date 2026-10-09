using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;

namespace Rex.Media.Tests.AppCore;

/// <summary>The visualisations' canvas: light that adds up, soft shapes, trails, bloom and the tone curve (AU-18).</summary>
public sealed class RasterTests
{
    private static readonly Rgb Red = new(1, 0, 0);

    [Fact]
    public void LightAddsUpAndTheToneCurveRollsItOffWithoutClipping()
    {
        Assert.Equal(0, Raster.Tone(0));
        Assert.Equal(0, Raster.Tone(-1));
        Assert.Equal(0, Raster.Tone(float.NaN));
        Assert.Equal(128, Raster.Tone(0.5f));
        Assert.InRange(Raster.Tone(1), 220, 240);
        Assert.Equal(255, Raster.Tone(50));
        Assert.True(Raster.Tone(2) > Raster.Tone(1));

        var canvas = new Raster(4, 4);
        canvas.Fill(0, 0, 4, 4, Red, 0.25f);
        canvas.Fill(1, 1, 1, 1, Red, 0.25f);
        Assert.Equal(new Rgb(0.5f, 0, 0), canvas[1, 1]);
        Assert.Equal(new Rgb(0.25f, 0, 0), canvas[0, 0]);
        canvas.Fade(0.5f);
        Assert.Equal(new Rgb(0.25f, 0, 0), canvas[1, 1]);

        var pixels = new byte[4 * 4 * 4];
        canvas.ToBgra(pixels);
        Assert.Equal([0, 0, Raster.Tone(0.25f), 255], pixels[20..24]);
        Assert.Throws<ArgumentException>(() => canvas.ToBgra(new byte[3]));
        Assert.Equal(0.2126f * 0.15625f, canvas.AverageLuma(), 5);
    }

    [Fact]
    public void ShapesAreSoftAndStayOnTheCanvas()
    {
        var canvas = new Raster(40, 30);

        canvas.Glow(20, 15, 8, Red);
        Assert.Equal(1, canvas[20, 15].R, 3);
        Assert.True(canvas[24, 15].R is > 0 and < 1);
        Assert.Equal(0, canvas[29, 15].R);
        canvas.Glow(20, 15, 0, Red);
        canvas.Glow(20, 15, 5, Red, 0);

        canvas.Clear(Rgb.Black);
        canvas.Disc(-5, -5, 12, Red);
        Assert.Equal(1, canvas[0, 0].R);
        Assert.Equal(0, canvas[20, 20].R);

        canvas.Clear(Rgb.Black);
        canvas.Line(2, 2, 37, 2, 2, Red, 1, halo: 2);
        Assert.Equal(1, canvas[20, 2].R);
        Assert.True(canvas[20, 4].R is > 0 and < 1);
        Assert.Equal(0, canvas[20, 20].R);
        canvas.Line(5, 5, 5, 5, 3, Red);
        Assert.True(canvas[5, 5].R > 0);

        canvas.Clear(Rgb.Black);
        canvas.Ring(20, 15, 10, 2, Red);
        Assert.Equal(1, canvas[30, 15].R);
        Assert.Equal(0, canvas[20, 15].R);

        canvas.Clear(Rgb.Black);
        canvas.Path([(5, 5), (35, 5), (35, 25)], 1, Red, closed: true);
        Assert.True(canvas[20, 15].R > 0);
        canvas.Path([(1, 1)], 1, Red);
        canvas.Fill(-10, -10, 100, 100, Red);
        Assert.Throws<ArgumentNullException>(() => canvas.Path(null!, 1, Red));
    }

    [Fact]
    public void FeedbackLeavesTrailsThatTurnAndScale()
    {
        var canvas = new Raster(21, 21);
        canvas.Fill(15, 10, 1, 1, Red);

        // Kept as it is.
        canvas.Feedback(1, 1, 0, 0, 0);
        Assert.Equal(1, canvas[15, 10].R, 4);

        // Half a turn about the middle, faded by half.
        canvas.Feedback(0.5f, 1, Math.PI, 0, 0);
        Assert.Equal(0.5f, canvas[5, 10].R, 3);
        Assert.Equal(0, canvas[15, 10].R, 3);

        // Doubled in size about the middle, then moved one pixel down; a nonsense zoom is no zoom.
        canvas.Clear(Rgb.Black);
        canvas.Fill(12, 10, 1, 1, Red);
        canvas.Feedback(1, 2, 0, 0, 1);
        Assert.True(canvas[14, 11].R > 0.4f);
        canvas.Feedback(1, 0, 0, 0, 0);
        canvas.Feedback(2, 1, 0, 0, 0);
    }

    [Fact]
    public void BloomSpreadsOnlyTheBrightLight()
    {
        var canvas = new Raster(30, 30);
        canvas.Fill(15, 15, 1, 1, Red, 4);
        canvas.Fill(2, 2, 1, 1, Red, 0.2f);

        canvas.Bloom(0.5f, 1, 3);

        Assert.True(canvas[18, 15].R > 0, "The bright light glows round it.");
        Assert.Equal(0, canvas[4, 2].R);
        var before = canvas[18, 15];
        canvas.Bloom(0.5f, 0, 3);
        canvas.Bloom(0.5f, 1, 0);
        Assert.Equal(before, canvas[18, 15]);
    }

    [Fact]
    public void PicturesAreDrawnThroughAMapAndSampledBetweenPixels()
    {
        // Two by two: blue, red / green, white.
        var picture = new VisualPicture([255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 255, 255], 2, 2);
        Assert.Equal(0.5f, picture.Sample(0.5, 0.5, 2), 3);
        Assert.Equal(1, picture.Sample(-3, -3, 0));
        Assert.Equal(1, picture.Sample(9, 9, 1));

        var canvas = new Raster(4, 4);
        canvas.Picture(picture.Bgra, 2, 2, (x, y) => (x - 1, y - 1));
        Assert.Equal(new Rgb(0, 0, 1), canvas[1, 1]);
        Assert.Equal(new Rgb(1, 1, 1), canvas[2, 2]);
        Assert.Equal(Rgb.Black, canvas[0, 0]);
        Assert.Throws<ArgumentException>(() => canvas.Picture(new byte[3], 2, 2, (x, y) => (x, y)));
        Assert.Throws<ArgumentException>(() => canvas.Picture(picture.Bgra, 0, 2, (x, y) => (x, y)));
        Assert.Throws<ArgumentNullException>(() => canvas.Picture(picture.Bgra, 2, 2, null!));
    }

    [Fact]
    public void ColoursMixAndTheCanvasHasASize()
    {
        Assert.Equal(new Rgb(0.5f, 0.25f, 0), new Rgb(1, 0.5f, 0).Times(0.5f));
        Assert.Equal(new Rgb(1, 1, 0), Red.Plus(new Rgb(0, 1, 0)));
        Assert.Equal(new Rgb(0.5f, 0, 0.5f), Red.Toward(new Rgb(0, 0, 1), 0.5f));
        Assert.Equal(1, Rgb.White.Luma, 4);
        Assert.Equal(new Rgb(1, 0, 0), Rgb.From(new Argb(255, 255, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Raster(1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Raster(5, 1));
    }

    [Fact]
    public void SparksLiveFadeAndMove()
    {
        var sparks = new Particles(2);
        Assert.Equal(2, sparks.Capacity);
        sparks.Add(10, 10, 100, 0, 3, 0.5, 1);
        sparks.Add(10, 10, 0, 0, 3, 0.5, 0.01);
        sparks.Add(5, 5, 0, 0, 3, 0.5, 1);
        Assert.Equal(2, sparks.Alive);

        sparks.Step(0.1, 0, 10, 0, (_, _) => 1);
        Assert.Equal(2, sparks.Alive);
        sparks.Step(0.95, 0, 0, 1);
        Assert.Equal(0, sparks.Alive);

        var context = new VisualContext(new Raster(20, 20), new MusicPulse());
        context.Prepare();
        sparks.Add(10, 10, 0, 0, 4, 0, 1);
        sparks.Draw(context);
        Assert.True(context.Canvas[10, 10].Luma > 0);
        sparks.Clear();
        Assert.Equal(0, sparks.Alive);
        Assert.Throws<ArgumentNullException>(() => sparks.Draw(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Particles(0));
    }
}
