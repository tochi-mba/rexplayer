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

    /// <summary>
    /// The colour description from ITU-T H.273 code points, the numbering MP4 "colr" boxes, Matroska
    /// and the H.264 and HEVC headers all share. Codes rexplayer does not distinguish stay unspecified.
    /// </summary>
    public static ColorInfo FromCodes(int primaries, int transfer, int matrix, bool fullRange) => new(
        matrix switch
        {
            0 => ColorMatrix.Rgb,
            1 => ColorMatrix.Bt709,
            5 or 6 => ColorMatrix.Bt601,
            9 or 10 => ColorMatrix.Bt2020NonConstant,
            _ => ColorMatrix.Unspecified,
        },
        transfer switch
        {
            1 or 6 or 14 or 15 => ColorTransfer.Bt709,
            8 => ColorTransfer.Linear,
            13 => ColorTransfer.Srgb,
            16 => ColorTransfer.Pq,
            18 => ColorTransfer.Hlg,
            _ => ColorTransfer.Unspecified,
        },
        primaries switch
        {
            1 => ColorPrimaries.Bt709,
            5 => ColorPrimaries.Bt601Pal,
            6 or 7 => ColorPrimaries.Bt601Ntsc,
            9 => ColorPrimaries.Bt2020,
            12 => ColorPrimaries.DisplayP3,
            _ => ColorPrimaries.Unspecified,
        },
        fullRange);

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
