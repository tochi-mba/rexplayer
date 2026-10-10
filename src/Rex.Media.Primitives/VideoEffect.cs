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
}

/// <summary>Names, categories and safe bounds for the optional effects.</summary>
public static class VideoEffects
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "Prism flow", "Neon contours", "Pixel drift", "Kaleidoscope",
        "Ink trace", "Topographic contours", "Chromatic contours",
        "Liquid glass", "Slice shift", "Vortex", "Cursor lens",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];


    /// <summary>Visual grouping for an accessible effect picker.</summary>
    public static string Category(VideoEffect effect) => effect switch
    {
        VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.ChromaticContours => "Contours",
        VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex => "Motion & geometry",
        VideoEffect.CursorLens => "Interactive",
        _ => "Essentials",
    };

    /// <summary>Only the lens requires pointer coordinates; no input is captured for other effects.</summary>
    public static bool UsesPointer(VideoEffect effect) => effect == VideoEffect.CursorLens;

    public static int Strength(int percent) => Math.Clamp(percent, 0, 100);
}
