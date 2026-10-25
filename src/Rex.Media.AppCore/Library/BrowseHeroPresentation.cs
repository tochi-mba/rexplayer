namespace Rex.Media.AppCore.Library;

/// <summary>
/// The non-negotiable accessibility rules for the photographic Windows Browse hero.
/// A high-contrast system palette takes precedence over thumbnail decoration and fades.
/// </summary>
public readonly record struct BrowseHeroPresentation(bool DrawsBackdrop, bool UsesSystemColors, bool FadesIn)
{
    /// <summary>Decides the presentation without a UI dependency, for pure tests.</summary>
    public static BrowseHeroPresentation For(bool highContrast, bool animationsAllowed) =>
        new(!highContrast, highContrast, !highContrast && animationsAllowed);
}
