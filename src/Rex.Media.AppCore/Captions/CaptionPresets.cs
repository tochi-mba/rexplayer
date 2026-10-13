using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// The named looks kinetic captions come in (OSD-11), each a different kind of video: a creator's
/// cut, a sing-along, a typewriter, a neon sign, a headline, a comic, the barest words, a rave,
/// classic subtitles with the spoken word lit, and a glitch. The user's energy, glyphs, picture
/// avoidance and subtitle colour still apply over any of them; Custom is their own mix.
/// </summary>
public static class CaptionPresets
{
    private static readonly Argb White = new(255, 255, 255, 255);

    /// <summary>The look of <paramref name="preset"/> with the user's <paramref name="own"/> choices laid over it, or their own look for Custom.</summary>
    public static CaptionStyle Style(CaptionPreset preset, CaptionStyle own)
    {
        ArgumentNullException.ThrowIfNull(own);
        var style = preset switch
        {
            CaptionPreset.Karaoke => new CaptionStyle(White, Rgb(0xFF4FA3), VisualPalette.Accent, 1, CaptionLayout.Stack, true, true, Font: CaptionFont.Rounded, Case: CaptionCase.Upper, Motion: CaptionMotion.Bounce, Highlight: true, PlateAlways: true, Tilt: 0),
            CaptionPreset.Typewriter => new CaptionStyle(Rgb(0xE8F5D0), Rgb(0x9BE564), VisualPalette.Accent, 0.8, CaptionLayout.Centre, false, false, Font: CaptionFont.Mono, Case: CaptionCase.Lower, Motion: CaptionMotion.Typewriter, PlateAlways: true, Tilt: 0),
            CaptionPreset.Neon => new CaptionStyle(Rgb(0xE0FBFF), Rgb(0x30E0FF), VisualPalette.Neon, 1.1, CaptionLayout.Scatter, true, true, Flicker: true, Outline: 1.4, Tilt: 0.6),
            CaptionPreset.Headline => new CaptionStyle(White, Rgb(0xE0202A), VisualPalette.Accent, 0.9, CaptionLayout.Stack, false, true, Font: CaptionFont.Serif, Case: CaptionCase.Upper, Motion: CaptionMotion.Drop, PlateAlways: true, Tilt: 0),
            CaptionPreset.Comic => new CaptionStyle(Rgb(0xFFF6D5), Rgb(0xFFE600), VisualPalette.Fire, 1.3, CaptionLayout.Scatter, true, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Motion: CaptionMotion.Bounce, Outline: 1.8, Tilt: 2.5),
            CaptionPreset.Minimal => new CaptionStyle(White, White, VisualPalette.Accent, 0.5, CaptionLayout.Centre, false, false, Case: CaptionCase.Lower, Motion: CaptionMotion.Slide, Tilt: 0, Outline: 0.7),
            CaptionPreset.Rave => new CaptionStyle(White, Rgb(0xFF2BD6), VisualPalette.Rainbow, 1.6, CaptionLayout.Scatter, true, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Flicker: true, Tilt: 2),
            CaptionPreset.Classic => new CaptionStyle(White, Rgb(0xFFB400), VisualPalette.Accent, 0.6, CaptionLayout.Stack, false, false, Motion: CaptionMotion.Slide, Highlight: true, PlateAlways: true, Tilt: 0),
            CaptionPreset.Glitch => new CaptionStyle(Rgb(0xD8FFF4), Rgb(0xFF2B2B), VisualPalette.Cool, 1.2, CaptionLayout.Scatter, true, true, Font: CaptionFont.Mono, Case: CaptionCase.Upper, Motion: CaptionMotion.Drop, Flicker: true, Tilt: 3, Outline: 0.6),
            CaptionPreset.Custom => own,
            _ => CaptionStyle.Default,
        };
        return preset == CaptionPreset.Custom ? own : style with
        {
            Energy = Math.Clamp(style.Energy * own.Energy, 0.25, 3),
            Glyphs = style.Glyphs && own.Glyphs,
            AvoidPicture = own.AvoidPicture,
        };
    }

    /// <summary>How a preset is named in menus and messages.</summary>
    public static string Name(CaptionPreset preset) => preset switch
    {
        CaptionPreset.Karaoke => "Sing-along",
        CaptionPreset.Typewriter => "Typewriter",
        CaptionPreset.Neon => "Neon sign",
        CaptionPreset.Headline => "Headline",
        CaptionPreset.Comic => "Comic",
        CaptionPreset.Minimal => "Minimal",
        CaptionPreset.Rave => "Rave",
        CaptionPreset.Classic => "Classic, word lit",
        CaptionPreset.Glitch => "Glitch",
        CaptionPreset.Custom => "Custom",
        _ => "Creator cut",
    };

    /// <summary>A line on what a preset is like, for the settings dialog.</summary>
    public static string Describe(CaptionPreset preset) => preset switch
    {
        CaptionPreset.Karaoke => "Rounded capitals bounce in on a dark plate at the bottom, the word being sung lit and the rest dimmed.",
        CaptionPreset.Typewriter => "Lower-case monospace letters type themselves out in the middle, green on black.",
        CaptionPreset.Neon => "Glowing neon colours scattered over the picture; a shout flickers as it lights.",
        CaptionPreset.Headline => "Serif capitals drop in as front-page headlines, red for the loudest.",
        CaptionPreset.Comic => "Heavy poster letters with thick outlines, tilted, bouncing in, fire colours for a shout.",
        CaptionPreset.Minimal => "Small lower-case words slide quietly into the middle; nothing shouts.",
        CaptionPreset.Rave => "Rainbow capitals, big and tilted, flickering, moved hard by the voice.",
        CaptionPreset.Classic => "Ordinary subtitles at the bottom, the spoken word lit as it is said.",
        CaptionPreset.Glitch => "Monospace capitals in cold colours drop in askew and flicker.",
        CaptionPreset.Custom => "Your own mix of the choices below.",
        _ => "The creator's cut: words scattered where the picture is clear, shouts huge in capitals, a lift in the voice in gold italics.",
    };

    private static Argb Rgb(int rgb) => Argb.From(rgb, 100);
}
