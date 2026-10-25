using System.Globalization;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// What the Subject Lock panel's removal section says: its two modes, what the preview does in
/// each, how its two sliders read, and where the fill on screen came from. The window only shows
/// what this says.
/// </summary>
public static class RemovalPanel
{
    /// <summary>The section's explanation in Remove mode, shown above its controls.</summary>
    public const string Description =
        "Hides the tracked subject. Background the video showed in other frames is put back, following camera pans; anything never visible is made from nearby texture.";

    /// <summary>The section's explanation in Blend mode.</summary>
    public const string BlendDescription =
        "Covers the selection with the surface just around it, as a retouching brush hides a logo or a stain on a shirt. Nothing behind it is shown.";

    /// <summary>The mode choice's label.</summary>
    public const string ModeLabel = "Mode";

    /// <summary>What the two modes do, for the mode choice's tooltip and accessible help.</summary>
    public const string ModeHelp = "Remove shows what is behind the subject. Blend covers the selection with the surface around it.";

    /// <summary>A mode's name on its option.</summary>
    public static string ModeName(RemovalMode mode) => mode == RemovalMode.Blend ? "Blend" : "Remove";

    /// <summary>The section's explanation for a mode.</summary>
    public static string Explain(RemovalMode mode) => mode == RemovalMode.Blend ? BlendDescription : Description;

    /// <summary>The note under the controls.</summary>
    public const string Note = "A preview only: the file is never changed.";

    /// <summary>The coverage slider's label.</summary>
    public const string CoverageLabel = "Subject coverage";

    /// <summary>What the coverage slider changes, for its tooltip and accessible name.</summary>
    public const string CoverageHelp = "How much of the box counts as the subject. Raise it if parts of the subject stay visible; lower it if the background beside the subject is replaced.";

    /// <summary>The softness slider's label.</summary>
    public const string SoftnessLabel = "Edge softness";

    /// <summary>What the softness slider changes, for its tooltip and accessible name.</summary>
    public const string SoftnessHelp = "How far beyond the subject's outline the fill fades into the picture.";

    /// <summary>The coverage readout: the value and what it means.</summary>
    public static string CoverageText(int coverage)
    {
        var value = Math.Clamp(coverage, 0, 100);
        var meaning = value switch
        {
            100 => "whole box",
            >= 65 => "wide",
            > 35 => "balanced",
            _ => "tight",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{value}%, {meaning}");
    }

    /// <summary>The softness readout, in pixels.</summary>
    public static string SoftnessText(int feather) =>
        feather <= 0 ? "Hard edge" : string.Create(CultureInfo.InvariantCulture, $"{feather} px");

    /// <summary>
    /// Where the fill on screen came from, or why there is none. <paramref name="erase"/> is whether
    /// the preview is asked for, which it stays while a lost subject is looked for.
    /// </summary>
    public static string FillText(bool tracking, bool erase, int observed, int estimated,
        RemovalFillSource source = RemovalFillSource.Synthesised, RemovalMode mode = RemovalMode.Remove)
    {
        var text = FillShares(tracking, erase, observed, estimated);
        if (!tracking || !erase || Math.Max(0, observed) + Math.Max(0, estimated) == 0)
        {
            return text;
        }

        if (mode == RemovalMode.Blend)
        {
            return "Blend: covered with the surface around the selection.";
        }

        if (Math.Max(0, estimated) == 0)
        {
            return text;
        }

        return source switch
        {
            RemovalFillSource.Model => text + " The estimate was painted by the inpainting model.",
            RemovalFillSource.ModelWorking => text + " Filling with the inpainting model...",
            _ => text,
        };
    }

    private static string FillShares(bool tracking, bool erase, int observed, int estimated)
    {
        if (!tracking)
        {
            return erase
                ? "Removal paused: the subject is lost. It resumes once the subject is found again."
                : "Removal works while the subject is being tracked.";
        }

        if (!erase)
        {
            return "Turn on the preview to see the subject removed.";
        }

        observed = Math.Max(0, observed);
        estimated = Math.Max(0, estimated);
        var total = observed + estimated;
        if (total == 0)
        {
            return "Nothing removed: the subject blends into its surroundings.";
        }

        if (estimated == 0)
        {
            return "Fill: all of it seen in other frames.";
        }

        if (observed == 0)
        {
            return "Fill: all of it estimated; this background has not been visible yet.";
        }

        var seen = (int)Math.Round(100.0 * observed / total);
        seen = Math.Clamp(seen, 1, 99);
        return string.Create(CultureInfo.InvariantCulture, $"Fill: {seen}% seen in other frames, {100 - seen}% estimated.");
    }
}
