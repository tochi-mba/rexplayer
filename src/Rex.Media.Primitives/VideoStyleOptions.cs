namespace Rex.Media.Primitives;

/// <summary>
/// A numeric option for one GPU picture look or effect, with a stable persistent key and a plain
/// sentence saying what it changes (shown as its tooltip).
/// </summary>
public sealed record VideoStyleOption(string Key, string Label, int Default, int Min, int Max, string Help = "");

/// <summary>An effect's four controls as the renderer receives them.</summary>
public readonly record struct EffectControls(int Intensity, int Detail, int Third, int Fourth);

/// <summary>
/// Controls shared with the video menu and settings. Each look and effect has its own keys:
/// switching styles restores the previously customized values rather than sharing one slider.
/// </summary>
public static class VideoStyleOptions
{
    public static IReadOnlyList<VideoStyleOption> ForLook(VideoLook look)
    {
        var name = Enum.IsDefined(look) ? look.ToString() : VideoLook.Original.ToString();
        return look == VideoLook.Original || !Enum.IsDefined(look)
            ?
            [
                new($"look.{name}.intensity", "Brightness (%)", 100, 0, 150, "Lifts or lowers the whole picture's brightness."),
                new($"look.{name}.detail", "Contrast (%)", 100, 50, 150, "Spreads or squeezes the picture's tones around mid grey."),
            ]
            :
            [
                new($"look.{name}.intensity", "Look intensity (%)", 100, 0, 150, "How strongly the look's colours replace the original's."),
                new($"look.{name}.detail", "Colour richness (%)", 100, 50, 150, "Saturation after the look: lower is greyer, higher is more vivid."),
            ];
    }

    /// <summary>The effect's controls in order: intensity, detail, then up to two of its own. Off has none.</summary>
    public static IReadOnlyList<VideoStyleOption> ForEffect(VideoEffect effect)
    {
        if (effect == VideoEffect.Off || !Enum.IsDefined(effect))
        {
            return [];
        }

        var name = effect.ToString();
        var (detailLabel, detailHelp) = effect switch
        {
            VideoEffect.Ghostwire => ("Filament sensitivity (%)", "Higher finds fainter outlines and folds; lower keeps only clear ones. Grain stays out either way."),
            VideoEffect.GhostwireMask => ("Inner contour sensitivity (%)", "Higher finds fainter outlines inside shapes; lower keeps only clear ones."),
            VideoEffect.NeonEdges or VideoEffect.InkTrace or VideoEffect.ChromaticContours or VideoEffect.GhostwireMotion
                => ("Contour sensitivity (%)", "Higher finds fainter outlines; lower keeps only clear ones. Grain stays out either way."),
            VideoEffect.TopographicContours => ("Line density (%)", "How many brightness levels get a contour line."),
            VideoEffect.Kaleidoscope => ("Mirror zoom (%)", "Higher magnifies the mirrored centre; lower shows more of the picture in each wedge."),
            VideoEffect.CursorLens => ("Lens size (%)", "The size of the lens under the pointer."),
            VideoEffect.ColourSpotlight => ("Colour selectivity (%)", "Higher highlights only colours very close to the one under the pointer."),
            VideoEffect.ReliefEtch => ("Surface relief (%)", "How deep the carved light and shade look."),
            VideoEffect.Halftone => ("Dot size (%)", "The size of the printed dot grid."),
            VideoEffect.ChromaticRift => ("Rift distance (%)", "How far red and blue move apart."),
            VideoEffect.OilPaint => ("Brush radius (%)", "The size of each brush stroke."),
            VideoEffect.GlyphMosaic => ("Cell size (%)", "The size of each character cell."),
            VideoEffect.ThermalMap => ("Edge emphasis (%)", "How brightly outlines glow over the heat colours; the lowest setting shows none."),
            VideoEffect.TapeWear => ("Tracking noise (%)", "How far the lines wobble sideways."),
            VideoEffect.PixelSort => ("Brightness threshold (%)", "Only pixels brighter than this start a streak."),
            VideoEffect.MotionSmear => ("Smear threshold (%)", "How much the picture must change before the previous picture lingers there."),
            VideoEffect.LumenWeave => ("Strand sensitivity (%)", "Higher turns fainter outlines into strands; lower keeps only clear ones."),
            VideoEffect.DepthHaze => ("Near detail (%)", "Higher counts finer texture as near, so less of the picture fades into haze."),
            VideoEffect.ParticleVeil => ("Spark threshold (%)", "How much a place must change from the previous picture before sparks rise from it."),
            VideoEffect.LensBloom => ("Bloom sensitivity (%)", "Higher lets dimmer highlights glow; lower keeps the glow to the brightest lights."),
            VideoEffect.StippleEngrave => ("Line weight (%)", "How thick the engraved lines are, and how readily outlines are inked."),
            VideoEffect.TimeEcho => ("Echo threshold (%)", "How much a place must change from the previous picture before it splits into echoes."),
            VideoEffect.BrickBuild => ("Brick size (%)", "The size of each brick: higher gives fewer, bigger bricks."),
            VideoEffect.StainedGlass => ("Pane size (%)", "The size of each glass pane: higher gives fewer, bigger panes."),
            VideoEffect.CrossStitch => ("Stitch size (%)", "The size of each stitch: higher gives fewer, bigger crosses."),
            VideoEffect.LedWall => ("LED size (%)", "The size of each light: higher gives fewer, bigger lights."),
            VideoEffect.DigitalRain => ("Glyph size (%)", "The size of each falling character."),
            VideoEffect.DiceMosaic => ("Die size (%)", "The size of each die: higher gives fewer, bigger dice."),
            VideoEffect.CubeMosaic => ("Cube size (%)", "The size of each cube of nine stickers."),
            VideoEffect.HexTiles => ("Tile size (%)", "The size of each hexagonal tile."),
            VideoEffect.RetroPixels => ("Pixel size (%)", "The size of each big pixel."),
            VideoEffect.MotionAmplify => ("Motion reach (%)", "How far movement is looked for between two pictures; higher follows faster movement."),
            _ => ("Movement (%)", "How fast the effect moves with the video's time."),
        };
        var options = new List<VideoStyleOption>
        {
            new($"effect.{name}.intensity", "Effect intensity (%)", DefaultIntensity(effect), 0, 100,
                "How strongly the effect changes the picture; 0 shows the original."),
            new($"effect.{name}.detail", detailLabel, 100, 25, 175, detailHelp),
        };

        // The third and fourth controls reach the shader as they are; a ".mode" key is a named
        // choice (see ModeNames) and everything else a number. Saved values are kept within 0-200.
        switch (effect)
        {
            case VideoEffect.GhostwireMotion:
                options.Add(new($"effect.{name}.trails", "Motion trail length (%)", 35, 0, 100, "How strongly the previous position of moving outlines lingers (hybrid and motion modes)."));
                options.Add(new($"effect.{name}.mode", "Visualization mode", 3, 0, 3, "What the effect draws: contours with movement, contours, movement, or surface shape."));
                break;
            case VideoEffect.Kaleidoscope:
                options.Add(new($"effect.{name}.folds", "Mirror folds", 3, 2, 12, "How many mirrored pairs of wedges go round the centre."));
                options.Add(new($"effect.{name}.drift", "Rotation speed (%)", 10, 0, 100, "How fast the wedges turn with the video's time; 0 holds them still."));
                break;
            case VideoEffect.Halftone:
                options.Add(new($"effect.{name}.angle", "Screen angle (degrees)", 15, 0, 90, "The angle of the first ink's dot rows; the others follow 30 degrees apart."));
                options.Add(new($"effect.{name}.ink", "Dot gain (%)", 60, 0, 100, "How much the dots spread: higher closes dark areas up into solid ink."));
                break;
            case VideoEffect.ChromaticRift:
                options.Add(new($"effect.{name}.angle", "Rift direction (degrees)", 0, 0, 180, "The direction red and blue move apart: 0 is sideways, 90 up and down."));
                options.Add(new($"effect.{name}.motion", "Motion boost (%)", 0, 0, 100, "Widens the rift where the picture moves; above 0 it compares each picture with the one before."));
                break;
            case VideoEffect.OilPaint:
                options.Add(new($"effect.{name}.sectors", "Brush sectors", 4, 4, 8, "How many directions each stroke is measured in; more follows edges more closely but takes more work from the graphics card."));
                options.Add(new($"effect.{name}.sharpness", "Edge sharpness (%)", 60, 0, 100, "Higher keeps edges crisper; lower blends strokes more softly."));
                break;
            case VideoEffect.GlyphMosaic:
                options.Add(new($"effect.{name}.contrast", "Glyph contrast (%)", 70, 0, 100, "How strongly the characters stand out from the background."));
                options.Add(new($"effect.{name}.mode", "Palette", 0, 0, 3, "The colours of the characters and their background."));
                break;
            case VideoEffect.ThermalMap:
                options.Add(new($"effect.{name}.range", "Heat contrast (%)", 100, 50, 150, "Higher spreads brightness over more of the heat colours."));
                options.Add(new($"effect.{name}.mode", "Gradient", 0, 0, 3, "The heat colours used."));
                break;
            case VideoEffect.TapeWear:
                options.Add(new($"effect.{name}.bleed", "Colour bleed (%)", 50, 0, 100, "How far colour smears to the right of where it belongs."));
                options.Add(new($"effect.{name}.band", "Head-switching noise (%)", 40, 0, 100, "The torn, noisy band along the bottom of the picture."));
                break;
            case VideoEffect.PixelSort:
                options.Add(new($"effect.{name}.span", "Streak length (%)", 50, 10, 100, "How far bright pixels run."));
                options.Add(new($"effect.{name}.mode", "Streak direction", 0, 0, 3, "The direction the streaks run."));
                break;
            case VideoEffect.MotionSmear:
                options.Add(new($"effect.{name}.decay", "Smear amount (%)", 60, 0, 100, "How much of the previous picture lingers where things change; 0 turns the smear off."));
                options.Add(new($"effect.{name}.blocks", "Block count (%)", 40, 0, 100, "Higher breaks the smear into more, smaller blocks."));
                break;
            case VideoEffect.LumenWeave:
                options.Add(new($"effect.{name}.flow", "Strand flow (%)", 40, 0, 100, "How fast light travels along the strands; 0 holds it still."));
                options.Add(new($"effect.{name}.mode", "Strand colour", 0, 0, 3, "How the strands are coloured: by how much they curve, by the picture, in white, or by brightness."));
                break;
            case VideoEffect.DepthHaze:
                options.Add(new($"effect.{name}.density", "Haze density (%)", 60, 0, 100, "How thickly the haze covers the far parts of the picture."));
                options.Add(new($"effect.{name}.mode", "Haze colour", 0, 0, 3, "The colour of the haze."));
                break;
            case VideoEffect.ParticleVeil:
                options.Add(new($"effect.{name}.density", "Spark density (%)", 50, 0, 100, "How many sparks can rise at once; higher also makes them smaller."));
                options.Add(new($"effect.{name}.mode", "Spark colour", 0, 0, 3, "The colour of the sparks."));
                break;
            case VideoEffect.LensBloom:
                options.Add(new($"effect.{name}.stretch", "Streak length (%)", 60, 0, 100, "How far the glowing streaks reach sideways from each highlight."));
                options.Add(new($"effect.{name}.mode", "Bloom tint", 0, 0, 3, "The colour the streaks fade to."));
                break;
            case VideoEffect.StippleEngrave:
                options.Add(new($"effect.{name}.hatch", "Hatch density (%)", 60, 0, 100, "How close together the engraved lines are."));
                options.Add(new($"effect.{name}.mode", "Ink and paper", 0, 0, 3, "The colours of the ink and the paper."));
                break;
            case VideoEffect.TimeEcho:
                options.Add(new($"effect.{name}.separation", "Echo distance (%)", 50, 0, 100, "How far the coloured echoes sit from what moved."));
                options.Add(new($"effect.{name}.hold", "Echo strength (%)", 40, 0, 100, "How readily smaller changes split into echoes, and how strongly."));
                break;
            case VideoEffect.BrickBuild:
                options.Add(new($"effect.{name}.studs", "Stud height (%)", 60, 0, 100, "How raised the studs look: the light on their rims and the shadows they cast."));
                options.Add(new($"effect.{name}.mode", "Palette", 0, 0, 3, "The colours the bricks come in."));
                break;
            case VideoEffect.StainedGlass:
                options.Add(new($"effect.{name}.lead", "Lead width (%)", 40, 0, 100, "How wide the dark lead lines between the panes are."));
                options.Add(new($"effect.{name}.glow", "Back light (%)", 60, 0, 100, "How brightly light shines through the middle of each pane."));
                break;
            case VideoEffect.CrossStitch:
                options.Add(new($"effect.{name}.thread", "Thread thickness (%)", 50, 0, 100, "How thick each stitch of thread is."));
                options.Add(new($"effect.{name}.mode", "Fabric", 0, 0, 3, "The cloth the picture is stitched on."));
                break;
            case VideoEffect.LedWall:
                options.Add(new($"effect.{name}.glow", "Glow (%)", 50, 0, 100, "How far each light glows onto the panel around it."));
                options.Add(new($"effect.{name}.mode", "Diodes", 0, 0, 3, "The kind of lights the wall is built from."));
                break;
            case VideoEffect.DigitalRain:
                options.Add(new($"effect.{name}.speed", "Rain speed (%)", 50, 0, 100, "How fast the columns of characters fall."));
                options.Add(new($"effect.{name}.mode", "Colour", 0, 0, 3, "The colour of the falling characters."));
                break;
            case VideoEffect.DiceMosaic:
                options.Add(new($"effect.{name}.pips", "Pip size (%)", 50, 0, 100, "The size of the dots on each die."));
                options.Add(new($"effect.{name}.mode", "Dice", 0, 0, 3, "The colour of the dice and their dots."));
                break;
            case VideoEffect.CubeMosaic:
                options.Add(new($"effect.{name}.gap", "Sticker gap (%)", 40, 0, 100, "How much black plastic shows between the stickers."));
                options.Add(new($"effect.{name}.mode", "Stickers", 0, 0, 3, "The colours the stickers come in."));
                break;
            case VideoEffect.HexTiles:
                options.Add(new($"effect.{name}.grout", "Grout width (%)", 40, 0, 100, "How wide the grout between the tiles is."));
                options.Add(new($"effect.{name}.mode", "Finish", 0, 0, 3, "The surface of the tiles."));
                break;
            case VideoEffect.MotionAmplify:
                options.Add(new($"effect.{name}.gain", "Amplification (%)", 50, 0, 100, "How much further than it really moved each moving part is drawn."));
                options.Add(new($"effect.{name}.steadiness", "Steadiness (%)", 50, 0, 100, "Higher ignores small or uncertain movement, so grain and slight shake stay still."));
                break;
            case VideoEffect.RetroPixels:
                options.Add(new($"effect.{name}.dither", "Dither (%)", 70, 0, 100, "How much a pattern of dots blends neighbouring colours; 0 gives flat bands."));
                options.Add(new($"effect.{name}.mode", "Palette", 0, 0, 3, "The few colours the picture is drawn in."));
                break;
            default:
                break;
        }

        return options;
    }

    private static int DefaultIntensity(VideoEffect effect) => effect switch
    {
        VideoEffect.Ghostwire or VideoEffect.GhostwireMask => 96,
        VideoEffect.NeonEdges or VideoEffect.InkTrace or VideoEffect.ChromaticContours or VideoEffect.GhostwireMotion
            or VideoEffect.Halftone or VideoEffect.GlyphMosaic or VideoEffect.ThermalMap or VideoEffect.OilPaint => 90,
        _ => 65,
    };

    /// <summary>The named choices of an effect's ".mode" option, in value order; empty when it has none.</summary>
    public static IReadOnlyList<string> ModeNames(VideoEffect effect) => effect switch
    {
        VideoEffect.GhostwireMotion => ["Hybrid (contours + movement)", "Contours only", "Motion only", "Surface shape (still-friendly)"],
        VideoEffect.GlyphMosaic => ["Phosphor green", "Amber", "Paper", "Source colour"],
        VideoEffect.ThermalMap => ["Iron", "Rainbow", "Arctic", "White hot"],
        VideoEffect.PixelSort => ["Down", "Up", "Right", "Left"],
        VideoEffect.LumenWeave => ["Curvature", "Source colour", "Monochrome", "Depth cue"],
        VideoEffect.DepthHaze => ["Blue hour", "Warm dust", "Grey mist", "Green"],
        VideoEffect.ParticleVeil => ["Embers", "Ice", "Source colour", "White"],
        VideoEffect.LensBloom => ["Blue", "Warm", "Neutral", "Source colour"],
        VideoEffect.StippleEngrave => ["Copperplate", "Blue ink", "White on black", "Sepia"],
        VideoEffect.BrickBuild => ["Toy bricks", "Source colour", "Greyscale", "Pastel"],
        VideoEffect.CrossStitch => ["Cream", "Black", "Linen", "Denim"],
        VideoEffect.LedWall => ["RGB triads", "Round", "Amber", "Square pixels"],
        VideoEffect.DigitalRain => ["Green", "Cyan", "Amber", "Source colour"],
        VideoEffect.DiceMosaic => ["White dice", "Black dice", "Colour dice", "Red translucent"],
        VideoEffect.CubeMosaic => ["Six standard colours", "Greyscale", "Source colour", "Pastel"],
        VideoEffect.HexTiles => ["Glazed ceramic", "Matte stone", "Honeycomb", "Flat colour"],
        VideoEffect.RetroPixels => ["Handheld green", "Home computer", "Grey four-tone", "Cyan and magenta"],
        _ => [],
    };

    /// <summary>
    /// The values of the third and fourth controls, each its default when unsaved, so the
    /// renderer gets a pair for every effect (zeros for the effects that have only two).
    /// </summary>
    public static (int Third, int Fourth) Extras(IReadOnlyDictionary<string, int>? values, IReadOnlyList<VideoStyleOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (options.Count > 2 ? Read(values, options[2]) : 0, options.Count > 3 ? Read(values, options[3]) : 0);
    }

    /// <summary>The strength every effect shared before each had its own, when never changed.</summary>
    public const int SharedStrengthDefault = 65;

    /// <summary>
    /// What an effect's control is set to: the saved value; for an intensity never saved, the
    /// strength every effect shared before each had its own, when someone changed it (except the
    /// two Ghostwire styles, which always start from their own default); or else the default.
    /// </summary>
    public static int ReadEffect(VideoEffect effect, IReadOnlyDictionary<string, int>? values, VideoStyleOption option, int legacyStrength)
    {
        ArgumentNullException.ThrowIfNull(option);
        var inherits = option.Key.EndsWith(".intensity", StringComparison.Ordinal)
            && (values is null || !values.ContainsKey(option.Key))
            && legacyStrength != SharedStrengthDefault
            && effect is not (VideoEffect.Ghostwire or VideoEffect.GhostwireMask);
        return inherits ? Math.Clamp(legacyStrength, option.Min, option.Max) : Read(values, option);
    }

    /// <summary>All four controls of an effect as saved; Off gets no strength.</summary>
    public static EffectControls Controls(VideoEffect effect, IReadOnlyDictionary<string, int>? values, int legacyStrength)
    {
        var options = ForEffect(effect);
        if (options.Count == 0)
        {
            return new EffectControls(0, 100, 0, 0);
        }

        var (third, fourth) = Extras(values, options);
        return new EffectControls(ReadEffect(effect, values, options[0], legacyStrength), Read(values, options[1]), third, fourth);
    }

    public static int Read(IReadOnlyDictionary<string, int>? values, VideoStyleOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return values is not null && values.TryGetValue(option.Key, out var saved)
            ? Math.Clamp(saved, option.Min, option.Max)
            : option.Default;
    }

    public static IReadOnlyDictionary<string, int> With(
        IReadOnlyDictionary<string, int>? previous, VideoStyleOption option, int value)
    {
        ArgumentNullException.ThrowIfNull(option);
        var updated = previous is null ? new Dictionary<string, int>(StringComparer.Ordinal)
            : previous.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        updated[option.Key] = Math.Clamp(value, option.Min, option.Max);
        return updated;
    }

    /// <summary>
    /// Removes this style's saved values. An effect's intensity is saved at its default instead of
    /// removed, so a reset really shows the default rather than an older shared strength.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Reset(IReadOnlyDictionary<string, int>? previous,
        IReadOnlyList<VideoStyleOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var updated = previous is null ? new Dictionary<string, int>(StringComparer.Ordinal)
            : previous.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var option in options)
        {
            if (option.Key.StartsWith("effect.", StringComparison.Ordinal) && option.Key.EndsWith(".intensity", StringComparison.Ordinal))
            {
                updated[option.Key] = option.Default;
            }
            else
            {
                updated.Remove(option.Key);
            }
        }

        return updated;
    }
}
