using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Preferred proportions of cards in a justified library collage. These are display crops, not
/// guesses about source pixel sizes: a photo retains its pixels' proportions in the Pictures viewer.
/// The small, repeatable variations keep rows varied without needing to decode invisible files.
/// </summary>
public static class CollageLayout
{
    private static readonly double[] Posters = [0.72, 0.88, 0.76, 1.06, 0.80, 0.95, 0.72, 1.12];
    private static readonly double[] Videos = [1.55, 1.96, 1.42, 1.78, 1.62, 2.04, 1.50, 1.82];
    private static readonly double[] Pictures = [1.16, 1.72, 0.88, 1.42, 1.96, 1.05, 1.61, 0.98];
    private static readonly double[] Covers = [1.02, 1.38, 0.90, 1.21, 1.46, 0.95, 1.30, 1.06];

    /// <summary>Desired card width / row height, always positive and bounded for the flow layout.</summary>
    public static double AspectRatio(LibraryKind kind, bool poster, int index)
    {
        var ratios = poster ? Posters : kind switch
        {
            LibraryKind.Video => Videos,
            LibraryKind.Picture => Pictures,
            _ => Covers,
        };
        return ratios[Math.Abs(index % ratios.Length)];
    }
}
