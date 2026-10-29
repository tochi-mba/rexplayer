namespace Rex.Media.Vision;

/// <summary>What a hand is doing, as the reader settles it: each pose is entered and left at different thresholds, so it never flickers at the edge.</summary>
public enum HandPose
{
    None,
    Open,
    Pinch,
    Fist,
}

/// <summary>Something the listener did with their hands, named once as it happens.</summary>
public enum GestureKind
{
    /// <summary>Thumb and index tip came together; a drag may follow (<see cref="HandReading.DragX"/>).</summary>
    PinchStarted,

    /// <summary>A pinch let go quickly without travelling: a tap.</summary>
    PinchTapped,

    /// <summary>A pinch let go after a drag or a hold.</summary>
    PinchReleased,

    /// <summary>A fist has been held long enough to mean it.</summary>
    FistHeld,

    FistReleased,

    /// <summary>An open hand moved quickly sideways; the value is +1 to the right of the picture, -1 to the left.</summary>
    Swiped,

    /// <summary>Both hands have been above the head for a moment.</summary>
    HandsRaised,

    HandsLowered,

    /// <summary>Both open palms came quickly toward the camera.</summary>
    PalmsPushed,

    /// <summary>Both open hands rested on the chest for a second.</summary>
    ChestHeld,
}

/// <summary>One gesture: its kind, which hand (0 left, 1 right, -1 both) and a value whose meaning the kind says.</summary>
public readonly record struct Gesture(GestureKind Kind, int Hand, float Value = 0);

/// <summary>A hand as the reader last saw it: where it is, what it is doing and how far a pinch has dragged.</summary>
public sealed class HandReading
{
    public bool Present { get; internal set; }

    public HandPose Pose { get; internal set; }

    /// <summary>The palm's centre, as shares of the picture.</summary>
    public float X { get; internal set; }

    public float Y { get; internal set; }

    /// <summary>The index fingertip: what points at things.</summary>
    public float TipX { get; internal set; }

    public float TipY { get; internal set; }

    /// <summary>How far the hand has moved since the pinch began, in shares of the picture; zero when not pinching.</summary>
    public float DragX { get; internal set; }

    public float DragY { get; internal set; }

    /// <summary>How long the present pose has been held, in seconds.</summary>
    public float PoseSeconds { get; internal set; }

    /// <summary>The palm's size, as a share of the picture: larger is nearer the camera.</summary>
    public float PalmSize { get; internal set; }
}

/// <summary>
/// Reads gestures from the hands and body in each <see cref="SceneSense"/>. Every pose has an
/// entering and a leaving threshold (hysteresis), every held gesture a dwell, every one-shot a
/// cooldown, and a hand lost for a few frames keeps its pose, so a finder's jitter never plays a
/// note the listener did not mean. All thresholds are in palms or shares of the picture, so they
/// hold near the camera and far from it. Nothing is allocated per frame.
/// </summary>
public sealed class GestureReader
{
    /// <summary>A pinch begins when thumb and index tips are within this many palms, and ends beyond <see cref="PinchOut"/>.</summary>
    public const float PinchIn = 0.3f;
    public const float PinchOut = 0.5f;

    /// <summary>A fist begins when the fingertips fold within this many palms of the wrist on average, and ends beyond <see cref="FistOut"/>.</summary>
    public const float FistIn = 1.2f;
    public const float FistOut = 1.45f;

    /// <summary>An open hand's fingertips reach at least this far, in palms.</summary>
    public const float OpenIn = 1.55f;

    /// <summary>A pinch let go within this time and travel is a tap.</summary>
    public const float TapSeconds = 0.3f;
    public const float TapTravel = 0.04f;

    /// <summary>How long a fist, raised hands or hands on the chest are held before they count.</summary>
    public const float FistDwell = 0.15f;
    public const float RaiseDwell = 0.4f;
    public const float ChestDwell = 1f;

    /// <summary>A swipe is this much travel across the picture within <see cref="SwipeWindow"/> seconds.</summary>
    public const float SwipeTravel = 0.12f;
    public const float SwipeWindow = 0.25f;

    /// <summary>A push is both palms growing by this factor within <see cref="PushWindow"/> seconds.</summary>
    public const float PushGrowth = 1.3f;
    public const float PushWindow = 0.5f;

    /// <summary>How long a hand may go unseen before its pose is dropped.</summary>
    public const float LostGrace = 0.2f;

    /// <summary>After a one-shot gesture, how long before the same one may fire again.</summary>
    public const float Cooldown = 0.6f;

    private const int History = 16;

    private readonly HandState[] _hands = [new(), new()];
    private readonly List<Gesture> _gestures = new(8);
    private float _raisedSeconds;
    private bool _raised;
    private float _chestSeconds;
    private bool _chestFired;
    private float _pushCooldown;

    public GestureReader()
    {
        Hands = [_hands[0].Reading, _hands[1].Reading];
    }

    /// <summary>The hands after the last <see cref="Read"/>: 0 the left, 1 the right.</summary>
    public HandReading[] Hands { get; }

    /// <summary>What happened in the last <see cref="Read"/>, in order; empty most frames.</summary>
    public IReadOnlyList<Gesture> Gestures => _gestures;

    /// <summary>Whether both hands are up, as of the last <see cref="Read"/>.</summary>
    public bool HandsUp => _raised;

    /// <summary>The distance between the palms while both pinch, as a share of the picture; NaN otherwise.</summary>
    public float Spread =>
        _hands[0].Reading.Pose == HandPose.Pinch && _hands[1].Reading.Pose == HandPose.Pinch
            ? MathF.Sqrt(Square(_hands[0].Reading.X - _hands[1].Reading.X) + Square(_hands[0].Reading.Y - _hands[1].Reading.Y))
            : float.NaN;

    /// <summary>Reads the next frame, <paramref name="dt"/> seconds after the last.</summary>
    public void Read(SceneSense scene, float dt)
    {
        ArgumentNullException.ThrowIfNull(scene);
        dt = float.IsFinite(dt) ? Math.Clamp(dt, 0, 0.25f) : 0;
        _gestures.Clear();
        _pushCooldown = Math.Max(0, _pushCooldown - dt);
        for (var i = 0; i < 2; i++)
        {
            ReadHand(i, scene.Hands[i], dt);
        }

        ReadBothHands(scene, dt);
    }

    /// <summary>Forgets every pose and timer, as when the camera or the conductor starts again.</summary>
    public void Reset()
    {
        _gestures.Clear();
        _hands[0].Reset();
        _hands[1].Reset();
        (_raisedSeconds, _raised, _chestSeconds, _chestFired, _pushCooldown) = (0, false, 0, false, 0);
    }

    private void ReadHand(int index, HandSense hand, float dt)
    {
        var state = _hands[index];
        var reading = state.Reading;
        if (!hand.Present)
        {
            state.Lost += dt;
            if (state.Lost > LostGrace && reading.Present)
            {
                EndPose(index, state);
                state.Reset();
            }

            return;
        }

        state.Lost = 0;
        var (x, y) = hand.Centre;
        reading.Present = true;
        (reading.X, reading.Y) = (x, y);
        (reading.TipX, reading.TipY) = (hand.Landmarks[HandSense.IndexTip].X, hand.Landmarks[HandSense.IndexTip].Y);
        reading.PalmSize = hand.PalmSize;
        state.Remember(x, y, hand.PalmSize, dt);
        state.Cooldown = Math.Max(0, state.Cooldown - dt);

        var pose = reading.Pose;
        var next = pose switch
        {
            HandPose.Pinch => hand.Pinch > PinchOut ? Settle(hand) : HandPose.Pinch,
            HandPose.Fist => hand.Openness > FistOut ? Settle(hand) : HandPose.Fist,
            _ => Settle(hand),
        };
        if (next != pose)
        {
            EndPose(index, state);
            reading.Pose = next;
            reading.PoseSeconds = 0;
            state.FistFired = false;
            if (next == HandPose.Pinch)
            {
                (state.PinchX, state.PinchY, state.PinchTravel) = (x, y, 0);
                _gestures.Add(new Gesture(GestureKind.PinchStarted, index));
            }
        }
        else
        {
            reading.PoseSeconds += dt;
        }

        if (reading.Pose == HandPose.Pinch)
        {
            (reading.DragX, reading.DragY) = (x - state.PinchX, y - state.PinchY);
            state.PinchTravel = Math.Max(state.PinchTravel, MathF.Sqrt(Square(reading.DragX) + Square(reading.DragY)));
        }
        else
        {
            (reading.DragX, reading.DragY) = (0, 0);
        }

        if (reading.Pose == HandPose.Fist && !state.FistFired && reading.PoseSeconds >= FistDwell)
        {
            state.FistFired = true;
            _gestures.Add(new Gesture(GestureKind.FistHeld, index));
        }

        if (reading.Pose == HandPose.Open && state.Cooldown == 0 && state.Travel(SwipeWindow, out var across) && MathF.Abs(across) >= SwipeTravel)
        {
            state.Cooldown = Cooldown;
            _gestures.Add(new Gesture(GestureKind.Swiped, index, MathF.Sign(across)));
        }
    }

    /// <summary>The pose a hand enters from rest or from another pose, by the entering thresholds; a half-closed hand is nothing.</summary>
    private static HandPose Settle(HandSense hand) =>
        hand.Pinch <= PinchIn ? HandPose.Pinch
        : hand.Openness <= FistIn ? HandPose.Fist
        : hand.Openness >= OpenIn ? HandPose.Open
        : HandPose.None;

    private void EndPose(int index, HandState state)
    {
        var reading = state.Reading;
        switch (reading.Pose)
        {
            case HandPose.Pinch:
                _gestures.Add(new Gesture(
                    reading.PoseSeconds <= TapSeconds && state.PinchTravel <= TapTravel ? GestureKind.PinchTapped : GestureKind.PinchReleased,
                    index, reading.PoseSeconds));
                break;
            case HandPose.Fist when state.FistFired:
                _gestures.Add(new Gesture(GestureKind.FistReleased, index, reading.PoseSeconds));
                break;
        }
    }

    private void ReadBothHands(SceneSense scene, float dt)
    {
        var left = _hands[0];
        var right = _hands[1];
        var both = left.Reading.Present && right.Reading.Present;

        // Raised: both palms above the head (the nose when the body is seen, else the top quarter).
        var head = scene.Body.Present ? scene.Body.Landmarks[BodySense.Nose].Y : 0.25f;
        var up = both && left.Reading.Y < head && right.Reading.Y < head;
        if (up)
        {
            _raisedSeconds += dt;
            if (!_raised && _raisedSeconds >= RaiseDwell)
            {
                _raised = true;
                _gestures.Add(new Gesture(GestureKind.HandsRaised, -1));
            }
        }
        else
        {
            _raisedSeconds = 0;
            if (_raised)
            {
                _raised = false;
                _gestures.Add(new Gesture(GestureKind.HandsLowered, -1));
            }
        }

        // Pushed: both open palms grew quickly, which is both hands coming at the camera.
        var open = both && left.Reading.Pose == HandPose.Open && right.Reading.Pose == HandPose.Open;
        if (open && _pushCooldown == 0 && left.Growth(PushWindow) >= PushGrowth && right.Growth(PushWindow) >= PushGrowth)
        {
            _pushCooldown = Cooldown;
            _gestures.Add(new Gesture(GestureKind.PalmsPushed, -1));
        }

        // On the chest: both open hands inside the box below the shoulders, held for a second.
        var onChest = false;
        if (open && scene.Body.Present && scene.Body.Width > 0)
        {
            var (sx, sy) = scene.Body.Shoulders;
            var width = scene.Body.Width;
            onChest = OnChest(left.Reading, sx, sy, width) && OnChest(right.Reading, sx, sy, width);
        }

        if (onChest)
        {
            _chestSeconds += dt;
            if (!_chestFired && _chestSeconds >= ChestDwell)
            {
                _chestFired = true;
                _gestures.Add(new Gesture(GestureKind.ChestHeld, -1));
            }
        }
        else
        {
            (_chestSeconds, _chestFired) = (0, false);
        }
    }

    private static bool OnChest(HandReading hand, float sx, float sy, float width) =>
        MathF.Abs(hand.X - sx) <= width * 0.6f && hand.Y >= sy && hand.Y <= sy + (width * 0.9f);

    private static float Square(float value) => value * value;

    /// <summary>What the reader keeps about one hand between frames: its reading and a short history of where it was.</summary>
    private sealed class HandState
    {
        private readonly float[] _x = new float[History];
        private readonly float[] _size = new float[History];
        private readonly float[] _age = new float[History];
        private int _count;

        public HandReading Reading { get; } = new();

        public float Lost { get; set; }

        public float Cooldown { get; set; }

        public float PinchX { get; set; }

        public float PinchY { get; set; }

        public float PinchTravel { get; set; }

        public bool FistFired { get; set; }

        /// <summary>Adds this frame's place to the history, ageing the rest by <paramref name="dt"/>; the oldest falls off the end.</summary>
        public void Remember(float x, float y, float size, float dt)
        {
            _ = y;
            var keep = Math.Min(_count, History - 1);
            for (var i = keep; i > 0; i--)
            {
                (_x[i], _size[i], _age[i]) = (_x[i - 1], _size[i - 1], _age[i - 1] + dt);
            }

            (_x[0], _size[0], _age[0]) = (x, size, 0);
            _count = keep + 1;
        }

        /// <summary>How far the hand moved across in the last <paramref name="seconds"/>; false when the history is too short to say.</summary>
        public bool Travel(float seconds, out float across)
        {
            across = 0;
            var oldest = -1;
            for (var i = 1; i < _count; i++)
            {
                if (_age[i] <= seconds)
                {
                    oldest = i;
                }
            }

            if (oldest < 0)
            {
                return false;
            }

            across = _x[0] - _x[oldest];
            return true;
        }

        /// <summary>How much the palm has grown in the last <paramref name="seconds"/>: now over then; 1 with too little history.</summary>
        public float Growth(float seconds)
        {
            var oldest = -1;
            for (var i = 1; i < _count; i++)
            {
                if (_age[i] <= seconds)
                {
                    oldest = i;
                }
            }

            return oldest < 0 || _size[oldest] <= 0 ? 1 : _size[0] / _size[oldest];
        }

        public void Reset()
        {
            Reading.Present = false;
            Reading.Pose = HandPose.None;
            (Reading.X, Reading.Y, Reading.TipX, Reading.TipY, Reading.DragX, Reading.DragY, Reading.PoseSeconds, Reading.PalmSize) = (0, 0, 0, 0, 0, 0, 0, 0);
            (Lost, Cooldown, PinchX, PinchY, PinchTravel, FistFired, _count) = (0, 0, 0, 0, 0, false, 0);
        }
    }
}
