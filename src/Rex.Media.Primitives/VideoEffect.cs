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
    FlowField,
    RippleTouch,
    Torchlight,
    MotionIsolate,
    CelShade,
    TerrainMesh,
    GravityWell,
    ScanSweep,
    Shatter,
    Parallax,
    RainGlass,
    ZoomBurst,
    LowPoly,
    Pinball,
    Fireflies,
    ElectricEdges,
    TimeLens,
    Melt,
    Jigsaw,
    ColourMagnet,
    SonarPing,
    Bokeh,
    InkSpill,
    MotionOutline,
    HeatHaze,
    Confetti,
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
        "Lumen weave", "Depth haze", "Particle veil", "Lens bloom", "Stipple engrave", "Time echo",
        "Brick build", "Stained glass", "Cross-stitch", "LED wall", "Digital rain", "Dice mosaic",
        "Cube mosaic", "Hex tiles", "Retro pixels", "Motion amplify",
        "Flow field", "Ripple touch", "Torchlight", "Motion isolate", "Cel shade", "Terrain mesh", "Gravity well",
        "Scan sweep", "Shatter", "Parallax", "Rain on glass", "Zoom burst", "Low poly",
        "Pinball", "Fireflies", "Electric edges", "Time lens", "Melt", "Jigsaw", "Colour magnet",
        "Sonar ping", "Bokeh", "Ink spill", "Motion outline", "Heat haze", "Confetti",
    ];

    public static string Name(VideoEffect effect) => Enum.IsDefined(effect) ? Names[(int)effect] : Names[0];

    /// <summary>Visual grouping for an accessible effect picker.</summary>
    public static string Category(VideoEffect effect) => effect switch
    {
        VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.ChromaticContours or VideoEffect.Ghostwire or VideoEffect.GhostwireMask or VideoEffect.GhostwireMotion or VideoEffect.LumenWeave => "Contours",
        VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex or VideoEffect.TimeEcho
            or VideoEffect.MotionAmplify or VideoEffect.FlowField or VideoEffect.MotionIsolate or VideoEffect.MotionOutline => "Motion & geometry",
        VideoEffect.CursorLens or VideoEffect.ColourSpotlight or VideoEffect.RippleTouch or VideoEffect.Torchlight
            or VideoEffect.GravityWell or VideoEffect.ScanSweep or VideoEffect.Shatter or VideoEffect.Parallax
            or VideoEffect.RainGlass or VideoEffect.ZoomBurst or VideoEffect.Pinball or VideoEffect.ElectricEdges or VideoEffect.TimeLens
            or VideoEffect.Jigsaw or VideoEffect.ColourMagnet or VideoEffect.SonarPing or VideoEffect.InkSpill => "Interactive",
        VideoEffect.EdgeGravity or VideoEffect.ReliefEtch or VideoEffect.ThermalMap or VideoEffect.DepthHaze or VideoEffect.CelShade or VideoEffect.TerrainMesh or VideoEffect.Fireflies or VideoEffect.Bokeh or VideoEffect.HeatHaze => "Image-aware",
        VideoEffect.Halftone or VideoEffect.OilPaint or VideoEffect.StippleEngrave => "Print & texture",
        VideoEffect.GlyphMosaic or VideoEffect.BrickBuild or VideoEffect.StainedGlass or VideoEffect.CrossStitch
            or VideoEffect.LedWall or VideoEffect.DigitalRain or VideoEffect.DiceMosaic or VideoEffect.CubeMosaic
            or VideoEffect.HexTiles or VideoEffect.RetroPixels or VideoEffect.LowPoly => "Mosaics",
        VideoEffect.ChromaticRift or VideoEffect.TapeWear or VideoEffect.PixelSort or VideoEffect.MotionSmear or VideoEffect.ParticleVeil or VideoEffect.Melt or VideoEffect.Confetti => "Playful",
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
        VideoEffect.LumenWeave => "Continuous outlines become flowing strands of light.",
        VideoEffect.DepthHaze => "Soft and flat areas fade into coloured haze, using visible contrast rather than measured depth.",
        VideoEffect.ParticleVeil => "Sparks rise from areas that changed since the previous picture.",
        VideoEffect.LensBloom => "Highlights spread into glowing streaks.",
        VideoEffect.StippleEngrave => "Engraved lines follow visible edges.",
        VideoEffect.TimeEcho => "Moving areas split into coloured echoes.",
        VideoEffect.BrickBuild => "The picture becomes a mosaic of coloured toy bricks.",
        VideoEffect.StainedGlass => "Coloured panes and dark lead lines rebuild the picture.",
        VideoEffect.CrossStitch => "Coloured thread stitches the picture onto fabric.",
        VideoEffect.LedWall => "A grid of glowing diodes displays the picture.",
        VideoEffect.DigitalRain => "Falling columns of characters carry the picture colours.",
        VideoEffect.DiceMosaic => "Dice and their pips rebuild the picture.",
        VideoEffect.CubeMosaic => "Coloured cube stickers form the picture.",
        VideoEffect.HexTiles => "Hexagonal tiles rebuild the picture.",
        VideoEffect.RetroPixels => "Large pixels and limited colours give the picture a retro look.",
        VideoEffect.MotionAmplify => "Differences from the previous picture exaggerate visible movement.",
        VideoEffect.FlowField => "Where the picture moved since the previous frame, drawn as coloured streaks along the motion; still areas stay dark. Camera moves light up everything.",
        VideoEffect.RippleTouch => "Rings spread from the pointer and bend the picture like water. Away from the picture they spread from the centre.",
        VideoEffect.Torchlight => "The picture goes dark except for a torch beam that follows the pointer.",
        VideoEffect.MotionIsolate => "Only what moves keeps its colour; the still scene fades to dim grey. It compares pictures, it does not recognise objects.",
        VideoEffect.CelShade => "Shading flattened into a few bands with ink lines on strong outlines, like a cartoon cel.",
        VideoEffect.TerrainMesh => "The picture as a wireframe landscape: bright areas rise as hills, lines take the picture colour.",
        VideoEffect.GravityWell => "The picture is pulled toward the pointer and twisted around it, fading with distance.",
        VideoEffect.ScanSweep => "A scanner line sweeps across; behind it the picture shows as heat, edges, a negative or an x-ray style. The pointer takes over the line.",
        VideoEffect.Shatter => "The picture breaks into glass shards that slide apart near the pointer and settle flush far from it.",
        VideoEffect.Parallax => "Sharp detail is treated as near and soft areas as far; the layers slide with the pointer so the picture seems to turn. It is a guess from sharpness, not measured depth.",
        VideoEffect.RainGlass => "Drops run down a fogged pane in front of the picture; the pointer wipes the fog clear.",
        VideoEffect.ZoomBurst => "Streaks race outward from the pointer, longer further out, like a zoom pulled mid exposure.",
        VideoEffect.LowPoly => "The picture as a mesh of flat triangles, each lit as a facet.",
        VideoEffect.Pinball => "A glowing ball bounces around the frame, squashing and flaring when it crosses a strong outline, pushing the picture aside and leaving a trail. The pointer is a bumper.",
        VideoEffect.Fireflies => "Lights drift toward the brightest nearby spot, so over time they gather on highlights while the picture dims to dusk.",
        VideoEffect.ElectricEdges => "Arcs crawl along the outlines nearest the pointer and the picture dims around them.",
        VideoEffect.TimeLens => "A round window at the pointer shows the picture a moment ago, so moving things lag inside it and the rim glows where they differ.",
        VideoEffect.Melt => "The picture runs downward like wax, each column at its own pace, bright areas furthest.",
        VideoEffect.Jigsaw => "The picture cut into jigsaw pieces; pieces near the pointer lift off the board with a shadow.",
        VideoEffect.ColourMagnet => "The pointer picks the colour under it and everything of that colour is drawn toward it and glows.",
        VideoEffect.SonarPing => "A ping spreads from the pointer; outlines light up as the ring crosses them and fade behind it.",
        VideoEffect.Bokeh => "Soft areas go out of focus and their highlights bloom into lens discs; crisp detail stays sharp. Depth is a guess from sharpness.",
        VideoEffect.InkSpill => "Ink spreads from the pointer and floods the darker areas first, feeling its way around bright ones.",
        VideoEffect.MotionOutline => "Marching ants around whatever moved since the previous picture, like a live selection. A still picture shows none.",
        VideoEffect.HeatHaze => "Air shimmers above the brightest parts of the picture.",
        VideoEffect.Confetti => "Confetti tumbles across the picture, catching its light; near the pointer it is blown aside.",
        _ => "",
    };

    /// <summary>The pointer-driven effects read pointer coordinates, never persisted or recorded.</summary>
    public static bool UsesPointer(VideoEffect effect) => effect is VideoEffect.CursorLens or VideoEffect.ColourSpotlight
        or VideoEffect.RippleTouch or VideoEffect.Torchlight or VideoEffect.GravityWell or VideoEffect.ScanSweep
        or VideoEffect.Shatter or VideoEffect.Parallax or VideoEffect.RainGlass or VideoEffect.ZoomBurst
        or VideoEffect.Pinball or VideoEffect.ElectricEdges or VideoEffect.TimeLens or VideoEffect.Jigsaw
        or VideoEffect.ColourMagnet or VideoEffect.SonarPing or VideoEffect.InkSpill or VideoEffect.Confetti;

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
        VideoEffect.ParticleVeil or VideoEffect.TimeEcho or VideoEffect.MotionAmplify or VideoEffect.FlowField or VideoEffect.MotionIsolate
            or VideoEffect.TimeLens or VideoEffect.MotionOutline => true,
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
