using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>An aspect ratio or crop the user can choose; no ratio means the picture's own.</summary>
public sealed record ShapePreset(string Name, Rational? Ratio);

/// <summary>The part of a picture shown, each edge from 0 to 1 of the picture.</summary>
public readonly record struct SourceRect(float Left, float Top, float Right, float Bottom)
{
    public static SourceRect Full { get; } = new(0, 0, 1, 1);
}

/// <summary>
/// The aspect-ratio and crop presets (VID-05, VID-06) and what they do to a picture. An aspect ratio
/// stretches the whole picture to a new shape, for media whose shape was recorded wrongly; a crop
/// cuts the picture down to a shape, equally from both sides, as when black bars are part of the
/// picture itself. Both are worked out in exact integers.
/// </summary>
public static class VideoGeometry
{
    public static IReadOnlyList<ShapePreset> AspectRatios { get; } =
    [
        new("Default", null), new("16:9", new(16, 9)), new("4:3", new(4, 3)), new("1:1", new(1, 1)), new("16:10", new(16, 10)),
        new("2.21:1", new(221, 100)), new("2.35:1", new(235, 100)), new("2.39:1", new(239, 100)), new("5:4", new(5, 4)),
    ];

    public static IReadOnlyList<ShapePreset> Crops { get; } =
    [
        new("None", null), new("16:10", new(16, 10)), new("16:9", new(16, 9)), new("4:3", new(4, 3)), new("1.85:1", new(185, 100)),
        new("2.21:1", new(221, 100)), new("2.35:1", new(235, 100)), new("2.39:1", new(239, 100)), new("5:3", new(5, 3)), new("5:4", new(5, 4)), new("1:1", new(1, 1)),
    ];

    /// <summary>The preset after <paramref name="current"/> in <paramref name="presets"/>, wrapping to the first.</summary>
    public static ShapePreset Next(IReadOnlyList<ShapePreset> presets, ShapePreset current)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var index = presets.ToList().IndexOf(current);
        return presets[(index + 1) % presets.Count];
    }

    /// <summary>
    /// What to draw of a <paramref name="width"/> by <paramref name="height"/> picture with
    /// <paramref name="pixelAspect"/>, given an <paramref name="aspect"/> and a <paramref name="crop"/>
    /// (null for neither): the part shown, and its shape on screen as across : down.
    /// </summary>
    public static (SourceRect Source, long Across, long Down) Shape(int width, int height, Rational pixelAspect, Rational? aspect, Rational? crop)
    {
        var (numerator, denominator) = pixelAspect.Numerator > 0 ? (pixelAspect.Numerator, pixelAspect.Denominator) : (1L, 1L);
        var (across, down) = aspect is { Numerator: > 0, Denominator: > 0 } forced
            ? (forced.Numerator, forced.Denominator)
            : ((long)Math.Max(width, 1) * numerator, (long)Math.Max(height, 1) * denominator);
        if (crop is not { Numerator: > 0, Denominator: > 0 } cut)
        {
            return (SourceRect.Full, across, down);
        }

        // Wider than the picture: keep the full width and take the height down; narrower: the reverse.
        if (cut.Numerator * down > across * cut.Denominator)
        {
            var kept = (double)(across * cut.Denominator) / (down * cut.Numerator);
            var edge = (float)((1 - kept) / 2);
            return (new SourceRect(0, edge, 1, 1 - edge), cut.Numerator, cut.Denominator);
        }

        var keptWidth = (double)(cut.Numerator * down) / (cut.Denominator * across);
        var side = (float)((1 - keptWidth) / 2);
        return (new SourceRect(side, 0, 1 - side, 1), cut.Numerator, cut.Denominator);
    }
}
