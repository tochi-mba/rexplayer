using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class SceneSenseTests
{
    /// <summary>A right hand at <paramref name="x"/>, <paramref name="y"/>, palm up the picture, open or closed, in a picture of shares.</summary>
    internal static void PlaceHand(HandSense hand, float x, float y, float palm, bool open, bool pinching = false)
    {
        hand.Present = true;
        hand.Confidence = 0.9f;
        hand.Landmarks[HandSense.Wrist] = new Landmark(x, y, 0, 1);
        hand.Landmarks[HandSense.IndexBase] = new Landmark(x - (palm * 0.3f), y - palm, 0, 1);
        hand.Landmarks[HandSense.MiddleBase] = new Landmark(x, y - palm, 0, 1);
        hand.Landmarks[HandSense.PinkyBase] = new Landmark(x + (palm * 0.3f), y - palm, 0, 1);
        var reach = open ? 2f : 0.9f;
        hand.Landmarks[HandSense.ThumbTip] = new Landmark(x - (palm * (open ? 1.0f : 0.4f)), y - (palm * (open ? 0.9f : 0.6f)), 0, 1);
        hand.Landmarks[HandSense.IndexTip] = new Landmark(x - (palm * 0.3f), y - (palm * reach), 0, 1);
        hand.Landmarks[HandSense.MiddleTip] = new Landmark(x, y - (palm * reach), 0, 1);
        hand.Landmarks[HandSense.RingTip] = new Landmark(x + (palm * 0.2f), y - (palm * reach), 0, 1);
        hand.Landmarks[HandSense.PinkyTip] = new Landmark(x + (palm * 0.4f), y - (palm * reach), 0, 1);
        if (pinching)
        {
            hand.Landmarks[HandSense.ThumbTip] = hand.Landmarks[HandSense.IndexTip] with { X = hand.Landmarks[HandSense.IndexTip].X + (palm * 0.05f) };
        }
    }

    [Fact]
    public void AnOpenHandIsOpenAndNotPinching()
    {
        var hand = new HandSense();
        PlaceHand(hand, 0.5f, 0.6f, 0.1f, open: true);

        Assert.Equal(0.1f, hand.PalmSize, 3);
        Assert.InRange(hand.Openness, 1.7f, 2.3f);
        Assert.True(hand.Pinch > 0.5f, $"an open hand's pinch measure is large, not {hand.Pinch}");
        Assert.Equal(0.5f, hand.Centre.X, 3);
        Assert.InRange(hand.Centre.Y, 0.5f, 0.6f);
    }

    [Fact]
    public void AFistIsClosedAndAPinchTouches()
    {
        var fist = new HandSense();
        PlaceHand(fist, 0.5f, 0.6f, 0.1f, open: false);
        var pinch = new HandSense();
        PlaceHand(pinch, 0.5f, 0.6f, 0.1f, open: true, pinching: true);

        Assert.InRange(fist.Openness, 0.6f, 1.2f);
        Assert.InRange(pinch.Pinch, 0, 0.1f);
        Assert.InRange(pinch.Openness, 1.7f, 2.3f);
    }

    [Fact]
    public void AHandNotSeenCannotPinchOrOpen()
    {
        var hand = new HandSense();
        PlaceHand(hand, 0.5f, 0.6f, 0.1f, open: true, pinching: true);
        hand.Clear();

        Assert.False(hand.Present);
        Assert.Equal(0, hand.PalmSize);
        Assert.Equal(0, hand.Openness);
        Assert.Equal(float.PositiveInfinity, hand.Pinch);
        Assert.All(hand.Landmarks, point => Assert.Equal(default, point));
    }

    [Fact]
    public void TheMeasuresMeanTheSameNearAndFar()
    {
        var near = new HandSense();
        var far = new HandSense();
        PlaceHand(near, 0.5f, 0.6f, 0.2f, open: true);
        PlaceHand(far, 0.5f, 0.6f, 0.05f, open: true);

        Assert.Equal(near.Openness, far.Openness, 3);
        Assert.Equal(near.Pinch, far.Pinch, 3);
    }

    [Fact]
    public void TheBodyGivesItsShouldersAndWidth()
    {
        var body = new BodySense { Present = true, Confidence = 0.8f };
        body.Landmarks[BodySense.LeftShoulder] = new Landmark(0.35f, 0.4f, 0, 1);
        body.Landmarks[BodySense.RightShoulder] = new Landmark(0.65f, 0.42f, 0, 1);

        Assert.Equal(0.5f, body.Shoulders.X, 3);
        Assert.Equal(0.41f, body.Shoulders.Y, 3);
        Assert.Equal(0.3f, body.Width, 2);

        body.Clear();
        Assert.False(body.Present);
        Assert.Equal(0, body.Width);
        Assert.Equal(0, body.Confidence);
    }

    [Fact]
    public void AnEmptySceneHasNoOneAndNoMotion()
    {
        var scene = new SceneSense();

        Assert.Equal(SceneSense.DefaultWidth, scene.Width);
        Assert.Equal(SceneSense.DefaultHeight, scene.Height);
        Assert.Equal(SceneSense.DefaultWidth / SceneSense.FlowStep, scene.FlowWidth);
        Assert.Equal(SceneSense.DefaultHeight / SceneSense.FlowStep, scene.FlowHeight);
        Assert.Equal(0, scene.Presence);
        Assert.Equal(0, scene.Motion);
        Assert.Equal(SenseSource.None, scene.Source);
        Assert.True(scene.Hands[0].IsLeft);
        Assert.False(scene.Hands[1].IsLeft);
    }

    [Fact]
    public void PresenceAndMotionAreAverages()
    {
        var scene = new SceneSense(8, 8);
        Array.Fill(scene.Matte, 1f, 0, 16);
        Array.Fill(scene.FlowX, 3f);
        Array.Fill(scene.FlowY, 4f);

        Assert.Equal(0.25f, scene.Presence, 3);
        Assert.Equal(5f, scene.Motion, 3);
        Assert.Equal(1f, scene.MatteAt(7, 1));
        Assert.Equal(0f, scene.MatteAt(0, 2));
        Assert.Equal(0f, scene.MatteAt(-1, 0));
        Assert.Equal(0f, scene.MatteAt(0, 8));
    }

    [Fact]
    public void CopyingTakesEverythingAndClearingForgetsIt()
    {
        var scene = new SceneSense(8, 4) { Timestamp = 42, Source = SenseSource.Model };
        scene.Matte[3] = 0.5f;
        scene.FlowX[0] = 1;
        scene.FlowY[1] = -1;
        PlaceHand(scene.Hands[1], 0.5f, 0.5f, 0.1f, open: true);
        scene.Body.Present = true;
        scene.Body.Landmarks[BodySense.Nose] = new Landmark(0.5f, 0.2f, 0, 1);

        var copy = new SceneSense(8, 4);
        scene.CopyTo(copy);

        Assert.Equal(scene.Matte, copy.Matte);
        Assert.Equal(scene.FlowX, copy.FlowX);
        Assert.Equal(scene.FlowY, copy.FlowY);
        Assert.Equal(scene.Hands[1].Landmarks, copy.Hands[1].Landmarks);
        Assert.True(copy.Hands[1].Present);
        Assert.False(copy.Hands[0].Present);
        Assert.True(copy.Body.Present);
        Assert.Equal(42, copy.Timestamp);
        Assert.Equal(SenseSource.Model, copy.Source);

        copy.Clear();
        Assert.Equal(0, copy.Presence);
        Assert.Equal(0, copy.Motion);
        Assert.False(copy.Hands[1].Present);
        Assert.False(copy.Body.Present);
        Assert.Equal(0, copy.Timestamp);
        Assert.Equal(SenseSource.None, copy.Source);
    }

    [Fact]
    public void BadSizesAndMismatchedCopiesAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneSense(3, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneSense(8, 0));
        Assert.Throws<ArgumentException>(() => new SceneSense(8, 8).CopyTo(new SceneSense(16, 8)));
        Assert.Throws<ArgumentNullException>(() => new SceneSense(8, 8).CopyTo(null!));
        Assert.Throws<ArgumentNullException>(() => new HandSense().CopyTo(null!));
        Assert.Throws<ArgumentNullException>(() => new BodySense().CopyTo(null!));
    }

    [Fact]
    public void ALandmarkMeasuresItsDistanceAcrossThePicture()
    {
        Assert.Equal(5f, new Landmark(0, 0, 9, 1).DistanceTo(new Landmark(3, 4, -9, 1)), 4);
    }
}
