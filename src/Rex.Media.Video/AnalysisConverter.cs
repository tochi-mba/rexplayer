using System.Buffers;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Converts decoded pictures to BGRA for live analysis (Subject Lock follows its subject in every
/// picture), many times faster than <see cref="ColorConverter"/>: the same <see cref="YuvTransform"/>
/// in 16-bit fixed point through lookup tables, so a channel may differ from the exact conversion by
/// one level, which analysis cannot tell but a snapshot must not have. At a step of 2 it gives a picture half as wide and half as high, from every other
/// sample, a quarter of the work again, for computers that cannot spare the time.
/// </summary>
public static class AnalysisConverter
{
    private const int Shift = 16;

    /// <summary>A BGRA copy of <paramref name="source"/> taking every <paramref name="step"/>th sample across and down (1 or 2), with its timing and aspect.</summary>
    public static VideoFrame ToBgra(VideoFrame source, int step = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(step, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(step, 2);
        if (source.Format == PixelFormat.Bgra32 && step == 1)
        {
            return ColorConverter.ToBgra(source);
        }

        var (width, height) = ((source.Width + step - 1) / step, (source.Height + step - 1) / step);
        var output = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        output.Pts = source.Pts;
        output.Duration = source.Duration;
        output.Generation = source.Generation;
        output.PixelAspect = source.PixelAspect;
        output.Color = source.Color;
        if (source.Format == PixelFormat.Bgra32)
        {
            for (var y = 0; y < height; y++)
            {
                var from = source.Row(0, y * step);
                var to = output.Row(0, y);
                for (var x = 0; x < width; x++)
                {
                    from.Slice(x * step * 4, 4).CopyTo(to.Slice(x * 4, 4));
                }
            }

            return output;
        }

        var bits = source.Format is PixelFormat.P010 or PixelFormat.I420P10 ? 10 : 8;
        var values = 1 << bits;
        var transform = YuvTransform.For(source.Color.Resolve(source.Width, source.Height), bits, source.Format != PixelFormat.Gray8);
        var tables = ArrayPool<int>.Shared.Rent(values * 9);
        var samples = ArrayPool<int>.Shared.Rent(width * 3);
        try
        {
            // tables[channel][input][value]: the value times its coefficient, in levels of 1/65536 of
            // an 8-bit level; the constant (and a half, for rounding) goes on the luma table.
            for (var channel = 0; channel < 3; channel++)
            {
                var (fromY, fromCb, fromCr, offset) = transform.Coefficients(channel);
                for (var v = 0; v < values; v++)
                {
                    tables[(channel * 3 * values) + v] = Fixed((fromY * v) + offset) + (1 << (Shift - 1));
                    tables[(((channel * 3) + 1) * values) + v] = Fixed(fromCb * v);
                    tables[(((channel * 3) + 2) * values) + v] = Fixed(fromCr * v);
                }
            }

            var lumas = samples.AsSpan(0, width);
            var cbs = samples.AsSpan(width, width);
            var crs = samples.AsSpan(width * 2, width);
            var r = tables.AsSpan(0, values * 3);
            var g = tables.AsSpan(values * 3, values * 3);
            var b = tables.AsSpan(values * 6, values * 3);
            for (var y = 0; y < height; y++)
            {
                ReadRow(source, y * step, step, values, lumas, cbs, crs);
                var row = output.Row(0, y);
                for (var x = 0; x < width; x++)
                {
                    var (luma, cb, cr) = (lumas[x], cbs[x], crs[x]);
                    var at = x * 4;
                    row[at] = Level(b[luma] + b[cb] + b[cr]);
                    row[at + 1] = Level(g[luma] + g[cb] + g[cr]);
                    row[at + 2] = Level(r[luma] + r[cb] + r[cr]);
                    row[at + 3] = 255;
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(tables);
            ArrayPool<int>.Shared.Return(samples);
        }

        return output;
    }

    /// <summary>A share of full scale (0 to 1) in fixed-point 8-bit levels, held well inside an int.</summary>
    private static int Fixed(double share) => (int)Math.Round(Math.Clamp(share, -4, 4) * 255 * (1 << Shift));

    private static byte Level(int value) => (byte)Math.Clamp(value >> Shift, 0, 255);

    /// <summary>
    /// The samples of one picture row, every <paramref name="step"/>th: luma, then each pixel's Cb and
    /// Cr already moved to their tables (by <paramref name="values"/> and twice that); no chroma is zero.
    /// </summary>
    private static void ReadRow(VideoFrame frame, int row, int step, int values, Span<int> lumas, Span<int> cbs, Span<int> crs)
    {
        var luma = frame.Row(0, row);
        var wide = frame.Format is PixelFormat.P010 or PixelFormat.I420P10;
        var down = frame.Format is PixelFormat.P010 ? 6 : 0;
        for (var x = 0; x < lumas.Length; x++)
        {
            var column = x * step;
            lumas[x] = wide ? (luma[column * 2] | (luma[(column * 2) + 1] << 8)) >> down : luma[column];
        }

        if (frame.Format == PixelFormat.Gray8)
        {
            cbs.Fill(values);
            crs.Fill(values * 2);
            return;
        }

        var shiftX = frame.Format is PixelFormat.I444 ? 0 : 1;
        var chromaRow = frame.Format is PixelFormat.I444 or PixelFormat.I422 ? row : row >> 1;
        var semiPlanar = frame.Format is PixelFormat.Nv12 or PixelFormat.P010;
        var cbPlane = frame.Row(1, chromaRow);
        var crPlane = semiPlanar ? cbPlane : frame.Row(2, chromaRow);
        for (var x = 0; x < lumas.Length; x++)
        {
            var column = (x * step) >> shiftX;
            var (cbAt, crAt) = semiPlanar ? (column * 2, (column * 2) + 1) : (column, column);
            var (cb, cr) = wide
                ? ((cbPlane[cbAt * 2] | (cbPlane[(cbAt * 2) + 1] << 8)) >> down, (crPlane[crAt * 2] | (crPlane[(crAt * 2) + 1] << 8)) >> down)
                : (cbPlane[cbAt], crPlane[crAt]);
            cbs[x] = values + cb;
            crs[x] = (values * 2) + cr;
        }
    }
}
