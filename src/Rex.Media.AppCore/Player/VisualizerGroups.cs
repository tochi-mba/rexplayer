using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// The visualisations as Preferences lists them (AU-18): grouped by family so the long list is easy
/// to scan, with the ones that use the camera kept together. Only the order shown changes; each
/// choice is still saved as its <see cref="VisualizerChoice"/>.
/// </summary>
public static class VisualizerGroups
{
    /// <summary>What Preferences calls <see cref="VisualizerChoice.Off"/>, listed before the groups.</summary>
    public const string Nothing = "Nothing";

    /// <summary>The groups in order, each a heading and its choices with what Preferences calls them.</summary>
    public static IReadOnlyList<(string Heading, (VisualizerChoice Choice, string Label)[] Choices)> All { get; } =
    [
        ("Sound pictured",
        [
            (VisualizerChoice.Spectrum, "A spectrum"),
            (VisualizerChoice.Oscilloscope, "An oscilloscope"),
            (VisualizerChoice.Meters, "Level meters"),
            (VisualizerChoice.Spectrogram, "A spectrogram"),
            (VisualizerChoice.Mirror, "A mirrored wave"),
            (VisualizerChoice.Ridgelines, "Ridgelines of sound"),
            (VisualizerChoice.Groove, "The groove, scrolling by"),
            (VisualizerChoice.Halo, "A halo"),
            (VisualizerChoice.Aurora, "An aurora"),
        ]),
        ("Light and colour",
        [
            (VisualizerChoice.Embers, "Embers"),
            (VisualizerChoice.Ripples, "Ripples"),
            (VisualizerChoice.Strobe, "A colour strobe"),
            (VisualizerChoice.Resonance, "Resonance light field"),
            (VisualizerChoice.Fireworks, "Fireworks"),
            (VisualizerChoice.Lightning, "A lightning storm"),
            (VisualizerChoice.Lava, "A lava lamp"),
            (VisualizerChoice.Kaleidoscope, "A kaleidoscope"),
            (VisualizerChoice.Mandala, "A mandala"),
            (VisualizerChoice.Ink, "Ink in water"),
        ]),
        ("Scenes",
        [
            (VisualizerChoice.Vinyl, "A vinyl record"),
            (VisualizerChoice.Flight, "A flight through the music"),
            (VisualizerChoice.Cymatics, "Sand on a singing plate"),
            (VisualizerChoice.Synthwave, "A synthwave drive"),
            (VisualizerChoice.Constellation, "The melody in the stars"),
            (VisualizerChoice.Galaxy, "A spiral galaxy"),
            (VisualizerChoice.Murmuration, "A murmuration of starlings"),
            (VisualizerChoice.Skyline, "A city at night"),
        ]),
        ("With the camera",
        [
            (VisualizerChoice.InkBody, "My body pouring ink that my moves stir"),
            (VisualizerChoice.NeonSkeleton, "My skeleton in neon, plasma between my hands"),
            (VisualizerChoice.EchoClones, "Echoes of my moves on the beat"),
            (VisualizerChoice.Silhouette, "My silhouette"),
            (VisualizerChoice.BeatEdit, "A live beat edit, from the camera or the cover"),
            (VisualizerChoice.Clones, "Clones of my dance"),
            (VisualizerChoice.TimeWarp, "My time warp"),
            (VisualizerChoice.Aura, "My aura"),
            (VisualizerChoice.LedWall, "Me on an LED wall"),
            (VisualizerChoice.Stardust, "Me in stardust"),
            (VisualizerChoice.HallOfMirrors, "Me in a hall of mirrors"),
            (VisualizerChoice.LightPainting, "Painting with light"),
            (VisualizerChoice.Scanner, "Scanned to the beat"),
            (VisualizerChoice.Shatter, "Shattered by the kick"),
            (VisualizerChoice.Spotlight, "Me under stage lights"),
            (VisualizerChoice.InfinityMirror, "Me in an infinity mirror"),
            (VisualizerChoice.Water, "Me through water"),
        ]),
    ];
}
