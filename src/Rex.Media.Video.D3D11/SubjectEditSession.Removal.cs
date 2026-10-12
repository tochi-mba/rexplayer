using System.Globalization;
using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// The removal preview: the tracked subject is hidden by the background behind it, taken from
/// other pictures where the video showed it and synthesised from nearby texture where it never did
/// (<see cref="SubjectRemoval"/>). The background is recorded on every tracked picture, so turning
/// the preview on later can already use what the camera saw.
/// </summary>
public sealed partial class SubjectEditSession
{
    private readonly SubjectRemoval _removal = new();

    /// <summary>How many pixels of the erased preview show background the video really showed.</summary>
    public int ObservedPixels { get; private set; }

    /// <summary>
    /// Record the background around the subject and, while <see cref="Erase"/> is on, draw the
    /// preview into a new picture the caller disposes. Null means show the original.
    /// </summary>
    private VideoFrame? Remove(VideoFrame frame, bool advanceTracking)
    {
        var box = Bounds(frame.Width, frame.Height);
        ObservedPixels = 0;
        if (!Erase)
        {
            if (advanceTracking)
            {
                _removal.Apply(frame, box, advance: true, Feather, MaskTolerance, output: null);
            }

            return null;
        }

        var output = VideoFrame.Rent(PixelFormat.Bgra32, frame.Width, frame.Height);
        output.Pts = frame.Pts;
        output.Duration = frame.Duration;
        output.Generation = frame.Generation;
        output.PixelAspect = frame.PixelAspect;
        output.Color = frame.Color;
        var edited = _removal.Apply(frame, box, advanceTracking, Feather, MaskTolerance, output);
        EstimatedPixels = _removal.EstimatedPixels;
        ObservedPixels = _removal.ObservedPixels;
        Status = RemovalStatus(edited, ObservedPixels, EstimatedPixels);
        return output;
    }

    /// <summary>What the preview shows, truthfully: seen background, estimates, or nothing removed.</summary>
    internal static string RemovalStatus(bool edited, int observed, int estimated)
    {
        if (!edited || observed + estimated == 0)
        {
            return "Selected region blends into its surroundings. Raise subject coverage or select again; original is shown.";
        }

        if (estimated == 0)
        {
            return "Observed fill: background seen in other frames. Preview only.";
        }

        var share = Math.Max(1, (int)Math.Round(100.0 * estimated / (observed + estimated)));
        return string.Create(CultureInfo.InvariantCulture,
            $"Estimated fill: {share}% was never visible and is made from nearby texture. Preview only.");
    }
}
