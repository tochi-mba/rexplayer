using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class GestureReaderTests
{
    private const float Frame = 1 / 30f;
    private readonly SceneSense _scene = new();
    private readonly GestureReader _reader = new();

    private void Hand(int index, float x, float y, float palm = 0.1f, bool open = true, bool pinching = false, bool present = true)
    {
        if (!present)
        {
            _scene.Hands[index].Clear();
            return;
        }

        SceneSenseTests.PlaceHand(_scene.Hands[index], x, y, palm, open, pinching);
    }

    private void Body(float shoulderY = 0.45f, float width = 0.3f)
    {
        _scene.Body.Present = true;
        _scene.Body.Landmarks[BodySense.Nose] = new Landmark(0.5f, shoulderY - 0.15f, 0, 1);
        _scene.Body.Landmarks[BodySense.LeftShoulder] = new Landmark(0.5f - (width / 2), shoulderY, 0, 1);
        _scene.Body.Landmarks[BodySense.RightShoulder] = new Landmark(0.5f + (width / 2), shoulderY, 0, 1);
    }

    /// <summary>Reads <paramref name="frames"/> frames of the scene as it is and gathers every gesture.</summary>
    private List<Gesture> Run(int frames)
    {
        var seen = new List<Gesture>();
        for (var i = 0; i < frames; i++)
        {
            _reader.Read(_scene, Frame);
            seen.AddRange(_reader.Gestures);
        }

        return seen;
    }

    [Fact]
    public void AnEmptySceneReadsNothing()
    {
        Assert.Empty(Run(10));
        Assert.False(_reader.Hands[0].Present);
        Assert.False(_reader.Hands[1].Present);
        Assert.False(_reader.HandsUp);
        Assert.True(float.IsNaN(_reader.Spread));
    }

    [Fact]
    public void AnOpenHandIsReadWhereItIs()
    {
        Hand(1, 0.6f, 0.5f);
        Run(1);

        var hand = _reader.Hands[1];
        Assert.True(hand.Present);
        Assert.Equal(HandPose.Open, hand.Pose);
        Assert.Equal(0.6f, hand.X, 3);
        Assert.Equal(0.1f, hand.PalmSize, 3);
        Assert.Equal(_scene.Hands[1].Landmarks[HandSense.IndexTip].X, hand.TipX);
        Assert.Equal(_scene.Hands[1].Landmarks[HandSense.IndexTip].Y, hand.TipY);
    }

    [Fact]
    public void AQuickPinchIsATapAndALongOneAHold()
    {
        Hand(1, 0.6f, 0.5f);
        Run(2);
        Hand(1, 0.6f, 0.5f, pinching: true);
        var started = Run(3);
        Hand(1, 0.6f, 0.5f);
        var released = Run(1);

        Assert.Equal([new Gesture(GestureKind.PinchStarted, 1)], started);
        Assert.Single(released);
        Assert.Equal(GestureKind.PinchTapped, released[0].Kind);
        Assert.Equal(1, released[0].Hand);

        Hand(1, 0.6f, 0.5f, pinching: true);
        Run(20);
        Hand(1, 0.6f, 0.5f);
        var held = Run(1);
        Assert.Equal(GestureKind.PinchReleased, held[0].Kind);
        Assert.InRange(held[0].Value, 0.5f, 0.7f);
    }

    [Fact]
    public void APinchThatTravelsIsADragNotATap()
    {
        Hand(0, 0.4f, 0.5f, pinching: true);
        Run(1);
        for (var i = 1; i <= 3; i++)
        {
            Hand(0, 0.4f, 0.5f - (0.03f * i), pinching: true);
            Run(1);
        }

        Assert.Equal(0, _reader.Hands[0].DragX, 3);
        Assert.Equal(-0.09f, _reader.Hands[0].DragY, 3);

        Hand(0, 0.4f, 0.41f);
        var released = Run(1);
        Assert.Equal(GestureKind.PinchReleased, released[0].Kind);
        Assert.Equal(0, _reader.Hands[0].DragY);
    }

    [Fact]
    public void AFistCountsAfterItsDwellAndIsReleasedOnce()
    {
        Hand(1, 0.5f, 0.5f, open: false);
        var early = Run(3);
        Assert.Empty(early);
        Assert.Equal(HandPose.Fist, _reader.Hands[1].Pose);

        var held = Run(3);
        Assert.Equal([new Gesture(GestureKind.FistHeld, 1)], held);
        Assert.Empty(Run(10));

        Hand(1, 0.5f, 0.5f);
        var released = Run(1);
        Assert.Single(released);
        Assert.Equal(GestureKind.FistReleased, released[0].Kind);
    }

    [Fact]
    public void AFistLetGoBeforeItsDwellIsNothing()
    {
        Hand(1, 0.5f, 0.5f, open: false);
        Run(2);
        Hand(1, 0.5f, 0.5f);
        Assert.Empty(Run(2));
    }

    [Fact]
    public void PosesHaveHysteresisSoTheEdgeNeverFlickers()
    {
        // A pinch opens slightly past the entering threshold but not past the leaving one: still a pinch.
        var hand = _scene.Hands[1];
        SceneSenseTests.PlaceHand(hand, 0.5f, 0.5f, 0.1f, open: true, pinching: true);
        Run(1);
        Assert.Equal(HandPose.Pinch, _reader.Hands[1].Pose);
        hand.Landmarks[HandSense.ThumbTip] = hand.Landmarks[HandSense.IndexTip] with { X = hand.Landmarks[HandSense.IndexTip].X + 0.04f };
        Assert.InRange(hand.Pinch, GestureReader.PinchIn, GestureReader.PinchOut);
        Run(1);
        Assert.Equal(HandPose.Pinch, _reader.Hands[1].Pose);
        Assert.Empty(_reader.Gestures);

        // Wider than the leaving threshold: released.
        hand.Landmarks[HandSense.ThumbTip] = hand.Landmarks[HandSense.IndexTip] with { X = hand.Landmarks[HandSense.IndexTip].X + 0.06f };
        Run(1);
        Assert.Equal(HandPose.Open, _reader.Hands[1].Pose);
    }

    [Fact]
    public void AHalfClosedHandIsNoPose()
    {
        var hand = _scene.Hands[1];
        SceneSenseTests.PlaceHand(hand, 0.5f, 0.5f, 0.1f, open: true);
        for (var tip = HandSense.IndexTip; tip <= HandSense.PinkyTip; tip += 4)
        {
            hand.Landmarks[tip] = hand.Landmarks[tip] with { Y = 0.5f - 0.135f };
        }

        Assert.InRange(hand.Openness, GestureReader.FistIn, GestureReader.OpenIn);
        Run(1);
        Assert.Equal(HandPose.None, _reader.Hands[1].Pose);
    }

    [Fact]
    public void ABriefDropoutKeepsThePoseAndALongerOneDropsIt()
    {
        Hand(1, 0.5f, 0.5f, pinching: true);
        Run(1);
        Hand(1, 0, 0, present: false);
        Assert.Empty(Run(4));
        Assert.Equal(HandPose.Pinch, _reader.Hands[1].Pose);
        Assert.True(_reader.Hands[1].Present);

        var dropped = Run(4);
        Assert.Single(dropped);
        Assert.Equal(GestureKind.PinchTapped, dropped[0].Kind);
        Assert.False(_reader.Hands[1].Present);
        Assert.Equal(HandPose.None, _reader.Hands[1].Pose);
    }

    [Fact]
    public void AFastSidewaysMoveIsASwipeOnceUntilTheHandSettles()
    {
        for (var i = 0; i < 6; i++)
        {
            Hand(1, 0.3f + (0.04f * i), 0.5f);
            _reader.Read(_scene, Frame);
            if (_reader.Gestures.Count > 0)
            {
                Assert.Equal([new Gesture(GestureKind.Swiped, 1, 1)], _reader.Gestures);
                break;
            }

            Assert.True(i < 5, "a swipe should have been read.");
        }

        // Still moving: the cooldown holds; moving back the other way after it: a swipe left.
        for (var i = 0; i < 5; i++)
        {
            Hand(1, 0.5f + (0.04f * i), 0.5f);
            Run(1);
            Assert.Empty(_reader.Gestures);
        }

        Run(20);
        var back = new List<Gesture>();
        for (var i = 0; i < 6; i++)
        {
            Hand(1, 0.7f - (0.04f * i), 0.5f);
            back.AddRange(Run(1));
        }

        Assert.Equal([new Gesture(GestureKind.Swiped, 1, -1)], back);
    }

    [Fact]
    public void ASlowDriftIsNotASwipe()
    {
        var seen = new List<Gesture>();
        for (var i = 0; i < 30; i++)
        {
            Hand(1, 0.3f + (0.01f * i), 0.5f);
            seen.AddRange(Run(1));
        }

        Assert.Empty(seen);
    }

    [Fact]
    public void BothHandsAboveTheHeadRaiseAfterADwellAndLowerAtOnce()
    {
        Body();
        Hand(0, 0.4f, 0.2f);
        Hand(1, 0.6f, 0.2f);
        var early = Run(6);
        Assert.Empty(early);
        Assert.False(_reader.HandsUp);

        var raised = Run(7);
        Assert.Equal([new Gesture(GestureKind.HandsRaised, -1)], raised);
        Assert.True(_reader.HandsUp);
        Assert.Empty(Run(10));

        Hand(1, 0.6f, 0.5f);
        Assert.Equal([new Gesture(GestureKind.HandsLowered, -1)], Run(1));
        Assert.False(_reader.HandsUp);
    }

    [Fact]
    public void WithoutABodyTheTopOfThePictureIsAboveTheHead()
    {
        Hand(0, 0.4f, 0.2f);
        Hand(1, 0.6f, 0.2f);
        Assert.Contains(new Gesture(GestureKind.HandsRaised, -1), Run(15));
    }

    [Fact]
    public void BothPalmsComingAtTheCameraPush()
    {
        var seen = new List<Gesture>();
        for (var i = 0; i < 8; i++)
        {
            var palm = 0.08f + (0.01f * i);
            Hand(0, 0.4f, 0.5f, palm);
            Hand(1, 0.6f, 0.5f, palm);
            seen.AddRange(Run(1));
        }

        Assert.Equal([new Gesture(GestureKind.PalmsPushed, -1)], seen);

        // Growing on: the cooldown holds it to one push.
        for (var i = 8; i < 12; i++)
        {
            var palm = 0.08f + (0.01f * i);
            Hand(0, 0.4f, 0.5f, palm);
            Hand(1, 0.6f, 0.5f, palm);
            Assert.Empty(Run(1));
        }
    }

    [Fact]
    public void OneHandAloneOrAFistDoesNotPush()
    {
        var seen = new List<Gesture>();
        for (var i = 0; i < 8; i++)
        {
            Hand(0, 0.4f, 0.5f, 0.08f + (0.01f * i));
            Hand(1, 0.6f, 0.5f, 0.08f + (0.01f * i), open: false);
            seen.AddRange(Run(1));
        }

        Assert.DoesNotContain(seen, gesture => gesture.Kind == GestureKind.PalmsPushed);
    }

    [Fact]
    public void BothHandsOnTheChestForASecondAreRead()
    {
        Body(shoulderY: 0.45f, width: 0.3f);
        Hand(0, 0.45f, 0.6f);
        Hand(1, 0.55f, 0.6f);
        Assert.Empty(Run(29));
        Assert.Equal([new Gesture(GestureKind.ChestHeld, -1)], Run(2));
        Assert.Empty(Run(30));

        // Away and back: it may be read again after another second.
        Hand(1, 0.9f, 0.6f);
        Run(1);
        Hand(1, 0.55f, 0.6f);
        Assert.Empty(Run(29));
        Assert.Equal([new Gesture(GestureKind.ChestHeld, -1)], Run(2));
    }

    [Fact]
    public void HandsBesideTheBodyOrWithNoBodyAreNotOnTheChest()
    {
        Hand(0, 0.45f, 0.6f);
        Hand(1, 0.55f, 0.6f);
        Assert.Empty(Run(40));

        // A hand beside the body rather than on the chest (the reader starts afresh, or the jump would be a swipe).
        _reader.Reset();
        Body(shoulderY: 0.45f, width: 0.3f);
        Hand(0, 0.1f, 0.6f);
        Assert.Empty(Run(40));
    }

    [Fact]
    public void TheSpreadIsTheDistanceBetweenTwoPinchingHands()
    {
        Hand(0, 0.3f, 0.5f, pinching: true);
        Hand(1, 0.6f, 0.5f, pinching: true);
        Run(1);
        Assert.Equal(0.3f, _reader.Spread, 3);

        Hand(1, 0.6f, 0.5f);
        Run(1);
        Assert.True(float.IsNaN(_reader.Spread));
    }

    [Fact]
    public void ResettingForgetsEverything()
    {
        Hand(0, 0.4f, 0.2f, pinching: true);
        Hand(1, 0.6f, 0.2f);
        Run(15);
        Assert.True(_reader.HandsUp);
        _reader.Reset();

        Assert.False(_reader.HandsUp);
        Assert.Empty(_reader.Gestures);
        Assert.False(_reader.Hands[0].Present);
        Assert.Equal(HandPose.None, _reader.Hands[0].Pose);
        // The pinch starts again rather than being released.
        Assert.Equal([new Gesture(GestureKind.PinchStarted, 0), new Gesture(GestureKind.HandsRaised, -1)], Run(15).Distinct());
    }

    [Fact]
    public void BadIntervalsAreTakenAsNoTimeAndANullSceneIsRefused()
    {
        Hand(1, 0.5f, 0.5f, open: false);
        _reader.Read(_scene, float.NaN);
        _reader.Read(_scene, -1);
        Assert.Equal(0, _reader.Hands[1].PoseSeconds);
        _reader.Read(_scene, 10);
        Assert.Equal(0.25f, _reader.Hands[1].PoseSeconds);
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!, Frame));
    }
}
