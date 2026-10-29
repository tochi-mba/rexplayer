namespace Rex.Media.Vision;

/// <summary>
/// A point the camera sees on the listener: across and down as shares of the picture (0 to 1, left
/// to right and top to bottom), depth in the same units relative to the hand's wrist or the body's
/// hips (nearer the camera is negative), and how sure the finder is of it (0 to 1).
/// </summary>
public readonly record struct Landmark(float X, float Y, float Z, float Confidence)
{
    /// <summary>The distance across the picture to <paramref name="other"/>, depth left out.</summary>
    public float DistanceTo(Landmark other) => MathF.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}

/// <summary>Where a frame's sense of the scene came from.</summary>
public enum SenseSource
{
    /// <summary>Nothing has looked at the camera yet.</summary>
    None,

    /// <summary>rexplayer's own algorithms: the outline from motion and colour, hands guessed from the outline's ends.</summary>
    HandWritten,

    /// <summary>Downloaded models (ADR-0022), or the platform's own finders on a Mac.</summary>
    Model,
}

/// <summary>
/// One hand: its 21 points in the order every hand finder rexplayer uses gives them (wrist, then
/// four points up each finger from thumb to little finger), and the measures the gestures read.
/// A measure is a share of the palm's size, so it means the same for a hand near the camera and far.
/// </summary>
public sealed class HandSense
{
    public const int Points = 21;
    public const int Wrist = 0;
    public const int ThumbTip = 4;
    public const int IndexBase = 5;
    public const int IndexTip = 8;
    public const int MiddleBase = 9;
    public const int MiddleTip = 12;
    public const int RingTip = 16;
    public const int PinkyBase = 17;
    public const int PinkyTip = 20;

    private static readonly int[] Tips = [ThumbTip, IndexTip, MiddleTip, RingTip, PinkyTip];

    /// <summary>Whether this hand was seen in the frame; the points mean nothing otherwise.</summary>
    public bool Present { get; set; }

    /// <summary>The listener's left hand (as they would call it, whatever mirroring the picture has).</summary>
    public bool IsLeft { get; set; }

    /// <summary>How sure the finder is that this is a hand (0 to 1).</summary>
    public float Confidence { get; set; }

    public Landmark[] Landmarks { get; } = new Landmark[Points];

    /// <summary>The palm's size: wrist to the middle finger's base. Zero for a hand not seen.</summary>
    public float PalmSize => Present ? Math.Max(1e-4f, Landmarks[Wrist].DistanceTo(Landmarks[MiddleBase])) : 0;

    /// <summary>The middle of the palm: between the wrist and the bases of the index and little fingers.</summary>
    public (float X, float Y) Centre => (
        (Landmarks[Wrist].X + Landmarks[IndexBase].X + Landmarks[PinkyBase].X) / 3,
        (Landmarks[Wrist].Y + Landmarks[IndexBase].Y + Landmarks[PinkyBase].Y) / 3);

    /// <summary>
    /// How far apart the thumb and index tips are, in palms: about 0 when they touch (a pinch), about
    /// 1 or more when the hand is open. Infinite for a hand not seen, so nothing pinches by accident.
    /// </summary>
    public float Pinch => Present ? Landmarks[ThumbTip].DistanceTo(Landmarks[IndexTip]) / PalmSize : float.PositiveInfinity;

    /// <summary>
    /// How open the hand is: the fingertips' average distance from the wrist, in palms. A fist is
    /// about 1 (the tips fold back to the palm), an open hand about 2. Zero for a hand not seen.
    /// </summary>
    public float Openness
    {
        get
        {
            if (!Present)
            {
                return 0;
            }

            var sum = 0f;
            foreach (var tip in Tips)
            {
                sum += Landmarks[Wrist].DistanceTo(Landmarks[tip]);
            }

            return sum / Tips.Length / PalmSize;
        }
    }

    public void Clear()
    {
        Present = false;
        Confidence = 0;
        Array.Clear(Landmarks);
    }

    public void CopyTo(HandSense other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other.Present = Present;
        other.IsLeft = IsLeft;
        other.Confidence = Confidence;
        Landmarks.CopyTo(other.Landmarks, 0);
    }
}

/// <summary>
/// The body: 33 points in the order the pose finders give them (the face first, then shoulders,
/// elbows, wrists, the hands' outlines, hips, knees, ankles and feet).
/// </summary>
public sealed class BodySense
{
    public const int Points = 33;
    public const int Nose = 0;
    public const int LeftShoulder = 11;
    public const int RightShoulder = 12;
    public const int LeftElbow = 13;
    public const int RightElbow = 14;
    public const int LeftWrist = 15;
    public const int RightWrist = 16;
    public const int LeftHip = 23;
    public const int RightHip = 24;

    public bool Present { get; set; }

    public float Confidence { get; set; }

    public Landmark[] Landmarks { get; } = new Landmark[Points];

    /// <summary>The middle of the shoulders: where the stem orbs sit, and the height a raised hand is measured from.</summary>
    public (float X, float Y) Shoulders => (
        (Landmarks[LeftShoulder].X + Landmarks[RightShoulder].X) / 2,
        (Landmarks[LeftShoulder].Y + Landmarks[RightShoulder].Y) / 2);

    /// <summary>The shoulders' width, as a share of the picture: how big the listener is on screen. Zero when not seen.</summary>
    public float Width => Present ? Landmarks[LeftShoulder].DistanceTo(Landmarks[RightShoulder]) : 0;

    public void Clear()
    {
        Present = false;
        Confidence = 0;
        Array.Clear(Landmarks);
    }

    public void CopyTo(BodySense other)
    {
        ArgumentNullException.ThrowIfNull(other);
        other.Present = Present;
        other.Confidence = Confidence;
        Landmarks.CopyTo(other.Landmarks, 0);
    }
}

/// <summary>
/// Everything the camera effects and the conductor know about the listener from one camera picture:
/// a soft matte (1 where they are, 0 where the room is, in between at the edges), how the picture is
/// moving (a flow field at a quarter of the matte's size, in matte pixels a frame), up to two hands
/// and the body. Made once and filled over and over, so a frame allocates nothing; the perception
/// worker fills one while the renderer reads another and they swap.
/// </summary>
public sealed class SceneSense
{
    /// <summary>The matte's size unless asked otherwise: the camera's 4:3 picture at a working size.</summary>
    public const int DefaultWidth = 192;
    public const int DefaultHeight = 144;

    /// <summary>The flow field has one cell for this many matte pixels across and down.</summary>
    public const int FlowStep = 4;

    public SceneSense(int width = DefaultWidth, int height = DefaultHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, FlowStep);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, FlowStep);
        (Width, Height) = (width, height);
        Matte = new float[width * height];
        (FlowWidth, FlowHeight) = (width / FlowStep, height / FlowStep);
        FlowX = new float[FlowWidth * FlowHeight];
        FlowY = new float[FlowWidth * FlowHeight];
        Hands = [new HandSense { IsLeft = true }, new HandSense()];
        Body = new BodySense();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Where the listener is, row by row, 0 to 1.</summary>
    public float[] Matte { get; }

    public int FlowWidth { get; }

    public int FlowHeight { get; }

    /// <summary>How each cell of the picture moved since the last frame, in matte pixels, across and down.</summary>
    public float[] FlowX { get; }

    public float[] FlowY { get; }

    /// <summary>The left hand first, then the right; <see cref="HandSense.Present"/> says whether each was seen.</summary>
    public HandSense[] Hands { get; }

    public BodySense Body { get; }

    /// <summary>When the camera picture this came from was taken, in ticks of a stopwatch.</summary>
    public long Timestamp { get; set; }

    public SenseSource Source { get; set; }

    /// <summary>The share of the picture the listener fills, 0 to 1.</summary>
    public float Presence
    {
        get
        {
            var sum = 0f;
            foreach (var value in Matte)
            {
                sum += value;
            }

            return sum / Matte.Length;
        }
    }

    /// <summary>The matte at a point, 0 beyond the picture.</summary>
    public float MatteAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Matte[(y * Width) + x] : 0;

    /// <summary>How fast the picture is moving overall, in matte pixels a frame: the average length of the flow.</summary>
    public float Motion
    {
        get
        {
            var sum = 0f;
            for (var i = 0; i < FlowX.Length; i++)
            {
                sum += MathF.Sqrt((FlowX[i] * FlowX[i]) + (FlowY[i] * FlowY[i]));
            }

            return sum / FlowX.Length;
        }
    }

    /// <summary>Forgets everything: an empty room, still.</summary>
    public void Clear()
    {
        Array.Clear(Matte);
        Array.Clear(FlowX);
        Array.Clear(FlowY);
        Hands[0].Clear();
        Hands[1].Clear();
        Body.Clear();
        Timestamp = 0;
        Source = SenseSource.None;
    }

    /// <summary>Copies everything into <paramref name="other"/>, which must be the same size.</summary>
    public void CopyTo(SceneSense other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Width != Width || other.Height != Height)
        {
            throw new ArgumentException("A scene is copied into one of the same size.", nameof(other));
        }

        Matte.CopyTo(other.Matte, 0);
        FlowX.CopyTo(other.FlowX, 0);
        FlowY.CopyTo(other.FlowY, 0);
        Hands[0].CopyTo(other.Hands[0]);
        Hands[1].CopyTo(other.Hands[1]);
        Body.CopyTo(other.Body);
        other.Timestamp = Timestamp;
        other.Source = Source;
    }
}
