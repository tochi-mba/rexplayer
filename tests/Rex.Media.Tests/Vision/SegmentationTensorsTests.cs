using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class SegmentationTensorsTests
{
    private static byte[] Bgra(int width, int height, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = colour(x, y);
                (pixels[((y * width) + x) * 4], pixels[(((y * width) + x) * 4) + 1], pixels[(((y * width) + x) * 4) + 2]) = (b, g, r);
            }
        }

        return pixels;
    }

    [Fact]
    public void ThePictureBecomesRedGreenBluePlanesBetweenZeroAndOne()
    {
        var pixels = Bgra(6, 3, (_, _) => (51, 102, 255));
        var tensor = new float[3 * 4 * 4];

        SegmentationTensors.Image(new BgraPicture(pixels, 6, 3, 24), 4, tensor);

        Assert.All(tensor[..16], value => Assert.Equal(1f, value, 5));
        Assert.All(tensor[16..32], value => Assert.Equal(0.4f, value, 5));
        Assert.All(tensor[32..], value => Assert.Equal(0.2f, value, 5));
    }

    [Fact]
    public void ResamplingIsBilinearOnPixelCentres()
    {
        // Two columns, 0 and 200, stretched to four: the outer samples clamp, the inner ones blend.
        var pixels = Bgra(2, 1, (x, _) => (0, 0, (byte)(x * 200)));
        var tensor = new float[3 * 4 * 4];

        SegmentationTensors.Image(new BgraPicture(pixels, 2, 1, 8), 4, tensor);

        Assert.Equal([0f, 50f, 150f, 200f], tensor[..4].Select(value => MathF.Round(value * 255, 3)));
        Assert.Equal(tensor[..4], tensor[12..16]);
    }

    [Fact]
    public void TheTensorMustFitTheSquare()
    {
        var pixels = new byte[16];

        Assert.Throws<ArgumentException>(() => SegmentationTensors.Image(new BgraPicture(pixels, 2, 2, 8), 4, new float[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentationTensors.Image(new BgraPicture(pixels, 2, 2, 8), 0, []));
    }

    [Fact]
    public void PromptsArePointsAndBoxCornersInsideThePicture()
    {
        var (coordinates, labels) = SegmentationTensors.Prompt(new SegmentPrompt(150, -4, new PixelBox(-10, 5, 30, 200)), 100, 50);

        Assert.Equal([99f, 0f, 0f, 5f, 19f, 49f], coordinates);
        Assert.Equal([SegmentationTensors.PointLabel, SegmentationTensors.BoxStartLabel, SegmentationTensors.BoxEndLabel], labels);
        var (point, pointLabels) = SegmentationTensors.Prompt(SegmentPrompt.At(10, 20), 100, 50);
        Assert.Equal([10f, 20f], point);
        Assert.Equal([SegmentationTensors.PointLabel], pointLabels);
    }

    [Fact]
    public void TheOutlineHoldingTheClickWinsOverABetterScoredOne()
    {
        // Three 3 x 2 candidates; the clicked pixel (2, 1) is inside the second and third only, and
        // pixel (1, 1) is inside none of them.
        float[] logits =
        [
            1, 1, 1, 1, -1, -1,
            -1, -1, -1, -1, -1, 1,
            1, -1, -1, -1, -1, 2,
        ];
        float[] overlap = [0.95f, 0.5f, 0.7f];

        var clicked = SegmentationTensors.Decode(logits, overlap, 3, 2, SegmentPrompt.At(2, 1));
        var boxed = SegmentationTensors.Decode(logits, overlap, 3, 2, SegmentPrompt.Around(new PixelBox(0, 0, 3, 2)));
        var missed = SegmentationTensors.Decode(logits, overlap, 3, 2, SegmentPrompt.At(1, 1));

        Assert.Equal([255, 0, 0, 0, 0, 255], clicked.Values);
        Assert.Equal(0.7, clicked.Score, 5);
        Assert.Equal([255, 255, 255, 255, 0, 0], boxed.Values);
        Assert.Equal(boxed.Values, missed.Values);
    }

    [Fact]
    public void AClickSelectsTheLargestConfidentOutlineSoTheWholeObjectIsChosen()
    {
        // The shirt (one pixel), the person (three) and the whole picture (six) all hold the click;
        // the person is nearly as confident as the shirt, the picture is not.
        float[] logits =
        [
            1, -1, -1, -1, -1, -1,
            1, 1, 1, -1, -1, -1,
            1, 1, 1, 1, 1, 1,
        ];

        var mask = SegmentationTensors.Decode(logits, [0.9f, 0.8f, 0.6f], 3, 2, SegmentPrompt.At(0, 0));

        Assert.Equal(3, mask.Area);
        Assert.Equal(0.8, mask.Score, 5);
    }

    [Fact]
    public void FollowingTakesTheOutlineNearestTheRememberedSize()
    {
        float[] logits =
        [
            1, -1, -1, -1, -1, -1,
            1, 1, 1, -1, -1, -1,
            1, 1, 1, 1, 1, 1,
        ];

        Assert.Equal(3, SegmentationTensors.Decode(logits, [0.9f, 0.8f, 0.95f], 3, 2, SegmentPrompt.Following(new PixelBox(0, 0, 3, 2), 3)).Area);
        Assert.Equal(1, SegmentationTensors.Decode(logits, [0.9f, 0.8f, 0.95f], 3, 2, SegmentPrompt.Following(new PixelBox(0, 0, 3, 2), 1)).Area);
        Assert.Equal(6, SegmentationTensors.Decode(logits, [0.9f, 0.8f, 0.95f], 3, 2, SegmentPrompt.Following(new PixelBox(0, 0, 3, 2), 50)).Area);
    }

    [Fact]
    public void TheDecoderAnswerMustMatchItsShape()
    {
        Assert.Throws<ArgumentException>(() => SegmentationTensors.Decode(new float[6], [], 3, 2, SegmentPrompt.At(0, 0)));
        Assert.Throws<ArgumentException>(() => SegmentationTensors.Decode(new float[5], [1f], 3, 2, SegmentPrompt.At(0, 0)));
    }
}
