namespace Rex.Media.AppCore.Design;

/// <summary>
/// The REX look in numbers: the ink and signal colours, corner radii, spacing, type sizes and motion
/// timings both shells draw with. The Windows shell projects these into <c>Theme/RexTheme.xaml</c> and
/// the Mac shell into its AppKit theme; tests hold both to these values, so the two cannot drift.
/// Colours are 0xAARRGGBB.
/// </summary>
public static class DesignTokens
{
    /// <summary>The REX signal: the playing entry, the seek bar's progress, the primary action.</summary>
    public const uint Signal = 0xFFD7FF3F;

    /// <summary>Text and icons drawn on <see cref="Signal"/>.</summary>
    public const uint OnSignal = 0xFF0B0F0C;

    /// <summary>The tool accent, kept for Subject Lock and other on-picture tools.</summary>
    public const uint Tool = 0xFF45ECD2;

    /// <summary>Attention and destructive actions.</summary>
    public const uint Attention = 0xFFFF7C60;

    /// <summary>The dark ink scale, deepest first: scrims, canvas, surface, raised.</summary>
    public static readonly uint[] DarkInk = [0xFF0B0F0C, 0xFF101713, 0xFF19221C, 0xFF232E26];

    /// <summary>The light ink scale, palest first: canvas, surface, raised, hairline.</summary>
    public static readonly uint[] LightInk = [0xFFF2F5F0, 0xFFFFFFFF, 0xFFE6EBE4, 0xFFD3DAD1];

    /// <summary>Text on dark ink: primary, secondary, tertiary.</summary>
    public static readonly uint[] DarkText = [0xFFF2F5F0, 0xFFB8C4BC, 0xFF8A968E];

    /// <summary>Text on light ink: primary, secondary, tertiary.</summary>
    public static readonly uint[] LightText = [0xFF0B0F0C, 0xFF3F4A42, 0xFF66716A];

    /// <summary>Corner radii: chip, small, card, page. Round controls use half their height.</summary>
    public static readonly double[] Radii = [4, 8, 12, 16];

    /// <summary>The spacing scale.</summary>
    public static readonly double[] Spacing = [4, 8, 12, 16, 24];

    /// <summary>Type sizes: micro, caption, body, section, title, display.</summary>
    public static readonly double[] TypeSizes = [10, 12, 14, 16, 20, 26];

    /// <summary>Letter spacing for all-caps kickers, in thousandths of an em.</summary>
    public const int KickerTracking = 120;

    /// <summary>Motion timings in milliseconds: fast, base, slow.</summary>
    public static readonly int[] MotionMilliseconds = [120, 200, 300];

    /// <summary>The opacity, 0 to 255, of an overlay laid over the picture.</summary>
    public const byte OverlayAlpha = 0xC0;

    /// <summary>The opacity of a panel over the picture that holds controls, not just a label.</summary>
    public const byte StrongOverlayAlpha = 0xF2;

    /// <summary>A faint fill over the picture, for a box drawn on it.</summary>
    public const uint OverlayFill = 0x22FFFFFF;

    /// <summary>A line over the picture: a panel's edge, a frame around a preview.</summary>
    public const uint OverlayHairline = 0x40FFFFFF;

    /// <summary>A tinted fill in the tool colour, behind a selection or a state pill.</summary>
    public const uint ToolFill = 0x2645ECD2;

    /// <summary>The steps of a scrim that fades the picture into text: clear, soft, medium, strong.</summary>
    public static readonly byte[] ScrimAlphas = [0x00, 0x40, 0x99, 0xE5];

    /// <summary><paramref name="color"/> with its alpha replaced by <paramref name="alpha"/>.</summary>
    public static uint WithAlpha(uint color, byte alpha) => (color & 0x00FFFFFF) | ((uint)alpha << 24);

    /// <summary>The colour written as #AARRGGBB, the way XAML and the tests spell it.</summary>
    public static string Hex(uint color) => $"#{color:X8}";

    /// <summary>The WCAG contrast ratio between two opaque colours, 1 to 21.</summary>
    public static double Contrast(uint a, uint b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint color) =>
        (0.2126 * Channel(color >> 16)) + (0.7152 * Channel(color >> 8)) + (0.0722 * Channel(color));

    private static double Channel(uint value)
    {
        var c = (value & 0xFF) / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
