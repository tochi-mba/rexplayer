using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// The named looks kinetic captions come in (OSD-11), each a different kind of video, not a
/// different colour of the same one: a creator's cut, a sing-along line that lights as it is sung,
/// a terminal typing itself out, a neon sign one word at a time, newspaper headlines, comic
/// speech bubbles, the barest words, confetti at a rave, classic subtitles with the spoken word
/// lit, and a glitching feed. The user's energy, glyphs, picture avoidance and subtitle colour
/// still apply over any of them; Custom is their own mix.
/// </summary>
public static class CaptionPresets
{
    private static readonly Argb White = new(255, 255, 255, 255);
    private static readonly Argb Ink = new(255, 20, 20, 24);

    /// <summary>The look of <paramref name="preset"/> with the user's <paramref name="own"/> choices laid over it, or their own look for Custom.</summary>
    public static CaptionStyle Style(CaptionPreset preset, CaptionStyle own)
    {
        ArgumentNullException.ThrowIfNull(own);
        var style = preset switch
        {
            // The whole line waits, dim, and each word lights and bounces as it is sung.
            CaptionPreset.Karaoke => new CaptionStyle(White, Rgb(0xFF4FA3), VisualPalette.Accent, 1, CaptionLayout.Stack, false, false, Font: CaptionFont.Rounded, Case: CaptionCase.Upper, Motion: CaptionMotion.Bounce, PlateAlways: true, Tilt: 0, Prefill: true, Flat: true),
            // A terminal in the corner: green monospace typing itself out behind a cursor.
            CaptionPreset.Typewriter => new CaptionStyle(Rgb(0xB6F58A), Rgb(0xE8FFD0), VisualPalette.Accent, 0.6, CaptionLayout.Left, false, false, Font: CaptionFont.Mono, Case: CaptionCase.Lower, Motion: CaptionMotion.Typewriter, PlateAlways: true, Tilt: 0, Flat: true, Cursor: true),
            // One glowing word at a time in the middle, flickering as it lights.
            CaptionPreset.Neon => new CaptionStyle(Rgb(0xE0FBFF), Rgb(0x30E0FF), VisualPalette.Neon, 1.4, CaptionLayout.Centre, false, true, Case: CaptionCase.Upper, Flicker: true, Outline: 1.6, Tilt: 0, OneAtATime: true),
            // Black serif capitals on white, dropped in as a front page is laid out; the loudest in red.
            CaptionPreset.Headline => new CaptionStyle(Ink, Rgb(0xC8102E), VisualPalette.Accent, 0.9, CaptionLayout.Stack, false, true, Font: CaptionFont.Serif, Case: CaptionCase.Upper, Motion: CaptionMotion.Drop, PlateAlways: true, PlateLight: true, Tilt: 0, Outline: 0),
            // Speech bubbles: black poster letters on white, tilted, bouncing in; a shout in fire colours.
            CaptionPreset.Comic => new CaptionStyle(Ink, Rgb(0xFF5A00), VisualPalette.Fire, 1.3, CaptionLayout.Scatter, true, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Motion: CaptionMotion.Bounce, PlateAlways: true, PlateLight: true, Outline: 0.5, Tilt: 2.5),
            // Small lower-case words slide quietly in at the bottom; nothing shouts, nothing is coloured.
            CaptionPreset.Minimal => new CaptionStyle(White, White, VisualPalette.Accent, 0.5, CaptionLayout.Stack, false, false, Case: CaptionCase.Lower, Motion: CaptionMotion.Slide, Tilt: 0, Outline: 0.7, Flat: true),
            // Confetti: every word lands somewhere of its own, in a colour of its own, jittering and flickering.
            CaptionPreset.Rave => new CaptionStyle(White, Rgb(0xFF2BD6), VisualPalette.Rainbow, 1.6, CaptionLayout.Confetti, true, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Flicker: true, Tilt: 2, Jitter: 0.04),
            // A subtitle line as ever, the spoken word lit in the accent colour.
            CaptionPreset.Classic => new CaptionStyle(White, Rgb(0xFFB400), VisualPalette.Accent, 0.6, CaptionLayout.Stack, false, false, PlateAlways: true, Tilt: 0, Prefill: true, Flat: true),
            // A feed breaking up: monospace capitals split into red and cyan, jittering, dropping in askew.
            CaptionPreset.Glitch => new CaptionStyle(Rgb(0xD8FFF4), Rgb(0xFF2B2B), VisualPalette.Cool, 1.2, CaptionLayout.Scatter, false, true, Font: CaptionFont.Mono, Case: CaptionCase.Upper, Motion: CaptionMotion.Drop, Flicker: true, Tilt: 3, Outline: 0.6, Split: true, Jitter: 0.025),
            // A news ticker: one steady line of capitals sliding along a dark bar at the bottom, every word the same size.
            CaptionPreset.Ticker => new CaptionStyle(White, Rgb(0xFFD400), VisualPalette.Accent, 0.6, CaptionLayout.Ticker, false, false, Case: CaptionCase.Upper, Motion: CaptionMotion.Fade, PlateAlways: true, Tilt: 0, Flat: true),
            // Ghosts: pale serif words fade up in the middle, then drift upward and dissolve of their own accord.
            CaptionPreset.Ghost => new CaptionStyle(Rgb(0xEDEDF5), Rgb(0xEDEDF5), VisualPalette.Accent, 0.5, CaptionLayout.Centre, false, false, Font: CaptionFont.Serif, Case: CaptionCase.Lower, Motion: CaptionMotion.Fade, Tilt: 0, Outline: 0.5, Flat: true, Dissolve: 2.2),
            // Rubber stamps: red poster capitals slam down from far too big, each a little askew.
            CaptionPreset.Stamp => new CaptionStyle(Rgb(0xB3101E), Rgb(0x1A1A1A), VisualPalette.Accent, 1.3, CaptionLayout.Scatter, false, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Motion: CaptionMotion.Stamp, Outline: 1.3, Tilt: 3),
            // Pills: every word in a soft pastel pill of its own colour, ink on it, bouncing in at the bottom.
            CaptionPreset.Pills => new CaptionStyle(Ink, Rgb(0xFF6FA3), VisualPalette.Rainbow, 0.9, CaptionLayout.Stack, true, false, Font: CaptionFont.Rounded, Motion: CaptionMotion.Bounce, PlateAlways: true, Tilt: 0, Outline: 0, PlateTint: true),
            // Marker: handwriting over a yellow highlighter, written out letter by letter, a little askew.
            CaptionPreset.Marker => new CaptionStyle(Ink, Rgb(0xFFF176), VisualPalette.Fire, 1, CaptionLayout.Scatter, false, false, Font: CaptionFont.Hand, Motion: CaptionMotion.Typewriter, PlateAlways: true, Tilt: 1.5, Outline: 0, PlateTint: true),
            // Stairs: rounded capitals climb a staircase from the lower left, sliding in step by step.
            CaptionPreset.Stairs => new CaptionStyle(White, Rgb(0x7CE0FF), VisualPalette.Cool, 1, CaptionLayout.Stairs, false, true, Font: CaptionFont.Rounded, Case: CaptionCase.Upper, Motion: CaptionMotion.Slide, Tilt: 0, Outline: 1.2),
            // Wire: outlined capitals only, hollow, until a stressed word fills in solid.
            CaptionPreset.Wire => new CaptionStyle(White, Rgb(0x00FFC8), VisualPalette.Neon, 1.2, CaptionLayout.Centre, false, true, Font: CaptionFont.Display, Case: CaptionCase.Upper, Hollow: true, Outline: 1.5, Tilt: 0),
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
        CaptionPreset.Ticker => "News ticker",
        CaptionPreset.Ghost => "Ghosts",
        CaptionPreset.Stamp => "Rubber stamp",
        CaptionPreset.Pills => "Pills",
        CaptionPreset.Marker => "Marker pen",
        CaptionPreset.Stairs => "Stairs",
        CaptionPreset.Wire => "Wire",
        CaptionPreset.Custom => "Custom",
        _ => "Creator cut",
    };

    /// <summary>A line on what a preset is like, for the settings dialog.</summary>
    public static string Describe(CaptionPreset preset) => preset switch
    {
        CaptionPreset.Karaoke => "The whole line waits at the bottom, dim; each word lights and bounces as it is sung.",
        CaptionPreset.Typewriter => "A terminal in the corner: green lower-case letters type themselves out behind a cursor.",
        CaptionPreset.Neon => "One glowing neon word at a time in the middle of the picture, flickering as it lights.",
        CaptionPreset.Headline => "Black serif capitals on white, dropped in as a front page is laid out; the loudest in red.",
        CaptionPreset.Comic => "Speech bubbles: black poster letters on white, tilted, bouncing in; a shout in fire colours.",
        CaptionPreset.Minimal => "Small lower-case words slide quietly in at the bottom; nothing shouts, nothing is coloured.",
        CaptionPreset.Rave => "Confetti: every word lands somewhere of its own in a colour of its own, jittering and flickering.",
        CaptionPreset.Classic => "A subtitle line as ever, the word being said lit in the accent colour.",
        CaptionPreset.Glitch => "A feed breaking up: monospace capitals split into red and cyan, jittering, dropping in askew.",
        CaptionPreset.Ticker => "A news ticker: one steady line of capitals sliding along a dark bar at the bottom.",
        CaptionPreset.Ghost => "Pale serif words fade up in the middle, then drift upward and dissolve on their own.",
        CaptionPreset.Stamp => "Red poster capitals slam down like rubber stamps, each a little askew.",
        CaptionPreset.Pills => "Every word in a soft pastel pill of its own colour, bouncing in at the bottom.",
        CaptionPreset.Marker => "Handwriting over a yellow highlighter, written out letter by letter.",
        CaptionPreset.Stairs => "Rounded capitals climb a staircase from the lower left, one step a word.",
        CaptionPreset.Wire => "Outlined capitals only, hollow, until a stressed word fills in solid.",
        CaptionPreset.Custom => "Your own mix of the choices below.",
        _ => "The creator's cut: words scattered where the picture is clear, shouts huge in capitals, a lift in the voice in gold italics.",
    };

    private static Argb Rgb(int rgb) => Argb.From(rgb, 100);
}
