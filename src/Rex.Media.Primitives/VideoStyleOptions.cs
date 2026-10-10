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
            VideoEffect.Ghostwire => "Filament detail (%)",
            VideoEffect.ColourSpotlight => "Colour selectivity (%)",
            VideoEffect.ReliefEtch => "Surface relief (%)",
            _ => animated ? "Movement (%)" : "Fine detail (%)",
        };
        return
        [
            new($"effect.{name}.intensity", "Effect intensity (%)", effect == VideoEffect.Off ? 0 : effect == VideoEffect.Ghostwire ? 96 : 65, 0, 100),
            new($"effect.{name}.detail", detailLabel, 100, 25, 175),
        ];
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
