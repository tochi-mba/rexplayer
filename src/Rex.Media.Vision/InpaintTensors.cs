namespace Rex.Media.Vision;

/// <summary>
/// The tensors of the inpainting pipeline (MI-GAN, see <see cref="ModelCatalogue.MiGan"/>): the
/// picture as planar 8-bit red, green and blue, and the hole as one 8-bit plane where 255 keeps a
/// pixel and 0 asks for it to be filled. The hole is grown a little first, because an outline drawn
/// by a person or a model leaves a fringe of the object's own colour that would otherwise be used
/// to fill it. Only pixels inside the grown hole ever change.
/// </summary>
public static class InpaintTensors
{
    /// <summary>How many pixels the hole is grown by on every side.</summary>
    public const int DefaultGrowth = 6;

    /// <summary>The kept value of the mask tensor.</summary>
    public const byte Kept = 255;

    /// <summary>The picture as planar RGB bytes, [3,H,W].</summary>
    public static void Image(BgraPicture picture, Span<byte> tensor)
    {
        var plane = picture.Width * picture.Height;
        if (tensor.Length != 3 * plane)
        {
            throw new ArgumentException("The tensor holds three planes of the picture's size.", nameof(tensor));
        }

        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(y);
            var at = y * picture.Width;
            for (var x = 0; x < picture.Width; x++)
            {
                tensor[at + x] = row[(x * 4) + 2];
                tensor[plane + at + x] = row[(x * 4) + 1];
                tensor[(2 * plane) + at + x] = row[x * 4];
            }
        }
    }

    /// <summary>
    /// The mask tensor, [1,H,W]: 0 inside the hole grown by <paramref name="growth"/> pixels (a square
    /// neighbourhood), 255 elsewhere.
    /// </summary>
    public static void Hole(PictureMask hole, int growth, Span<byte> tensor)
    {
        ArgumentNullException.ThrowIfNull(hole);
        ArgumentOutOfRangeException.ThrowIfNegative(growth);
        int width = hole.Width, height = hole.Height;
        if (tensor.Length != width * height)
        {
            throw new ArgumentException("The tensor holds one plane of the mask's size.", nameof(tensor));
        }

        // A separable maximum: grow along rows, then along columns, each with a running count of
        // hole pixels inside the window, so the cost does not depend on the growth.
        var across = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Grow(hole.Values.AsSpan(y * width, width), across.AsSpan(y * width, width), growth);
        }

        var column = new byte[height];
        var grown = new byte[height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                column[y] = across[(y * width) + x];
            }

            Grow(column, grown, growth);
            for (var y = 0; y < height; y++)
            {
                tensor[(y * width) + x] = grown[y] != 0 ? (byte)0 : Kept;
            }
        }
    }

    /// <summary>
    /// The filled picture back as BGRA: pixels the mask tensor kept are copied from the original
    /// exactly (alpha always), pixels in the hole come from the model's planar RGB result.
    /// </summary>
    public static void Compose(BgraPicture picture, ReadOnlySpan<byte> result, ReadOnlySpan<byte> mask, Span<byte> output)
    {
        var plane = picture.Width * picture.Height;
        if (result.Length != 3 * plane || mask.Length != plane)
        {
            throw new ArgumentException("The result holds three planes and the mask one, each of the picture's size.", nameof(result));
        }

        if (output.Length < picture.Pixels.Length)
        {
            throw new ArgumentException("The output must be as large as the picture.", nameof(output));
        }

        picture.Pixels.CopyTo(output);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = output.Slice(y * picture.Stride, picture.Width * 4);
            var at = y * picture.Width;
            for (var x = 0; x < picture.Width; x++)
            {
                if (mask[at + x] == Kept)
                {
                    continue;
                }

                row[x * 4] = result[(2 * plane) + at + x];
                row[(x * 4) + 1] = result[plane + at + x];
                row[(x * 4) + 2] = result[at + x];
            }
        }
    }

    private static void Grow(ReadOnlySpan<byte> source, Span<byte> target, int radius)
    {
        var length = source.Length;
        var inside = 0;
        for (var i = 0; i < Math.Min(radius, length); i++)
        {
            inside += source[i] != 0 ? 1 : 0;
        }

        for (var i = 0; i < length; i++)
        {
            var entering = i + radius;
            if (entering < length && source[entering] != 0)
            {
                inside++;
            }

            target[i] = inside > 0 ? (byte)255 : (byte)0;
            var leaving = i - radius;
            if (leaving >= 0 && source[leaving] != 0)
            {
                inside--;
            }
        }
    }
}
