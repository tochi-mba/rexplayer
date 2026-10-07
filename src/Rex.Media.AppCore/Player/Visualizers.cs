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

    private static byte Mix(byte from, byte to, float share) => (byte)Math.Round(from + ((to - from) * share));
}
