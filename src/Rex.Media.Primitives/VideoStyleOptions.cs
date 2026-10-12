namespace Rex.Media.Primitives;

/// <summary>A numeric option for one GPU picture look or effect, with a stable persistent key.</summary>
public sealed record VideoStyleOption(string Key, string Label, int Default, int Min, int Max);

/// <summary>
/// Controls shared with the video menu and settings. Each look and effect has its own keys:
/// switching styles restores the previously customized values rather than sharing one slider.
/// </summary>
public static class VideoStyleOptions
{
    public static IReadOnlyList<VideoStyleOption> ForLook(VideoLook look)
    {
        var name = Enum.IsDefined(look) ? look.ToString() : VideoLook.Original.ToString();
        return
        [
            new($"look.{name}.intensity", look == VideoLook.Original ? "Brightness (%)" : "Look intensity (%)", 100, 0, 150),
            new($"look.{name}.detail", look == VideoLook.Original ? "Contrast (%)" : "Colour richness (%)", 100, 50, 150),
        ];
    }

    public static IReadOnlyList<VideoStyleOption> ForEffect(VideoEffect effect)
    {
        effect = Enum.IsDefined(effect) ? effect : VideoEffect.Off;
        var name = effect.ToString();
        var animated = effect is VideoEffect.PrismFlow or VideoEffect.PixelDrift or VideoEffect.Kaleidoscope
            or VideoEffect.LiquidGlass or VideoEffect.SliceShift or VideoEffect.Vortex or VideoEffect.EdgeGravity;
        var detailLabel = effect switch
        {
            VideoEffect.Ghostwire => "Filament sensitivity (%)",
            VideoEffect.GhostwireMotion => "Contour sensitivity (%)",
            VideoEffect.GhostwireMask => "Inner contour sensitivity (%)",
            VideoEffect.NeonEdges or VideoEffect.InkTrace or VideoEffect.TopographicContours
                or VideoEffect.ChromaticContours => "Contour sensitivity (%)",
            VideoEffect.ColourSpotlight => "Colour selectivity (%)",
            VideoEffect.ReliefEtch => "Surface relief (%)",
            VideoEffect.Halftone => "Dot size (%)",
            VideoEffect.ChromaticRift => "Rift distance (%)",
            VideoEffect.OilPaint => "Brush radius (%)",
            VideoEffect.GlyphMosaic => "Cell size (%)",
            VideoEffect.ThermalMap => "Edge emphasis (%)",
            VideoEffect.TapeWear => "Tracking noise (%)",
            VideoEffect.PixelSort => "Brightness threshold (%)",
            VideoEffect.MotionSmear => "Smear threshold (%)",
            _ => animated ? "Movement (%)" : "Fine detail (%)",
        };
        var options = new List<VideoStyleOption>
        {
            new($"effect.{name}.intensity", effect == VideoEffect.GhostwireMotion ? "Motion emphasis (%)" : "Effect intensity (%)",
                effect == VideoEffect.Off ? 0 : effect is VideoEffect.Ghostwire or VideoEffect.GhostwireMask ? 96 : 65, 0, 100),
            new($"effect.{name}.detail", detailLabel, 100, 25, 175),
        };

        // The third and fourth controls reach the shader as they are; a ".mode" key is a named
        // choice (see ModeNames) and everything else a number. Saved values are kept within 0-200.
        switch (effect)
        {
            case VideoEffect.GhostwireMotion:
                options.Add(new($"effect.{name}.trails", "Motion trail length (%)", 35, 0, 100));
                options.Add(new($"effect.{name}.mode", "Visualization mode", 3, 0, 3));
                break;
            case VideoEffect.Kaleidoscope:
                options.Add(new($"effect.{name}.folds", "Mirror folds", 3, 2, 12));
                options.Add(new($"effect.{name}.drift", "Rotation drift (%)", 10, 0, 100));
                break;
            case VideoEffect.Halftone:
                options.Add(new($"effect.{name}.angle", "Screen angle (degrees)", 15, 0, 90));
                options.Add(new($"effect.{name}.ink", "Ink gain (%)", 60, 0, 100));
                break;
            case VideoEffect.ChromaticRift:
                options.Add(new($"effect.{name}.angle", "Rift direction (degrees)", 0, 0, 180));
                options.Add(new($"effect.{name}.motion", "Motion drive (%)", 0, 0, 100));
                break;
            case VideoEffect.OilPaint:
                options.Add(new($"effect.{name}.sectors", "Brush sectors", 4, 4, 8));
                options.Add(new($"effect.{name}.sharpness", "Edge sharpness (%)", 50, 0, 100));
                break;
            case VideoEffect.GlyphMosaic:
                options.Add(new($"effect.{name}.contrast", "Glyph contrast (%)", 70, 0, 100));
                options.Add(new($"effect.{name}.mode", "Palette", 0, 0, 3));
                break;
            case VideoEffect.ThermalMap:
                options.Add(new($"effect.{name}.range", "Heat range (%)", 100, 50, 150));
                options.Add(new($"effect.{name}.mode", "Gradient", 0, 0, 3));
                break;
            case VideoEffect.TapeWear:
                options.Add(new($"effect.{name}.bleed", "Chroma bleed (%)", 50, 0, 100));
                options.Add(new($"effect.{name}.band", "Head-switch band (%)", 40, 0, 100));
                break;
            case VideoEffect.PixelSort:
                options.Add(new($"effect.{name}.span", "Sort span (%)", 50, 10, 100));
                options.Add(new($"effect.{name}.mode", "Sort direction", 0, 0, 3));
                break;
            case VideoEffect.MotionSmear:
                options.Add(new($"effect.{name}.decay", "Smear decay (%)", 60, 0, 100));
                options.Add(new($"effect.{name}.blocks", "Block size (%)", 40, 0, 100));
                break;
            default:
                break;
        }

        return options;
    }

    /// <summary>The named choices of an effect's ".mode" option, in value order; empty when it has none.</summary>
    public static IReadOnlyList<string> ModeNames(VideoEffect effect) => effect switch
    {
        VideoEffect.GhostwireMotion => ["Hybrid (contours + movement)", "Contours only", "Motion only", "Surface shape (still-friendly)"],
        VideoEffect.GlyphMosaic => ["Phosphor green", "Amber", "Paper", "Source colour"],
        VideoEffect.ThermalMap => ["Iron", "Rainbow", "Arctic", "White hot"],
        VideoEffect.PixelSort => ["Down", "Up", "Right", "Left"],
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

    public static IReadOnlyDictionary<string, int> Reset(IReadOnlyDictionary<string, int>? previous,
        IReadOnlyList<VideoStyleOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var updated = previous is null ? new Dictionary<string, int>(StringComparer.Ordinal)
            : previous.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var option in options)
        {
            updated.Remove(option.Key);
        }

        return updated;
    }
}
