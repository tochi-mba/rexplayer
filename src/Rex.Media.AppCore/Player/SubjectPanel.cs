using System.Globalization;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>What the Subject Lock panel's state pill says the tracker is doing.</summary>
public enum SubjectState
{
    /// <summary>Nothing is selected yet.</summary>
    Waiting,

    /// <summary>A selection was made and the tracker is reading its texture from the next picture.</summary>
    Acquiring,

    /// <summary>The subject is visible and being followed.</summary>
    Tracking,

    /// <summary>The subject left the picture or could not be followed; the tracker is looking for it.</summary>
    Searching,

    /// <summary>Two or more places look alike, so the tracker refuses to guess between them.</summary>
    Ambiguous,
}

/// <summary>
/// The decisions behind the Subject Lock panel: what the state pill says, how the slider readouts
/// read, where the panel sits and how wide it is, what the help says in each selection mode, and
/// the box a single click stands for. The window only shows what this says.
/// </summary>
public static class SubjectPanel
{
    /// <summary>The removal mask's edge softness when the panel opens, in pixels.</summary>
    public const int DefaultFeather = 3;

    /// <summary>The removal mask's colour tolerance when the panel opens, in percent.</summary>
    public const int DefaultTolerance = 50;

    /// <summary>The widest the panel gets, in layout units; narrower stages get less.</summary>
    public const double WidestPanel = 320;

    /// <summary>The narrowest the panel gets before its content scrolls sideways out of reach.</summary>
    public const double NarrowestPanel = 140;

    /// <summary>The side of the picture's shorter edge a click locks onto, as a share of it.</summary>
    public const double ClickBoxShare = 0.12;

    /// <summary>How far the pointer may move between press and release and still count as a click.</summary>
    public const double ClickSlop = 8;

    /// <summary>The tracker's state, read from the status it reports.</summary>
    public static SubjectState State(bool locked, bool tracking, string? status)
    {
        if (tracking)
        {
            return SubjectState.Tracking;
        }

        if (status is not null && status.Contains("Similar-looking", StringComparison.Ordinal))
        {
            return SubjectState.Ambiguous;
        }

        if (!locked)
        {
            return SubjectState.Waiting;
        }

        return status is not null && status.StartsWith("Acquiring", StringComparison.Ordinal)
            ? SubjectState.Acquiring
            : SubjectState.Searching;
    }

    /// <summary>The state pill's text, with the match confidence while tracking.</summary>
    public static string Pill(SubjectState state, double confidence) => state switch
    {
        SubjectState.Tracking => string.Create(CultureInfo.InvariantCulture,
            $"Tracking, {Math.Round(Math.Clamp(double.IsFinite(confidence) ? confidence : 0, 0, 1) * 100)}% match"),
        SubjectState.Acquiring => "Acquiring",
        SubjectState.Searching => "Searching",
        SubjectState.Ambiguous => "Ambiguous",
        _ => "Waiting for selection",
    };

    /// <summary>The pill's colour as 0xAARRGGBB: teal while tracking, amber while unsure, grey while waiting.</summary>
    public static uint PillColour(SubjectState state) => state switch
    {
        SubjectState.Tracking => 0xFF45ECD2,
        SubjectState.Searching or SubjectState.Ambiguous => 0xFFFFC857,
        SubjectState.Acquiring => 0xFFB8E0F5,
        _ => 0xFFC2C9D4,
    };

    /// <summary>The feather slider's readout.</summary>
    public static string FeatherText(int feather) =>
        feather <= 0 ? "Off" : string.Create(CultureInfo.InvariantCulture, $"{feather} px");

    /// <summary>The tolerance slider's readout.</summary>
    public static string ToleranceText(int tolerance) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(tolerance, 0, 100)}%");

    /// <summary>The primary action's label: a first selection, or another one.</summary>
    public static string SelectText(bool hasSelection) => hasSelection ? "Select again" : "Select";

    /// <summary>The removal button's label: it previews the removal, or shows the original again.</summary>
    public static string EraseText(bool erasing) => erasing ? "Show original" : "Preview removal";

    /// <summary>
    /// The panel's width: as wide as it can be without covering the picture's middle, so it never
    /// takes more than two fifths of the stage and always leaves a margin.
    /// </summary>
    public static double Width(double stageWidth) =>
        Math.Max(NarrowestPanel, Math.Min(WidestPanel, Math.Min(stageWidth - 24, stageWidth * 0.4)));

    /// <summary>The panel's greatest height; beyond it the content scrolls.</summary>
    public static double MaxHeight(double stageHeight) => Math.Max(48, stageHeight - 24);

    /// <summary>The other side of the picture.</summary>
    public static PanelSide Opposite(PanelSide side) => side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    /// <summary>The label of the button that moves the panel to the other side.</summary>
    public static string MoveText(PanelSide side) => side == PanelSide.Left ? "Move right" : "Move left";

    /// <summary>What the mode switch says about the current selection mode.</summary>
    public static string ModeText(SubjectSelectionMode mode) =>
        mode == SubjectSelectionMode.Draw ? "Draw a box" : "Click to lock";

    /// <summary>What to do next when nothing is selected, in the current selection mode.</summary>
    public static string Instruction(SubjectSelectionMode mode, bool follow)
    {
        var select = mode == SubjectSelectionMode.Draw
            ? "Drag a box around the object you want to follow."
            : "Click the object you want to follow, or drag a box around it.";
        var leave = follow
            ? "If it leaves the picture, auto-follow widens the view and keeps looking for it."
            : "If it leaves the picture, Subject Lock keeps looking for it; auto-follow is off.";
        return select + " " + leave;
    }

    /// <summary>The help section's lines, which name the selection mode in use.</summary>
    public static IReadOnlyList<string> Help(SubjectSelectionMode mode) =>
    [
        mode == SubjectSelectionMode.Draw
            ? "Draw a box: drag around the object. A box that fits it closely tracks best."
            : "Click to lock: one click on the object selects a small area around it. Drag instead to choose the area yourself.",
        "Choose a textured area with clear edges, such as a face, a jacket or a number plate. Sky, a plain wall or a dark corner has too little detail to follow.",
        "If the subject leaves the picture, tracking pauses and the original texture is searched for; it resumes only on a clear match, and never guesses between lookalikes. Nothing is saved or changed in the file.",
    ];

    /// <summary>Whether a press and a release this far apart count as a click in the given mode.</summary>
    public static bool IsClick(SubjectSelectionMode mode, double dx, double dy) =>
        mode == SubjectSelectionMode.Click && Math.Abs(dx) < ClickSlop && Math.Abs(dy) < ClickSlop;

    /// <summary>
    /// The box a click at (<paramref name="x"/>, <paramref name="y"/>) stands for, in picture
    /// coordinates from 0 to 1: a square of <see cref="ClickBoxShare"/> of the picture's shorter
    /// side, centred on the click and moved inwards where it would leave the picture.
    /// </summary>
    public static (double Left, double Top, double Width, double Height) ClickBox(double x, double y, long across, long down)
    {
        across = Math.Max(1, across);
        down = Math.Max(1, down);
        var shorter = (double)Math.Min(across, down);
        var width = ClickBoxShare * shorter / across;
        var height = ClickBoxShare * shorter / down;
        var left = Math.Clamp(x - width / 2, 0, 1 - width);
        var top = Math.Clamp(y - height / 2, 0, 1 - height);
        return (left, top, width, height);
    }
}
