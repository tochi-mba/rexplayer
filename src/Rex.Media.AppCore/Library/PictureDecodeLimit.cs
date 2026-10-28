namespace Rex.Media.AppCore.Library;

/// <summary>
/// A bounded preview uses only the longer oriented image dimension so a tall panorama
/// does not allocate thousands of rows merely because the requested width was small.
/// BitmapImage preserves aspect when only one decoding dimension is constrained.
/// </summary>
public readonly record struct PictureDecodeLimit(int Width, int Height)
{
    /// <summary>Reject corrupted or excessive encoded cache files before allocating for them.</summary>
    public const long MaxCachedBytes = 32L * 1024 * 1024;

    public static bool CacheBytesAllowed(long length) => length is >= 0 and <= MaxCachedBytes;

    public static PictureDecodeLimit For(uint orientedWidth, uint orientedHeight, int requestedSide)
    {
        var side = Math.Clamp(requestedSide, 1, 2048);
        return orientedWidth > 0 && orientedHeight > orientedWidth
            ? new PictureDecodeLimit(0, side)
            : new PictureDecodeLimit(side, 0);
    }

    /// <summary>
    /// What a picture decoded at most <paramref name="side"/> pixels on its longer side costs: four
    /// bytes a pixel, no more than its own size when that is known and smaller.
    /// </summary>
    public static long DecodedBytes(int side, int naturalWidth, int naturalHeight)
    {
        var bound = (long)Math.Max(1, side) * Math.Max(1, side) * 4;
        var natural = naturalWidth > 0 && naturalHeight > 0 ? (long)naturalWidth * naturalHeight * 4 : bound;
        return Math.Min(bound, natural);
    }
}
