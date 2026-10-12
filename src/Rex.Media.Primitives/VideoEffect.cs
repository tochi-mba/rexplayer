namespace Rex.Media.Primitives;

/// <summary>
/// Non-destructive, GPU-computed picture effects. Separate from the colour grading looks:
/// every effect changes sampling geometry or responds to spatial detail in the video. The values
/// are the shader's effect indices, so members are only ever added at the end.
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
}

/// <summary>Names, categories, descriptions and safe bounds for the optional effects.</summary>
public static class VideoEffects
{
    /// <summary>The longest gap between two pictures that still counts as the picture moving on.</summary>
    public const double HistoryGapSeconds = 0.25;

    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "Prism flow", "Neon contours", "Pixel drift", "Kaleidoscope",
        "Ink trace", "Topographic contours", "Chromatic contours",
        "Liquid glass", "Slice shift", "Vortex", "Cursor lens", "Edge gravity",
        "Ghostwire", "Colour spotlight", "Relief etch", "Ghostwire mask mode", "Ghostwire motion",
        "Halftone print", "Chromatic rift", "Oil paint", "Glyph mosaic", "Thermal map",
        "Tape wear", "Pixel sort", "Motion smear",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];

    /// <summary>Visual grouping for an accessible effect picker.</summary>
    public static string Category(VideoEffect effect) => effect switch
    {
        VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.ChromaticContours or VideoEffect.Ghostwire or VideoEffect.GhostwireMask or VideoEffect.GhostwireMotion => "Contours",
        VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex => "Motion & geometry",
        VideoEffect.CursorLens or VideoEffect.ColourSpotlight => "Interactive",
        VideoEffect.EdgeGravity or VideoEffect.ReliefEtch or VideoEffect.ThermalMap => "Image-aware",
        VideoEffect.Halftone or VideoEffect.OilPaint or VideoEffect.GlyphMosaic => "Print & texture",
        VideoEffect.ChromaticRift or VideoEffect.TapeWear or VideoEffect.PixelSort or VideoEffect.MotionSmear => "Playful",
        _ => "Essentials",
    };

    /// <summary>The effect's name with its group, for a flat list: "Ghostwire (Contours)"; essentials by name alone.</summary>
    public static string PickerLabel(VideoEffect effect) =>
        Category(effect) is var category && category == "Essentials" ? Name(effect) : $"{Name(effect)} ({category})";

    /// <summary>One or two plain sentences saying what the effect does, for its settings.</summary>
    public static string Description(VideoEffect effect) => effect switch
    {
        VideoEffect.PrismFlow => "Slow waves bend the picture and split its colours at the edges, following the video's time.",
        VideoEffect.NeonEdges => "The main outlines (silhouettes and big shapes) glow as pink neon tubes over a darkened picture, humming gently with time. Texture and small detail stay dark.",
        VideoEffect.PixelDrift => "The picture breaks into square blocks whose rows sway with time. Intensity sets the block size.",
        VideoEffect.Kaleidoscope => "Mirrored wedges repeat the middle of the picture around the centre. Intensity sets how much of the picture they fill.",
        VideoEffect.InkTrace => "A pen sketch on paper: clear outlines in broad strokes, faint ones as hairlines, and shadows cross-hatched.",
        VideoEffect.TopographicContours => "The picture's brightness drawn as a relief map: tinted height layers, hill shading and a contour line at every level.",
        VideoEffect.ChromaticContours => "Every fine line, faint ones included, drawn on black in a colour chosen by the direction it faces.",
        VideoEffect.LiquidGlass => "The picture ripples as if seen through moving glass.",
        VideoEffect.SliceShift => "Horizontal bands jump sideways a few times a second.",
        VideoEffect.Vortex => "A swirl at the centre of the picture that breathes with time; the edges stay still.",
        VideoEffect.CursorLens => "A magnifying lens follows the pointer while it is over the picture.",
        VideoEffect.EdgeGravity => "Strong outlines ripple along their own direction and shimmer; flat areas stay still.",
        VideoEffect.Ghostwire => "An almost invisible glass-like picture with luminous contours. It shows visible edges, not hidden surfaces.",
        VideoEffect.ColourSpotlight => "Move the pointer over a colour to highlight similar colours in the current frame. This does not follow an object.",
        VideoEffect.ReliefEtch => "Simulated directional light carves relief from visible contrast; it does not estimate real depth.",
        VideoEffect.GhostwireMask => "Flat, dim shapes from the picture's brightness, with solid outlines and dotted inner contours. It does not detect clothing, remove it, or reveal skin or anatomy hidden behind it.",
        VideoEffect.GhostwireMotion => "Surface shape (default) reveals visible curves, highlights and soft shading even when the image is paused or still. Hybrid adds motion highlights; Contours and Motion isolate their respective signals. This is artistic image contrast, not a reconstruction of hidden anatomy. Camera movement and cuts can produce artefacts.",
        VideoEffect.Halftone => "Printed dots in three inks whose size follows the picture's colour.",
        VideoEffect.ChromaticRift => "Red and blue separate along a direction; with motion boost, more where the picture moves.",
        VideoEffect.OilPaint => "Smooths the picture into flat strokes while keeping its edges.",
        VideoEffect.GlyphMosaic => "The picture rebuilt from simple character shapes, denser where it is brighter (darker on paper).",
        VideoEffect.ThermalMap => "Brightness shown as heat colours. It is not a temperature reading.",
        VideoEffect.TapeWear => "Wobbling lines, bleeding colour, a noisy band at the bottom and specks, like a worn tape.",
        VideoEffect.PixelSort => "Bright pixels run in streaks in the chosen direction.",
        VideoEffect.MotionSmear => "Where the picture changes, blocks of the previous picture linger.",
        _ => "",
    };

    /// <summary>Only the two pointer-driven effects read pointer coordinates, never persisted or recorded.</summary>
    public static bool UsesPointer(VideoEffect effect) => effect is VideoEffect.CursorLens or VideoEffect.ColourSpotlight;

    /// <summary>
    /// Whether the effect, at this strength and with its third and fourth controls at these values,
    /// reads the previous frame. Only then does the renderer keep a copy of the last picture on the
    /// graphics card: Ghostwire motion in its hybrid and motion modes, Motion smear while its amount
    /// is above zero, and Chromatic rift only while its motion boost is above zero. Nothing at zero
    /// strength reads anything.
    /// </summary>
    public static bool UsesHistory(VideoEffect effect, int strength, int third, int fourth) => strength > 0 && effect switch
    {
        VideoEffect.GhostwireMotion => fourth is 0 or 2,
        VideoEffect.MotionSmear => third > 0,
        VideoEffect.ChromaticRift => fourth > 0,
        _ => false,
    };

    /// <summary>
    /// Whether a picture continues the one before it, so a previous-frame effect may compare them:
    /// the same playback generation (no seek or new item between), and a time a little later.
    /// A repeat, a step backwards, a long gap or an unknown time starts the history again.
    /// </summary>
    public static bool ContinuesHistory(long? previousGeneration, double previousSeconds, long generation, double seconds) =>
        previousGeneration == generation && double.IsFinite(previousSeconds) && double.IsFinite(seconds)
        && seconds > previousSeconds && seconds - previousSeconds <= HistoryGapSeconds;

    public static int Strength(int percent) => Math.Clamp(percent, 0, 100);
}
