using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// A user-selected moving video region. The selected pixels never leave this process. Matching is
/// conservative: when a selected pattern cannot be distinguished from the next picture, the lock
/// is marked lost rather than silently jumping to another person or object.
/// </summary>
public sealed partial class SubjectEditSession : IDisposable
{
    private const int Samples = 16;
    private const int MaxPixels = 3840 * 2160;

    // Count consecutive pictures since the lock was lost. The first few are checked more
    // eagerly because a subject that just crossed an edge is most likely to reappear then.
    private int _lostFrames;
    private int _identityWidth;
    private int _identityHeight;
    private (int X, int Y, int Width, int Height)? _returnCandidate;
    private int _returnConfirmations;
    private int _reacquiredFrames;
    private bool _needsReselection;
    private MediaTime _lastPts = MediaTime.Unknown;
    private long? _generation;
    private int _width;
    private int _height;

    /// <summary>The selected rectangle in uncropped decoded picture coordinates (0 to 1).</summary>
    public (float Left, float Top, float Width, float Height) Region { get; private set; }

    /// <summary>Whether the user has selected a region to follow.</summary>
    public bool Locked { get; private set; }

    /// <summary>Whether an erased-background preview is being shown instead of the unedited picture.</summary>
    public bool Erase { get; private set; }

    /// <summary>Whether the current picture still matches the original user-selected subject.</summary>
    public bool Tracking { get; private set; }

    /// <summary>A human-readable explanation of the last frame's status.</summary>
    public string Status { get; private set; } = "Select a region in the picture.";

    /// <summary>Confidence of the template match, between 0 and 1. Never semantic identity confidence.</summary>
    public double Confidence { get; private set; }

    /// <summary>How many pixels of the erased preview were estimated rather than previously observed.</summary>
    public int EstimatedPixels { get; private set; }

    /// <summary>Full-template comparisons in the latest global search, for local performance diagnostics.</summary>
    public int LastSearchComparisons { get; private set; }

    /// <summary>How far the removal fades out beyond the subject's outline, in pixels; 0 is a hard edge.</summary>
    public int Feather { get; private set; } = 3;

    /// <summary>How much of the box counts as the subject, from 0 (only what clearly is) to 100 (all of it); 50 is balanced.</summary>
    public int MaskTolerance { get; private set; } = 50;

    /// <summary>Live, bounded mask adjustments; preview and original frames remain independent.</summary>
    public void Refine(int feather, int tolerance)
    {
        Feather = Math.Clamp(feather, 0, 12);
        MaskTolerance = Math.Clamp(tolerance, 0, 100);
    }

    /// <summary>Choose a bounded patch in the decoded video, independent of the viewport and crop.</summary>
    public void Select(float left, float top, float width, float height)
    {
        if (!float.IsFinite(left) || !float.IsFinite(top) || !float.IsFinite(width) || !float.IsFinite(height)
            || width < 0.01f || height < 0.01f || left < 0 || top < 0 || left + width > 1.00001f || top + height > 1.00001f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Select an area inside the picture at least one percent across.");
        }

        Reset();
        Region = (Math.Clamp(left, 0, 1), Math.Clamp(top, 0, 1),
            Math.Min(width, 1 - left), Math.Min(height, 1 - top));
        Locked = true;
        Status = "Acquiring the selected region...";
    }

    /// <summary>Toggle the non-destructive reconstruction preview; no file is ever rewritten.</summary>
    public void SetErase(bool enabled)
    {
        _eraseWanted = Locked && enabled;
        Erase = Locked && Tracking && enabled;
        EstimatedPixels = 0;
    }

    /// <summary>Restore the unedited picture and forget the selected subject and cached pixels.</summary>
    public void Reset()
    {
        CancelRecovery();
        ResetRemoval();
        Locked = Erase = Tracking = false;
        _positive.Clear();
        _negative.Clear();
        _untilRemember = RememberInterval;
        _lostFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 0;
        _identityWidth = _identityHeight = 0;
        _width = _height = 0;
        _needsReselection = false;
        _removal.Release();
        ObservedPixels = 0;
        _click = null;
        _shots.Reset();
        _glanceKnown = false;
        IsCut = false;
        _shotChanged = false;
        _scan = _scanColour = null;
        _scanWidth = _scanHeight = 0;
        _lightRed = _lightGreen = 0;
        Confidence = 0;
        EstimatedPixels = 0;
        _lastPts = MediaTime.Unknown;
        _generation = null;
        LastSearchComparisons = 0;
        Status = "Select a region in the picture.";
    }

    /// <summary>
    /// Analyze a BGRA video picture and, when requested, create an edited copy. Null means show
    /// the original. Never modifies the caller's frame. The removal shows background seen in other
    /// frames, followed through pans and zooms, and synthesises only what was never visible.
    /// </summary>
    public VideoFrame? Process(VideoFrame frame, bool advanceTracking = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format != PixelFormat.Bgra32 || frame.Surface is not null)
        {
            throw new ArgumentException("A system-memory BGRA frame is required.", nameof(frame));
        }

        if (!Locked || _needsReselection)
        {
            // A rejected initial selection must not silently attach to a different
            // object that later happens to enter its old picture coordinates.
            return null;
        }

        EstimatedPixels = 0;
        if (_generation is { } generation && generation != frame.Generation)
        {
            Reset();
            Status = "The playback generation changed. Select the subject again.";
            return null;
        }

        _generation = frame.Generation;
        if (_click is not null)
        {
            // A click is settled on the first picture after it, paused or playing, and that
            // picture is then the selection's first observation.
            if (!SettleClick(frame))
            {
                return null;
            }

            advanceTracking = true;
        }

        // A paused redraw changes only the preview, not the evidence for identity.
        if (!advanceTracking && !Tracking)
        {
            return null;
        }

        if (!AcceptPictureSize(frame.Width, frame.Height))
        {
            return null;
        }

        if (_width != frame.Width || _height != frame.Height)
        {
            if (_width > 0 && _height > 0 && _positive.Count > 0)
            {
                // A resized decoder picture may put an entirely different object under
                // the old normalized region. Do not adopt it as a new identity.
                Reset();
                Status = "Picture dimensions changed. Select the subject again.";
                return null;
            }

            (_width, _height) = (frame.Width, frame.Height);
            _removal.Release();
            _positive.Clear();
            _negative.Clear();
            _lostFrames = 0;
            _returnCandidate = null;
            _returnConfirmations = 0;
            _reacquiredFrames = 0;
            Tracking = false;
        }

        if (_lastPts.IsKnown && frame.Pts.IsKnown
            && (frame.Pts < _lastPts || frame.Pts - _lastPts > MediaTime.FromSeconds(2)))
        {
            CancelRecovery();
            _positive.Clear();
            _negative.Clear();
            _lostFrames = 0;
            _returnCandidate = null;
            _returnConfirmations = 0;
            _reacquiredFrames = 0;
            Tracking = false;
            Confidence = 0;
            Erase = false;
            Status = "The timeline jumped. Select the subject again to avoid following a different object.";
            Locked = false;
            _removal.Release();
            return null;
        }

        _lastPts = frame.Pts;
        // Read the colour of the light before anything in this picture is described, so every
        // view taken from it records the light it was seen under.
        MeasureLight(frame);
        if (_positive.Count == 0)
        {
            var selection = Bounds(frame.Width, frame.Height);
            _identityWidth = Math.Max(1, (int)Math.Round(Region.Width * frame.Width));
            _identityHeight = Math.Max(1, (int)Math.Round(Region.Height * frame.Height));
            Describe(frame, selection.Left, selection.Top,
                selection.Right - selection.Left, selection.Bottom - selection.Top, _probe);
            var (selectedCentre, nearbyBorder, _) = CompareSubjectWithBorder(frame, selection);
            var borderContrast = Math.Abs(selectedCentre.B - nearbyBorder.B)
                + Math.Abs(selectedCentre.G - nearbyBorder.G)
                + Math.Abs(selectedCentre.R - nearbyBorder.R);
            if (!IsDistinctive(_probe) && borderContrast < 40)
            {
                // A flat patch of sky, wall or clothing with no contrasting edges can
                // match thousands of unrelated places. Never claim an identity lock.
                Tracking = false;
                Erase = false;
                _needsReselection = true;
                Confidence = 0;
                Status = "Selection has too little distinguishing detail. Select again around a visible edge or textured area.";
                return null;
            }

            RememberSubject(_probe);
            // Learning the rest of this picture at the same moment is what lets every later
            // comparison ask whether a patch is more like the subject than like its
            // surroundings, instead of only whether it is roughly similar to the subject.
            LearnSurroundings(frame, selection);
            Tracking = true;
            Confidence = 1;
            Status = "Selected region locked. Tracking uses its visible texture, not a person recognizer.";
        }
        else if (!Tracking)
        {
            // Keep the original identity template after the target leaves view. A bounded
            // sparse scan periodically looks for it again; never resume from a weak or
            // ambiguous match, and never turn the removal preview on automatically.
            // No expiry while the original selection remains locked. Check every second
            // picture immediately after loss so an obvious re-entry is not visibly late,
            // then fall back to every fourth picture for a long disappearance. Two
            // consistent sightings are still required before the viewport may follow.
            _lostFrames++;
            var searchInterval = _lostFrames <= 24 ? 2 : 4;
            if (_lostFrames % searchInterval != 0
                || !(_asynchronousRecovery ? RecoverWithoutBlocking(frame) : TryReacquire(frame)))
            {
                Status = _returnCandidate is null
                    ? "Tracking uncertain: searching the picture for the original texture."
                    : "Possible match: verifying the original texture before following.";
                return null;
            }
        }
        else if (Tracking && advanceTracking)
        {
            Track(frame);
        }

        if (!Tracking)
        {
            return null;
        }

        return Remove(frame, advanceTracking);
    }

    /// <summary>Reject over-budget pictures before a presenter copies a hardware surface to the CPU.</summary>
    internal bool AcceptPictureSize(int width, int height)
    {
        if ((long)width * height <= MaxPixels)
        {
            return true;
        }

        Reset();
        Status = "Picture too large for local tracking. Select a smaller video.";
        return false;
    }

    /// <summary>
    /// Local foreground/background colour estimate for a user-defined rectangle. Sampling just
    /// outside its border avoids classifying a loose rectangular selection as an entire object.
    /// A weak colour separation leaves the user's rectangle intact instead of inventing a mask.
    /// </summary>
    private static ((int B, int G, int R) Foreground, (int B, int G, int R) Background, int Difference)
        CompareSubjectWithBorder(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var cx = (box.Left + box.Right) / 2;
        var cy = (box.Top + box.Bottom) / 2;
        var center = frame.Row(0, Math.Min(frame.Height - 1, cy));
        var foreground = (B: (int)center[Math.Min(frame.Width - 1, cx) * 4],
            G: (int)center[Math.Min(frame.Width - 1, cx) * 4 + 1],
            R: (int)center[Math.Min(frame.Width - 1, cx) * 4 + 2]);
        var b = 0;
        var g = 0;
        var r = 0;
        var count = 0;
        void Add(int x, int y)
        {
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height)
            {
                return;
            }

            var row = frame.Row(0, y);
            var index = x * 4;
            b += row[index];
            g += row[index + 1];
            r += row[index + 2];
            count++;
        }

        Add(box.Left - 1, cy);
        Add(box.Right, cy);
        Add(cx, box.Top - 1);
        Add(cx, box.Bottom);
        var background = count > 0
            ? (B: b / count, G: g / count, R: r / count)
            : foreground;
        var difference = Math.Abs(foreground.B - background.B)
            + Math.Abs(foreground.G - background.G)
            + Math.Abs(foreground.R - background.R);
        return (foreground, background, difference);
    }

    private (int Left, int Top, int Right, int Bottom) Bounds(int width, int height) =>
        (Math.Clamp((int)(Region.Left * width), 0, width - 1),
         Math.Clamp((int)(Region.Top * height), 0, height - 1),
         Math.Clamp((int)Math.Ceiling((Region.Left + Region.Width) * width), 1, width),
         Math.Clamp((int)Math.Ceiling((Region.Top + Region.Height) * height), 1, height));
}
