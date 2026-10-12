using System.Globalization;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// What the Subject Lock panel's removal section says: what the preview does, how its two sliders
/// read, and where the fill on screen came from. The window only shows what this says.
/// </summary>
public static class RemovalPanel
{
    /// <summary>The section's explanation, shown above its controls.</summary>
    public const string Description =
        "Hides the tracked subject. Background the video showed in other frames is put back, following camera pans; anything never visible is made from nearby texture.";

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

    /// <summary>Where the fill on screen came from, or why there is none.</summary>
    public static string FillText(bool tracking, bool erase, int observed, int estimated)
    {
        if (!tracking)
        {
            return "Removal works while the subject is being tracked.";
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
