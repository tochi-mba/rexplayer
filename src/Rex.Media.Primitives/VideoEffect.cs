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
}

/// <summary>Names and safe bounds for the four optional effects.</summary>
public static class VideoEffects
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "Prism flow", "Neon contours", "Pixel drift", "Kaleidoscope",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];

    public static int Strength(int percent) => Math.Clamp(percent, 0, 100);
}
