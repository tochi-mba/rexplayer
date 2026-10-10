using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// A user-selected moving video region. The selected pixels never leave this process. Matching is
/// conservative: when a selected pattern cannot be distinguished from the next picture, the lock
/// is marked lost rather than silently jumping to another person or object.
/// </summary>
public sealed class SubjectEditSession
{
    private const int Samples = 16;
    private const int MaxPixels = 3840 * 2160;

    private readonly float[] _samples = new float[Samples * Samples * 3];
    private float[]? _reference;
    private float[]? _returnReference;
    private int _returnFrames;
    private int _identityWidth;
    private int _identityHeight;
    private int _originalBorderContrast;
    private (int X, int Y, int Width, int Height)? _returnCandidate;
    private int _returnConfirmations;
    private int _reacquiredFrames;
    private byte[]? _clean;
    private byte[]? _known;
    private MediaTime _lastPts = MediaTime.Unknown;
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

    /// <summary>Soft-edge width in pixels, adjustable without modifying the selected media.</summary>
    public int Feather { get; private set; } = 3;

    /// <summary>How broadly the foreground's colours are included, from 0 to 100.</summary>
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

        Region = (Math.Clamp(left, 0, 1), Math.Clamp(top, 0, 1),
            Math.Min(width, 1 - left), Math.Min(height, 1 - top));
        Locked = true;
        Erase = false;
        Tracking = false;
        _reference = null;
        _returnReference = null;
        _returnFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 0;
        _identityWidth = _identityHeight = 0;
        _clean = null;
        _known = null;
        _lastPts = MediaTime.Unknown;
        Confidence = 0;
        EstimatedPixels = 0;
        Status = "Acquiring the selected region...";
    }

    /// <summary>Toggle the non-destructive reconstruction preview; no file is ever rewritten.</summary>
    public void SetErase(bool enabled)
    {
        Erase = Locked && enabled;
        EstimatedPixels = 0;
    }

    /// <summary>Restore the unedited picture and forget the selected subject and cached pixels.</summary>
    public void Reset()
    {
        Locked = Erase = Tracking = false;
        _reference = null;
        _returnReference = null;
        _returnFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 0;
        _identityWidth = _identityHeight = 0;
        _clean = _known = null;
        Confidence = 0;
        EstimatedPixels = 0;
        _lastPts = MediaTime.Unknown;
        Status = "Select a region in the picture.";
    }

    /// <summary>
    /// Analyze a BGRA video picture and, when requested, create an edited copy. Null means show
    /// the original. Never modifies the caller's frame. A cache can use genuinely uncovered pixels
    /// from preceding frames only if the camera appears stationary; otherwise estimates from edges.
    /// </summary>
    public VideoFrame? Process(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format != PixelFormat.Bgra32 || frame.Surface is not null)
        {
            throw new ArgumentException("A system-memory BGRA frame is required.", nameof(frame));
        }

        if (!Locked)
        {
            return null;
        }

        if ((long)frame.Width * frame.Height > MaxPixels)
        {
            Tracking = false;
            Confidence = 0;
            Status = "Picture too large for local tracking. Original picture is shown.";
            return null;
        }

        if (_width != frame.Width || _height != frame.Height)
        {
            (_width, _height) = (frame.Width, frame.Height);
            _clean = null;
            _known = null;
            _reference = null;
            _returnReference = null;
            _returnFrames = 0;
            _returnCandidate = null;
            _returnConfirmations = 0;
            _reacquiredFrames = 0;
            Tracking = false;
        }

        if (_lastPts.IsKnown && frame.Pts.IsKnown
            && (frame.Pts < _lastPts || frame.Pts - _lastPts > MediaTime.FromSeconds(2)))
        {
            _reference = null;
            _returnReference = null;
            _returnFrames = 0;
            _returnCandidate = null;
            _returnConfirmations = 0;
            _reacquiredFrames = 0;
            Tracking = false;
            Confidence = 0;
            Erase = false;
            Status = "The timeline jumped. Select the subject again to avoid following a different object.";
            Locked = false;
            _clean = _known = null;
            return null;
        }

        _lastPts = frame.Pts;
        if (_reference is null)
        {
            _reference = Sample(frame, Region.Left, Region.Top, Region.Width, Region.Height);
            _returnReference = (float[])_reference.Clone();
            _identityWidth = Math.Max(1, (int)Math.Round(Region.Width * frame.Width));
            _identityHeight = Math.Max(1, (int)Math.Round(Region.Height * frame.Height));
            var (selectedCentre, nearbyBorder, _) = CompareSubjectWithBorder(frame, Bounds(frame.Width, frame.Height));
            var borderContrast = Math.Abs(selectedCentre.B - nearbyBorder.B)
                + Math.Abs(selectedCentre.G - nearbyBorder.G)
                + Math.Abs(selectedCentre.R - nearbyBorder.R);
            _originalBorderContrast = borderContrast;
            if (!HasDistinctiveAppearance(_reference) && borderContrast < 40)
            {
                // A flat patch of sky, wall or clothing with no contrasting edges can
                // match thousands of unrelated places. Never claim an identity lock.
                Tracking = false;
                Erase = false;
                _reference = _returnReference = null;
                Confidence = 0;
                Status = "Selection has too little distinguishing detail. Pick a visible edge or textured area.";
                return null;
            }

            Tracking = true;
            Confidence = 1;
            Status = "Selected region locked. Tracking uses its visible texture, not a person recognizer.";
        }
        else if (!Tracking)
        {
            // Keep the original identity template after the target leaves view. A bounded
            // sparse scan periodically looks for it again; never resume from a weak or
            // ambiguous match, and never turn the removal preview on automatically.
            // No expiry while the original selection remains locked. Search at a bounded
            // cadence; require two consistent sightings before the viewport can follow again.
            _returnFrames = (_returnFrames + 1) % 4;
            if (_returnFrames != 0 || !TryReacquire(frame))
            {
                Status = _returnCandidate is null
                    ? "Tracking uncertain: searching the picture for the original texture."
                    : "Possible match: verifying the original texture before following.";
                return null;
            }
        }
        else if (Tracking)
        {
            // Search a coarse motion window first, then refine to individual pixels. The
            // previous fixed 9-pixel search lost fast-moving accessories even at 1080p.
            // The bounded window keeps the work deterministic on low-end WARP machines.
            var radius = Math.Clamp((int)Math.Round(Math.Min(
                frame.Width * Region.Width, frame.Height * Region.Height) * 0.9), 12, 64);
            var coarse = Math.Max(1, radius / 8);
            var best = double.MaxValue;
            var bestScore = double.MaxValue;
            var bestDx = 0;
            var bestDy = 0;
            void TryMatch(int dx, int dy)
            {
                var left = Region.Left + (float)dx / frame.Width;
                var top = Region.Top + (float)dy / frame.Height;
                if (left < 0 || top < 0 || left + Region.Width > 1 || top + Region.Height > 1)
                {
                    return;
                }

                var distance = Distance(frame, left, top, Region.Width, Region.Height, _reference);
                // A repeated or flat pattern may match dozens of places equally well.
                // Prefer continuity at the last known position to avoid drifting across
                // an unrelated but identically-coloured surface.
                var score = distance + 0.06 * Math.Sqrt((double)dx * dx + (double)dy * dy);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = distance;
                    bestDx = dx;
                    bestDy = dy;
                }
            }

            for (var dy = -radius; dy <= radius; dy += coarse)
            {
                for (var dx = -radius; dx <= radius; dx += coarse)
                {
                    TryMatch(dx, dy);
                }
            }

            var roughX = bestDx;
            var roughY = bestDy;
            for (var dy = roughY - coarse; dy <= roughY + coarse; dy++)
            {
                for (var dx = roughX - coarse; dx <= roughX + coarse; dx++)
                {
                    if (Math.Abs(dx) <= radius && Math.Abs(dy) <= radius)
                    {
                        TryMatch(dx, dy);
                    }
                }
            }

            Confidence = Math.Clamp(1 - best / 75, 0, 1);
            if (Confidence < 0.38)
            {
                Tracking = false;
                Erase = false;
                _returnFrames = 0;
                _returnCandidate = null;
                _returnConfirmations = 0;
                _reacquiredFrames = 0;
                Status = "Tracking uncertain: looking for the selected object to return.";
                return null;
            }

            // Distant candidate matches with the same appearance are ambiguous: do not
            // jump to a nearby identical shoe/person when the selected one is lost.
            if ((Math.Abs(bestDx) > 2 || Math.Abs(bestDy) > 2) && best < 12)
            {
                var original = Distance(frame, Region.Left, Region.Top,
                    Region.Width, Region.Height, _reference);
                if (original < best + 2)
                {
                    Tracking = false;
                    Erase = false;
                    Confidence = 0;
                    _returnFrames = 0;
                    _returnCandidate = null;
                    _returnConfirmations = 0;
                    _reacquiredFrames = 0;
                    Status = "Similar-looking regions: watching for a clear match. Select again if necessary.";
                    return null;
                }
            }

            Region = (Region.Left + (float)bestDx / frame.Width,
                Region.Top + (float)bestDy / frame.Height, Region.Width, Region.Height);
            // Update very slowly to accommodate lighting, without adopting a neighbouring target.
            SampleInto(frame, Region.Left, Region.Top, Region.Width, Region.Height, _samples);
            for (var i = 0; i < _reference.Length; i++)
            {
                _reference[i] = _reference[i] * 0.97f + _samples[i] * 0.03f;
            }

            Status = _reacquiredFrames > 0
                ? "Original visual texture found again. Tracking resumed."
                : "Tracking selected region.";
            if (_reacquiredFrames > 0)
            {
                _reacquiredFrames--;
            }
        }

        if (!Tracking)
        {
            return null;
        }

        // The earliest reference is only reliable for a stationary camera. A scene cut or
        // significant movement invalidates recovered pixels; do not paint a stale background.
        var pixels = frame.Width * frame.Height;
        if (_clean is null || _known is null)
        {
            _clean = new byte[pixels * 4];
            _known = new byte[pixels];
        }
        else if (CameraMoved(frame, _clean, _known))
        {
            Array.Clear(_known);
        }

        var box = Bounds(frame.Width, frame.Height);
        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < frame.Width; x++)
            {
                if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
                {
                    continue;
                }

                var at = y * frame.Width + x;
                var p = x * 4;
                _clean[at * 4] = row[p];
                _clean[at * 4 + 1] = row[p + 1];
                _clean[at * 4 + 2] = row[p + 2];
                _clean[at * 4 + 3] = 255;
                _known[at] = 1;
            }
        }

        if (!Erase)
        {
            return null;
        }

        var output = ColorConverter.ToBgra(frame);
        EstimatedPixels = 0;
        // A roughly chosen rectangle should not automatically erase pixels resembling the
        // surrounding backdrop. This colour-aware soft mask is deliberately conservative,
        // not a semantic segmentation or a reconstruction of obscured anatomy.
        var (foreground, background, difference) = CompareSubjectWithBorder(frame, box);
        var edited = 0;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            var row = output.Row(0, y);
            for (var x = box.Left; x < box.Right; x++)
            {
                var at = y * frame.Width + x;
                var p = x * 4;
                var edge = Math.Min(Math.Min(x - box.Left, box.Right - x - 1),
                    Math.Min(y - box.Top, box.Bottom - y - 1));
                var alpha = Feather == 0 ? 1.0 : Math.Min(1.0, (edge + 1) / (double)Feather);
                if (difference > 60)
                {
                    var foregroundDistance = ColourDistance(row, p, foreground);
                    var backgroundDistance = ColourDistance(row, p, background);
                    alpha *= Math.Clamp((backgroundDistance - foregroundDistance + MaskTolerance) / 100.0, 0, 1);
                }

                if (alpha < 0.01)
                {
                    continue;
                }

                edited++;
                var observed = _known[at] != 0;
                if (!observed)
                {
                    EstimatedPixels++;
                }

                for (var c = 0; c < 3; c++)
                {
                    var restored = observed ? _clean[at * 4 + c]
                        : EstimateFromBorders(frame, box, x, y, c);
                    row[p + c] = (byte)Math.Clamp(Math.Round(
                        row[p + c] * (1 - alpha) + restored * alpha), 0, 255);
                }
            }
        }

        Status = edited == 0
            ? "Selected region blends into its surroundings. Refine the selection; original is shown."
            : EstimatedPixels > 0
                ? "Estimated fill: some hidden pixels were never seen in the video. Preview only."
                : "Observed fill: using background pixels seen in other frames. Preview only.";
        return output;
    }

    /// <summary>
    /// Periodic, bounded full-frame re-acquisition after an object moves outside the picture
    /// or is hidden. Sparse colour-template comparisons shortlist candidates; only a sharp,
    /// spatially unambiguous full-resolution match can restore tracking. This is not a
    /// person recognizer and cannot distinguish two physically identical appearances.
    /// </summary>
    private bool TryReacquire(VideoFrame frame)
    {
        if (_returnReference is not { } fingerprint)
        {
            return false;
        }

        // The original appearance survives any number of missing frames. Search in
        // several sizes so a shirt that returns nearer or farther can still be found.
        // Limit candidate work and use sparse RGB samples before the full comparison.
        List<(double Error, int X, int Y, int Width, int Height)> shortlist = new(24);
        ReadOnlySpan<float> scales = stackalloc float[] { 0.7f, 0.85f, 1f, 1.3f, 1.5f };
        foreach (var scale in scales)
        {
            // Preserve a separate shortlist per scale: otherwise proposals from one
            // patch size can evict another size's correct match or a distant lookalike.
            List<(double Error, int X, int Y, int Width, int Height)> candidates = new(8);
            var patchWidth = Math.Clamp((int)Math.Round(_identityWidth * scale), 1, frame.Width);
            var patchHeight = Math.Clamp((int)Math.Round(_identityHeight * scale), 1, frame.Height);
            var maxX = frame.Width - patchWidth;
            var maxY = frame.Height - patchHeight;
            var stride = Math.Max(2, Math.Max(Math.Min(patchWidth, patchHeight) / 3,
                Math.Max(frame.Width / 200, frame.Height / 130)));
            for (var y = 0; y <= maxY; y += stride)
            {
                for (var x = 0; x <= maxX; x += stride)
                {
                    var error = SparseDistance(frame, x, y, patchWidth, patchHeight, fingerprint);
                    if (error > 105)
                    {
                        continue;
                    }

                    // Proposals for the same physical patch at this scale compete for
                    // one shortlist place; spatially separate matches remain to guard
                    // against visually identical subjects.
                    var existing = -1;
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        if (Math.Abs(candidate.X + candidate.Width / 2 - x - patchWidth / 2) <
                                Math.Min(candidate.Width, patchWidth) * 0.75 &&
                            Math.Abs(candidate.Y + candidate.Height / 2 - y - patchHeight / 2) <
                                Math.Min(candidate.Height, patchHeight) * 0.75)
                        {
                            existing = i;
                            break;
                        }
                    }

                    if (existing >= 0)
                    {
                        if (candidates[existing].Error <= error)
                        {
                            continue;
                        }

                        candidates.RemoveAt(existing);
                    }

                    candidates.Add((error, x, y, patchWidth, patchHeight));
                    candidates.Sort((a, b) => a.Error.CompareTo(b.Error));
                    if (candidates.Count > 8)
                    {
                        candidates.RemoveAt(8);
                    }
                }
            }

            shortlist.AddRange(candidates);
        }

        List<(double Error, int X, int Y, int Width, int Height)> refined = new(shortlist.Count);
        foreach (var candidate in shortlist)
        {
            var bestError = double.MaxValue;
            var bestX = candidate.X;
            var bestY = candidate.Y;
            var maxX = frame.Width - candidate.Width;
            var maxY = frame.Height - candidate.Height;
            var radius = Math.Max(2, Math.Min(candidate.Width, candidate.Height) / 3);
            var refinementStep = Math.Max(1, radius / 4);
            for (var y = Math.Max(0, candidate.Y - radius); y <= Math.Min(maxY, candidate.Y + radius); y += refinementStep)
            {
                for (var x = Math.Max(0, candidate.X - radius); x <= Math.Min(maxX, candidate.X + radius); x += refinementStep)
                {
                    var error = ReturnDistance(frame, (float)x / frame.Width, (float)y / frame.Height,
                        (float)candidate.Width / frame.Width, (float)candidate.Height / frame.Height, fingerprint);
                    if (error < bestError)
                    {
                        (bestError, bestX, bestY) = (error, x, y);
                    }
                }
            }

            var centreX = bestX;
            var centreY = bestY;
            for (var y = Math.Max(0, centreY - refinementStep); y <= Math.Min(maxY, centreY + refinementStep); y++)
            {
                for (var x = Math.Max(0, centreX - refinementStep); x <= Math.Min(maxX, centreX + refinementStep); x++)
                {
                    var error = ReturnDistance(frame, (float)x / frame.Width, (float)y / frame.Height,
                        (float)candidate.Width / frame.Width, (float)candidate.Height / frame.Height, fingerprint);
                    if (error < bestError)
                    {
                        (bestError, bestX, bestY) = (error, x, y);
                    }
                }
            }

            // Matching only the middle of a larger object can favour an undersized
            // patch. When the original selection touched visible object boundaries,
            // compare that foreground-to-surroundings contrast as an additional spatial
            // cue. Keep texture-only matching for interior selections with no boundary.
            if (_originalBorderContrast >= 40)
            {
                var (centre, outside, _) = CompareSubjectWithBorder(frame,
                    (bestX, bestY, bestX + candidate.Width, bestY + candidate.Height));
                var contrast = Math.Abs(centre.B - outside.B)
                    + Math.Abs(centre.G - outside.G)
                    + Math.Abs(centre.R - outside.R);
                bestError += Math.Max(0, _originalBorderContrast - contrast) * 0.12;
            }

            refined.Add((bestError, bestX, bestY, candidate.Width, candidate.Height));
        }

        refined.Sort((a, b) => a.Error.CompareTo(b.Error));
        if (refined.Count == 0 || refined[0].Error > 22)
        {
            _returnCandidate = null;
            _returnConfirmations = 0;
            return false;
        }

        var best = refined[0];
        // A similarly patterned shirt at another location must not steal the lock.
        if (refined.Skip(1).Any(candidate =>
            (Math.Abs(candidate.X + candidate.Width / 2 - best.X - best.Width / 2) >=
                 Math.Min(candidate.Width, best.Width) * 0.75 ||
             Math.Abs(candidate.Y + candidate.Height / 2 - best.Y - best.Height / 2) >=
                 Math.Min(candidate.Height, best.Height) * 0.75) &&
            candidate.Error <= best.Error + 9))
        {
            _returnCandidate = null;
            _returnConfirmations = 0;
            return false;
        }

        if (_returnCandidate is { } previous &&
            Math.Abs(previous.X + previous.Width / 2 - best.X - best.Width / 2) <
                Math.Max(12, best.Width * 3) &&
            Math.Abs(previous.Y + previous.Height / 2 - best.Y - best.Height / 2) <
                Math.Max(12, best.Height * 3) &&
            Math.Abs(previous.Width - best.Width) <= Math.Max(4, best.Width / 3) &&
            Math.Abs(previous.Height - best.Height) <= Math.Max(4, best.Height / 3))
        {
            _returnConfirmations++;
        }
        else
        {
            _returnConfirmations = 1;
        }

        _returnCandidate = (best.X, best.Y, best.Width, best.Height);
        if (_returnConfirmations < 2)
        {
            return false;
        }

        Region = ((float)best.X / frame.Width, (float)best.Y / frame.Height,
            (float)best.Width / frame.Width, (float)best.Height / frame.Height);
        _reference = Sample(frame, Region.Left, Region.Top, Region.Width, Region.Height);
        Tracking = true;
        Erase = false;
        _returnFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 15;
        Confidence = Math.Clamp(1 - best.Error / 75, 0, 1);
        Status = "Original visual texture found again. Tracking resumed.";
        return true;
    }

    /// <summary>
    /// Compare texture after allowing a small, uniform lighting shift. Absolute RGB matching
    /// misses the same moving object when the scene brightens or darkens. Keep a colour-shift
    /// penalty so a differently coloured lookalike is not treated as an identical match.
    /// Only the lost-subject search uses this; ordinary frame-to-frame tracking is unchanged.
    /// </summary>
    private static double ReturnDistance(VideoFrame frame, float left, float top, float width,
        float height, float[] reference)
    {
        Span<double> shift = stackalloc double[3];
        for (var y = 2; y < Samples; y += 4)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 2; x < Samples; x += 4)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                var at = (y * Samples + x) * 3;
                for (var channel = 0; channel < 3; channel++)
                {
                    shift[channel] += row[fx + channel] - reference[at + channel];
                }
            }
        }

        for (var channel = 0; channel < 3; channel++)
        {
            shift[channel] = Math.Clamp(shift[channel] / 16, -60, 60);
        }

        var original = 0d;
        var adjusted = 0d;
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 0; x < Samples; x++)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = row[fx + channel] - reference[i++];
                    original += Math.Abs(difference);
                    adjusted += Math.Abs(difference - shift[channel]);
                }
            }
        }

        const int components = Samples * Samples * 3;
        var lightingPenalty = (Math.Abs(shift[0]) + Math.Abs(shift[1]) + Math.Abs(shift[2])) * 0.16 / 3;
        return Math.Min(original / components, adjusted / components + lightingPenalty);
    }

    /// <summary>Cheap 4-by-4 thumbnail comparison before full template refinement.</summary>
    private static double SparseDistance(VideoFrame frame, int left, int top, int width, int height, float[] reference)
    {
        var error = 0d;
        for (var y = 2; y < Samples; y += 4)
        {
            var sourceY = Math.Min(frame.Height - 1, top + (int)((y + 0.5f) * height / Samples));
            var row = frame.Row(0, sourceY);
            for (var x = 2; x < Samples; x += 4)
            {
                var sourceX = Math.Min(frame.Width - 1, left + (int)((x + 0.5f) * width / Samples));
                var sample = sourceX * 4;
                var at = (y * Samples + x) * 3;
                error += Math.Abs(row[sample] - reference[at]);
                error += Math.Abs(row[sample + 1] - reference[at + 1]);
                error += Math.Abs(row[sample + 2] - reference[at + 2]);
            }
        }

        return error / 48;
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

    private static int ColourDistance(ReadOnlySpan<byte> row, int index, (int B, int G, int R) colour) =>
        Math.Abs(row[index] - colour.B) + Math.Abs(row[index + 1] - colour.G)
        + Math.Abs(row[index + 2] - colour.R);

    /// <summary>
    /// Smooth four-sided boundary interpolation for a never-observed area. This is a plausible
    /// flat-surface estimate, *not* inference of the actual physically hidden object. It uses
    /// only original pixels outside the selected area, including selections touching an edge.
    /// </summary>
    private static double EstimateFromBorders(VideoFrame frame,
        (int Left, int Top, int Right, int Bottom) box, int x, int y, int channel)
    {
        double total = 0;
        double weight = 0;
        var width = box.Right - box.Left + 1;
        var height = box.Bottom - box.Top + 1;
        if (box.Left > 0)
        {
            var w = (double)(box.Right - x) / width;
            total += w * frame.Row(0, y)[(box.Left - 1) * 4 + channel];
            weight += w;
        }

        if (box.Right < frame.Width)
        {
            var w = (double)(x - box.Left + 1) / width;
            total += w * frame.Row(0, y)[box.Right * 4 + channel];
            weight += w;
        }

        if (box.Top > 0)
        {
            var w = (double)(box.Bottom - y) / height;
            total += w * frame.Row(0, box.Top - 1)[x * 4 + channel];
            weight += w;
        }

        if (box.Bottom < frame.Height)
        {
            var w = (double)(y - box.Top + 1) / height;
            total += w * frame.Row(0, box.Bottom)[x * 4 + channel];
            weight += w;
        }

        return weight > 0 ? total / weight : frame.Row(0, y)[x * 4 + channel];
    }

    private (int Left, int Top, int Right, int Bottom) Bounds(int width, int height) =>
        (Math.Clamp((int)(Region.Left * width), 0, width - 1),
         Math.Clamp((int)(Region.Top * height), 0, height - 1),
         Math.Clamp((int)Math.Ceiling((Region.Left + Region.Width) * width), 1, width),
         Math.Clamp((int)Math.Ceiling((Region.Top + Region.Height) * height), 1, height));

    /// <summary>
    /// Reject a textureless, borderless patch before announcing a confident lock. Colour
    /// channels are measured separately so a solid colourful region is not mistaken for
    /// a textured one just because its red and blue values differ.
    /// </summary>
    private static bool HasDistinctiveAppearance(float[] template)
    {
        var averages = new double[3];
        for (var i = 0; i < template.Length; i++)
        {
            averages[i % 3] += template[i];
        }

        for (var channel = 0; channel < 3; channel++)
        {
            averages[channel] /= template.Length / 3;
        }

        var difference = 0d;
        for (var i = 0; i < template.Length; i++)
        {
            difference += Math.Abs(template[i] - averages[i % 3]);
        }

        return difference / template.Length >= 3;
    }

    private static float[] Sample(VideoFrame frame, float left, float top, float width, float height)
    {
        var samples = new float[Samples * Samples * 3];
        SampleInto(frame, left, top, width, height, samples);
        return samples;
    }

    private static void SampleInto(VideoFrame frame, float left, float top, float width, float height,
        Span<float> samples)
    {
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 0; x < Samples; x++)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                samples[i++] = row[fx];
                samples[i++] = row[fx + 1];
                samples[i++] = row[fx + 2];
            }
        }

    }

    private static double Distance(VideoFrame frame, float left, float top, float width, float height, float[] reference)
    {
        var error = 0d;
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 0; x < Samples; x++)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                error += Math.Abs(row[fx] - reference[i++]);
                error += Math.Abs(row[fx + 1] - reference[i++]);
                error += Math.Abs(row[fx + 2] - reference[i++]);
            }
        }

        return error / (Samples * Samples * 3);
    }

    private static bool CameraMoved(VideoFrame frame, byte[] history, byte[] seen)
    {
        var compared = 0;
        var changed = 0;
        for (var y = 0; y < frame.Height; y += Math.Max(1, frame.Height / 16))
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < frame.Width; x += Math.Max(1, frame.Width / 20))
            {
                var at = y * frame.Width + x;
                if (seen[at] == 0)
                {
                    continue;
                }

                compared++;
                var p = x * 4;
                if (Math.Abs(row[p] - history[at * 4]) +
                    Math.Abs(row[p + 1] - history[at * 4 + 1]) +
                    Math.Abs(row[p + 2] - history[at * 4 + 2]) > 135)
                {
                    changed++;
                }
            }
        }

        return compared >= 32 && changed * 5 > compared;
    }
}
