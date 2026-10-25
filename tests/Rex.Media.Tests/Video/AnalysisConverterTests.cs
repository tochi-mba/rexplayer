using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

public sealed class AnalysisConverterTests
{
    private static VideoFrame Random(PixelFormat format, int width, int height, ColorInfo color, int seed = 3)
    {
        var frame = VideoFrame.Rent(format, width, height);
        var random = new Random(seed);
        var planes = format switch
        {
            PixelFormat.Gray8 or PixelFormat.Bgra32 => 1,
            PixelFormat.Nv12 or PixelFormat.P010 => 2,
            _ => 3,
        };
        for (var plane = 0; plane < planes; plane++)
        {
            random.NextBytes(frame.Plane(plane));
        }

        if (format is PixelFormat.P010 or PixelFormat.I420P10)
        {
            // Keep 16-bit samples to their ten bits, where P010 holds them in the top and the other format in the bottom.
            for (var plane = 0; plane < planes; plane++)
            {
                var bytes = frame.Plane(plane);
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    if (format == PixelFormat.P010)
                    {
                        bytes[i] &= 0xC0;
                    }
                    else
                    {
                        bytes[i + 1] &= 0x03;
                    }
                }
            }
        }

        frame.Color = color;
        frame.Pts = MediaTime.FromMilliseconds(40);
        frame.Duration = MediaTime.FromMilliseconds(40);
        frame.Generation = 7;
        frame.PixelAspect = new Rational(4, 3);
        return frame;
    }

    public static TheoryData<PixelFormat, ColorMatrix, bool> Formats()
    {
        var data = new TheoryData<PixelFormat, ColorMatrix, bool>();
        foreach (var format in new[] { PixelFormat.Nv12, PixelFormat.P010, PixelFormat.I420, PixelFormat.I420P10, PixelFormat.I422, PixelFormat.I444, PixelFormat.Gray8 })
        {
            data.Add(format, ColorMatrix.Bt709, false);
            data.Add(format, ColorMatrix.Bt2020NonConstant, true);
        }

        data.Add(PixelFormat.I444, ColorMatrix.Rgb, true);
        return data;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryChannelIsWithinOneLevelOfTheExactConversion(PixelFormat format, ColorMatrix matrix, bool fullRange)
    {
        using var source = Random(format, 17, 9, new ColorInfo(matrix, ColorTransfer.Bt709, ColorPrimaries.Bt709, fullRange));
        using var exact = ColorConverter.ToBgra(source);
        using var fast = AnalysisConverter.ToBgra(source);

        Assert.Equal((17, 9, PixelFormat.Bgra32), (fast.Width, fast.Height, fast.Format));
        Assert.Equal((source.Pts, source.Duration, source.Generation, source.PixelAspect, source.Color), (fast.Pts, fast.Duration, fast.Generation, fast.PixelAspect, fast.Color));
        for (var y = 0; y < 9; y++)
        {
            var want = exact.Row(0, y);
            var got = fast.Row(0, y);
            for (var i = 0; i < 17 * 4; i++)
            {
                Assert.True(Math.Abs(want[i] - got[i]) <= 1, $"{format} row {y} byte {i}: {got[i]} where {want[i]} is exact.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void HalfSizeTakesEveryOtherSampleOfTheFullSize(PixelFormat format, ColorMatrix matrix, bool fullRange)
    {
        using var source = Random(format, 17, 9, new ColorInfo(matrix, ColorTransfer.Bt709, ColorPrimaries.Bt709, fullRange));
        using var full = AnalysisConverter.ToBgra(source);
        using var half = AnalysisConverter.ToBgra(source, 2);

        Assert.Equal((9, 5), (half.Width, half.Height));
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.True(full.Row(0, y * 2).Slice(x * 8, 4).SequenceEqual(half.Row(0, y).Slice(x * 4, 4)), $"{format} pixel {x},{y}");
            }
        }
    }

    [Fact]
    public void BgraIsCopiedWholeOrEveryOtherPixel()
    {
        using var source = Random(PixelFormat.Bgra32, 5, 3, ColorInfo.Unspecified);
        using var whole = AnalysisConverter.ToBgra(source);
        using var half = AnalysisConverter.ToBgra(source, 2);

        Assert.True(source.Row(0, 2).SequenceEqual(whole.Row(0, 2)));
        Assert.Equal((3, 2), (half.Width, half.Height));
        Assert.True(source.Row(0, 2).Slice(16, 4).SequenceEqual(half.Row(0, 1).Slice(8, 4)));
    }

    [Fact]
    public void OnlyWholeAndHalfSizeAreOffered()
    {
        using var source = Random(PixelFormat.Nv12, 4, 4, ColorInfo.Unspecified);

        Assert.Throws<ArgumentNullException>(() => AnalysisConverter.ToBgra(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisConverter.ToBgra(source, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisConverter.ToBgra(source, 3));
    }

    [Fact]
    public void ATransformGivesEachRowsCoefficients()
    {
        var transform = YuvTransform.For(new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, true), 8);
        var (y, cb, cr, offset) = transform.Coefficients(2);

        Assert.Equal(transform.Apply(10, 20, 30).B, (y * 10) + (cb * 20) + (cr * 30) + offset, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.Coefficients(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.Coefficients(3));
    }
}
