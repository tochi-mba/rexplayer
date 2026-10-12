namespace Rex.Media.Vision;

/// <summary>
/// The tensors of the promptable segmenter (EfficientSAM, see <see cref="ModelCatalogue.EfficientSamTiny"/>),
/// built and read here so the Windows ML adapter only runs the model. The encoder's own preprocessing
/// stretches any picture to a square of 1024 and normalises it; resizing here first, the same way,
/// keeps a 4K picture from crossing to the graphics card at full size.
/// </summary>
public static class SegmentationTensors
{
    /// <summary>The side of the square picture the encoder works on.</summary>
    public const int EncoderSide = 1024;

    /// <summary>The label of a point on the object.</summary>
    public const float PointLabel = 1;

    /// <summary>The label of a box's top-left corner.</summary>
    public const float BoxStartLabel = 2;

    /// <summary>The label of a box's bottom-right corner.</summary>
    public const float BoxEndLabel = 3;

    /// <summary>
    /// The picture as planar red, green and blue between 0 and 1, stretched to <paramref name="side"/>
    /// square with bilinear filtering on pixel centres (the encoder's own resampling).
    /// </summary>
    public static void Image(BgraPicture picture, int side, Span<float> tensor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(side);
        var plane = side * side;
        if (tensor.Length != 3 * plane)
        {
            throw new ArgumentException("The tensor holds three planes of the square side.", nameof(tensor));
        }

        var (x0, x1, wx) = Taps(picture.Width, side);
        var (y0, y1, wy) = Taps(picture.Height, side);
        const float Scale = 1f / 255;
        for (var y = 0; y < side; y++)
        {
            var top = picture.Row(y0[y]);
            var bottom = picture.Row(y1[y]);
            var fy = wy[y];
            var at = y * side;
            for (var x = 0; x < side; x++)
            {
                int a = x0[x] * 4, b = x1[x] * 4;
                var fx = wx[x];
                for (var channel = 0; channel < 3; channel++)
                {
                    var upper = top[a + channel] + ((top[b + channel] - top[a + channel]) * fx);
                    var lower = bottom[a + channel] + ((bottom[b + channel] - bottom[a + channel]) * fx);
                    // BGRA in memory, RGB planes out: blue is channel 0 and goes to the third plane.
                    tensor[((2 - channel) * plane) + at + x] = (upper + ((lower - upper) * fy)) * Scale;
                }
            }
        }
    }

    /// <summary>
    /// The decoder's point coordinates ([1,1,N,2], x then y, in the picture's pixels) and labels
    /// ([1,1,N]) for a prompt; points outside the picture are moved to its edge.
    /// </summary>
    public static (float[] Coordinates, float[] Labels) Prompt(SegmentPrompt prompt, int width, int height)
    {
        var coordinates = new List<float>(6);
        var labels = new List<float>(3);
        if (prompt.X is { } x && prompt.Y is { } y)
        {
            coordinates.AddRange([Math.Clamp(x, 0, width - 1), Math.Clamp(y, 0, height - 1)]);
            labels.Add(PointLabel);
        }

        if (prompt.Box is { } box)
        {
            coordinates.AddRange([Math.Clamp(box.Left, 0, width - 1), Math.Clamp(box.Top, 0, height - 1)]);
            coordinates.AddRange([Math.Clamp(box.Right - 1, 0, width - 1), Math.Clamp(box.Bottom - 1, 0, height - 1)]);
            labels.AddRange([BoxStartLabel, BoxEndLabel]);
        }

        return ([.. coordinates], [.. labels]);
    }

    /// <summary>How far below the best predicted overlap an outline may be and still be preferred for its size.</summary>
    public const float OverlapMargin = 0.15f;

    /// <summary>The predicted overlap below which a larger outline is never preferred.</summary>
    public const float MinimumOverlap = 0.5f;

    /// <summary>
    /// The decoder's answer as a mask, choosing among its candidate outlines (logits, each
    /// <paramref name="width"/> by <paramref name="height"/>; a logit above zero is inside). A click
    /// on a shirt should select the person, not the shirt: of the outlines holding the clicked point
    /// whose predicted overlap is within <see cref="OverlapMargin"/> of the best (and at least
    /// <see cref="MinimumOverlap"/>), the largest wins. When none qualifies, the best-scored outline
    /// holding the point wins, and without a point (or when none holds it) the best-scored outline.
    /// A prompt with an expected area (following an object) takes the outline nearest that size.
    /// </summary>
    public static PictureMask Decode(ReadOnlySpan<float> logits, ReadOnlySpan<float> overlap, int width, int height, SegmentPrompt prompt)
    {
        var plane = width * height;
        var candidates = overlap.Length;
        if (candidates == 0 || logits.Length != candidates * plane)
        {
            throw new ArgumentException("The decoder gives one outline per overlap estimate.", nameof(logits));
        }

        var fallback = 0;
        for (var candidate = 1; candidate < candidates; candidate++)
        {
            if (overlap[candidate] > overlap[fallback])
            {
                fallback = candidate;
            }
        }

        var chosen = prompt.ExpectedArea is { } expected
            ? Nearest(logits, plane, candidates, expected)
            : Clicked(logits, overlap, width, height, prompt, fallback);
        var source = logits.Slice(chosen * plane, plane);
        var values = new byte[plane];
        for (var i = 0; i < plane; i++)
        {
            values[i] = source[i] > 0 ? (byte)255 : (byte)0;
        }

        return new PictureMask(values, width, height, overlap[chosen]);
    }

    /// <summary>
    /// Following an object: the outline nearest the remembered size (by ratio), so a person followed
    /// stays a person and a shirt stays a shirt, whatever else the search box happens to enclose.
    /// </summary>
    private static int Nearest(ReadOnlySpan<float> logits, int plane, int candidates, int expected)
    {
        var nearest = 0;
        var distance = double.MaxValue;
        for (var candidate = 0; candidate < candidates; candidate++)
        {
            var gap = Math.Abs(Math.Log((Inside(logits.Slice(candidate * plane, plane)) + 1.0) / expected));
            if (gap < distance)
            {
                (nearest, distance) = (candidate, gap);
            }
        }

        return nearest;
    }

    /// <summary>A click: the largest confident outline holding the point (see <see cref="Decode"/>).</summary>
    private static int Clicked(ReadOnlySpan<float> logits, ReadOnlySpan<float> overlap, int width, int height, SegmentPrompt prompt, int fallback)
    {
        var plane = width * height;
        var floor = Math.Max(MinimumOverlap, overlap[fallback] - OverlapMargin);
        int holding = -1, largest = -1, largestArea = -1;
        for (var candidate = 0; candidate < overlap.Length; candidate++)
        {
            var outline = logits.Slice(candidate * plane, plane);
            var holdsPoint = prompt.X is { } x && prompt.Y is { } y
                && outline[(Math.Clamp(y, 0, height - 1) * width) + Math.Clamp(x, 0, width - 1)] > 0;
            if (!holdsPoint)
            {
                continue;
            }

            if (holding < 0 || overlap[candidate] > overlap[holding])
            {
                holding = candidate;
            }

            if (overlap[candidate] >= floor && Inside(outline) is var area && area > largestArea)
            {
                (largest, largestArea) = (candidate, area);
            }
        }

        return largest >= 0 ? largest : holding >= 0 ? holding : fallback;
    }

    private static int Inside(ReadOnlySpan<float> outline)
    {
        var count = 0;
        foreach (var logit in outline)
        {
            count += logit > 0 ? 1 : 0;
        }

        return count;
    }

    private static (int[] Near, int[] Far, float[] Weight) Taps(int source, int target)
    {
        var near = new int[target];
        var far = new int[target];
        var weight = new float[target];
        var scale = (double)source / target;
        for (var i = 0; i < target; i++)
        {
            var at = Math.Max(0, ((i + 0.5) * scale) - 0.5);
            var low = Math.Min((int)at, source - 1);
            near[i] = low;
            far[i] = Math.Min(low + 1, source - 1);
            weight[i] = (float)(at - low);
        }

        return (near, far, weight);
    }
}
