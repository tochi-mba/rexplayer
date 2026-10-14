using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// A large subject's removal runs on a reduced copy of the picture, two, three or four times
/// smaller, and is drawn back at full size.
/// <para>
/// The copy averages each block: all four pixels of a two-by-two block, and for larger blocks four
/// pixels spread evenly over it. Drawing back blends each full-size pixel from the four nearest
/// reduced ones with bilinear weights. Where those four disagree on how much of the subject to
/// hide, the boundary, the opacity is upsampled jointly with the full-size picture as the guide
/// (Kopf, Cohen, Lischinski and Uyttendaele, "Joint Bilateral Upsampling", SIGGRAPH 2007): a
/// reduced pixel counts for more the closer its colour is to the full-size pixel's, so the soft
/// edge follows the picture's real edge rather than the reduced grid. Inside the hole every pixel
/// takes the enlarged fill, and outside it the picture is left exactly as it was.
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
    /// Fills <paramref name="small"/> (BGRA, <paramref name="scale"/> times smaller than
    /// <paramref name="frame"/>, rounded down) with the rounded mean of four pixels of each block:
    /// the whole block when it is two by two, else four spread evenly over it.
    /// </summary>
    public static void Reduce(VideoFrame frame, VideoFrame small, int scale)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(small);
        if (scale < 2 || small.Width != frame.Width / scale || small.Height != frame.Height / scale)
        {
            throw new ArgumentException("The reduced picture must be the picture's size divided by a scale of two or more.", nameof(small));
        }

        var source = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var target = MemoryMarshal.Cast<byte, uint>(small.Plane(0));
        var (inStride, outStride) = (frame.Stride(0) / 4, small.Stride(0) / 4);
        var width = small.Width;
        // The two sample offsets inside a block, along each axis: 0 and 1 for two, else a quarter in from either side.
        var (near, far) = (scale / 4, scale - 1 - scale / 4);
        for (var y = 0; y < small.Height; y++)
        {
            var top = source.Slice((scale * y + near) * inStride, frame.Width);
            var bottom = source.Slice((scale * y + far) * inStride, frame.Width);
            var row = target.Slice(y * outStride, width);
            var x = 0;
            // Two by two, eight reduced pixels at a time: blue and red, then green and alpha, each in 16-bit lanes.
            for (; scale == 2 && Vector256.IsHardwareAccelerated && x + 8 <= width; x += 8)
            {
                var a = Vector256.Create(top.Slice(2 * x, 8));
                var b = Vector256.Create(top.Slice(2 * x + 8, 8));
                var c = Vector256.Create(bottom.Slice(2 * x, 8));
                var d = Vector256.Create(bottom.Slice(2 * x + 8, 8));
                var evens = Vector256.Create(0u, 2, 4, 6, 0, 2, 4, 6);
                var odds = Vector256.Create(1u, 3, 5, 7, 1, 3, 5, 7);
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
                var (left, right) = (scale * x + near, scale * x + far);
                row[x] = Average(top[left], top[right], bottom[left], bottom[right]);
            }
        }
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

    /// <summary>
    /// Draws a removal worked out on the reduced picture <paramref name="small"/>, <paramref name="scale"/>
    /// times smaller, into the full-size <paramref name="output"/>, which already holds
    /// <paramref name="frame"/>. <paramref name="area"/> is the reduced area the removal covers;
    /// <paramref name="alpha"/> and <paramref name="fill"/> hold its opacity and fill colour (packed
    /// BGR) row by row. Opacity and fill are blended premultiplied, so a reduced pixel left untouched
    /// lends no colour to its neighbours' fill. With <paramref name="estimate"/> (full size), every
    /// pixel whose nearest reduced pixel is marked in <paramref name="estimated"/> takes the
    /// estimate's own colour instead. <paramref name="scratch"/> is scratch space of at least
    /// <see cref="ScratchPerColumn"/> values for each full-size column of the picture.
    /// <para>
    /// Inside the hole the enlargement is separable: each reduced row the drawing needs is enlarged
    /// across once, into a full-width row of opacities and colours, and each full-size row then
    /// only blends the two enlarged rows above and below it. Only the boundary, where some of the
    /// four neighbours hide less than all, does the joint upsampling's work.
    /// </para>
    /// </summary>
    public static void Enlarge(VideoFrame frame, VideoFrame small, VideoFrame output, int scale,
        (int Left, int Top, int Right, int Bottom) area, ReadOnlySpan<byte> alpha, ReadOnlySpan<uint> fill, Span<int> scratch,
        VideoFrame? estimate = null, ReadOnlySpan<bool> estimated = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(small);
        ArgumentNullException.ThrowIfNull(output);
        var areaWidth = area.Right - area.Left;
        var areaHeight = area.Bottom - area.Top;
        if (areaWidth <= 0 || areaHeight <= 0)
        {
            return;
        }

        var source = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var guide = MemoryMarshal.Cast<byte, uint>(small.Plane(0));
        var target = MemoryMarshal.Cast<byte, uint>(output.Plane(0));
        var (inStride, smallStride, outStride) = (frame.Stride(0) / 4, small.Stride(0) / 4, output.Stride(0) / 4);
        var (left, right) = (scale * area.Left, Math.Min(frame.Width, scale * area.Right));
        var bottom = Math.Min(frame.Height, scale * area.Bottom);
        var count = right - left;
        var columns = scratch[..(3 * count)];
        // Two enlarged rows in two slots, with the reduced row each was made from: opacities in
        // sixteenths of 255 after the columns, then colours.
        Span<int> made = [-1, -1];
        // Each full-size column's two reduced neighbours (as places in the area's rows) and the
        // second one's weight in sixteenths, worked out once for every row.
        for (var x = left; x < right; x++)
        {
            var (first, weight) = Position(x, scale, area.Left, areaWidth);
            (columns[3 * (x - left)], columns[3 * (x - left) + 1], columns[3 * (x - left) + 2]) =
                (first, Math.Min(first + 1, areaWidth - 1), weight);
        }

        for (var y = scale * area.Top; y < bottom; y++)
        {
            var (upper, down) = Position(y, scale, area.Top, areaHeight);
            var lower = Math.Min(upper + 1, areaHeight - 1);
            var rows = new Rows(alpha.Slice(upper * areaWidth, areaWidth), alpha.Slice(lower * areaWidth, areaWidth),
                fill.Slice(upper * areaWidth, areaWidth), fill.Slice(lower * areaWidth, areaWidth),
                guide.Slice((upper + area.Top) * smallStride + area.Left, areaWidth), guide.Slice((lower + area.Top) * smallStride + area.Left, areaWidth),
                estimate is null ? default : estimated.Slice((down >= 8 ? lower : upper) * areaWidth, areaWidth), down);
            var picture = source.Slice(y * inStride + left, right - left);
            var drawn = target.Slice(y * outStride + left, right - left);
            var better = estimate is null ? default : MemoryMarshal.Cast<byte, uint>(estimate.Row(0, y)).Slice(left, right - left);
            // Only the stretch of the two rows where either hides anything, a reduced pixel either side.
            var (first, last) = Stretch(rows.UpperAlpha, rows.LowerAlpha);
            var from = Math.Max(0, (first - 1) * scale);
            var to = Math.Min(right - left, (last + 2) * scale);
            // The enlarged rows for the reduced rows above and below, kept while they are still needed:
            // a row not yet made goes into the slot the other one does not hold.
            foreach (var row in (ReadOnlySpan<int>)[upper, lower])
            {
                if (made[0] != row && made[1] != row)
                {
                    var slot = made[0] == upper || made[0] == lower ? 1 : 0;
                    Across(alpha.Slice(row * areaWidth, areaWidth), fill.Slice(row * areaWidth, areaWidth), columns, count,
                        scratch.Slice((3 + slot) * count, count), MemoryMarshal.Cast<int, uint>(scratch.Slice((5 + slot) * count, count)));
                    made[slot] = row;
                }
            }

            var (above, below) = (made[0] == upper ? 0 : 1, made[0] == lower ? 0 : 1);
            ReadOnlySpan<int> aboveOpacity = scratch.Slice((3 + above) * count, count);
            ReadOnlySpan<int> belowOpacity = scratch.Slice((3 + below) * count, count);
            ReadOnlySpan<uint> aboveColour = MemoryMarshal.Cast<int, uint>(scratch.Slice((5 + above) * count, count));
            ReadOnlySpan<uint> belowColour = MemoryMarshal.Cast<int, uint>(scratch.Slice((5 + below) * count, count));
            var (keep, take) = ((uint)(16 - down), (uint)down);
            for (var x = from; x < to; x++)
            {
                // Opacity in 256ths of 255: nothing hidden, all hidden, or the boundary between.
                var hidden = (16 - down) * aboveOpacity[x] + down * belowOpacity[x];
                if (hidden == 0)
                {
                    continue;
                }

                if (hidden == 255 * 256 && better.IsEmpty)
                {
                    // Inside the hole, the usual case: the enlarged fill, the picture's alpha kept.
                    var (a, b) = (aboveColour[x], belowColour[x]);
                    var blueRed = (((a & 0xFF00FF) * keep + (b & 0xFF00FF) * take + 0x80008) >> 4) & 0xFF00FF;
                    var green = (((a & 0xFF00) * keep + (b & 0xFF00) * take + 0x800) >> 4) & 0xFF00;
                    drawn[x] = (picture[x] & 0xFF000000) | blueRed | green;
                    continue;
                }

                Draw(rows, columns[3 * x], columns[3 * x + 1], columns[3 * x + 2], picture, drawn, better, x);
            }
        }
    }

    /// <summary>How many scratch values <see cref="Enlarge"/> needs for each full-size column.</summary>
    public const int ScratchPerColumn = 7;

    /// <summary>
    /// Enlarges one reduced row across its <paramref name="count"/> full-size columns: each opacity
    /// in sixteenths of the reduced ones, each colour blended by the same weights.
    /// </summary>
    private static void Across(ReadOnlySpan<byte> alpha, ReadOnlySpan<uint> fill, ReadOnlySpan<int> columns, int count,
        Span<int> opacity, Span<uint> colour)
    {
        for (var x = 0; x < count; x++)
        {
            var (one, two, across) = (columns[3 * x], columns[3 * x + 1], columns[3 * x + 2]);
            opacity[x] = (16 - across) * alpha[one] + across * alpha[two];
            var (a, b, keep, take) = (fill[one], fill[two], (uint)(16 - across), (uint)across);
            colour[x] = ((((a & 0xFF00FF) * keep + (b & 0xFF00FF) * take + 0x80008) >> 4) & 0xFF00FF)
                | ((((a & 0xFF00) * keep + (b & 0xFF00) * take + 0x800) >> 4) & 0xFF00);
        }
    }

    /// <summary>
    /// Where the full-size pixel <paramref name="at"/> falls between reduced pixels: the first of its
    /// two neighbours, counted from <paramref name="start"/> and kept within <paramref name="count"/>,
    /// and the second one's weight in sixteenths.
    /// </summary>
    private static (int First, int Weight) Position(int at, int scale, int start, int count)
    {
        var place = (at + 0.5f) / scale - 0.5f - start;
        var first = (int)MathF.Floor(place);
        var weight = (int)MathF.Round((place - first) * 16);
        return first < 0 ? (0, 0) : first >= count - 1 ? (count - 1, 0) : (first, weight);
    }

    /// <summary>The first and last places where either row is not zero; an empty stretch when neither has any.</summary>
    private static (int First, int Last) Stretch(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var (fa, fb) = (a.IndexOfAnyExcept((byte)0), b.IndexOfAnyExcept((byte)0));
        var first = fa < 0 ? fb : fb < 0 ? fa : Math.Min(fa, fb);
        return first < 0 ? (0, -2) : (first, Math.Max(a.LastIndexOfAnyExcept((byte)0), b.LastIndexOfAnyExcept((byte)0)));
    }

    /// <summary>The two reduced rows a full-size row is drawn from, and the lower one's weight in sixteenths.</summary>
    private readonly ref struct Rows(ReadOnlySpan<byte> upperAlpha, ReadOnlySpan<byte> lowerAlpha, ReadOnlySpan<uint> upperFill,
        ReadOnlySpan<uint> lowerFill, ReadOnlySpan<uint> upperGuide, ReadOnlySpan<uint> lowerGuide, ReadOnlySpan<bool> estimated, int down)
    {
        public readonly ReadOnlySpan<byte> UpperAlpha = upperAlpha;
        public readonly ReadOnlySpan<byte> LowerAlpha = lowerAlpha;
        public readonly ReadOnlySpan<uint> UpperFill = upperFill;
        public readonly ReadOnlySpan<uint> LowerFill = lowerFill;
        public readonly ReadOnlySpan<uint> UpperGuide = upperGuide;
        public readonly ReadOnlySpan<uint> LowerGuide = lowerGuide;
        public readonly ReadOnlySpan<bool> Estimated = estimated;
        public readonly int Down = down;
    }

    /// <summary>
    /// Draws the full-size pixel <paramref name="x"/> of a row from reduced pixels <paramref name="first"/>
    /// and <paramref name="second"/> of the two rows, <paramref name="across"/> sixteenths of the way to the second.
    /// </summary>
    private static void Draw(in Rows rows, int first, int second, int across, ReadOnlySpan<uint> picture, Span<uint> drawn,
        ReadOnlySpan<uint> better, int x)
    {
        // The caller draws only pixels where a neighbour with some weight hides something.
        var (a0, a1, a2, a3) = (rows.UpperAlpha[first], rows.UpperAlpha[second], rows.LowerAlpha[first], rows.LowerAlpha[second]);
        var down = rows.Down;
        // Bilinear weights in 256ths.
        var (w0, w1, w2, w3) = ((16 - across) * (16 - down), across * (16 - down), (16 - across) * down, across * down);
        var (f0, f1, f2, f3) = (rows.UpperFill[first], rows.UpperFill[second], rows.LowerFill[first], rows.LowerFill[second]);
        var original = picture[x];
        uint colour;
        int opacity;
        if ((a0 & a1 & a2 & a3) == 255)
        {
            // Inside the hole: the enlarged fill.
            colour = Blend(f0, f1, f2, f3, w0, w1, w2, w3);
            opacity = 256;
        }
        else
        {
            // At the boundary each neighbour's bilinear weight is scaled by how alike its colour is
            // to this pixel's, and opacity and fill are blended premultiplied. Every range weight is
            // at least one, so a neighbour with any weight keeps some.
            (w0, w1, w2, w3) = (w0 * RangeWeights[Difference(original, rows.UpperGuide[first])],
                w1 * RangeWeights[Difference(original, rows.UpperGuide[second])],
                w2 * RangeWeights[Difference(original, rows.LowerGuide[first])],
                w3 * RangeWeights[Difference(original, rows.LowerGuide[second])]);
            var (s0, s1, s2, s3) = ((long)w0 * a0, (long)w1 * a1, (long)w2 * a2, (long)w3 * a3);
            var covered = s0 + s1 + s2 + s3;
            var total = w0 + w1 + w2 + w3;
            // An opacity that rounds to nothing, or a pixel whose only hiding neighbour has no
            // weight, leaves the pixel as it was.
            opacity = (int)((covered * 256 / 255 + total / 2) / total);
            covered = Math.Max(1, covered);
            colour = Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 0) | (Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 8) << 8)
                | (Channel(f0, f1, f2, f3, s0, s1, s2, s3, covered, 16) << 16);
        }

        if (!better.IsEmpty && rows.Estimated[across >= 8 ? second : first])
        {
            // A model's fill is defined at full size: its marked pixels take the model's own
            // colour outright, not a feathered mix with the subject.
            colour = better[x] & 0xFFFFFF;
            opacity = 256;
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

    /// <summary>Four packed colours blended by weights in 256ths that sum to 256, alpha dropped.</summary>
    private static uint Blend(uint a, uint b, uint c, uint d, int wa, int wb, int wc, int wd)
    {
        const uint Mask = 0x00FF00FF;
        var blueRed = ((a & Mask) * (uint)wa + (b & Mask) * (uint)wb + (c & Mask) * (uint)wc + (d & Mask) * (uint)wd + 0x00800080) >> 8;
        var green = (((a >> 8) & 0xFF) * (uint)wa + ((b >> 8) & 0xFF) * (uint)wb + ((c >> 8) & 0xFF) * (uint)wc + ((d >> 8) & 0xFF) * (uint)wd + 0x80) >> 8;
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
}
