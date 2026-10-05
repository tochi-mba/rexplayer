namespace Rex.Media.Primitives;

/// <summary>The matrix that turns Y'CbCr into R'G'B'.</summary>
public enum ColorMatrix
{
    Unspecified = 0,
    Bt601,
    Bt709,
    Bt2020NonConstant,
    Rgb,
}

/// <summary>The transfer characteristic (gamma or HDR curve) of the signal.</summary>
public enum ColorTransfer
{
    Unspecified = 0,
    Bt709,
    Srgb,
    Pq,
    Hlg,
    Linear,
}

/// <summary>The colour primaries the signal was mastered against.</summary>
public enum ColorPrimaries
{
    Unspecified = 0,
    Bt601Ntsc,
    Bt601Pal,
    Bt709,
    Bt2020,
    DisplayP3,
}

/// <summary>
/// How a stream's colour is coded. Unspecified fields are resolved by <see cref="Resolve"/> from the
/// picture size, the way broadcast practice does: standard definition is BT.601, everything larger
/// is BT.709 unless it says otherwise.
/// </summary>
public sealed record ColorInfo(ColorMatrix Matrix, ColorTransfer Transfer, ColorPrimaries Primaries, bool FullRange)
{
    public static ColorInfo Unspecified { get; } = new(ColorMatrix.Unspecified, ColorTransfer.Unspecified, ColorPrimaries.Unspecified, false);

    public bool IsHdr => Transfer is ColorTransfer.Pq or ColorTransfer.Hlg;

    /// <summary>A copy with every unspecified field filled in for a picture of the given size.</summary>
    public ColorInfo Resolve(int width, int height)
    {
        var standardDefinition = width <= 1024 && height <= 576;
        var matrix = Matrix != ColorMatrix.Unspecified ? Matrix : standardDefinition ? ColorMatrix.Bt601 : ColorMatrix.Bt709;
        var primaries = Primaries != ColorPrimaries.Unspecified
            ? Primaries
            : matrix switch
            {
                ColorMatrix.Bt2020NonConstant => ColorPrimaries.Bt2020,
                ColorMatrix.Bt601 => height == 576 ? ColorPrimaries.Bt601Pal : ColorPrimaries.Bt601Ntsc,
                _ => ColorPrimaries.Bt709,
            };
        var transfer = Transfer != ColorTransfer.Unspecified ? Transfer : ColorTransfer.Bt709;
        return new ColorInfo(matrix, transfer, primaries, FullRange);
    }
}
