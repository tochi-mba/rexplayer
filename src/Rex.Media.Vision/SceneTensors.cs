namespace Rex.Media.Vision;

/// <summary>How a model wants its picture laid out and scaled.</summary>
public enum TensorLayout
{
    /// <summary>Rows, then columns, then red, green, blue: what the MediaPipe models take.</summary>
    Nhwc,

    /// <summary>Red plane, green plane, blue plane: what PaddlePaddle's take.</summary>
    Nchw,
}

/// <summary>
/// A square window on the picture, in the picture's pixels: its centre, its side and how it is
/// turned (<see cref="Angle"/>, so that the window's up points along a part of the body). A model
/// sees the window as a square tensor; its answers in that square come back through
/// <see cref="ToPicture"/>. The letterbox the detectors take is the window around the whole picture.
/// </summary>
public readonly record struct CropFrame(float CentreX, float CentreY, float Side, float Angle)
{
    /// <summary>The window around the whole picture: the picture centred in a square of its longer side.</summary>
    public static CropFrame Around(int width, int height) => new(width / 2f, height / 2f, Math.Max(width, height), 0);

    /// <summary>
    /// A window of <paramref name="side"/> on <paramref name="centreX"/>, <paramref name="centreY"/>
    /// whose up points from (<paramref name="fromX"/>, <paramref name="fromY"/>) toward
    /// (<paramref name="toX"/>, <paramref name="toY"/>): the hand's wrist to its middle finger, the
    /// body's hips to its head.
    /// </summary>
    public static CropFrame Upright(float centreX, float centreY, float side, float fromX, float fromY, float toX, float toY)
    {
        var (dx, dy) = (toX - fromX, toY - fromY);
        var angle = dx == 0 && dy == 0 ? 0 : MathF.Atan2(dx, -dy);
        return new CropFrame(centreX, centreY, side, angle);
    }

    /// <summary>The picture pixel that the window's point (<paramref name="u"/>, <paramref name="v"/>), as shares of the square, sits on.</summary>
    public (float X, float Y) ToPicture(float u, float v)
    {
        var (ox, oy) = ((u - 0.5f) * Side, (v - 0.5f) * Side);
        var (sin, cos) = MathF.SinCos(Angle);
        return (CentreX + (cos * ox) - (sin * oy), CentreY + (sin * ox) + (cos * oy));
    }

    /// <summary>Where a picture pixel falls in the window, as shares of the square (outside 0 to 1 when beyond it).</summary>
    public (float U, float V) FromPicture(float x, float y)
    {
        var (dx, dy) = (x - CentreX, y - CentreY);
        var (sin, cos) = MathF.SinCos(Angle);
        return ((((cos * dx) + (sin * dy)) / Side) + 0.5f, (((cos * dy) - (sin * dx)) / Side) + 0.5f);
    }
}

/// <summary>
/// Fills a model's square tensor from a window on the picture, sampling each tensor pixel from
/// the picture point it covers (nearest when the window is smaller than the tensor, so a far hand
/// stays sharp; averaged when larger, so a whole picture does not alias), red, green, blue scaled
/// as the model wants, and black beyond the picture. No allocation: the tensor is the caller's.
/// </summary>
public static class PictureTensor
{
    /// <summary>
    /// Writes the window <paramref name="frame"/> of <paramref name="picture"/> into
    /// <paramref name="tensor"/>, <paramref name="size"/> square in <paramref name="layout"/>, each
    /// channel as (value / 255 - <paramref name="offset"/>) * <paramref name="scale"/>.
    /// </summary>
    public static void Fill(BgraPicture picture, CropFrame frame, int size, TensorLayout layout, float offset, float scale, Span<float> tensor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (tensor.Length != size * size * 3)
        {
            throw new ArgumentException("The tensor holds three values per pixel of the square.", nameof(tensor));
        }

        var plane = size * size;
        // Pixels of the picture under one pixel of the tensor, each way; more than one is averaged.
        var span = Math.Max(1, (int)(frame.Side / size));
        var weight = 1f / (span * span);
        // The window is an affine map, so a sample's place is a start plus steps across and down:
        // one sine and cosine for the whole tensor rather than one per sample.
        var (sin, cos) = MathF.SinCos(frame.Angle);
        var step = frame.Side / (size * span);
        var (acrossX, acrossY) = (cos * step, sin * step);
        var (downX, downY) = (-sin * step, cos * step);
        var (originX, originY) = frame.ToPicture(0, 0);
        originX += (acrossX + downX) * 0.5f;
        originY += (acrossY + downY) * 0.5f;
        for (var v = 0; v < size; v++)
        {
            for (var u = 0; u < size; u++)
            {
                float r = 0, g = 0, b = 0;
                for (var sy = 0; sy < span; sy++)
                {
                    var row0 = (v * span) + sy;
                    for (var sx = 0; sx < span; sx++)
                    {
                        var column = (u * span) + sx;
                        var x = originX + (column * acrossX) + (row0 * downX);
                        var y = originY + (column * acrossY) + (row0 * downY);
                        var (px, py) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
                        if (px < 0 || py < 0 || px >= picture.Width || py >= picture.Height)
                        {
                            continue;
                        }

                        var row = picture.Row(py);
                        b += row[px * 4];
                        g += row[(px * 4) + 1];
                        r += row[(px * 4) + 2];
                    }
                }

                var at = (v * size) + u;
                var (red, green, blue) = (((r * weight / 255) - offset) * scale, ((g * weight / 255) - offset) * scale, ((b * weight / 255) - offset) * scale);
                if (layout == TensorLayout.Nhwc)
                {
                    (tensor[at * 3], tensor[(at * 3) + 1], tensor[(at * 3) + 2]) = (red, green, blue);
                }
                else
                {
                    (tensor[at], tensor[plane + at], tensor[(2 * plane) + at]) = (red, green, blue);
                }
            }
        }
    }
}

/// <summary>
/// The windows the second-stage models take, placed from what the detectors found, as MediaPipe
/// places them: the hand's window is turned so the fingers point up, is three times the palm and
/// sits above the palm's centre where the fingers are; the body's window is centred on the hips,
/// twice the hips-to-head distance, turned so the head is up.
/// </summary>
public static class CropPlacement
{
    /// <summary>The palm detector's points: the wrist and the middle finger's base, by which the hand is turned upright.</summary>
    public const int PalmWrist = 0;
    public const int PalmMiddleBase = 2;

    /// <summary>The person detector's points: the hips' middle and the point above the head.</summary>
    public const int PersonHips = 0;
    public const int PersonHead = 1;

    /// <summary>The hand's window from a palm detection in <paramref name="letterbox"/> (the detector's square).</summary>
    public static CropFrame Hand(Detection palm, CropFrame letterbox)
    {
        ArgumentNullException.ThrowIfNull(palm);
        var (wristX, wristY) = letterbox.ToPicture(palm.Points[PalmWrist * 2], palm.Points[(PalmWrist * 2) + 1]);
        var (middleX, middleY) = letterbox.ToPicture(palm.Points[PalmMiddleBase * 2], palm.Points[(PalmMiddleBase * 2) + 1]);
        var upright = CropFrame.Upright(0, 0, 1, wristX, wristY, middleX, middleY);

        // The palm points' box in the upright frame, then shifted up by 0.4 of its height and made three times its longer side.
        float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
        for (var p = 0; p < palm.Points.Length; p += 2)
        {
            var (x, y) = letterbox.ToPicture(palm.Points[p], palm.Points[p + 1]);
            var (u, v) = upright.FromPicture(x, y);
            (minU, minV, maxU, maxV) = (Math.Min(minU, u), Math.Min(minV, v), Math.Max(maxU, u), Math.Max(maxV, v));
        }

        var (width, height) = (maxU - minU, maxV - minV);
        var (centreU, centreV) = ((minU + maxU) / 2, ((minV + maxV) / 2) - (0.4f * height));
        var (centreX, centreY) = upright.ToPicture(centreU, centreV);
        return new CropFrame(centreX, centreY, Math.Max(1, 3 * Math.Max(width, height)), upright.Angle);
    }

    /// <summary>The body's window from a person detection in <paramref name="letterbox"/>.</summary>
    public static CropFrame Body(Detection person, CropFrame letterbox)
    {
        ArgumentNullException.ThrowIfNull(person);
        var (hipX, hipY) = letterbox.ToPicture(person.Points[PersonHips * 2], person.Points[(PersonHips * 2) + 1]);
        var (headX, headY) = letterbox.ToPicture(person.Points[PersonHead * 2], person.Points[(PersonHead * 2) + 1]);
        var reach = MathF.Sqrt(((headX - hipX) * (headX - hipX)) + ((headY - hipY) * (headY - hipY)));
        return CropFrame.Upright(hipX, hipY, Math.Max(1, 2 * reach), hipX, hipY, headX, headY);
    }
}

/// <summary>
/// Reads the second-stage models' answers into a <see cref="SceneSense"/>: points from the hand
/// and pose models come in the window's pixels and go out as shares of the picture, mirrored when
/// the camera is shown as a mirror; the matte models' class scores become a soft matte at the
/// scene's size.
/// </summary>
public static class SceneDecoding
{
    /// <summary>The hand model's window side in its own pixels.</summary>
    public const int HandSide = 224;

    /// <summary>The pose model's window side in its own pixels, and how many points it gives (33 of the body and 6 helpers).</summary>
    public const int PoseSide = 256;
    public const int PosePoints = 39;

    /// <summary>The matte model's square.</summary>
    public const int MatteSide = 192;

    /// <summary>
    /// Fills <paramref name="hand"/> from the hand model's 63 values (x, y, z per point in window
    /// pixels, z relative to the wrist), its confidence and its handedness (near 1 for the right hand).
    /// </summary>
    public static void Hand(ReadOnlySpan<float> landmarks, float confidence, float handedness, CropFrame window, int pictureWidth, int pictureHeight, bool mirror, HandSense hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (landmarks.Length != HandSense.Points * 3)
        {
            throw new ArgumentException("The hand model gives three values per point.", nameof(landmarks));
        }

        hand.Present = true;
        hand.Confidence = Math.Clamp(confidence, 0, 1);
        // The model names hands for a picture already flipped like a mirror (a selfie); the camera's
        // picture is not flipped, so its "right" is the listener's own left. Showing the picture as a
        // mirror changes where a hand is drawn, never whose hand it is.
        hand.IsLeft = handedness >= 0.5f;
        for (var i = 0; i < HandSense.Points; i++)
        {
            hand.Landmarks[i] = Point(landmarks[i * 3], landmarks[(i * 3) + 1], landmarks[(i * 3) + 2], HandSide, window, pictureWidth, pictureHeight, mirror, hand.Confidence);
        }
    }

    /// <summary>Fills <paramref name="body"/> from the pose model's 195 values (x, y, z, visibility, presence per point, the first 33 the body).</summary>
    public static void Body(ReadOnlySpan<float> landmarks, float confidence, CropFrame window, int pictureWidth, int pictureHeight, bool mirror, BodySense body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (landmarks.Length != PosePoints * 5)
        {
            throw new ArgumentException("The pose model gives five values per point.", nameof(landmarks));
        }

        body.Present = true;
        body.Confidence = Math.Clamp(confidence, 0, 1);
        for (var i = 0; i < BodySense.Points; i++)
        {
            var visibility = 1 / (1 + MathF.Exp(-landmarks[(i * 5) + 3]));
            body.Landmarks[i] = Point(landmarks[i * 5], landmarks[(i * 5) + 1], landmarks[(i * 5) + 2], PoseSide, window, pictureWidth, pictureHeight, mirror, visibility);
        }

        // A shoulder stays the same shoulder in a mirror; only where it is drawn flips, in Point.
    }

    /// <summary>
    /// Fills <paramref name="matte"/> (the scene's size, row by row) from the matte model's two
    /// class scores (room then person, each <see cref="MatteSide"/> square, planes one after the
    /// other) for the window <paramref name="letterbox"/>: the person's share of the two, mirrored when asked.
    /// </summary>
    public static void Matte(ReadOnlySpan<float> scores, CropFrame letterbox, int pictureWidth, int pictureHeight, bool mirror, int width, int height, Span<float> matte)
    {
        if (scores.Length != 2 * MatteSide * MatteSide)
        {
            throw new ArgumentException("The matte model gives two planes of its square.", nameof(scores));
        }

        if (matte.Length != width * height)
        {
            throw new ArgumentException("The matte has one value per pixel of the scene.", nameof(matte));
        }

        var plane = MatteSide * MatteSide;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceX = mirror ? width - 1 - x : x;
                var (u, v) = letterbox.FromPicture((sourceX + 0.5f) * pictureWidth / width, (y + 0.5f) * pictureHeight / height);
                var (su, sv) = (Math.Clamp((int)(u * MatteSide), 0, MatteSide - 1), Math.Clamp((int)(v * MatteSide), 0, MatteSide - 1));
                var at = (sv * MatteSide) + su;
                // The converted model ends in its own softmax: the two planes are already shares.
                var (room, person) = (Math.Max(0, scores[at]), Math.Max(0, scores[plane + at]));
                matte[(y * width) + x] = room + person > 0 ? person / (room + person) : 0;
            }
        }
    }

    private static Landmark Point(float u, float v, float z, int side, CropFrame window, int pictureWidth, int pictureHeight, bool mirror, float confidence)
    {
        var (x, y) = window.ToPicture(u / side, v / side);
        var shareX = x / pictureWidth;
        return new Landmark(mirror ? 1 - shareX : shareX, y / pictureHeight, z / side * window.Side / pictureWidth, confidence);
    }
}
