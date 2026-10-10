using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>
/// Pictures encoded here from RGB with ITU-T H.273's equations, independently of the converter,
/// then converted back: every format, matrix and range must return the colours within rounding.
/// </summary>
public sealed class ColorConverterTests
{
    private static readonly (byte R, byte G, byte B)[] Colours =
    [
        (0, 0, 0), (255, 255, 255), (255, 0, 0), (0, 255, 0), (0, 0, 255), (215, 255, 63), (8, 10, 9), (128, 64, 200),
    ];

    /// <summary>Encodes one colour as Y, Cb, Cr at a bit depth (forward H.273 equations).</summary>
    private static (int Y, int Cb, int Cr) Encode((byte R, byte G, byte B) colour, ColorMatrix matrix, bool fullRange, int bits)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        var scale = 1 << (bits - 8);
        double max = (1 << bits) - 1;
        int Luma(double value) => (int)Math.Round(fullRange ? value * max : (16 * scale) + (219 * scale * value));
        int Chroma(double value) => (int)Math.Clamp(Math.Round((1 << (bits - 1)) + ((fullRange ? max : 224 * scale) * value)), 0, max);
        if (matrix == ColorMatrix.Rgb)
        {
            return (Luma(g), Luma(b), Luma(r));
        }

        var (kr, kb) = matrix switch
        {
            ColorMatrix.Bt709 => (0.2126, 0.0722),
            ColorMatrix.Bt2020NonConstant => (0.2627, 0.0593),
            _ => (0.299, 0.114),
        };
        var y = (kr * r) + ((1 - kr - kb) * g) + (kb * b);
        return (Luma(y), Chroma((b - y) / (2 * (1 - kb))), Chroma((r - y) / (2 * (1 - kr))));
    }

    /// <summary>A picture of 2x2 blocks, one per colour, so subsampled chroma holds each colour exactly.</summary>
    private static VideoFrame Picture(PixelFormat format, ColorMatrix matrix, bool fullRange)
    {
        var bits = format is PixelFormat.P010 or PixelFormat.I420P10 ? 10 : 8;
        var frame = VideoFrame.Rent(format, Colours.Length * 2, 2);
        frame.Color = new ColorInfo(matrix, ColorTransfer.Unspecified, ColorPrimaries.Unspecified, fullRange);
        void Put(int plane, int column, int row, int value)
        {
            var bytes = frame.Row(plane, row);
            switch (format)
            {
                case PixelFormat.P010:
                    value <<= 6;
                    goto case PixelFormat.I420P10;
                case PixelFormat.I420P10:
                    bytes[column * 2] = (byte)value;
                    bytes[(column * 2) + 1] = (byte)(value >> 8);
                    break;
                default:
                    bytes[column] = (byte)value;
                    break;
            }
        }

        for (var i = 0; i < Colours.Length; i++)
        {
            var (y, cb, cr) = Encode(Colours[i], matrix, fullRange, bits);
            for (var dy = 0; dy < 2; dy++)
            {
                for (var dx = 0; dx < 2; dx++)
                {
                    var x = (i * 2) + dx;
                    Put(0, x, dy, y);
                    switch (format)
                    {
                        case PixelFormat.Nv12 or PixelFormat.P010:
                            Put(1, i * 2, 0, cb);
                            Put(1, (i * 2) + 1, 0, cr);
                            break;
                        case PixelFormat.I420 or PixelFormat.I420P10:
                            Put(1, i, 0, cb);
                            Put(2, i, 0, cr);
                            break;
                        case PixelFormat.I422:
                            Put(1, i, dy, cb);
                            Put(2, i, dy, cr);
                            break;
                        case PixelFormat.I444:
                            Put(1, x, dy, cb);
                            Put(2, x, dy, cr);
                            break;
                    }
                }
            }
        }

        return frame;
    }

    [Theory]
    [InlineData(PixelFormat.Nv12, ColorMatrix.Bt709, false)]
    [InlineData(PixelFormat.Nv12, ColorMatrix.Bt601, true)]
    [InlineData(PixelFormat.P010, ColorMatrix.Bt2020NonConstant, false)]
    [InlineData(PixelFormat.I420, ColorMatrix.Bt601, false)]
    [InlineData(PixelFormat.I420P10, ColorMatrix.Bt709, true)]
    [InlineData(PixelFormat.I422, ColorMatrix.Bt709, false)]
    [InlineData(PixelFormat.I444, ColorMatrix.Rgb, true)]
    [InlineData(PixelFormat.I444, ColorMatrix.Rgb, false)]
    [Capability("VID-23")]
    public void EveryFormatMatrixAndRangeReturnsTheColoursItWasMadeFrom(PixelFormat format, ColorMatrix matrix, bool fullRange)
    {
        using var source = Picture(format, matrix, fullRange);
        source.Pts = MediaTime.FromSeconds(1);
        source.Generation = 17;
        source.PixelAspect = new Rational(4, 3);

        using var bgra = ColorConverter.ToBgra(source);

        Assert.Equal((PixelFormat.Bgra32, source.Width, source.Height), (bgra.Format, bgra.Width, bgra.Height));
        Assert.Equal((source.Pts, source.PixelAspect), (bgra.Pts, bgra.PixelAspect));
        Assert.Equal(source.Generation, bgra.Generation);
        for (var i = 0; i < Colours.Length; i++)
        {
            for (var dy = 0; dy < 2; dy++)
            {
                var pixel = bgra.Row(0, dy).Slice(i * 8, 4).ToArray();
                Assert.InRange(Math.Abs(pixel[2] - Colours[i].R), 0, 1);
                Assert.InRange(Math.Abs(pixel[1] - Colours[i].G), 0, 1);
                Assert.InRange(Math.Abs(pixel[0] - Colours[i].B), 0, 1);
                Assert.Equal(255, pixel[3]);
            }
        }
    }

    [Fact]
    public void AnUnspecifiedMatrixIsResolvedForThePictureSize()
    {
        using var standard = Picture(PixelFormat.I420, ColorMatrix.Bt601, false);
        standard.Color = ColorInfo.Unspecified;

        using var bgra = ColorConverter.ToBgra(standard);

        var red = bgra.Row(0, 0).Slice(2 * 8, 4).ToArray();
        Assert.True(red[2] >= 253 && red[1] <= 1 && red[0] <= 1, $"BT.601 red came back as {red[2]}, {red[1]}, {red[0]}");
        Assert.Null(ColorConverter.Weights(ColorMatrix.Rgb));
        Assert.Equal((0.299, 0.114), ColorConverter.Weights(ColorMatrix.Unspecified));
    }

    [Fact]
    public void GreyHasNoChromaAndBgraIsCopied()
    {
        using var grey = VideoFrame.Rent(PixelFormat.Gray8, 2, 1);
        grey.Row(0, 0)[0] = 16;
        grey.Row(0, 0)[1] = 235;
        using var bgra = VideoFrame.Rent(PixelFormat.Bgra32, 1, 1);
        bgra.Row(0, 0)[0] = 9;
        bgra.Generation = 23;

        using var fromGrey = ColorConverter.ToBgra(grey);
        using var copy = ColorConverter.ToBgra(bgra);

        Assert.Equal([0, 0, 0, 255, 255, 255, 255, 255], fromGrey.Row(0, 0).ToArray());
        Assert.Equal(bgra.Row(0, 0).ToArray(), copy.Row(0, 0).ToArray());
        Assert.Equal(bgra.Generation, copy.Generation);
    }
}
