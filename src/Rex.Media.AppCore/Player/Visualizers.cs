using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>The visualisations' names, their order, and the spectrogram's colours (AU-18).</summary>
public static class Visualizers
{
    // Silence is black; louder runs through blue, magenta, red and yellow to white at full scale.
    private static readonly (float At, Argb Color)[] Heats =
    [
        (0f, new Argb(255, 0, 0, 0)),
        (0.25f, new Argb(255, 0, 0, 140)),
        (0.5f, new Argb(255, 180, 0, 160)),
        (0.7f, new Argb(255, 240, 40, 0)),
        (0.88f, new Argb(255, 255, 210, 0)),
        (1f, new Argb(255, 255, 255, 255)),
    ];

    /// <summary>The one after <paramref name="current"/>, back to off after the last.</summary>
    public static VisualizerChoice Next(VisualizerChoice current) =>
        Enum.IsDefined(current) && current != Enum.GetValues<VisualizerChoice>()[^1] ? current + 1 : VisualizerChoice.Off;

    public static string Name(VisualizerChoice choice) => choice switch
    {
        VisualizerChoice.Spectrum => "Spectrum",
        VisualizerChoice.Oscilloscope => "Oscilloscope",
        VisualizerChoice.Meters => "Level meters",
        VisualizerChoice.Spectrogram => "Spectrogram",
        VisualizerChoice.Vinyl => "Vinyl",
        VisualizerChoice.Halo => "Halo",
        VisualizerChoice.Mirror => "Mirror wave",
        VisualizerChoice.Aurora => "Aurora",
        VisualizerChoice.Embers => "Embers",
        VisualizerChoice.Ripples => "Ripples",
        VisualizerChoice.Strobe => "Strobe",
        VisualizerChoice.Silhouette => "Camera silhouette",
        _ => "No visualisation",
    };

    /// <summary>The spectrogram's colour for a level from 0 (silence) to 1 (full scale).</summary>
    public static Argb Heat(float level)
    {
        level = float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        var upper = 1;
        while (Heats[upper].At < level)
        {
            upper++;
        }

        var (from, to) = (Heats[upper - 1], Heats[upper]);
        var share = (level - from.At) / (to.At - from.At);
        return new Argb(255, Mix(from.Color.R, to.Color.R, share), Mix(from.Color.G, to.Color.G, share), Mix(from.Color.B, to.Color.B, share));
    }

    /// <summary>
    /// A colour of <paramref name="palette"/>: <paramref name="share"/> (0 to 1) is how far along
    /// the pitch, or a shape among its fellows; <paramref name="seconds"/> turns the rainbow.
    /// </summary>
    public static Argb PaletteColor(VisualPalette palette, double share, double seconds, Argb accent, int own) => palette switch
    {
        VisualPalette.Pitch => Hsv(270 * share, 1, 1),
        VisualPalette.Rainbow => Hsv((360 * share) + (seconds * 18), 1, 1),
        VisualPalette.Warm => Hsv(5 + (40 * share), 1, 1),
        VisualPalette.Cool => Hsv(190 + (90 * share), 0.85, 1),
        VisualPalette.OneColor => new Argb(255, (byte)(own >> 16), (byte)(own >> 8), (byte)own),
        _ => accent,
    };

    /// <summary>The band carrying the most energy, for colours that follow the pitch; 0 in silence.</summary>
    public static int DominantBand(IReadOnlyList<float> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var band = 0;
        for (var i = 1; i < levels.Count; i++)
        {
            if (levels[i] > levels[band])
            {
                band = i;
            }
        }

        return band;
    }

    /// <summary>A colour from its hue (degrees), saturation and value, each brought into range; what is not a number is black.</summary>
    public static Argb Hsv(double hue, double saturation, double value)
    {
        hue = double.IsFinite(hue) ? ((hue % 360) + 360) % 360 : 0;
        saturation = double.IsFinite(saturation) ? Math.Clamp(saturation, 0, 1) : 0;
        value = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var (r, g, b) = ((int)(hue / 60)) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        var floor = value - c;
        return new Argb(255, (byte)Math.Round((r + floor) * 255), (byte)Math.Round((g + floor) * 255), (byte)Math.Round((b + floor) * 255));
    }

    private static byte Mix(byte from, byte to, float share) => (byte)Math.Round(from + ((to - from) * share));
}
