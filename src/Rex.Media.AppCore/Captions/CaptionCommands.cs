using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Captions;

/// <summary>What the kinetic caption commands change (OSD-11), and what is said when they do.</summary>
public static class CaptionCommands
{
    /// <summary>How much livelier or calmer each step makes the captions, in percent.</summary>
    public const int EnergyStep = 25;

    /// <summary>The ids handled here.</summary>
    public static IReadOnlyList<string> Ids { get; } =
    [
        CommandCatalog.ToggleKineticCaptions,
        CommandCatalog.KineticCaptionsLivelier,
        CommandCatalog.KineticCaptionsCalmer,
        CommandCatalog.CycleCaptionLayout,
        CommandCatalog.CycleCaptionWords,
        CommandCatalog.CycleCaptionStyle,
    ];

    /// <summary>
    /// The settings after <paramref name="commandId"/>, and the message to show; null for a command
    /// that is not one of <see cref="Ids"/>. Changing the energy, layout or words turns the captions on.
    /// </summary>
    public static (KineticCaptionSettings Settings, string Message)? Apply(string commandId, KineticCaptionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        switch (commandId)
        {
            case CommandCatalog.ToggleKineticCaptions:
                return (settings with { On = !settings.On }, settings.On ? "Kinetic captions off" : "Kinetic captions on");
            case CommandCatalog.KineticCaptionsLivelier or CommandCatalog.KineticCaptionsCalmer:
                var step = commandId == CommandCatalog.KineticCaptionsLivelier ? EnergyStep : -EnergyStep;
                var energy = Math.Clamp(settings.Energy + step, KineticCaptionSettings.LeastEnergy, KineticCaptionSettings.MostEnergy);
                return (settings with { On = true, Energy = energy }, string.Create(CultureInfo.InvariantCulture, $"Kinetic caption energy {energy}%"));
            case CommandCatalog.CycleCaptionLayout:
                var layout = Next(settings.Layout);
                return (settings with { On = true, Layout = layout }, "Kinetic captions: " + LayoutName(layout));
            case CommandCatalog.CycleCaptionStyle:
                var preset = Next(settings.Preset);
                return (settings with { On = true, Preset = preset }, "Kinetic captions: " + CaptionPresets.Name(preset));
            case CommandCatalog.CycleCaptionWords:
                var words = Next(settings.Words);
                return (settings with { On = true, Words = words }, "Kinetic caption words: " + WordsName(words));
            default:
                return null;
        }
    }

    /// <summary>How a layout is named in menus and messages.</summary>
    public static string LayoutName(CaptionLayoutChoice layout) => layout switch
    {
        CaptionLayoutChoice.Stack => "stacked at the bottom",
        CaptionLayoutChoice.Centre => "centred",
        _ => "scattered",
    };

    /// <summary>How a word source is named in menus and messages.</summary>
    public static string WordsName(CaptionWordSource words) => words switch
    {
        CaptionWordSource.Subtitles => "the subtitles",
        CaptionWordSource.Transcript => "the transcript",
        CaptionWordSource.SubtitlesTimedByTranscript => "the subtitles, timed by the transcript",
        _ => "automatic",
    };

    private static T Next<T>(T value)
        where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, value) + 1) % values.Length];
    }
}
