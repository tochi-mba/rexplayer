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

    /// <summary>Magenta through violet to electric cyan.</summary>
    Neon,

    /// <summary>Deep red through orange to yellow-white, as flames are.</summary>
    Fire,

    /// <summary>The green of the aurora's lower edge, through teal, to the violet and pink above.</summary>
    Aurora,

    /// <summary>Deep blue through cyan to foam.</summary>
    Ocean,

    /// <summary>Purple through pink and orange to gold.</summary>
    Sunset,
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

    /// <summary>The beat meter along the bottom of the visualisations drawn in software.</summary>
    public const string Meter = "meter";

    private static readonly string[] PaletteNames = ["Windows' accent", "Follow the pitch", "A rainbow", "Warm", "Cool", "One colour", "Neon", "Fire", "Aurora", "Ocean", "Sunset"];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<VisualizerChoice, IReadOnlyList<VisualOption>> Made = new();

    /// <summary>The settings every visualisation has, then its own; made once for each, as they never change.</summary>
    public static IReadOnlyList<VisualOption> For(VisualizerChoice choice) => Made.GetOrAdd(choice, Make);

    private static IReadOnlyList<VisualOption> Make(VisualizerChoice choice)
    {
        List<VisualOption> options =
        [
            new(Colors, "Colours", VisualOptionKind.Choice, (int)DefaultPalette(choice), Choices: PaletteNames),
            // Offered only while the colours are One colour: see Applies.
            new(Color, "Colour", VisualOptionKind.Color, 0xFF3B30),
            new(Sensitivity, "Sensitivity", VisualOptionKind.Number, 1, 0.25, 4),
        ];
        // The beat meter is drawn by the software stage; the graphics-card effects have none.
        if (Visuals.VisualScene.Draws(choice) && !Visuals.Stage.CameraEffects.OnStage(choice))
        {
            options.Add(new(Meter, "Show the beat meter (what the music is heard as)", VisualOptionKind.Toggle, 0));
        }

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
                new("depth", "How brightly the music lights the groove", VisualOptionKind.Number, 1, 0.25, 2.5),
                new("arm", "Show the tone-arm", VisualOptionKind.Toggle, 1),
                new("label", "The cover as the label", VisualOptionKind.Toggle, 1),
                new("sheen", "The lamp's sheen on the vinyl", VisualOptionKind.Toggle, 1),
                new("wave", "The sound as a ring round the record", VisualOptionKind.Toggle, 1),
                new("strobe", "Strobe dots on the platter that flash on the beat", VisualOptionKind.Toggle, 1),
                new("glow", "Glow", VisualOptionKind.Number, 0.7, 0, 2),
            ],
            VisualizerChoice.Halo =>
            [
                new("rays", "Rays", VisualOptionKind.Number, 48, 12, 128),
                new("thickness", "Ray thickness", VisualOptionKind.Number, 4, 1, 12),
                new("spin", "Turn slowly", VisualOptionKind.Toggle, 0),
                new("trails", "Light trails", VisualOptionKind.Number, 0.72, 0, 0.95),
                new("burst", "Sparks on the beat", VisualOptionKind.Toggle, 1),
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
                new("curtains", "Light curtains", VisualOptionKind.Number, 3, 1, 6),
                new("height", "Curtain height", VisualOptionKind.Number, 1, 0.25, 2),
                new("drift", "How fast they drift", VisualOptionKind.Number, 1, 0, 3),
                new("stars", "Stars", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Resonance =>
            [
                new("density", "Luminous filaments", VisualOptionKind.Number, 7, 3, 12),
                new("depth", "Spectral movement", VisualOptionKind.Number, 1.1, 0.3, 2),
                new("flow", "Fluid flow", VisualOptionKind.Number, 0.8, 0.1, 2),
                new("contrast", "Light intensity", VisualOptionKind.Number, 1.15, 0.5, 1.8),
                new("trails", "Light trails", VisualOptionKind.Number, 0.45, 0, 0.9),
                new("mirror", "Reflect frequencies across the screen", VisualOptionKind.Toggle, 1),
                new("rings", "Pressure waves on drum hits", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Groove =>
            [
                new("seconds", "Seconds shown", VisualOptionKind.Number, 4, 2, 10),
                new("spectrum", "The spectrum underneath", VisualOptionKind.Toggle, 1),
                new("grid", "The beat grid", VisualOptionKind.Toggle, 1),
                new("lanes", "The kick, snare and hi-hat lanes", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Cymatics =>
            [
                new("detail", "How fine the figures get", VisualOptionKind.Number, 1, 0.5, 1.5),
                new("plate", "Plate", VisualOptionKind.Choice, 0, Choices: ["Square", "Round"]),
                new("glow", "Glow", VisualOptionKind.Number, 1, 0.3, 2),
            ],
            VisualizerChoice.Ridgelines =>
            [
                new("lines", "Ridges", VisualOptionKind.Number, 36, 12, 72),
                new("height", "Ridge height", VisualOptionKind.Number, 1, 0.4, 2),
            ],
            VisualizerChoice.Synthwave =>
            [
                new("speed", "Grid lines a beat", VisualOptionKind.Number, 2, 1, 4),
                new("sun", "The sun", VisualOptionKind.Toggle, 1),
                new("mountains", "The mountains", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Fireworks =>
            [
                new("gravity", "Gravity", VisualOptionKind.Number, 1, 0.4, 2),
                new("trails", "Trails", VisualOptionKind.Number, 0.75, 0, 0.92),
            ],
            VisualizerChoice.Lightning =>
            [
                new("branches", "Forks in each bolt", VisualOptionKind.Number, 3, 0, 6),
                new("flash", "How brightly the sky flashes", VisualOptionKind.Number, 0.6, 0, 1),
                new("rain", "Rain", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Lava =>
            [
                new("blobs", "Blobs", VisualOptionKind.Number, 7, 3, 12),
                new("glow", "Glow", VisualOptionKind.Number, 1, 0, 2),
                new("merge", "Gather on the kick", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Constellation =>
            [
                new("stars", "Stars", VisualOptionKind.Number, 70, 30, 160),
                new("memory", "How long the lines last (seconds)", VisualOptionKind.Number, 8, 2, 20),
            ],
            VisualizerChoice.Galaxy =>
            [
                new("arms", "Arms", VisualOptionKind.Number, 3, 2, 6),
                new("stars", "Stars", VisualOptionKind.Number, 2000, 500, 4000),
                new("spin", "How far it turns", VisualOptionKind.Number, 1, 0, 2.5),
            ],
            VisualizerChoice.Kaleidoscope =>
            [
                new("mirrors", "Mirrors", VisualOptionKind.Number, 6, 3, 12),
                new("shards", "Shards", VisualOptionKind.Number, 6, 3, 10),
                new("turn", "Turn and change mirrors with the music", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Murmuration =>
            [
                new("birds", "Birds", VisualOptionKind.Number, 700, 200, 1500),
                new("tightness", "How tight the flock keeps", VisualOptionKind.Number, 1, 0.3, 2),
                new("sky", "Dusk sky (else glowing birds in the dark)", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Ink =>
            [
                new("drops", "Drop size", VisualOptionKind.Number, 1, 0.5, 2),
                new("current", "Current", VisualOptionKind.Number, 1, 0, 2.5),
                new("fade", "How long the ink lasts", VisualOptionKind.Number, 0.985, 0.9, 0.997),
            ],
            VisualizerChoice.Skyline =>
            [
                new("buildings", "Towers", VisualOptionKind.Number, 24, 10, 48),
                new("lights", "Searchlights", VisualOptionKind.Toggle, 1),
                new("windows", "Lit windows", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Mandala =>
            [
                new("petals", "Petals", VisualOptionKind.Number, 8, 4, 16),
                new("layers", "Rings", VisualOptionKind.Number, 3, 1, 5),
                new("step", "Change the petals each bar", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Clones =>
            [
                new("clones", "Clones kept", VisualOptionKind.Number, 6, 3, 12),
                new("drift", "How far they drift", VisualOptionKind.Number, 1, 0, 2),
            ],
            VisualizerChoice.TimeWarp =>
            [
                new("depth", "Frames back", VisualOptionKind.Number, 30, 10, 60),
                new("direction", "Time runs", VisualOptionKind.Choice, 0, Choices: ["Down the picture", "Across it", "Out from the middle"]),
                new("tint", "Colour the past", VisualOptionKind.Number, 0.35, 0, 1),
            ],
            VisualizerChoice.Aura =>
            [
                new("glow", "Glow", VisualOptionKind.Number, 1, 0.3, 2),
                new("rings", "Shockwaves on the kick", VisualOptionKind.Toggle, 1),
                new("heat", "Draw me as heat (else a dark figure)", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.LedWall =>
            [
                new("tiles", "Lights across", VisualOptionKind.Number, 48, 16, 96),
                new("gate", "How much the spectrum gates the lights", VisualOptionKind.Number, 0.65, 0, 1),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["By brightness", "Across the wall"]),
            ],
            VisualizerChoice.Stardust =>
            [
                new("particles", "Motes", VisualOptionKind.Number, 2500, 500, 5000),
                new("blast", "How hard the kick blasts them", VisualOptionKind.Number, 1, 0.3, 2),
                new("trails", "Trails", VisualOptionKind.Number, 0.55, 0, 0.9),
            ],
            VisualizerChoice.HallOfMirrors =>
            [
                new("mirrors", "Mirrors", VisualOptionKind.Number, 6, 2, 12),
                new("zoom", "How far the bass draws it in", VisualOptionKind.Number, 1, 0, 2),
                new("turn", "Turn and change mirrors with the music", VisualOptionKind.Toggle, 1),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.LightPainting =>
            [
                new("brush", "Brush size", VisualOptionKind.Number, 1, 0.4, 3),
                new("fade", "How long the strokes last", VisualOptionKind.Number, 0.94, 0.8, 0.985),
                new("body", "Show my body faintly", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Scanner =>
            [
                new("direction", "Sweeps", VisualOptionKind.Choice, 0, Choices: ["Down", "Across"]),
                new("width", "Bar width", VisualOptionKind.Number, 1, 0.3, 3),
                new("hold", "How long the trace holds", VisualOptionKind.Number, 0.93, 0.8, 0.985),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.Shatter =>
            [
                new("tiles", "Tiles across", VisualOptionKind.Number, 14, 6, 28),
                new("blast", "How hard the kick blasts them", VisualOptionKind.Number, 1, 0.3, 2),
                new("spin", "Tiles spin", VisualOptionKind.Toggle, 1),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.Spotlight =>
            [
                new("lights", "Lights", VisualOptionKind.Number, 3, 1, 5),
                new("beam", "Beam width", VisualOptionKind.Number, 1, 0.4, 2.5),
                new("haze", "Haze", VisualOptionKind.Number, 1, 0, 2),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.InfinityMirror =>
            [
                new("levels", "Levels", VisualOptionKind.Number, 5, 2, 9),
                new("shrink", "How much smaller each level is", VisualOptionKind.Number, 0.72, 0.5, 0.9),
                new("turn", "Turn a step on each beat", VisualOptionKind.Toggle, 1),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.Water =>
            [
                new("depth", "How deeply the ripples bend the picture", VisualOptionKind.Number, 1, 0.2, 3),
                new("lifetime", "Ripple lifetime", VisualOptionKind.Number, 2, 0.5, 5),
                new("shimmer", "Shimmer on the hi-hats", VisualOptionKind.Toggle, 1),
                new("colour", "Colours", VisualOptionKind.Choice, 0, Choices: ["The palette", "The camera's own"]),
            ],
            VisualizerChoice.Flight =>
            [
                new("ahead", "Beats each ring takes to reach you", VisualOptionKind.Number, 8, 2, 16),
                new("trails", "Motion blur", VisualOptionKind.Number, 0.55, 0, 0.9),
                new("spectrum", "Rings shaped by the spectrum", VisualOptionKind.Toggle, 1),
                new("stars", "Stars streaming past", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Embers =>
            [
                new("amount", "How many sparks", VisualOptionKind.Number, 90, 20, 300),
                new("speed", "How fast they rise", VisualOptionKind.Number, 1, 0.25, 3),
                new("wind", "Sideways drift", VisualOptionKind.Number, 0.3, -2, 2),
                new("smoke", "How long their smoke lingers", VisualOptionKind.Number, 0.6, 0, 0.9),
                new("burst", "Burst on beats", VisualOptionKind.Toggle, 1),
            ],
            VisualizerChoice.Ripples =>
            [
                new("thickness", "Ring thickness", VisualOptionKind.Number, 2.5, 1, 8),
                new("rings", "Rings in each drop", VisualOptionKind.Number, 3, 1, 4),
                new("lifetime", "Ring lifetime", VisualOptionKind.Number, 1.5, 0.4, 4),
                new("where", "Ripple origin", VisualOptionKind.Choice, 0, Choices: ["Centre", "Across the stage", "Follow the pitch"]),
                new("drops", "On each", VisualOptionKind.Choice, 0, Choices: ["Beat", "Strong beat", "Drop"]),
            ],
            VisualizerChoice.Strobe =>
            [
                new("flashes", "At most this many flashes a second", VisualOptionKind.Number, 3, 1, 10),
                new("fade", "How long a flash takes to fade (seconds)", VisualOptionKind.Number, 0.4, 0.1, 2),
                new("pattern", "Flash shape", VisualOptionKind.Choice, 0, Choices: ["Whole stage", "Halves that trade places", "Bars that step along", "Rays from the middle", "Chequerboard"]),
                new("step", "Colour", VisualOptionKind.Choice, 0, Choices: ["Follows the pitch", "Steps through the colours each flash"]),
                new("glow", "Background glow", VisualOptionKind.Number, 0.25, 0, 1),
            ],
            VisualizerChoice.Silhouette =>
            [
                new("style", "Draw me as", VisualOptionKind.Choice, 0, Choices: ["A glowing outline", "Filled with the music", "Sparks", "Motion echoes", "Split neon"]),
                new("mirror", "Mirror the camera, like a mirror", VisualOptionKind.Toggle, 1),
                new("threshold", "Foreground sensitivity (lower detects more detail)", VisualOptionKind.Number, 0.09, 0.03, 0.35),
                new("background", "Room behind me", VisualOptionKind.Choice, 0, Choices: ["Hidden", "Dimly visible"]),
            ],
            VisualizerChoice.BeatEdit =>
            [
                new("style", "Edit style", VisualOptionKind.Choice, 0, Choices: ["Velocity", "Glitch", "Hype", "Dreamy", "Everything"]),
                new("source", "Picture source", VisualOptionKind.Choice, 0, Choices: ["Camera, then cover", "Cover art"]),
                new("follow", "Keep people centred (best with a clear background)", VisualOptionKind.Toggle, 1),
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

    /// <summary>The colours each visualisation is first drawn in: those that suit what it pictures.</summary>
    public static VisualPalette DefaultPalette(VisualizerChoice choice) => choice switch
    {
        VisualizerChoice.Strobe or VisualizerChoice.Mirror => VisualPalette.Pitch,
        VisualizerChoice.Vinyl => VisualPalette.Sunset,
        VisualizerChoice.Halo or VisualizerChoice.Silhouette or VisualizerChoice.BeatEdit => VisualPalette.Neon,
        VisualizerChoice.Aurora => VisualPalette.Aurora,
        VisualizerChoice.Resonance => VisualPalette.Ocean,
        VisualizerChoice.Groove => VisualPalette.Neon,
        VisualizerChoice.Flight => VisualPalette.Sunset,
        VisualizerChoice.Cymatics => VisualPalette.Ocean,
        VisualizerChoice.Ridgelines => VisualPalette.Aurora,
        VisualizerChoice.Synthwave => VisualPalette.Sunset,
        VisualizerChoice.Fireworks => VisualPalette.Rainbow,
        VisualizerChoice.Lightning => VisualPalette.Cool,
        VisualizerChoice.Lava => VisualPalette.Fire,
        VisualizerChoice.Constellation => VisualPalette.Cool,
        VisualizerChoice.Galaxy => VisualPalette.Aurora,
        VisualizerChoice.Kaleidoscope => VisualPalette.Rainbow,
        VisualizerChoice.Murmuration => VisualPalette.Sunset,
        VisualizerChoice.Ink => VisualPalette.Neon,
        VisualizerChoice.Skyline => VisualPalette.Neon,
        VisualizerChoice.Mandala => VisualPalette.Sunset,
        VisualizerChoice.Clones => VisualPalette.Neon,
        VisualizerChoice.TimeWarp => VisualPalette.Aurora,
        VisualizerChoice.Aura => VisualPalette.Fire,
        VisualizerChoice.LedWall => VisualPalette.Rainbow,
        VisualizerChoice.Stardust => VisualPalette.Ocean,
        VisualizerChoice.HallOfMirrors => VisualPalette.Neon,
        VisualizerChoice.LightPainting => VisualPalette.Rainbow,
        VisualizerChoice.Scanner => VisualPalette.Cool,
        VisualizerChoice.Shatter => VisualPalette.Fire,
        VisualizerChoice.Spotlight => VisualPalette.Sunset,
        VisualizerChoice.InfinityMirror => VisualPalette.Aurora,
        VisualizerChoice.Water => VisualPalette.Ocean,
        VisualizerChoice.Embers => VisualPalette.Fire,
        VisualizerChoice.Ripples => VisualPalette.Ocean,
        VisualizerChoice.InkBody => VisualPalette.Neon,
        VisualizerChoice.NeonSkeleton => VisualPalette.Neon,
        VisualizerChoice.EchoClones => VisualPalette.Neon,
        _ => VisualPalette.Accent,
    };

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

    /// <summary>
    /// Whether a setting does anything with the others as they are, so is worth offering: a
    /// visualisation's own colour counts only while its colours are One colour.
    /// </summary>
    public static bool Applies(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key)
    {
        _ = Find(choice, key);
        return key != Color || Choice(options, choice, Colors) == (int)VisualPalette.OneColor;
    }

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

    /// <summary>A setting by key: read many times a frame, so without a search that allocates.</summary>
    private static VisualOption Find(VisualizerChoice choice, string key)
    {
        var options = For(choice);
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Key == key)
            {
                return options[i];
            }
        }

        throw new ArgumentException($"{choice} has no setting {key}.", nameof(key));
    }

    private static double? Read(IReadOnlyDictionary<string, string> options, VisualizerChoice choice, string key)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = Find(choice, key);
        return options.TryGetValue(KeyOf(choice, key), out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }

    private static string Prefix(VisualizerChoice choice) => choice.ToString().ToLowerInvariant() + ".";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(VisualizerChoice, string), string> Keys = new();

    /// <summary>The stored key of a setting, made once: a frame reads settings many times and must not allocate.</summary>
    private static string KeyOf(VisualizerChoice choice, string key) => Keys.GetOrAdd((choice, key), static pair => Prefix(pair.Item1) + pair.Item2);
}
