namespace Rex.Media.Primitives;

/// <summary>
/// Non-destructive, GPU-computed picture effects. Separate from the colour grading looks:
/// every effect changes sampling geometry or responds to spatial detail in the video.
/// </summary>
public enum VideoEffect
{
    Off,
    PrismFlow,
    NeonEdges,
    PixelDrift,
    Kaleidoscope,
    InkTrace,
    TopographicContours,
    ChromaticContours,
    LiquidGlass,
    SliceShift,
    Vortex,
    CursorLens,
    EdgeGravity,
    PrecisionContours,
    ColourSpotlight,
    ReliefEtch,
}

/// <summary>Names, categories and safe bounds for the optional effects.</summary>
public static class VideoEffects
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "Prism flow", "Neon contours", "Pixel drift", "Kaleidoscope",
        "Ink trace", "Topographic contours", "Chromatic contours",
        "Liquid glass", "Slice shift", "Vortex", "Cursor lens", "Edge gravity",
        "Precision contours", "Colour spotlight", "Relief etch",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];

    /// <summary>Visual grouping for an accessible effect picker.</summary>
    public static string Category(VideoEffect effect) => effect switch
    {
        VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.ChromaticContours or VideoEffect.PrecisionContours => "Contours",
        VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex => "Motion & geometry",
        VideoEffect.CursorLens or VideoEffect.ColourSpotlight => "Interactive",
        VideoEffect.EdgeGravity or VideoEffect.ReliefEtch => "Image-aware",
        _ => "Essentials",
    };

    /// <summary>Only the two pointer-driven effects read pointer coordinates, never persisted or recorded.</summary>
    public static bool UsesPointer(VideoEffect effect) => effect is VideoEffect.CursorLens or VideoEffect.ColourSpotlight;

    public static int Strength(int percent) => Math.Clamp(percent, 0, 100);
}
