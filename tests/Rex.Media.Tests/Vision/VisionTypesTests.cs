using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class VisionTypesTests
{
    [Fact]
    public void APictureViewChecksItsShapeAndReadsRows()
    {
        var pixels = new byte[(12 * 2) + 8];
        pixels[12] = 7;
        var picture = new BgraPicture(pixels, 2, 3, 12);

        Assert.Equal((2, 3, 12), (picture.Width, picture.Height, picture.Stride));
        Assert.Equal(8, picture.Row(1).Length);
        Assert.Equal(7, picture.Row(1)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraPicture(pixels, 0, 3, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraPicture(pixels, 2, 0, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraPicture(pixels, 2, 3, 7));
        Assert.Throws<ArgumentException>(() => new BgraPicture(pixels, 2, 4, 12));
    }

    [Fact]
    public void BoxesGrowInsideThePicture()
    {
        var box = new PixelBox(10, 20, 40, 20);

        Assert.Equal((50, 40, false), (box.Right, box.Bottom, box.IsEmpty));
        Assert.Equal(new PixelBox(0, 15, 60, 30), box.Inflate(0.25, 100, 100));
        Assert.Equal(new PixelBox(0, 10, 55, 30), box.Inflate(0.5, 55, 40));
        Assert.True(new PixelBox(500, 500, 10, 10).Inflate(0.1, 100, 100).IsEmpty);
        Assert.True(new PixelBox(0, 0, 10, 0).IsEmpty);
    }

    [Fact]
    public void APromptIsAPointABoxOrBoth()
    {
        var point = SegmentPrompt.At(3, 4);
        var around = SegmentPrompt.Around(new PixelBox(1, 2, 3, 4));
        var both = new SegmentPrompt(3, 4, new PixelBox(1, 2, 3, 4));

        Assert.Equal((3, 4, (PixelBox?)null), (point.X, point.Y, point.Box));
        Assert.Equal(((int?)null, (int?)null, new PixelBox(1, 2, 3, 4)), (around.X, around.Y, around.Box));
        Assert.NotNull(both.Box);
        Assert.Throws<ArgumentException>(() => new SegmentPrompt(3, null));
        Assert.Throws<ArgumentException>(() => new SegmentPrompt(box: null));
        Assert.Throws<ArgumentException>(() => SegmentPrompt.Around(new PixelBox(1, 2, 0, 4)));
        Assert.Equal(40, SegmentPrompt.Following(new PixelBox(1, 2, 3, 4), 40).ExpectedArea);
        Assert.Null(point.ExpectedArea);
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentPrompt.Following(new PixelBox(1, 2, 3, 4), 0));
    }

    [Fact]
    public void AMaskKnowsItsAreaAndBounds()
    {
        var values = new byte[5 * 4];
        values[(1 * 5) + 1] = 255;
        values[(2 * 5) + 3] = 255;
        values[(2 * 5) + 2] = 255;
        var mask = new PictureMask(values, 5, 4, 1.4);

        Assert.Equal(3, mask.Area);
        Assert.Equal(new PixelBox(1, 1, 3, 2), mask.Bounds);
        Assert.Equal(1, mask.Score);
        Assert.True(mask.Contains(3, 2));
        Assert.False(mask.Contains(0, 0));
        Assert.False(mask.Contains(-1, 2));
        Assert.False(mask.Contains(2, -1));
        Assert.False(mask.Contains(5, 2));
        Assert.False(mask.Contains(2, 4));

        var empty = new PictureMask(new byte[4], 2, 2, -1);
        Assert.Equal((0, 0.0), (empty.Area, empty.Score));
        Assert.True(empty.Bounds.IsEmpty);
    }

    [Fact]
    public void AMaskChecksItsShape()
    {
        Assert.Throws<ArgumentNullException>(() => new PictureMask(null!, 2, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PictureMask(new byte[4], 0, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PictureMask(new byte[4], 2, 0, 0));
        Assert.Throws<ArgumentException>(() => new PictureMask(new byte[5], 2, 2, 0));
    }

    [Fact]
    public void TrackResultsSayWhatHappened()
    {
        var mask = new PictureMask([255], 1, 1, 1);

        Assert.Equal((TrackOutcome.Tracked, mask, ""), (TrackResult.Found(mask).Outcome, TrackResult.Found(mask).Mask, TrackResult.Found(mask).Reason));
        Assert.Equal((TrackOutcome.Lost, (PictureMask?)null, "gone"), (TrackResult.Lost("gone").Outcome, TrackResult.Lost("gone").Mask, TrackResult.Lost("gone").Reason));
    }
}
