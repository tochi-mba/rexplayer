using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// The affine map from coded samples (Y, Cb, Cr) to red, green and blue in [0, 1]: three rows of
/// four, the fourth column the constant term. One description serves the CPU converter and the GPU
/// shader, so a snapshot and the screen agree. The equations are ITU-T H.273's, from the matrix's
/// Kr and Kb, with the range's offsets and scales; H.273 matrix 0 holds G, B and R in the planes.
/// </summary>
public sealed class YuvTransform
{
    private readonly double[] _rows;

    private YuvTransform(double[] rows) => _rows = rows;

    /// <summary>The map for samples of <paramref name="bits"/> bits; with no chroma (grey) the chroma columns are zero.</summary>
    public static YuvTransform For(ColorInfo color, int bits, bool hasChroma = true)
    {
        ArgumentNullException.ThrowIfNull(color);
        var scale = 1 << (bits - 8);
        double max = (1 << bits) - 1;
        var lumaScale = 1 / (color.FullRange ? max : 219 * scale);
        var lumaOffset = -(color.FullRange ? 0 : 16 * scale) * lumaScale;
        if (ColorConverter.Weights(color.Matrix) is not { } k)
        {
            return new YuvTransform(
            [
                0, 0, lumaScale, lumaOffset,
                lumaScale, 0, 0, lumaOffset,
                0, lumaScale, 0, lumaOffset,
            ]);
        }

        var chromaScale = hasChroma ? 1 / (color.FullRange ? max : 224 * scale) : 0;
        var chromaOffset = -(1 << (bits - 1)) * chromaScale;
        var kg = 1 - k.Kr - k.Kb;
        var redFromCr = 2 * (1 - k.Kr);
        var blueFromCb = 2 * (1 - k.Kb);
        var greenFromCr = 2 * k.Kr * (1 - k.Kr) / kg;
        var greenFromCb = 2 * k.Kb * (1 - k.Kb) / kg;
        return new YuvTransform(
        [
            lumaScale, 0, redFromCr * chromaScale, lumaOffset + (redFromCr * chromaOffset),
            lumaScale, -greenFromCb * chromaScale, -greenFromCr * chromaScale, lumaOffset - ((greenFromCb + greenFromCr) * chromaOffset),
            lumaScale, blueFromCb * chromaScale, 0, lumaOffset + (blueFromCb * chromaOffset),
        ]);
    }

    /// <summary>Red, green and blue (unclamped) for one set of samples.</summary>
    public (double R, double G, double B) Apply(int y, int cb, int cr) => (Row(0, y, cb, cr), Row(1, y, cb, cr), Row(2, y, cb, cr));

    /// <summary>
    /// The rows for samples as a texture hands them to a shader: each sample read as value / range,
    /// where <paramref name="sampleRange"/> is what a texel of 1.0 means in coded units (255 for 8-bit
    /// textures; 65535 / 64 for P010, whose 10 bits sit at the top of 16).
    /// </summary>
    public float[] ForShader(double sampleRange)
    {
        var rows = new float[12];
        for (var i = 0; i < 12; i++)
        {
            rows[i] = (float)(i % 4 == 3 ? _rows[i] : _rows[i] * sampleRange);
        }

        return rows;
    }

    private double Row(int row, int y, int cb, int cr) =>
        (_rows[row * 4] * y) + (_rows[(row * 4) + 1] * cb) + (_rows[(row * 4) + 2] * cr) + _rows[(row * 4) + 3];
}
