using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// A large subject's removal runs on a half-size copy of the picture and is drawn back at full size.
/// <para>
/// The copy is a two-by-two box average. Drawing back blends each full-size pixel from the four
/// nearest half-size ones with the bilinear weights of a two-times enlargement (a quarter and three
/// quarters along each axis). Where those four disagree on how much of the subject to hide, the
/// boundary, the opacity is upsampled jointly with the full-size picture as the guide (Kopf, Cohen,
/// Lischinski and Uyttendaele, "Joint Bilateral Upsampling", SIGGRAPH 2007): a half-size pixel
/// counts for more the closer its colour is to the full-size pixel's, so the soft edge follows the
/// picture's real edge rather than the half-size grid. Inside the hole every pixel takes the
/// enlarged fill, and outside it the picture is left exactly as it was.
/// </para>
/// </summary>
public static class SubjectRemovalScaling
{
    // The range weights of the joint upsampling, in 256ths, by the summed difference of the three
    // channels: a Gaussian with a spread of about twelve levels a channel.
    private static readonly ushort[] RangeWeights = BuildRangeWeights();

    private static ushort[] BuildRangeWeights()
    {
        var weights = new ushort[3 * 255 + 1];
        for (var d = 0; d < weights.Length; d++)
        {
            var spread = d / (3 * 12f);
            weights[d] = (ushort)Math.Max(1, (int)(256 * MathF.Exp(-0.5f * spread * spread) + 0.5f));
        }

        return weights;
    }

    /// <summary>
    /// Fills <paramref name="half"/> (BGRA, half the size of <paramref name="frame"/> rounded down)
    /// with the mean of each two-by-two block of <paramref name="frame"/>, rounded half up.
    /// </summary>
    public static void Halve(VideoFrame frame, VideoFrame half)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(half);
        if (half.Width != frame.Width / 2 || half.Height != frame.Height / 2)
        {
            throw new ArgumentException("The half-size picture must be half the picture's size.", nameof(half));
        }

        var source = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var target = MemoryMarshal.Cast<byte, uint>(half.Plane(0));
        var (inStride, outStride) = (frame.Stride(0) / 4, half.Stride(0) / 4);
        var width = half.Width;
        for (var y = 0; y < half.Height; y++)
        {
            var top = source.Slice(2 * y * inStride, 2 * width);
            var bottom = source.Slice((2 * y + 1) * inStride, 2 * width);
            var row = target.Slice(y * outStride, width);
            var x = 0;
            // Eight half-size pixels at a time: blue and red, then green and alpha, each in 16-bit lanes.
            for (; Vector256.IsHardwareAccelerated && x + 8 <= width; x += 8)
            {
                var a = Vector256.Create(top.Slice(2 * x, 8));
                var b = Vector256.Create(top.Slice(2 * x + 8, 8));
                var c = Vector256.Create(bottom.Slice(2 * x, 8));
                var d = Vector256.Create(bottom.Slice(2 * x + 8, 8));
                var evens = Vector256.Create(0u, 2, 4, 6, 0, 2, 4, 6);
                var odds = Vector256.Create(1u, 3, 5, 7, 1, 3, 5, 7);
                // Left and right pixels of each block, the first four blocks from a and c, the rest from b and d.
                var left = Join(Vector256.Shuffle(a, evens), Vector256.Shuffle(b, evens));
                var right = Join(Vector256.Shuffle(a, odds), Vector256.Shuffle(b, odds));
                var lowerLeft = Join(Vector256.Shuffle(c, evens), Vector256.Shuffle(d, evens));
                var lowerRight = Join(Vector256.Shuffle(c, odds), Vector256.Shuffle(d, odds));
                var mask = Vector256.Create(0x00FF00FFu);
                var blueRed = (left & mask) + (right & mask) + (lowerLeft & mask) + (lowerRight & mask) + Vector256.Create(0x00020002u);
                var greenAlpha = ((left >> 8) & mask) + ((right >> 8) & mask) + ((lowerLeft >> 8) & mask) + ((lowerRight >> 8) & mask)
                    + Vector256.Create(0x00020002u);
                var result = ((blueRed >> 2) & mask) | (((greenAlpha >> 2) & mask) << 8);
                result.CopyTo(row.Slice(x, 8));
            }

            for (; x < width; x++)
            {
                row[x] = Average(top[2 * x], top[2 * x + 1], bottom[2 * x], bottom[2 * x + 1]);
            }
        }
    }

    /// <summary>
    /// Draws a removal worked out on the half-size picture <paramref name="half"/> into the full-size
    /// <paramref name="output"/>, which already holds <paramref name="frame"/>. <paramref name="area"/>
    /// is the half-size area the removal covers; <paramref name="alpha"/> and <paramref name="fill"/>
    /// hold its opacity and fill colour (packed BGR) row by row. Opacity and fill are blended
    /// premultiplied, so a half-size pixel left untouched lends no colour to its neighbours' fill.
    /// With <paramref name="estimate"/> (full size), every pixel whose nearest half-size pixel is
    /// marked in <paramref name="estimated"/> takes the estimate's own full-size colour instead.
    /// </summary>
    public static void Enlarge(VideoFrame frame, VideoFrame half, VideoFrame output, (int Left, int Top, int Right, int Bottom) area,
        ReadOnlySpan<byte> alpha, ReadOnlySpan<uint> fill, VideoFrame? estimate = null, ReadOnlySpan<bool> estimated = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(half);
        ArgumentNullException.ThrowIfNull(output);
        var areaWidth = area.Right - area.Left;
        if (areaWidth <= 0 || area.Bottom <= area.Top)
        {
            return;
        }

        var source = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var guide = MemoryMarshal.Cast<byte, uint>(half.Plane(0));
        var target = MemoryMarshal.Cast<byte, uint>(output.Plane(0));
        var better = estimate is null ? default : MemoryMarshal.Cast<byte, uint>(estimate.Plane(0));
        var (inStride, halfStride, outStride) = (frame.Stride(0) / 4, half.Stride(0) / 4, output.Stride(0) / 4);
        var estimateStride = estimate is null ? 0 : estimate.Stride(0) / 4;
        var (right, bottom) = (Math.Min(frame.Width, 2 * area.Right), Math.Min(frame.Height, 2 * area.Bottom));
        Span<int> rows = stackalloc int[2];
        Span<int> spatial = stackalloc int[4];
        Span<int> at = stackalloc int[4];
        for (var y = 2 * area.Top; y < bottom; y++)
        {
            // A full-size row lies a quarter of a half-size row from the nearer of its two neighbours.
            var near = y >> 1;
            var far = (y & 1) == 0 ? near - 1 : near + 1;
            rows[0] = (Math.Clamp(near, area.Top, area.Bottom - 1) - area.Top) * areaWidth;
            rows[1] = (Math.Clamp(far, area.Top, area.Bottom - 1) - area.Top) * areaWidth;
            var guideRows = (Math.Clamp(near, 0, half.Height - 1) * halfStride, Math.Clamp(far, 0, half.Height - 1) * halfStride);
            for (var x = 2 * area.Left; x < right; x++)
            {
                var nearX = x >> 1;
                var farX = (x & 1) == 0 ? nearX - 1 : nearX + 1;
                var column0 = Math.Clamp(nearX, area.Left, area.Right - 1) - area.Left;
                var column1 = Math.Clamp(farX, area.Left, area.Right - 1) - area.Left;
                (at[0], at[1], at[2], at[3]) = (rows[0] + column0, rows[0] + column1, rows[1] + column0, rows[1] + column1);
                var (a0, a1, a2, a3) = (alpha[at[0]], alpha[at[1]], alpha[at[2]], alpha[at[3]]);
                if ((a0 | a1 | a2 | a3) == 0)
                {
                    continue;
                }

                var original = source[y * inStride + x];
                uint colour;
                int opacity;
                if ((a0 & a1 & a2 & a3) == 255)
                {
                    // Inside the hole: the enlarged fill, nine, three, three and one sixteenths.
                    colour = Blend(fill[at[0]], fill[at[1]], fill[at[2]], fill[at[3]]);
                    opacity = 256;
                }
                else
                {
                    var guideX = (Math.Clamp(nearX, 0, half.Width - 1), Math.Clamp(farX, 0, half.Width - 1));
                    // Each neighbour's bilinear weight times how alike its colour is to this pixel's.
                    (spatial[0], spatial[1], spatial[2], spatial[3]) = (
                        9 * RangeWeights[Difference(original, guide[guideRows.Item1 + guideX.Item1])],
                        3 * RangeWeights[Difference(original, guide[guideRows.Item1 + guideX.Item2])],
                        3 * RangeWeights[Difference(original, guide[guideRows.Item2 + guideX.Item1])],
                        RangeWeights[Difference(original, guide[guideRows.Item2 + guideX.Item2])]);
                    long total = 0, covered = 0, blue = 0, green = 0, red = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var weight = spatial[k];
                        var share = (long)weight * alpha[at[k]];
                        total += weight;
                        covered += share;
                        var value = fill[at[k]];
                        (blue, green, red) = (blue + share * (value & 0xFF), green + share * ((value >> 8) & 0xFF), red + share * ((value >> 16) & 0xFF));
                    }

                    // At least one of the four is opaque in part and every weight is positive, so
                    // covered is too; an opacity that rounds to nothing leaves the pixel as it was.
                    opacity = (int)((covered * 256 / 255 + total / 2) / total);
                    colour = (uint)((blue + covered / 2) / covered) | ((uint)((green + covered / 2) / covered) << 8)
                        | ((uint)((red + covered / 2) / covered) << 16);
                }

                if (estimate is not null && estimated[at[0]])
                {
                    colour = better[y * estimateStride + x] & 0xFFFFFF;
                }

                target[y * outStride + x] = Over(original, colour, opacity);
            }
        }
    }

    /// <summary>The summed difference of two packed pixels' blue, green and red.</summary>
    private static int Difference(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF))
            + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));

    /// <summary>Four packed colours blended nine, three, three and one sixteenths, alpha dropped.</summary>
    private static uint Blend(uint a, uint b, uint c, uint d)
    {
        const uint Mask = 0x00FF00FF;
        var blueRed = ((a & Mask) * 9 + (b & Mask) * 3 + (c & Mask) * 3 + (d & Mask) + 0x00080008) >> 4;
        var green = (((a >> 8) & 0xFF) * 9 + ((b >> 8) & 0xFF) * 3 + ((c >> 8) & 0xFF) * 3 + ((d >> 8) & 0xFF) + 8) >> 4;
        return (blueRed & Mask) | (green << 8);
    }

    /// <summary>A colour laid over a picture's pixel with an opacity in 256ths; the pixel's alpha is kept.</summary>
    internal static uint Over(uint original, uint colour, int opacity)
    {
        var keep = (uint)(256 - opacity);
        var over = (uint)opacity;
        var blueRed = (((original & 0xFF00FF) * keep + (colour & 0xFF00FF) * over + 0x800080) >> 8) & 0xFF00FF;
        var green = (((original & 0xFF00) * keep + (colour & 0xFF00) * over + 0x8000) >> 8) & 0xFF00;
        return (original & 0xFF000000) | blueRed | green;
    }

    /// <summary>The low halves of two vectors, one after the other.</summary>
    private static Vector256<uint> Join(Vector256<uint> low, Vector256<uint> high) =>
        Vector256.Create(low.GetLower(), high.GetLower());

    /// <summary>The rounded mean of four packed BGRA pixels, alpha included.</summary>
    internal static uint Average(uint a, uint b, uint c, uint d)
    {
        const uint Mask = 0x00FF00FF;
        var blueRed = (a & Mask) + (b & Mask) + (c & Mask) + (d & Mask) + 0x00020002;
        var greenAlpha = ((a >> 8) & Mask) + ((b >> 8) & Mask) + ((c >> 8) & Mask) + ((d >> 8) & Mask) + 0x00020002;
        return ((blueRed >> 2) & Mask) | (((greenAlpha >> 2) & Mask) << 8);
    }
}
