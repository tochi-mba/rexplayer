using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class VideoFrameTests
{
    [Theory]
    [InlineData(PixelFormat.Nv12, 2, 8, new[] { 5, 3, 6, 2 })]
    [InlineData(PixelFormat.P010, 2, 16, new[] { 10, 3, 12, 2 })]
    [InlineData(PixelFormat.I420, 3, 8, new[] { 5, 3, 3, 2, 3, 2 })]
    [InlineData(PixelFormat.I420P10, 3, 16, new[] { 10, 3, 6, 2, 6, 2 })]
    [InlineData(PixelFormat.I422, 3, 8, new[] { 5, 3, 3, 3, 3, 3 })]
    [InlineData(PixelFormat.I444, 3, 8, new[] { 5, 3, 5, 3, 5, 3 })]
    [InlineData(PixelFormat.Gray8, 1, 8, new[] { 5, 3 })]
    [InlineData(PixelFormat.Bgra32, 1, 8, new[] { 20, 3 })]
    public void EachFormatLaysOutItsPlanes(PixelFormat format, int planes, int bits, int[] sizes)
    {
        Assert.Equal(planes, format.PlaneCount());
        Assert.Equal(bits, format.StorageBits());
        for (var plane = 0; plane < planes; plane++)
        {
            Assert.Equal((sizes[plane * 2], sizes[(plane * 2) + 1]), format.PlaneSize(plane, 5, 3));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => format.PlaneSize(planes, 5, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => format.PlaneSize(-1, 5, 3));
    }

    [Fact]
    public void AnUnknownFormatHasNoLayout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelFormat.Unknown.PlaneCount());
    }

    [Fact]
    public void RowsArePaddedToSixtyFourBytesAndPlanesDoNotOverlap()
    {
        using var frame = VideoFrame.Rent(PixelFormat.I420, 100, 50);

        Assert.Equal((128, 64, 64), (frame.Stride(0), frame.Stride(1), frame.Stride(2)));
        Assert.Equal(128 * 50, frame.Plane(0).Length);
        Assert.Equal(64 * 25, frame.Plane(1).Length);
        Assert.Equal(100, frame.Row(0, 49).Length);
        frame.Plane(0).Fill(1);
        frame.Plane(1).Fill(2);
        frame.Plane(2).Fill(3);
        Assert.All(frame.Row(0, 49).ToArray(), b => Assert.Equal(1, b));
        Assert.All(frame.Row(1, 0).ToArray(), b => Assert.Equal(2, b));
        Assert.Equal((MediaTime.Unknown, MediaTime.Zero, new Rational(1, 1), 0L), (frame.Pts, frame.Duration, frame.PixelAspect, frame.Generation));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Row(0, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Row(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Stride(3));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(16385, 1)]
    [InlineData(1, 16385)]
    public void SizesOutsideTheLimitsAreRefused(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoFrame.Rent(PixelFormat.Nv12, width, height));
    }

    [Fact]
    public void ADisposedFrameRefusesItsPlanesAndAPooledOneIsReset()
    {
        var unpooled = VideoFrame.Rent(null, PixelFormat.Gray8, 4, 4);
        unpooled.Dispose();
        unpooled.Dispose();

        Assert.True(unpooled.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => unpooled.Plane(0));

        var pool = new SlotPool<VideoFrame>(1);
        var first = VideoFrame.Rent(pool, PixelFormat.Gray8, 4, 4);
        first.Pts = MediaTime.FromSeconds(3);
        first.Generation = 9;
        first.Color = new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, true);
        first.Dispose();
        using var second = VideoFrame.Rent(pool, PixelFormat.Nv12, 8, 8);

        Assert.Same(first, second);
        Assert.False(second.IsDisposed);
        Assert.Equal((MediaTime.Unknown, 0L, ColorInfo.Unspecified), (second.Pts, second.Generation, second.Color));
    }
}
