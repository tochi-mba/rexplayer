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
        var (inStride, halfStride, outStride) = (frame.Stride(0) / 4, half.Stride(0) / 4, output.Stride(0) / 4);
        var bottom = Math.Min(frame.Height, 2 * area.Bottom);
        var areaHeight = area.Bottom - area.Top;
        for (var y = 2 * area.Top; y < bottom; y++)
        {
            // A full-size row lies a quarter of a half-size row from the nearer of its two neighbours.
            var near = (y >> 1) - area.Top;
            var far = Math.Clamp((y & 1) == 0 ? near - 1 : near + 1, 0, areaHeight - 1);
            var rows = new Rows(alpha.Slice(near * areaWidth, areaWidth), alpha.Slice(far * areaWidth, areaWidth),
                fill.Slice(near * areaWidth, areaWidth), fill.Slice(far * areaWidth, areaWidth),
                guide.Slice((near + area.Top) * halfStride + area.Left, areaWidth), guide.Slice((far + area.Top) * halfStride + area.Left, areaWidth),
                estimate is null ? default : estimated.Slice(near * areaWidth, areaWidth));
            var picture = source.Slice(y * inStride + 2 * area.Left, Math.Min(frame.Width, 2 * area.Right) - 2 * area.Left);
            var drawn = target.Slice(y * outStride + 2 * area.Left, picture.Length);
            var better = estimate is null ? default : MemoryMarshal.Cast<byte, uint>(estimate.Row(0, y)).Slice(2 * area.Left, picture.Length);
            // Only the stretch of the two rows where either hides anything, a pixel either side.
            var (first, last) = Stretch(rows.NearAlpha, rows.FarAlpha);
            for (var i = Math.Max(0, first - 1); i <= Math.Min(areaWidth - 1, last + 1); i++)
            {
                // Full-size pixel 2i lies a quarter of a pixel from half-size pixel i towards i - 1,
                // and pixel 2i + 1 towards i + 1.
                Draw(rows, i, Math.Max(0, i - 1), picture, drawn, better, 2 * i);
                if (2 * i + 1 < picture.Length)
                {
                    Draw(rows, i, Math.Min(areaWidth - 1, i + 1), picture, drawn, better, 2 * i + 1);
                }
            }
        }
    }

    /// <summary>The first and last places where either row is not zero; an empty stretch when neither has any.</summary>
    private static (int First, int Last) Stretch(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var (fa, fb) = (a.IndexOfAnyExcept((byte)0), b.IndexOfAnyExcept((byte)0));
        var first = fa < 0 ? fb : fb < 0 ? fa : Math.Min(fa, fb);
        return first < 0 ? (0, -2) : (first, Math.Max(a.LastIndexOfAnyExcept((byte)0), b.LastIndexOfAnyExcept((byte)0)));
    }

    /// <summary>The two half-size rows a full-size row is drawn from: the nearer and the farther.</summary>
    private readonly ref struct Rows(ReadOnlySpan<byte> nearAlpha, ReadOnlySpan<byte> farAlpha, ReadOnlySpan<uint> nearFill,
        ReadOnlySpan<uint> farFill, ReadOnlySpan<uint> nearGuide, ReadOnlySpan<uint> farGuide, ReadOnlySpan<bool> estimated)
    {
        public readonly ReadOnlySpan<byte> NearAlpha = nearAlpha;
        public readonly ReadOnlySpan<byte> FarAlpha = farAlpha;
        public readonly ReadOnlySpan<uint> NearFill = nearFill;
        public readonly ReadOnlySpan<uint> FarFill = farFill;
        public readonly ReadOnlySpan<uint> NearGuide = nearGuide;
        public readonly ReadOnlySpan<uint> FarGuide = farGuide;
        public readonly ReadOnlySpan<bool> Estimated = estimated;
    }

    /// <summary>
    /// Draws the full-size pixel <paramref name="x"/> of a row from half-size pixels <paramref name="near"/>
    /// and <paramref name="far"/> of the nearer and farther rows, weighted nine, three, three and one.
    /// </summary>
    private static void Draw(in Rows rows, int near, int far, ReadOnlySpan<uint> picture, Span<uint> drawn, ReadOnlySpan<uint> better, int x)
    {
        var (a0, a1, a2, a3) = (rows.NearAlpha[near], rows.NearAlpha[far], rows.FarAlpha[near], rows.FarAlpha[far]);
        if ((a0 | a1 | a2 | a3) == 0)
        {
            return;
        }

        var original = picture[x];
        uint colour;
        int opacity;
        if ((a0 & a1 & a2 & a3) == 255)
        {
            // Inside the hole: the enlarged fill.
            colour = Blend(rows.NearFill[near], rows.NearFill[far], rows.FarFill[near], rows.FarFill[far]);
            opacity = 256;
        }
        else
        {
            // At the boundary each neighbour's bilinear weight is scaled by how alike its colour is
            // to this pixel's, and opacity and fill are blended premultiplied.
            var w0 = 9 * RangeWeights[Difference(original, rows.NearGuide[near])];
            var w1 = 3 * RangeWeights[Difference(original, rows.NearGuide[far])];
            var w2 = 3 * RangeWeights[Difference(original, rows.FarGuide[near])];
            var w3 = RangeWeights[Difference(original, rows.FarGuide[far])];
            var (s0, s1, s2, s3) = ((long)w0 * a0, (long)w1 * a1, (long)w2 * a2, (long)w3 * a3);
            // At least one of the four is opaque in part and every weight is positive, so covered
            // is too; an opacity that rounds to nothing leaves the pixel as it was.
            var covered = s0 + s1 + s2 + s3;
            var total = w0 + w1 + w2 + w3;
            opacity = (int)((covered * 256 / 255 + total / 2) / total);
            var (f0, f1, f2, f3) = (rows.NearFill[near], rows.NearFill[far], rows.FarFill[near], rows.FarFill[far]);
            colour = Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 0) | (Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 8) << 8)
                | (Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 16) << 16);
        }

        if (!better.IsEmpty && rows.Estimated[near])
        {
            colour = better[x] & 0xFFFFFF;
        }

        drawn[x] = Over(original, colour, opacity);
    }

    /// <summary>One channel of four packed colours blended by the given weights, rounded.</summary>
    private static uint Channel(uint a, uint b, uint c, uint d, long wa, long wb, long wc, long wd, long total, int shift) =>
        (uint)((((a >> shift) & 0xFF) * wa + ((b >> shift) & 0xFF) * wb + ((c >> shift) & 0xFF) * wc + ((d >> shift) & 0xFF) * wd + total / 2) / total);

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
