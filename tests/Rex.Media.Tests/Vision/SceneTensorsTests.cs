using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class SceneTensorsTests
{
    private static BgraPicture Picture(byte[] pixels, int width, int height) => new(pixels, width, height, width * 4);

    /// <summary>A picture whose red is x and green is y (each clamped to a byte), so a sample says where it came from.</summary>
    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (0, (byte)Math.Min(255, y), (byte)Math.Min(255, x), 255);
            }
        }

        return pixels;
    }

    [Fact]
    public void TheLetterboxCentresThePictureInItsLongerSide()
    {
        var frame = CropFrame.Around(640, 480);
        Assert.Equal((320f, 240f, 640f, 0f), (frame.CentreX, frame.CentreY, frame.Side, frame.Angle));
        Assert.Equal((0f, -80f), frame.ToPicture(0, 0));
        Assert.Equal((640f, 560f), frame.ToPicture(1, 1));
        var (u, v) = frame.FromPicture(320, 240);
        Assert.Equal((0.5f, 0.5f), (u, v));
    }

    [Fact]
    public void AnUprightWindowTurnsSoItsUpPointsAlongThePart()
    {
        // The part points to the right of the picture: the window's up is the picture's right.
        var frame = CropFrame.Upright(100, 100, 50, 0, 0, 10, 0);
        var (x, y) = frame.ToPicture(0.5f, 0);
        Assert.Equal(125, x, 3);
        Assert.Equal(100, y, 3);
        var (u, v) = frame.FromPicture(125, 100);
        Assert.Equal(0.5f, u, 3);
        Assert.Equal(0, v, 3);

        // Pointing up the picture is no turn at all; no direction at all, likewise.
        Assert.Equal(0, CropFrame.Upright(0, 0, 1, 0, 10, 0, 0).Angle);
        Assert.Equal(0, CropFrame.Upright(0, 0, 1, 3, 3, 3, 3).Angle);
        // Pointing down turns the window half way round.
        Assert.Equal(MathF.PI, MathF.Abs(CropFrame.Upright(0, 0, 1, 0, 0, 0, 10).Angle), 4);
    }

    [Fact]
    public void ToPictureAndFromPictureAreInverses()
    {
        var frame = CropFrame.Upright(37, 91, 120, 10, 10, 25, 3);
        foreach (var (u, v) in new[] { (0f, 0f), (1f, 1f), (0.3f, 0.8f) })
        {
            var (x, y) = frame.ToPicture(u, v);
            var (back, backV) = frame.FromPicture(x, y);
            Assert.Equal(u, back, 4);
            Assert.Equal(v, backV, 4);
        }
    }

    [Fact]
    public void ATensorIsFilledFromTheWindowScaledAsAsked()
    {
        var picture = Picture(Gradient(8, 8), 8, 8);
        var tensor = new float[4 * 4 * 3];
        // A window on the picture's lower right quarter, nearest sampling, 0 to 1.
        PictureTensor.Fill(picture, new CropFrame(6, 6, 4, 0), 4, TensorLayout.Nhwc, 0, 1, tensor);
        // Tensor pixel (0, 0) covers picture (4, 4): red 4, green 4.
        Assert.Equal(4 / 255f, tensor[0], 4);
        Assert.Equal(4 / 255f, tensor[1], 4);
        Assert.Equal(0, tensor[2]);
        // Tensor pixel (3, 3) covers picture (7, 7).
        Assert.Equal(7 / 255f, tensor[15 * 3], 4);

        // Planes, and the MediaPipe scaling to -1 to 1.
        PictureTensor.Fill(picture, new CropFrame(6, 6, 4, 0), 4, TensorLayout.Nchw, 0.5f, 2, tensor);
        Assert.Equal(((4 / 255f) - 0.5f) * 2, tensor[0], 4);
        Assert.Equal(((4 / 255f) - 0.5f) * 2, tensor[16], 4);
        Assert.Equal(-1, tensor[32], 4);
    }

    [Fact]
    public void AWindowLargerThanTheTensorAveragesAndBeyondThePictureIsBlack()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            // Alternate rows red 100 and red 200: averaged over two rows, 150.
            pixels[(i * 4) + 2] = (byte)(i / 4 % 2 == 0 ? 100 : 200);
            pixels[(i * 4) + 3] = 255;
        }

        var tensor = new float[2 * 2 * 3];
        PictureTensor.Fill(Picture(pixels, 4, 4), CropFrame.Around(4, 4), 2, TensorLayout.Nhwc, 0, 1, tensor);
        Assert.All(new[] { tensor[0], tensor[3], tensor[6], tensor[9] }, red => Assert.Equal(150 / 255f, red, 4));

        // A window hanging off the picture's edge sees black there.
        PictureTensor.Fill(Picture(pixels, 4, 4), new CropFrame(4, 4, 4, 0), 2, TensorLayout.Nhwc, 0, 1, tensor);
        Assert.Equal(0, tensor[9]);
        Assert.True(tensor[0] > 0);
    }

    [Fact]
    public void ANhwcTensorIsLaidOutAsPlanesWithItsOwnScaling()
    {
        // Two pixels: (0.1, 0.2, 0.3) and (0.4, 0.5, 0.6).
        float[] nhwc = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0, 0, 0, 1, 1, 1];
        var nchw = new float[12];
        PictureTensor.Relayout(nhwc, 2, 0.5f, 2, nchw);

        Assert.Equal(-0.8f, nchw[0], 4);
        Assert.Equal(-0.2f, nchw[1], 4);
        Assert.Equal(-0.6f, nchw[4], 4);
        Assert.Equal(0, nchw[5], 4);
        Assert.Equal(-0.4f, nchw[8], 4);
        Assert.Equal(0.2f, nchw[9], 4);
        Assert.Equal(1, nchw[11], 4);
        Assert.Throws<ArgumentException>(() => PictureTensor.Relayout(nhwc, 3, 0, 1, nchw));
        Assert.Throws<ArgumentException>(() => PictureTensor.Relayout(nhwc, 2, 0, 1, new float[3]));
    }

    [Fact]
    public void AVeryLargeWindowIsAveragedTwoByTwoAtMost()
    {
        // A 12 pixel square to a 2 pixel tensor: each tensor pixel samples 2 by 2 points, not 6 by 6.
        var pixels = new byte[12 * 12 * 4];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                // Red is 240 on the sampled points (columns and rows 1 and 4 of each 6), 0 elsewhere.
                var sampled = x % 6 is 1 or 4 && y % 6 is 1 or 4;
                pixels[(((y * 12) + x) * 4) + 2] = (byte)(sampled ? 240 : 0);
            }
        }

        var tensor = new float[2 * 2 * 3];
        PictureTensor.Fill(Picture(pixels, 12, 12), CropFrame.Around(12, 12), 2, TensorLayout.Nhwc, 0, 1, tensor);
        Assert.Equal(240 / 255f, tensor[0], 4);
    }

    [Fact]
    public void TensorFillingIsChecked()
    {
        var pixels = Gradient(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureTensor.Fill(Picture(pixels, 4, 4), CropFrame.Around(4, 4), 0, TensorLayout.Nhwc, 0, 1, []));
        Assert.Throws<ArgumentException>(() => PictureTensor.Fill(Picture(pixels, 4, 4), CropFrame.Around(4, 4), 2, TensorLayout.Nhwc, 0, 1, new float[5]));
    }

    private static Detection PalmAt(float wristU, float wristV, float middleU, float middleV)
    {
        var palm = new Detection(7);
        for (var p = 0; p < 7; p++)
        {
            (palm.Points[p * 2], palm.Points[(p * 2) + 1]) = (wristU, wristV);
        }

        (palm.Points[CropPlacement.PalmMiddleBase * 2], palm.Points[(CropPlacement.PalmMiddleBase * 2) + 1]) = (middleU, middleV);
        // The other finger bases spread either side of the middle one.
        (palm.Points[2], palm.Points[3]) = (middleU - 0.05f, middleV);
        (palm.Points[6], palm.Points[7]) = (middleU + 0.05f, middleV);
        return palm;
    }

    [Fact]
    public void TheHandWindowIsThreePalmsAboveThePalmAndUpright()
    {
        var letterbox = CropFrame.Around(100, 100);
        // Wrist at (50, 60), middle finger base at (50, 50): the hand points up.
        var window = CropPlacement.Hand(PalmAt(0.5f, 0.6f, 0.5f, 0.5f), letterbox);
        Assert.Equal(0, window.Angle, 4);
        Assert.Equal(50, window.CentreX, 3);
        // The palm's box runs from y 50 to 60 and x 45 to 55, 10 by 10: shifted up 4, side 30.
        Assert.Equal(51, window.CentreY, 3);
        Assert.Equal(30, window.Side, 3);

        // The same hand lying on its side points the window's up to the right.
        var sideways = CropPlacement.Hand(PalmAt(0.4f, 0.5f, 0.5f, 0.5f), letterbox);
        Assert.Equal(MathF.PI / 2, sideways.Angle, 4);
        Assert.Throws<ArgumentNullException>(() => CropPlacement.Hand(null!, letterbox));
    }

    [Fact]
    public void TheBodyWindowIsCentredOnTheHipsTwiceTheReachToTheHead()
    {
        var letterbox = CropFrame.Around(200, 100);
        var person = new Detection(4);
        (person.Points[0], person.Points[1]) = (0.5f, 0.6f);
        (person.Points[2], person.Points[3]) = (0.5f, 0.3f);
        var window = CropPlacement.Body(person, letterbox);
        Assert.Equal(100, window.CentreX, 3);
        Assert.Equal(70, window.CentreY, 3);
        Assert.Equal(120, window.Side, 3);
        Assert.Equal(0, window.Angle, 4);
        Assert.Throws<ArgumentNullException>(() => CropPlacement.Body(null!, letterbox));
    }

    [Fact]
    public void HandPointsComeBackAsSharesOfThePictureMirroredWhenAsked()
    {
        var window = new CropFrame(100, 50, 56, 0);
        var landmarks = new float[63];
        // The wrist in the window's middle, the index tip a quarter of the window up and right.
        (landmarks[HandSense.IndexTip * 3], landmarks[(HandSense.IndexTip * 3) + 1], landmarks[(HandSense.IndexTip * 3) + 2]) = (168, 56, -28);
        for (var i = 0; i < 21; i++)
        {
            if (i != HandSense.IndexTip)
            {
                (landmarks[i * 3], landmarks[(i * 3) + 1]) = (112, 112);
            }
        }

        var hand = new HandSense();
        SceneDecoding.Hand(landmarks, 0.9f, 0.9f, window, 200, 100, mirror: false, hand);
        Assert.True(hand.Present);
        // The model names hands for a picture already flipped like a mirror; the camera's is not, so its "right" is the listener's left.
        Assert.True(hand.IsLeft);
        Assert.Equal(0.9f, hand.Confidence);
        Assert.Equal(0.5f, hand.Landmarks[HandSense.Wrist].X, 4);
        Assert.Equal(0.5f, hand.Landmarks[HandSense.Wrist].Y, 4);
        Assert.Equal(0.57f, hand.Landmarks[HandSense.IndexTip].X, 4);
        Assert.Equal(0.36f, hand.Landmarks[HandSense.IndexTip].Y, 4);
        // Depth is in the same units as x: an eighth of the window is 7 pixels of the 200 across.
        Assert.Equal(-0.035f, hand.Landmarks[HandSense.IndexTip].Z, 4);

        SceneDecoding.Hand(landmarks, 2f, 0.2f, window, 200, 100, mirror: true, hand);
        Assert.False(hand.IsLeft);
        Assert.Equal(1, hand.Confidence);
        Assert.Equal(0.43f, hand.Landmarks[HandSense.IndexTip].X, 4);
        Assert.Throws<ArgumentException>(() => SceneDecoding.Hand(new float[3], 1, 1, window, 200, 100, false, hand));
        Assert.Throws<ArgumentNullException>(() => SceneDecoding.Hand(landmarks, 1, 1, window, 200, 100, false, null!));
    }

    [Fact]
    public void BodyPointsCarryTheirVisibilityAndSwapSidesInAMirror()
    {
        // The window spans the whole 200-pixel picture.
        var window = new CropFrame(100, 100, 200, 0);
        var landmarks = new float[SceneDecoding.PosePoints * 5];
        for (var i = 0; i < SceneDecoding.PosePoints; i++)
        {
            (landmarks[i * 5], landmarks[(i * 5) + 1], landmarks[(i * 5) + 3]) = (128, 128, 0);
        }

        // The left shoulder at the window's left, fully visible; the right shoulder at the right, hidden.
        (landmarks[BodySense.LeftShoulder * 5], landmarks[(BodySense.LeftShoulder * 5) + 3]) = (64, 20);
        (landmarks[BodySense.RightShoulder * 5], landmarks[(BodySense.RightShoulder * 5) + 3]) = (192, -20);
        (landmarks[1 * 5], landmarks[4 * 5]) = (0, 256);

        var body = new BodySense();
        SceneDecoding.Body(landmarks, 0.7f, window, 200, 200, mirror: false, body);
        Assert.True(body.Present);
        Assert.Equal(0.7f, body.Confidence);
        Assert.Equal(0.25f, body.Landmarks[BodySense.LeftShoulder].X, 4);
        Assert.Equal(1, body.Landmarks[BodySense.LeftShoulder].Confidence, 4);
        Assert.Equal(0.75f, body.Landmarks[BodySense.RightShoulder].X, 4);
        Assert.Equal(0, body.Landmarks[BodySense.RightShoulder].Confidence, 4);
        Assert.Equal(0.5f, body.Landmarks[BodySense.Nose].Confidence, 4);

        SceneDecoding.Body(landmarks, 0.7f, window, 200, 200, mirror: true, body);
        // Mirrored, a shoulder is still the same shoulder: only its place across the picture flips.
        Assert.Equal(0.75f, body.Landmarks[BodySense.LeftShoulder].X, 4);
        Assert.Equal(1, body.Landmarks[BodySense.LeftShoulder].Confidence, 4);
        Assert.Equal(0.25f, body.Landmarks[BodySense.RightShoulder].X, 4);
        Assert.Equal(1, body.Landmarks[1].X, 4);
        Assert.Equal(0, body.Landmarks[4].X, 4);
        Assert.Throws<ArgumentException>(() => SceneDecoding.Body(new float[5], 1, window, 200, 200, false, body));
        Assert.Throws<ArgumentNullException>(() => SceneDecoding.Body(landmarks, 1, window, 200, 200, false, null!));
    }

    [Fact]
    public void TheMatteIsThePersonsShareAtTheScenesSize()
    {
        const int side = SceneDecoding.MatteSide;
        var scores = new float[2 * side * side];
        // The person is the left half of the square: strongly so.
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                scores[(y * side) + x] = x < side / 2 ? 0.01f : 0.99f;
                scores[(side * side) + (y * side) + x] = x < side / 2 ? 0.99f : 0.01f;
            }
        }

        var matte = new float[8 * 6];
        SceneDecoding.Matte(scores, CropFrame.Around(8, 6), 8, 6, mirror: false, 8, 6, matte);
        Assert.InRange(matte[0], 0.99f, 1);
        Assert.InRange(matte[7], 0, 0.01f);
        SceneDecoding.Matte(scores, CropFrame.Around(8, 6), 8, 6, mirror: true, 8, 6, matte);
        Assert.InRange(matte[0], 0, 0.01f);
        Assert.InRange(matte[7], 0.99f, 1);

        // Nothing either way is no one.
        SceneDecoding.Matte(new float[2 * side * side], CropFrame.Around(8, 6), 8, 6, false, 8, 6, matte);
        Assert.All(matte, value => Assert.Equal(0, value));
        Assert.Throws<ArgumentException>(() => SceneDecoding.Matte(new float[3], CropFrame.Around(8, 6), 8, 6, false, 8, 6, matte));
        Assert.Throws<ArgumentException>(() => SceneDecoding.Matte(scores, CropFrame.Around(8, 6), 8, 6, false, 8, 6, new float[3]));
    }
}
