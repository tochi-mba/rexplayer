using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>A large subject's removal at half size: the copy, and the drawing back at full size.</summary>
public sealed class SubjectRemovalScalingTests
{
    private static VideoFrame Noise(int width, int height, int seed)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        new Random(seed).NextBytes(frame.Plane(0));
        return frame;
    }

    private static uint Pixel(VideoFrame frame, int x, int y) => BitConverter.ToUInt32(frame.Row(0, y).Slice(x * 4, 4));

    [Fact]
    public void TheReducedCopyIsTheRoundedMeanOfEachBlock()
    {
        // 37 pixels across makes 18 half-size ones: two runs of eight and a remainder of two.
        using var frame = Noise(37, 21, 1);
        using var half = VideoFrame.Rent(PixelFormat.Bgra32, 18, 10);
        SubjectRemovalScaling.Reduce(frame, half, 2);
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 18; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    var sum = frame.Row(0, 2 * y)[8 * x + c] + frame.Row(0, 2 * y)[8 * x + 4 + c]
                        + frame.Row(0, 2 * y + 1)[8 * x + c] + frame.Row(0, 2 * y + 1)[8 * x + 4 + c];
                    Assert.Equal((sum + 2) / 4, half.Row(0, y)[4 * x + c]);
                }
            }
        }

        Assert.Equal(0xFFFFFFFFu, SubjectRemovalScaling.Average(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue));

        // Three times smaller: four pixels spread over each three by three block, its corners.
        using var third = VideoFrame.Rent(PixelFormat.Bgra32, 12, 7);
        SubjectRemovalScaling.Reduce(frame, third, 3);
        for (var c = 0; c < 4; c++)
        {
            var sum = frame.Row(0, 3)[12 + c] + frame.Row(0, 3)[20 + c] + frame.Row(0, 5)[12 + c] + frame.Row(0, 5)[20 + c];
            Assert.Equal((sum + 2) / 4, third.Row(0, 1)[4 + c]);
        }

        Assert.Throws<ArgumentException>(() => SubjectRemovalScaling.Reduce(frame, half, 1));
        using var wrong = VideoFrame.Rent(PixelFormat.Bgra32, 19, 10);
        Assert.Throws<ArgumentException>(() => SubjectRemovalScaling.Reduce(frame, wrong, 2));
        Assert.Throws<ArgumentNullException>(() => SubjectRemovalScaling.Reduce(null!, half, 2));
        Assert.Throws<ArgumentNullException>(() => SubjectRemovalScaling.Reduce(frame, null!, 2));
    }

    /// <summary>
    /// A picture dark on the left and bright from column <paramref name="edge"/> on, its half-size
    /// copy, and a removal hiding the half-size pixels left of <paramref name="cut"/> with a red fill.
    /// </summary>
    private static (VideoFrame Frame, VideoFrame Half, byte[] Alpha, uint[] Fill) Edge(int edge, int cut)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 32, 16);
        for (var y = 0; y < 16; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < 32; x++)
            {
                var value = (byte)(x < edge ? 30 : 220);
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]) = (value, value, value, 255);
            }
        }

        var half = VideoFrame.Rent(PixelFormat.Bgra32, 16, 8);
        SubjectRemovalScaling.Reduce(frame, half, 2);
        var alpha = new byte[16 * 8];
        var fill = new uint[16 * 8];
        for (var at = 0; at < alpha.Length; at++)
        {
            alpha[at] = (byte)(at % 16 < cut ? 255 : 0);
            fill[at] = 0x0000C0;
        }

        return (frame, half, alpha, fill);
    }

    [Fact]
    public void InsideTheHoleTheFillIsEnlargedAndOutsideThePictureIsKept()
    {
        var (frame, half, alpha, fill) = Edge(edge: 32, cut: 8);
        using var _ = frame;
        using var __ = half;
        using var output = VideoFrame.Rent(PixelFormat.Bgra32, 32, 16);
        frame.Plane(0).CopyTo(output.Plane(0));
        // A full-size column right of every half-size pixel's neighbours: some fill blends in.
        fill[3] = 0x00C000;
        SubjectRemovalScaling.Enlarge(frame, half, output, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]);
        Assert.Equal(0xFF0000C0u, Pixel(output, 2, 8));
        Assert.Equal(Pixel(frame, 24, 8), Pixel(output, 24, 8));
        // Green bleeds into the full-size pixels beside the half-size pixel that holds it, by the
        // nine, three, three and one sixteenths of a two-times enlargement.
        Assert.Equal(0xFF00_90_30u, Pixel(output, 6, 0));
        // At the edge of the hole the opacity fades over a pixel either side.
        Assert.InRange(Pixel(output, 16, 8) & 0xFF, 31u, 219u);
        Assert.Equal(Pixel(frame, 17, 8), Pixel(output, 17, 8));

        // An area with nothing in it draws nothing.
        SubjectRemovalScaling.Enlarge(frame, half, output, 2, (4, 4, 4, 6), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]);
        SubjectRemovalScaling.Enlarge(frame, half, output, 2, (4, 4, 6, 4), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]);
        Assert.Throws<ArgumentNullException>(() => SubjectRemovalScaling.Enlarge(null!, half, output, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]));
        Assert.Throws<ArgumentNullException>(() => SubjectRemovalScaling.Enlarge(frame, null!, output, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]));
        Assert.Throws<ArgumentNullException>(() => SubjectRemovalScaling.Enlarge(frame, half, null!, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]));
    }

    [Fact]
    public void TheSoftEdgeFollowsThePicturesOwnEdgeRatherThanTheHalfSizeGrid()
    {
        // The subject (dark) ends at full-size column 15, the last of the half-size pixel 7, which
        // the removal hides. Bilinear enlargement alone would hide column 16 by a quarter and column
        // 15 by three quarters; joined to the picture, column 16 is bright like the half-size pixel 8
        // beside it and is left alone, and column 15 is dark like pixel 7 and is hidden entirely.
        var (frame, half, alpha, fill) = Edge(edge: 16, cut: 8);
        using var _ = frame;
        using var __ = half;
        using var output = VideoFrame.Rent(PixelFormat.Bgra32, 32, 16);
        frame.Plane(0).CopyTo(output.Plane(0));
        SubjectRemovalScaling.Enlarge(frame, half, output, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32]);
        Assert.Equal(0xFF0000C0u, Pixel(output, 15, 6));
        var bilinear = SubjectRemovalScaling.Over(Pixel(frame, 16, 6), 0xC0, 64);
        Assert.Equal(Pixel(frame, 16, 6), Pixel(output, 16, 6));
        Assert.NotEqual(Pixel(frame, 16, 6), bilinear);
    }

    [Fact]
    public void AnEstimateGivesTheMarkedPixelsItsOwnFullSizeColours()
    {
        var (frame, half, alpha, fill) = Edge(edge: 32, cut: 8);
        using var _ = frame;
        using var __ = half;
        using var output = VideoFrame.Rent(PixelFormat.Bgra32, 32, 16);
        using var estimate = Noise(32, 16, 4);
        frame.Plane(0).CopyTo(output.Plane(0));
        var estimated = alpha.Select((a, at) => a == 255 && at / 16 < 4).ToArray();
        // Even a marked edge pixel with partial matte opacity must take the model's exact full-size colour.
        alpha[1 * 16 + 1] = 128;
        SubjectRemovalScaling.Enlarge(frame, half, output, 2, (0, 0, 16, 8), alpha, fill, new int[SubjectRemovalScaling.ScratchPerColumn * 32], estimate, estimated);
        Assert.Equal(Pixel(estimate, 3, 2) | 0xFF000000, Pixel(output, 3, 2));
        Assert.Equal(Pixel(estimate, 2, 2) | 0xFF000000, Pixel(output, 2, 2));
        Assert.Equal(0xFF0000C0u, Pixel(output, 3, 12));
    }
}
