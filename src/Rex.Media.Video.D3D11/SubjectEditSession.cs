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

    private float[]? _reference;
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
            Tracking = false;
        }

        if (_lastPts.IsKnown && frame.Pts.IsKnown
            && (frame.Pts < _lastPts || frame.Pts - _lastPts > MediaTime.FromSeconds(2)))
        {
            _reference = null;
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
            Tracking = true;
            Confidence = 1;
            Status = "Selected region locked. Tracking uses its visible texture, not a person recognizer.";
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
                if (distance < best)
                {
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
                Status = "Tracking uncertain. The selection is frozen; choose the object again.";
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
                    Status = "Similar-looking regions: select the intended subject again.";
                    return null;
                }
            }

            Region = (Region.Left + (float)bestDx / frame.Width,
                Region.Top + (float)bestDy / frame.Height, Region.Width, Region.Height);
            // Update very slowly to accommodate lighting, without adopting a neighbouring target.
            var fresh = Sample(frame, Region.Left, Region.Top, Region.Width, Region.Height);
            for (var i = 0; i < _reference.Length; i++)
            {
                _reference[i] = _reference[i] * 0.97f + fresh[i] * 0.03f;
            }

            Status = "Tracking selected region.";
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
        for (var y = box.Top; y < box.Bottom; y++)
        {
            var row = output.Row(0, y);
            for (var x = box.Left; x < box.Right; x++)
            {
                var at = y * frame.Width + x;
                var p = x * 4;
                if (_known[at] != 0)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        row[p + c] = _clean[at * 4 + c];
                    }
                }
                else
                {
                    // An entirely unseen patch is an *estimate*, never a recovered pixel:
                    // interpolate nearby observed surface colours with bounded image coordinates.
                    EstimatedPixels++;
                    var a = Math.Max(0, box.Left - 1);
                    var b = Math.Min(frame.Width - 1, box.Right);
                    var alpha = box.Right == box.Left ? 0.5 : (double)(x - box.Left + 1) / (box.Right - box.Left + 1);
                    for (var c = 0; c < 3; c++)
                    {
                        var first = row[a * 4 + c];
                        var last = row[b * 4 + c];
                        row[p + c] = (byte)Math.Clamp(Math.Round(first * (1 - alpha) + last * alpha), 0, 255);
                    }
                }

                row[p + 3] = 255;
            }
        }

        Status = EstimatedPixels > 0
            ? "Estimated fill: parts of the hidden area were never observed. Preview only."
            : "Observed fill: using pixels revealed in other frames. Preview only.";
        return output;
    }

    private (int Left, int Top, int Right, int Bottom) Bounds(int width, int height) =>
        (Math.Clamp((int)(Region.Left * width), 0, width - 1),
         Math.Clamp((int)(Region.Top * height), 0, height - 1),
         Math.Clamp((int)Math.Ceiling((Region.Left + Region.Width) * width), 1, width),
         Math.Clamp((int)Math.Ceiling((Region.Top + Region.Height) * height), 1, height));

    private static float[] Sample(VideoFrame frame, float left, float top, float width, float height)
    {
        var samples = new float[Samples * Samples * 3];
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

        return samples;
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
