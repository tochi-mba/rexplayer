namespace Rex.Media.AppCore.Library;

/// <summary>
/// A bounded preview uses only the longer oriented image dimension so a tall panorama
/// does not allocate thousands of rows merely because the requested width was small.
/// BitmapImage preserves aspect when only one decoding dimension is constrained.
/// </summary>
public readonly record struct PictureDecodeLimit(int Width, int Height)
{
    public static PictureDecodeLimit For(uint orientedWidth, uint orientedHeight, int requestedSide)
    {
        var side = Math.Clamp(requestedSide, 1, 2048);
        return orientedWidth > 0 && orientedHeight > orientedWidth
            ? new PictureDecodeLimit(0, side)
            : new PictureDecodeLimit(side, 0);
    }
}
