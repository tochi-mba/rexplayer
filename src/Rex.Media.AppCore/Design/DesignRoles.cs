namespace Rex.Media.AppCore.Design;

/// <summary>What a colour is for, whatever the theme: the names both shells ask their theme for.</summary>
public enum DesignRole
{
    /// <summary>The window behind everything.</summary>
    Canvas,

    /// <summary>A panel on the canvas: the transport bar, a page, a pane.</summary>
    Surface,

    /// <summary>A control or card raised off a surface.</summary>
    Raised,

    /// <summary>A one-pixel line between two areas.</summary>
    Hairline,

    /// <summary>Headings and the text that is read first.</summary>
    TextPrimary,

    /// <summary>Supporting text.</summary>
    TextSecondary,

    /// <summary>Hints and text that may be passed over.</summary>
    TextTertiary,
}

/// <summary>
/// Which <see cref="DesignTokens"/> colour each <see cref="DesignRole"/> is in the dark and the
/// light theme, and the colours of the stage, which is always dark because the picture is. The
/// Windows shell spells these in App.xaml and the Mac shell reads them here; a test holds App.xaml
/// to this mapping, so the two shells cannot drift. Colours are 0xAARRGGBB.
/// </summary>
public static class DesignRoles
{
    /// <summary>The stage behind the picture.</summary>
    public const uint Stage = 0xFF000000;

    /// <summary>A panel on the stage that nothing behind it may show through.</summary>
    public static uint StageSurface => DesignTokens.DarkInk[0];

    /// <summary>A panel laid over the picture.</summary>
    public static uint Overlay => DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.OverlayAlpha);

    /// <summary>A panel over the picture that holds controls.</summary>
    public static uint StrongOverlay => DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.StrongOverlayAlpha);

    /// <summary>Text on the stage: primary.</summary>
    public static uint OverlayText => DesignTokens.DarkText[0];

    /// <summary>Text on the stage: secondary.</summary>
    public static uint OverlaySecondaryText => DesignTokens.DarkText[1];

    /// <summary>Text on the stage: tertiary.</summary>
    public static uint OverlayTertiaryText => DesignTokens.DarkText[2];

    /// <summary>The colour of <paramref name="role"/> in the dark or the light theme.</summary>
    public static uint Color(DesignRole role, bool dark) => role switch
    {
        DesignRole.Canvas => dark ? DesignTokens.DarkInk[1] : DesignTokens.LightInk[0],
        DesignRole.Surface => dark ? DesignTokens.DarkInk[2] : DesignTokens.LightInk[1],
        DesignRole.Raised => dark ? DesignTokens.DarkInk[3] : DesignTokens.LightInk[2],
        DesignRole.Hairline => dark ? DesignTokens.DarkInk[3] : DesignTokens.LightInk[3],
        DesignRole.TextPrimary => dark ? DesignTokens.DarkText[0] : DesignTokens.LightText[0],
        DesignRole.TextSecondary => dark ? DesignTokens.DarkText[1] : DesignTokens.LightText[1],
        DesignRole.TextTertiary => dark ? DesignTokens.DarkText[2] : DesignTokens.LightText[2],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
