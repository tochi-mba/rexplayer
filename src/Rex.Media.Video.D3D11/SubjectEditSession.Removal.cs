using System.Globalization;
using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// The removal preview: the tracked subject is hidden by the background behind it, taken from
/// other pictures where the video showed it and synthesised from nearby texture where it never did
/// (<see cref="SubjectRemoval"/>), or, in <see cref="RemovalMode.Blend"/>, covered by the surface
/// around it. Nothing is recorded or drawn until the preview is asked for, so following a subject
/// without it costs the removal nothing. The preview, once asked for, stays asked for: when the
/// subject is lost it pauses, still recording the background around the subject whenever it is
/// followed, and it resumes once the subject is found again and followed steadily.
/// </summary>
public sealed partial class SubjectEditSession
{
    private readonly SubjectRemoval _removal = new();
    private bool _eraseWanted;

    /// <summary>Whether the preview was asked for and still is, whether or not it can be shown now.</summary>
    public bool RemovalWanted => _eraseWanted && Locked;

    /// <summary>How the preview hides the subject: show what is behind it, or cover it with the surface around it.</summary>
    public RemovalMode Mode { get; private set; }

    /// <summary>Choose how the preview hides the subject; the next picture drawn shows the change.</summary>
    public void SetRemovalMode(RemovalMode mode)
    {
        mode = Enum.IsDefined(mode) ? mode : RemovalMode.Remove;
        if (mode != Mode)
        {
            Mode = mode;
            CancelModelWork();
        }
    }

    /// <summary>Forget the preview asked for and any model work for it, as a new selection does.</summary>
    private void ResetRemoval()
    {
        _eraseWanted = false;
        CancelModelWork();
    }

    /// <summary>How many pixels of the erased preview show background the video really showed.</summary>
    public int ObservedPixels { get; private set; }

    /// <summary>
    /// While the preview is asked for, record the background around the subject and, while
    /// <see cref="Erase"/> is on, draw the preview into a new picture the caller disposes. Null
    /// means show the original.
    /// </summary>
    private VideoFrame? Remove(VideoFrame frame, bool advanceTracking)
    {
        var box = Bounds(frame.Width, frame.Height);
        ObservedPixels = 0;
        if (!Erase && _eraseWanted && _reacquiredFrames == 0)
        {
            // Asked for before the subject was lost, and the subject is followed steadily again.
            Erase = true;
        }

        if (!Erase)
        {
            if (advanceTracking && _eraseWanted)
            {
                _removal.Apply(frame, box, advance: true, Feather, MaskTolerance, output: null, mode: Mode);
            }

            return null;
        }

        var output = VideoFrame.Rent(PixelFormat.Bgra32, frame.Width, frame.Height);
        output.Pts = frame.Pts;
        output.Duration = frame.Duration;
        output.Generation = frame.Generation;
        output.PixelAspect = frame.PixelAspect;
        output.Color = frame.Color;
        var edited = _removal.Apply(frame, box, advanceTracking, Feather, MaskTolerance, output, mode: Mode);
        FillSource = RemovalFillSource.Synthesised;
        if (edited && !advanceTracking && Mode == RemovalMode.Remove && ModelFill(frame, output) is { } filled)
        {
            // A paused picture the inpainting model has filled: its pixels replace the estimates.
            _removal.Apply(frame, box, advance: false, Feather, MaskTolerance, output, filled);
        }

        EstimatedPixels = _removal.EstimatedPixels;
        ObservedPixels = _removal.ObservedPixels;
        Status = RemovalStatus(edited, ObservedPixels, EstimatedPixels, FillSource, Mode);
        return output;
    }

    /// <summary>What the preview shows, truthfully: seen background, estimates and who made them, or nothing removed.</summary>
    internal static string RemovalStatus(bool edited, int observed, int estimated,
        RemovalFillSource source = RemovalFillSource.Synthesised, RemovalMode mode = RemovalMode.Remove)
    {
        if (!edited || observed + estimated == 0)
        {
            return "Selected region blends into its surroundings. Raise subject coverage or select again; original is shown.";
        }

        if (mode == RemovalMode.Blend)
        {
            return "Blended: covered with the surface around the selection. Preview only.";
        }

        if (estimated == 0)
        {
            return "Observed fill: background seen in other frames. Preview only.";
        }

        var share = Math.Max(1, (int)Math.Round(100.0 * estimated / (observed + estimated)));
        return source switch
        {
            RemovalFillSource.Model => string.Create(CultureInfo.InvariantCulture,
                $"Model fill: {share}% was never visible and was painted by the inpainting model. Preview only."),
            RemovalFillSource.ModelWorking => string.Create(CultureInfo.InvariantCulture,
                $"Filling with the inpainting model... {share}% was never visible; nearby texture is shown meanwhile. Preview only."),
            _ => string.Create(CultureInfo.InvariantCulture,
                $"Estimated fill: {share}% was never visible and is made from nearby texture. Preview only."),
        };
    }
}
