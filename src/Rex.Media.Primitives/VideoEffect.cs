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
    Ghostwire,
    ColourSpotlight,
    ReliefEtch,
    GhostwireMask,
    GhostwireMotion,
    Halftone,
    ChromaticRift,
    OilPaint,
    GlyphMosaic,
    ThermalMap,
    TapeWear,
    PixelSort,
    MotionSmear,
    LumenWeave,
    DepthHaze,
    ParticleVeil,
    LensBloom,
    StippleEngrave,
    TimeEcho,
    BrickBuild,
    StainedGlass,
    CrossStitch,
    LedWall,
    DigitalRain,
    DiceMosaic,
    CubeMosaic,
    HexTiles,
    RetroPixels,
    MotionAmplify,
}

/// <summary>Names, categories and safe bounds for the optional effects.</summary>
public static class VideoEffects
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "Prism flow", "Neon contours", "Pixel drift", "Kaleidoscope",
        "Ink trace", "Topographic contours", "Chromatic contours",
        "Liquid glass", "Slice shift", "Vortex", "Cursor lens", "Edge gravity",
        "Ghostwire", "Colour spotlight", "Relief etch", "Ghostwire mask mode", "Ghostwire motion",
        "Halftone print", "Chromatic rift", "Oil paint", "Glyph mosaic", "Thermal map",
        "Tape wear", "Pixel sort", "Motion smear",
        "Lumen weave", "Depth haze", "Particle veil", "Lens bloom", "Stipple engrave", "Time echo",
        "Brick build", "Stained glass", "Cross-stitch", "LED wall", "Digital rain", "Dice mosaic",
        "Cube mosaic", "Hex tiles", "Retro pixels", "Motion amplify",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];

    /// <summary>Visual grouping for an accessible effect picker.</summary>
    public static string Category(VideoEffect effect) => effect switch
    {
        VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.ChromaticContours or VideoEffect.Ghostwire or VideoEffect.GhostwireMask or VideoEffect.GhostwireMotion or VideoEffect.LumenWeave => "Contours",
        VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex or VideoEffect.TimeEcho
            or VideoEffect.MotionAmplify => "Motion & geometry",
        VideoEffect.CursorLens or VideoEffect.ColourSpotlight => "Interactive",
        VideoEffect.EdgeGravity or VideoEffect.ReliefEtch or VideoEffect.ThermalMap or VideoEffect.DepthHaze => "Image-aware",
        VideoEffect.Halftone or VideoEffect.OilPaint or VideoEffect.StippleEngrave => "Print & texture",
        VideoEffect.GlyphMosaic or VideoEffect.BrickBuild or VideoEffect.StainedGlass or VideoEffect.CrossStitch
            or VideoEffect.LedWall or VideoEffect.DigitalRain or VideoEffect.DiceMosaic or VideoEffect.CubeMosaic
            or VideoEffect.HexTiles or VideoEffect.RetroPixels => "Mosaics",
        VideoEffect.ChromaticRift or VideoEffect.TapeWear or VideoEffect.PixelSort or VideoEffect.MotionSmear or VideoEffect.ParticleVeil => "Playful",
        _ => "Essentials",
    };

    /// <summary>Only the two pointer-driven effects read pointer coordinates, never persisted or recorded.</summary>
    public static bool UsesPointer(VideoEffect effect) => effect is VideoEffect.CursorLens or VideoEffect.ColourSpotlight;

    /// <summary>
    /// Whether the effect, with its fourth option at this value, reads the previous frame. Only
    /// then does the renderer keep a copy of the last picture on the graphics card: Ghostwire
    /// motion in its hybrid and motion modes, Motion smear, Particle veil, Time echo and Motion amplify always,
    /// and Chromatic rift only while its motion drive is above zero. A zero normalized strength
    /// disables history entirely.
    /// </summary>
    public static bool UsesHistory(VideoEffect effect, int fourth, float strength = 1) => strength > 0 && (effect switch
    {
        VideoEffect.GhostwireMotion => fourth is 0 or 2,
        VideoEffect.MotionSmear or VideoEffect.ParticleVeil or VideoEffect.TimeEcho or VideoEffect.MotionAmplify => true,
        VideoEffect.ChromaticRift => fourth > 0,
        _ => false,
    });

    public static int Strength(int percent) => Math.Clamp(percent, 0, 100);
}
