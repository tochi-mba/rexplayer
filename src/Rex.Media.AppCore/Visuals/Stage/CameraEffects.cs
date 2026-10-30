using Rex.Media.Settings;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// Which visualisations are camera effects drawn on the graphics card (ADR-0022), how each is made,
/// and the visualisation drawn in software in its place wherever the stage cannot run: on a Mac until
/// its Metal runner lands, and on Windows if the graphics card will not draw. The stand-in uses its
/// own settings, so nothing saved for it is lost.
/// </summary>
public static class CameraEffects
{
    /// <summary>Whether <paramref name="choice"/> is drawn on the graphics-card stage.</summary>
    public static bool OnStage(VisualizerChoice choice) => choice is VisualizerChoice.InkBody or VisualizerChoice.NeonSkeleton or VisualizerChoice.EchoClones;

    /// <summary>A new effect for <paramref name="choice"/>, or null for a visualisation that is not on the stage.</summary>
    public static CameraEffect? Make(VisualizerChoice choice) => choice switch
    {
        VisualizerChoice.InkBody => new InkBodyEffect(),
        VisualizerChoice.NeonSkeleton => new NeonSkeletonEffect(),
        VisualizerChoice.EchoClones => new EchoClonesEffect(),
        _ => null,
    };

    /// <summary>The visualisation drawn in software in place of <paramref name="choice"/>; itself for one not on the stage.</summary>
    public static VisualizerChoice StandIn(VisualizerChoice choice) => choice switch
    {
        VisualizerChoice.InkBody => VisualizerChoice.Ink,
        VisualizerChoice.NeonSkeleton => VisualizerChoice.Silhouette,
        VisualizerChoice.EchoClones => VisualizerChoice.Clones,
        _ => choice,
    };
}
