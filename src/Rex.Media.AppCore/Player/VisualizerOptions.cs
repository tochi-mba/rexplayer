using System.Globalization;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>The kinds of setting a visualisation has.</summary>
public enum VisualOptionKind
{
    Number,
    Toggle,
    Choice,
    Color,
}

/// <summary>
/// One setting of a visualisation (AU-18): its key, what the window calls it, its kind, and its
/// range or choices and default. Numbers keep to their range; a choice is its index.
/// </summary>
public sealed record VisualOption(string Key, string Label, VisualOptionKind Kind, double Default, double Minimum = 0, double Maximum = 1, IReadOnlyList<string>? Choices = null);

/// <summary>The colours a visualisation draws in.</summary>
public enum VisualPalette
{
    Accent,

    /// <summary>The colour follows the pitch: bass is red, treble violet.</summary>
    Pitch,
    Rainbow,
    Warm,
    Cool,

    /// <summary>One colour of the user's own (the "color" setting).</summary>
    OneColor,
}

/// <summary>
/// Every visualisation's own settings (AU-18), kept in <see cref="PlayerSettings.VisualOptions"/>
/// under "visualisation.key", with the defaults and ranges that make any stored value safe. A value
/// that is missing, out of range or unreadable reads as the default (or the nearest in range).
/// </summary>
public static class VisualizerOptions
{
    public const string Colors = "colors";
    public const string Color = "color";
    public const string Sensitivity = "sensitivity";

    private static readonly string[] PaletteNames = ["Windows' accent", "Follow the pitch", "A rainbow", "Warm", "Cool", "One colour"];

    /// <summary>The settings every visualisation has, then its own.</summary>
    public static IReadOnlyList<VisualOption> For(VisualizerChoice choice)
    {
        List<VisualOption> options =
        [
            new(Colors, "Colours", VisualOptionKind.Choice, choice == VisualizerChoice.Strobe ? (int)VisualPalette.Pitch : (int)VisualPalette.Accent, Choices: PaletteNames),
            new(Color, "Its own colour (with One colour)", VisualOptionKind.Color, 0xFF3B30),
            new(Sensitivity, "Sensitivity", VisualOptionKind.Number, 1, 0.25, 4),
        ];
        options.AddRange(choice switch
        {
            VisualizerChoice.Spectrum =>
            [
                new("bars", "Bars", VisualOptionKind.Number, 48, 12, 128),
                new("peaks", "Show the peaks", VisualOptionKind.Toggle, 1),
                new("mirrored", "Mirror about the middle", VisualOptionKind.Toggle, 0),
            ],
            VisualizerChoice.Oscilloscope => [new("thickness", "Line thickness", VisualOptionKind.Number, 2, 1, 8)],
            VisualizerChoice.Vinyl =>
            [
                new("speed", "Turns at", VisualOptionKind.Choice, 0, Choices: ["33 1/3 rpm", "45 rpm", "78 rpm"]),
                new("depth", "How deep the sound cuts the groove", VisualOptionKind.Number, 0.16, 0.04, 0.3),
                new("arm", "Show the tone-arm", VisualOptionKind.Toggle, 1),
                new("label", "The cover as the label", VisualOptionKind.Toggle, 1),
                new("sheen", "Moving light across the record", VisualOptionKind.Toggle, 1),
                new("glow", "Edge glow", VisualOptionKind.Number, 0.7, 0, 2),
            ],
            VisualizerChoice.Halo =>
            [
                new("rays", "Rays", VisualOptionKind.Number, 48, 12, 128),
                new("thickness", "Ray thickness", VisualOptionKind.Number, 4, 1, 12),
                new("spin", "Turn slowly", VisualOptionKind.Toggle, 1),
                new("trails", "Light trails", VisualOptionKind.Number, 0.72, 0, 0.95),
                new("burst", "Burst on drops", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Mirror =>
            [
                new("columns", "Columns", VisualOptionKind.Number, 96, 24, 192),
                new("thickness", "Wave thickness", VisualOptionKind.Number, 2, 1, 8),
                new("trails", "Light trails", VisualOptionKind.Number, 0.7, 0, 0.95),
                new("stereo", "Split left and right channels", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Aurora =>
            [
                new("glow", "Glow beneath the ridge", VisualOptionKind.Toggle, 1),
                new("bands", "Points along the ridge", VisualOptionKind.Number, 48, 12, 128),
                new("curtains", "Light curtains", VisualOptionKind.Number, 5, 1, 10),
                new("height", "Curtain height", VisualOptionKind.Number, 1, 0.25, 2),
            ],
            VisualizerChoice.Embers =>
            [
                new("amount", "How many sparks", VisualOptionKind.Number, 90, 20, 300),
                new("speed", "How fast they rise", VisualOptionKind.Number, 1, 0.25, 3),
                new("wind", "Sideways drift", VisualOptionKind.Number, 0.3, -2, 2),
                new("smoke", "Smoke and glow", VisualOptionKind.Number, 0.7, 0, 2),
                new("burst", "Burst on beats", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Ripples =>
            [
                new("beat", "Beat sensitivity", VisualOptionKind.Number, 1.4, 1.1, 2.5),
                new("thickness", "Ring thickness", VisualOptionKind.Number, 2.5, 1, 8),
                new("rings", "Maximum rings", VisualOptionKind.Number, 16, 4, 40),
                new("lifetime", "Ring lifetime", VisualOptionKind.Number, 1.5, 0.4, 4),
                new("where", "Ripple origin", VisualOptionKind.Choice, 0, Choices: ["Centre", "Across the stage", "Follow the pitch"]),
                new("drops", "On each", VisualOptionKind.Choice, 0, Choices: ["Beat", "Strong beat", "Drop"]),
            ],
            VisualizerChoice.Strobe =>
            [
                new("flashes", "At most this many flashes a second", VisualOptionKind.Number, 3, 1, 10),
                new("fade", "How long a flash takes to fade (seconds)", VisualOptionKind.Number, 0.4, 0.1, 2),
                new("pattern", "Flash shape", VisualOptionKind.Choice, 0, Choices: ["Whole stage", "Stripes", "Radial", "Blocks"]),
                new("step", "Colour step", VisualOptionKind.Choice, 1, Choices: ["Every beat", "Every 2 beats", "Every 4 beats"]),
                new("glow", "Background glow", VisualOptionKind.Number, 0.25, 0, 1),
            ],
            VisualizerChoice.Silhouette =>
            [
                new("style", "Draw me as", VisualOptionKind.Choice, 0, Choices: ["A glowing outline", "Filled with the music", "Sparks", "Motion echoes", "Split neon"]),
                new("mirror", "Mirror the camera, like a mirror", VisualOptionKind.Toggle, 1),
                new("threshold", "How different from the room I must be", VisualOptionKind.Number, 0.12, 0.04, 0.4),
                new("background", "Room behind me", VisualOptionKind.Choice, 0, Choices: ["Hidden", "Dimly visible"]),
            ],
            VisualizerChoice.BeatEdit =>
            [
                new("style", "Edit style", VisualOptionKind.Choice, 0, Choices: ["Velocity", "Glitch", "Hype", "Dreamy", "Everything"]),
                new("source", "Picture source", VisualOptionKind.Choice, 0, Choices: ["Camera, then cover", "Cover art"]),
                new("intensity", "Edit intensity", VisualOptionKind.Number, 1, 0.2, 2),
                new("cuts", "Change grade every", VisualOptionKind.Choice, 1, Choices: ["Beat", "2 beats", "4 beats", "8 beats"]),
                new("mirror", "Mirror the camera", VisualOptionKind.Toggle, 1),
                new("flash", "White flashes on strong beats", VisualOptionKind.Toggle, 1),
                new("flashes", "At most this many flashes a second", VisualOptionKind.Number, 3, 1, 3),
                new("grain", "Film grain", VisualOptionKind.Toggle, 1),
                new("bars", "Cinematic bars", VisualOptionKind.Toggle, 0),
            ],
            _ => [],
        });
        return options;
    }

    /// <summary>A number setting, kept to its range; the default when it is missing or unreadable.</summary>
    public static double Number(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key)
    {
        var option = Find(choice, key);
        return Read(options, choice, key) is { } value ? Math.Clamp(value, option.Minimum, option.Maximum) : option.Default;
    }

    public static bool Toggle(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key) =>
        (Read(options, choice, key) ?? Find(choice, key).Default) != 0;

    /// <summary>A choice's index, or the default when it is not one of the choices.</summary>
    public static int Choice(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key)
    {
        var option = Find(choice, key);
        return Read(options, choice, key) is { } value && value == Math.Floor(value) && value >= 0 && value < option.Choices!.Count ? (int)value : (int)option.Default;
    }

    /// <summary>A colour as RGB.</summary>
    public static int Rgb(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key) =>
        (int)(Read(options, choice, key) ?? Find(choice, key).Default) & 0xFFFFFF;

    /// <summary>The settings with one changed.</summary>
    public static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key, double value)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = Find(choice, key);
        return new Dictionary<string, string>(options, StringComparer.Ordinal) { [KeyOf(choice, key)] = value.ToString("R", CultureInfo.InvariantCulture) };
    }

    /// <summary>The settings with every one of <paramref name="choice"/>'s back to its default.</summary>
    public static IReadOnlyDictionary<string, string> Reset(IReadOnlyDictionary<string, string> options, VisualizerChoice choice)
    {
        ArgumentNullException.ThrowIfNull(options);
        var prefix = Prefix(choice);
        return options.Where(pair => !pair.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(StringComparer.Ordinal);
    }

    private static VisualOption Find(VisualizerChoice choice, string key) =>
        For(choice).FirstOrDefault(option => option.Key == key) ?? throw new ArgumentException($"{choice} has no setting {key}.", nameof(key));

    private static double? Read(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = Find(choice, key);
        return options.TryGetValue(KeyOf(choice, key), out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }

    private static string Prefix(VisualizerChoice choice) => choice.ToString().ToLowerInvariant() + ".";

    private static string KeyOf(VisualizerChoice choice, string key) => Prefix(choice) + key;
}
