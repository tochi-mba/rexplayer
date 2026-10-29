using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class HandWrittenSenseTests
{
    private const int Width = 32;
    private const int Height = 24;

    /// <summary>A BGRA picture of a flat grey room with a bright figure (a rectangle) at a place, if any.</summary>
    private static byte[] Picture(int width, int height, (int Left, int Top, int Right, int Bottom)? figure = null, byte room = 60, byte person = 220)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = figure is { } f && x >= f.Left && x < f.Right && y >= f.Top && y < f.Bottom;
                var value = inside ? person : room;
                var at = ((y * width) + x) * 4;
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (value, value, value, 255);
            }
        }

        return pixels;
    }

    private static BgraPicture View(byte[] pixels, int width, int height) => new(pixels, width, height, width * 4);

    [Fact]
    public void ShrinkingAveragesAndMirrors()
    {
        var pixels = Picture(8, 4, (4, 0, 8, 4), room: 0, person: 200);
        var luma = new byte[4 * 2];
        MotionMatte.Shrink(View(pixels, 8, 4), 4, 2, mirror: false, luma);
        Assert.Equal([0, 0, 200, 200, 0, 0, 200, 200], luma);
        MotionMatte.Shrink(View(pixels, 8, 4), 4, 2, mirror: true, luma);
        Assert.Equal([200, 200, 0, 0, 200, 200, 0, 0], luma);
    }

    [Fact]
    public void WhatMovesIsMarkedAtOnceAndTheStillRoomIsNot()
    {
        var matte = new MotionMatte(Width, Height);
        var result = new float[Width * Height];
        matte.Update(View(Picture(Width, Height), Width, Height), false, result);
        Assert.All(result, value => Assert.Equal(0, value));
        Assert.False(matte.RoomKnown);

        // A figure appears: it differs from the last frame, so it is marked before the room is known.
        matte.Update(View(Picture(Width, Height, (10, 4, 16, 20)), Width, Height), false, result);
        Assert.True(result[(10 * Width) + 13] > 0.5f, "inside the figure is marked");
        Assert.Equal(0, result[(2 * Width) + 2]);
        Assert.Equal(0, result[(22 * Width) + 30]);
    }

    [Fact]
    public void AFigureThatStaysIsStillMarkedOnceTheRoomIsKnownFromBeforeIt()
    {
        var matte = new MotionMatte(Width, Height);
        var result = new float[Width * Height];
        var room = View(Picture(Width, Height), Width, Height);
        for (var i = 0; i < MotionMatte.Stride * 3; i++)
        {
            matte.Update(room, false, result);
        }

        Assert.True(matte.RoomKnown);
        var figure = Picture(Width, Height, (10, 4, 16, 20));
        for (var i = 0; i < 5; i++)
        {
            matte.Update(View(figure, Width, Height), false, result);
        }

        // Still, but different from the remembered room: marked; the room around is not.
        Assert.True(result[(10 * Width) + 13] > 0.5f);
        Assert.Equal(0, result[(2 * Width) + 2]);
        Assert.Equal(0, result[(10 * Width) + 25]);
    }

    [Fact]
    public void TheRoomIsForgottenOnReset()
    {
        var matte = new MotionMatte(Width, Height) { Threshold = 0.2f };
        var result = new float[Width * Height];
        for (var i = 0; i < MotionMatte.Stride * 3; i++)
        {
            matte.Update(View(Picture(Width, Height), Width, Height), false, result);
        }

        matte.Reset();
        Assert.False(matte.RoomKnown);
        Assert.Equal(0.2f, matte.Threshold);
        Assert.Throws<ArgumentException>(() => matte.Update(View(Picture(Width, Height), Width, Height), false, new float[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionMatte(2, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionMatte(8, 2));
    }

    [Fact]
    public void TheFlowFollowsAMovingEdgeAndIsStillOnAFlatPicture()
    {
        var flow = new BlockFlow(Width, Height);
        var cells = (Width / SceneSense.FlowStep) * (Height / SceneSense.FlowStep);
        var flowX = new float[cells];
        var flowY = new float[cells];
        var luma = new byte[Width * Height];
        MotionMatte.Shrink(View(Picture(Width, Height, (8, 0, 14, Height)), Width, Height), Width, Height, false, luma);
        flow.Update(luma, flowX, flowY);
        Assert.All(flowX, value => Assert.Equal(0, value));

        // The figure moves three pixels right: the cells at its edges say so, the flat room says nothing.
        MotionMatte.Shrink(View(Picture(Width, Height, (11, 0, 17, Height)), Width, Height), Width, Height, false, luma);
        flow.Update(luma, flowX, flowY);
        var edgeCell = (2 * (Width / SceneSense.FlowStep)) + 2;
        Assert.Equal(3, flowX[edgeCell]);
        Assert.Equal(0, flowY[edgeCell]);
        Assert.Equal(0, flowX[(2 * (Width / SceneSense.FlowStep)) + 7]);

        flow.Reset();
        flow.Update(luma, flowX, flowY);
        Assert.All(flowX, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TheFlowChecksItsInput()
    {
        var flow = new BlockFlow(8, 8);
        Assert.Throws<ArgumentException>(() => flow.Update(new byte[8], new float[4], new float[4]));
        Assert.Throws<ArgumentException>(() => flow.Update(new byte[64], new float[3], new float[4]));
        Assert.Throws<ArgumentException>(() => flow.Update(new byte[64], new float[4], new float[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockFlow(3, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlockFlow(8, 3));
    }

    [Fact]
    public void ThePerceiverFillsASceneAndGuessesHandsAtTheOutlinesUpperEnds()
    {
        var perceiver = new HandWrittenPerceiver(Width, Height) { Threshold = 0.1f };
        var scene = new SceneSense(Width, Height);
        perceiver.Perceive(View(Picture(Width, Height), Width, Height), false, scene, 1);
        Assert.Equal(SenseSource.HandWritten, scene.Source);
        Assert.Equal(1, scene.Timestamp);
        Assert.False(scene.Hands[0].Present);
        Assert.False(scene.Hands[1].Present);

        // A figure with both arms up: a block with two columns rising from its top corners.
        var pixels = Picture(Width, Height, (10, 10, 22, 24));
        foreach (var (left, right) in new[] { (9, 11), (21, 23) })
        {
            for (var y = 2; y < 10; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var at = ((y * Width) + x) * 4;
                    (pixels[at], pixels[at + 1], pixels[at + 2]) = (220, 220, 220);
                }
            }
        }

        perceiver.Perceive(View(pixels, Width, Height), false, scene, 2);
        Assert.True(scene.Hands[0].Present);
        Assert.True(scene.Hands[1].Present);
        Assert.InRange(scene.Hands[0].Centre.X, 0.2f, 0.4f);
        Assert.InRange(scene.Hands[1].Centre.X, 0.6f, 0.8f);
        Assert.True(scene.Hands[0].Centre.Y < 0.3f);
        Assert.Equal(HandWrittenPerceiver.GuessConfidence, scene.Hands[0].Confidence);
        Assert.False(scene.Body.Present);
        // A guessed hand is open: it cannot pinch or make a fist.
        Assert.True(scene.Hands[0].Openness > GestureReader.OpenIn);
        Assert.True(scene.Hands[0].Pinch > GestureReader.PinchOut);
        Assert.Equal(2, scene.Timestamp);
        Assert.Equal((Width, Height), (perceiver.Width, perceiver.Height));
        Assert.Equal(0.1f, perceiver.Threshold);
    }

    [Fact]
    public void AFigureWithArmsDownHasNoHandsAndAResetForgetsTheRoom()
    {
        var perceiver = new HandWrittenPerceiver(Width, Height) { Threshold = 0.1f };
        var scene = new SceneSense(Width, Height);
        perceiver.Perceive(View(Picture(Width, Height), Width, Height), false, scene, 1);
        perceiver.Perceive(View(Picture(Width, Height, (10, 12, 22, 24)), Width, Height), false, scene, 2);
        Assert.True(scene.Presence > 0.1f);
        Assert.False(scene.Hands[0].Present);
        Assert.False(scene.Hands[1].Present);

        perceiver.Reset();
        perceiver.Perceive(View(Picture(Width, Height, (10, 12, 22, 24)), Width, Height), false, scene, 3);
        // After a reset the still figure is the new room's first picture: nothing differs.
        Assert.Equal(0, scene.Presence);
        Assert.Throws<ArgumentException>(() => perceiver.Perceive(View(Picture(Width, Height), Width, Height), false, new SceneSense(16, 8), 4));
        Assert.Throws<ArgumentNullException>(() => perceiver.Perceive(View(Picture(Width, Height), Width, Height), false, null!, 4));
    }

    [Fact]
    public void TheDefaultPerceiverIsTheDefaultSceneSize()
    {
        var perceiver = new HandWrittenPerceiver();
        Assert.Equal((SceneSense.DefaultWidth, SceneSense.DefaultHeight), (perceiver.Width, perceiver.Height));
    }

    [Fact]
    public void AnOutlineWithNothingLeftOfItsMiddleGuessesOnlyTheRightHand()
    {
        var perceiver = new HandWrittenPerceiver(Width, Height) { Threshold = 0.1f };
        var scene = new SceneSense(Width, Height);
        perceiver.Perceive(View(Picture(Width, Height), Width, Height), false, scene, 1);
        // A pole two pixels wide: its middle is its left column, so nothing is left of the middle.
        perceiver.Perceive(View(Picture(Width, Height, (10, 0, 12, Height)), Width, Height), false, scene, 2);

        Assert.False(scene.Hands[0].Present);
        Assert.True(scene.Hands[1].Present);
        Assert.True(scene.Hands[1].Centre.Y < 0.2f);
    }
}
