using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// How the camera moved between two pictures, as a zoom and a shift: the point at (x, y) in the
/// newer picture showed what the point at (Scale * x + ShiftX, Scale * y + ShiftY) showed in the
/// older one. Pans and zooms are what a camera mostly does between two frames.
/// </summary>
public readonly record struct CameraMotion(float Scale, float ShiftX, float ShiftY)
{
    /// <summary>A camera that did not move.</summary>
    public static CameraMotion Still { get; } = new(1, 0, 0);

    /// <summary>Where the point at (<paramref name="x"/>, <paramref name="y"/>) of the newer picture was in the older one.</summary>
    public (float X, float Y) Previous(float x, float y) => (Scale * x + ShiftX, Scale * y + ShiftY);
}

/// <summary>
/// Estimates the camera's motion between consecutive pictures from a small grey copy of each, with
/// the subject's box left out so the subject's own movement is not mistaken for the camera's.
/// <para>
/// Method: hierarchical model-based motion estimation (Bergen, Anandan, Hanna and Hingorani,
/// "Hierarchical Model-Based Motion Estimation", ECCV 1992). A three-level pyramid is block-matched
/// coarse to fine for the shift, with a parabola through the finest scores for its fraction; then
/// zoom and shift are refined by Gauss-Newton steps on the brightness-constancy residual (Lucas
/// and Kanade, "An Iterative Image Registration Technique", IJCAI 1981) at full or near-full
/// resolution over the area around the subject, because a background recorded from many pictures
/// is only as sharp as the sum of their alignment errors. Residuals are Huber-weighted so that
/// people and other independently moving things count less than the background (Black and
/// Anandan, "The Robust Estimation of Multiple Motions", CVIU 63(1), 1996).
/// </para>
/// </summary>
public sealed class SubjectRemovalMotion
{
    /// <summary>A mean grey-level error after alignment above which two pictures are different shots.</summary>
    public const float CutResidual = 24;

    private const int Across = 160;
    private const int Levels = 3;
    private const int Search = 6;
    private const int MinimumSamples = 24;
    private const float Huber = 8;
    // The detailed stage reads at most this many pixels and fits at most this many samples.
    private const int DetailBudget = 120_000;
    private const int DetailSamples = 4_000;

    private readonly float[][] _previous = [[], [], []];
    private readonly float[][] _current = [[], [], []];
    private readonly int[] _widths = new int[Levels];
    private readonly int[] _heights = new int[Levels];
    private int _frameWidth;
    private int _frameHeight;
    private int _step = 1;
    private bool _hasPrevious;
    private float[] _detail = [];
    private float[] _detailPrevious = [];
    private (int Left, int Top, int Width, int Height, int Step) _detailArea;
    private (int Left, int Top, int Width, int Height, int Step) _detailPreviousArea;

    /// <summary>The mean grey-level difference left after the last alignment, from 0 to 255.</summary>
    public float Residual { get; private set; }

    /// <summary>Whether the last picture belongs to a different shot from the one before it.</summary>
    public bool Cut { get; private set; }

    /// <summary>Forget the previous picture, so the next one starts a new sequence.</summary>
    public void Reset()
    {
        _hasPrevious = false;
        Cut = false;
        Residual = 0;
    }

    /// <summary>
    /// The camera's motion from the previous picture to <paramref name="frame"/> (BGRA), ignoring the
    /// pixels inside <paramref name="exclude"/> and refined in detail over <paramref name="detail"/>.
    /// The first picture, and any picture with too little visible background to measure, report a
    /// still camera.
    /// </summary>
    public CameraMotion Estimate(VideoFrame frame, (int Left, int Top, int Right, int Bottom) exclude,
        (int Left, int Top, int Right, int Bottom) detail)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != _frameWidth || frame.Height != _frameHeight)
        {
            Allocate(frame.Width, frame.Height);
        }

        Shrink(frame);
        ReadDetail(frame, detail);
        Cut = false;
        Residual = 0;
        var motion = CameraMotion.Still;
        if (_hasPrevious && Align(exclude) is { } coarse)
        {
            motion = Snap(Cut ? coarse : RefineDetail(coarse, exclude));
        }

        _hasPrevious = true;
        for (var level = 0; level < Levels; level++)
        {
            (_previous[level], _current[level]) = (_current[level], _previous[level]);
        }

        (_detail, _detailPrevious) = (_detailPrevious, _detail);
        _detailPreviousArea = _detailArea;
        return motion;
    }

    /// <summary>Noise in an unmoving shot must not make a recorded background creep: under half a pixel anywhere is still.</summary>
    private CameraMotion Snap(CameraMotion motion)
    {
        var reach = Math.Abs(motion.Scale - 1) * Math.Max(_frameWidth, _frameHeight) / 2;
        return reach < 0.5f && Math.Abs(motion.ShiftX) < 0.3f && Math.Abs(motion.ShiftY) < 0.3f
            ? CameraMotion.Still
            : motion;
    }

    /// <summary>A grey copy of the detail area, two-by-two averaged when it is too big to read whole.</summary>
    private void ReadDetail(VideoFrame frame, (int Left, int Top, int Right, int Bottom) area)
    {
        var left = Math.Clamp(area.Left, 0, frame.Width - 1);
        var top = Math.Clamp(area.Top, 0, frame.Height - 1);
        var width = Math.Clamp(area.Right, left + 1, frame.Width) - left;
        var height = Math.Clamp(area.Bottom, top + 1, frame.Height) - top;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / DetailBudget)));
        var (w, h) = (Math.Max(1, width / step), Math.Max(1, height / step));
        SubjectRemovalMatte.Ensure(ref _detail, w * h);
        _detailArea = (left, top, w, h, step);
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var far = step / 2;
        for (var y = 0; y < h; y++)
        {
            var row0 = (top + y * step) * stride;
            var row1 = (top + y * step + far) * stride;
            for (var x = 0; x < w; x++)
            {
                var a = (left + x * step) * 4;
                var b = (left + x * step + far) * 4;
                _detail[y * w + x] = (Grey(plane, row0 + a) + Grey(plane, row0 + b)
                    + Grey(plane, row1 + a) + Grey(plane, row1 + b)) * 0.25f;
            }
        }
    }

    /// <summary>
    /// Gauss-Newton refinement of zoom and shift on samples of the detail area, against the previous
    /// picture's detail area, starting from the pyramid's estimate.
    /// </summary>
    private CameraMotion RefineDetail(CameraMotion motion, (int Left, int Top, int Right, int Bottom) exclude)
    {
        var current = _detailArea;
        var previous = _detailPreviousArea;
        var sampleStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)current.Width * current.Height / DetailSamples)));
        // The zoom is fitted about the area's centre so its column of the system is well scaled.
        var cx = current.Left + current.Width * current.Step / 2f;
        var cy = current.Top + current.Height * current.Step / 2f;
        var (scale, u, v) = (motion.Scale, motion.ShiftX + (motion.Scale - 1) * cx, motion.ShiftY + (motion.Scale - 1) * cy);
        var offset = current.Step / 2 / 2f;
        var previousOffset = previous.Step / 2 / 2f;
        for (var iteration = 0; iteration < 6; iteration++)
        {
            double h00 = 0, h01 = 0, h02 = 0, h11 = 0, h12 = 0, h22 = 0, b0 = 0, b1 = 0, b2 = 0;
            var samples = 0;
            for (var blockY = 0; blockY < current.Height; blockY += sampleStep)
            {
                for (var blockX = 0; blockX < current.Width; blockX += sampleStep)
                {
                    // One sample a block at a scrambled place in it: a regular lattice of samples
                    // aliases with fine texture and drags the fit off the true shift.
                    var scramble = (uint)(blockX * 73856093 ^ blockY * 19349663);
                    var i = blockX + (int)(scramble % (uint)sampleStep);
                    var j = blockY + (int)(scramble / 7 % (uint)sampleStep);
                    var x = current.Left + i * current.Step + offset;
                    var y = current.Top + j * current.Step + offset;
                    if (i >= current.Width || j >= current.Height
                        || (x >= exclude.Left && x < exclude.Right && y >= exclude.Top && y < exclude.Bottom))
                    {
                        continue;
                    }

                    var px = (scale * (x - cx) + cx + u - previous.Left - previousOffset) / previous.Step;
                    var py = (scale * (y - cy) + cy + v - previous.Top - previousOffset) / previous.Step;
                    if (px < 1 || py < 1 || px >= previous.Width - 2 || py >= previous.Height - 2)
                    {
                        continue;
                    }

                    var (value, gx, gy) = Interpolate(_detailPrevious, previous.Width, px, py);
                    var r = value - _detail[j * current.Width + i];
                    (gx, gy) = (gx / previous.Step, gy / previous.Step);
                    var magnitude = Math.Abs(r);
                    var weight = magnitude <= Huber ? 1f : Huber / magnitude;
                    var j0 = gx * (x - cx) + gy * (y - cy);
                    h00 += weight * j0 * j0;
                    h01 += weight * j0 * gx;
                    h02 += weight * j0 * gy;
                    h11 += weight * gx * gx;
                    h12 += weight * gx * gy;
                    h22 += weight * gy * gy;
                    b0 += weight * j0 * r;
                    b1 += weight * gx * r;
                    b2 += weight * gy * r;
                    samples++;
                }
            }

            if (samples < MinimumSamples)
            {
                return motion;
            }

            h00 = h00 * 1.02 + 1e-3;
            var (da, du, dv) = Solve(h00, h01, h02, h11, h12, h22, -b0, -b1, -b2);
            scale = Math.Clamp(scale + (float)da, 0.85f, 1.15f);
            u += (float)du;
            v += (float)dv;
            if (Math.Abs(du) + Math.Abs(dv) < 0.001 && Math.Abs(da) < 1e-6)
            {
                break;
            }
        }

        // A zoom that moves nothing by a quarter of a pixel is noise; dropping it keeps the recorded
        // background on whole-pixel arithmetic, which is both sharper and faster.
        return Math.Abs(scale - 1) * Math.Max(current.Width, current.Height) * current.Step / 2 < 0.25f
            ? new CameraMotion(1, u, v)
            : new CameraMotion(scale, u - (scale - 1) * cx, v - (scale - 1) * cy);
    }

    private void Allocate(int width, int height)
    {
        (_frameWidth, _frameHeight) = (width, height);
        _step = Math.Max(1, (Math.Max(width, height) + Across - 1) / Across);
        var (w, h) = (Math.Max(1, width / _step), Math.Max(1, height / _step));
        for (var level = 0; level < Levels; level++)
        {
            (_widths[level], _heights[level]) = (w, h);
            _previous[level] = new float[w * h];
            _current[level] = new float[w * h];
            (w, h) = (Math.Max(1, w / 2), Math.Max(1, h / 2));
        }

        _hasPrevious = false;
    }

    /// <summary>Reads a grey copy, averaging four samples a cell, then halves it twice.</summary>
    private void Shrink(VideoFrame frame)
    {
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var (w, h) = (_widths[0], _heights[0]);
        var top = _current[0];
        var near = _step / 4;
        var far = _step * 3 / 4;
        for (var y = 0; y < h; y++)
        {
            var row0 = (y * _step + near) * stride;
            var row1 = (y * _step + far) * stride;
            for (var x = 0; x < w; x++)
            {
                var a = (x * _step + near) * 4;
                var b = (x * _step + far) * 4;
                top[y * w + x] = (Grey(plane, row0 + a) + Grey(plane, row0 + b)
                    + Grey(plane, row1 + a) + Grey(plane, row1 + b)) * 0.25f;
            }
        }

        for (var level = 1; level < Levels; level++)
        {
            var (fine, fw, fh) = (_current[level - 1], _widths[level - 1], _heights[level - 1]);
            var (coarse, cw, ch) = (_current[level], _widths[level], _heights[level]);
            for (var y = 0; y < ch; y++)
            {
                var y0 = Math.Min(fh - 1, y * 2) * fw;
                var y1 = Math.Min(fh - 1, y * 2 + 1) * fw;
                for (var x = 0; x < cw; x++)
                {
                    var x0 = Math.Min(fw - 1, x * 2);
                    var x1 = Math.Min(fw - 1, x * 2 + 1);
                    coarse[y * cw + x] = (fine[y0 + x0] + fine[y0 + x1] + fine[y1 + x0] + fine[y1 + x1]) * 0.25f;
                }
            }
        }
    }

    private static float Grey(Span<byte> plane, int at) =>
        0.114f * plane[at] + 0.587f * plane[at + 1] + 0.299f * plane[at + 2];

    /// <summary>
    /// Block matching down the pyramid: every whole-cell shift on the coarsest level, then the cell
    /// either side of the doubled estimate on each finer one, and a parabola through the finest
    /// level's scores for the fraction of a cell. Matching scores cannot be led astray by a gradient,
    /// so a repetitive wall does not trap the estimate a pattern away from the truth.
    /// </summary>
    private CameraMotion? Align((int Left, int Top, int Right, int Bottom) exclude)
    {
        Span<float> scores = stackalloc float[(2 * Search + 1) * (2 * Search + 1)];
        var (x, y, cx, cy, reach) = (0, 0, 0, 0, Search);
        var mean = 0f;
        for (var level = Levels - 1; level >= 0; level--)
        {
            (cx, cy, reach) = level == Levels - 1 ? (0, 0, Search) : (x * 2, y * 2, 1);
            if (SearchShift(level, exclude, cx, cy, reach, scores) is not { } best)
            {
                return null;
            }

            (x, y, mean) = best;
        }

        var side = 2 * reach + 1;
        var (ox, oy) = (x - cx + reach, y - cy + reach);
        var at = oy * side + ox;
        var fractionX = ox > 0 && ox < side - 1 ? Vertex(scores[at - 1], scores[at], scores[at + 1]) : 0;
        var fractionY = oy > 0 && oy < side - 1 ? Vertex(scores[at - side], scores[at], scores[at + side]) : 0;
        Residual = mean;
        Cut = mean > CutResidual;
        // A cell's centre lies half a cell in from its corner, the same in both pictures.
        return new CameraMotion(1, _step * (x + fractionX), _step * (y + fractionY));
    }

    /// <summary>Where a parabola through three equally spaced scores is lowest, as an offset from the middle one.</summary>
    internal static float Vertex(float before, float middle, float after)
    {
        var curvature = before - 2 * middle + after;
        return curvature > 0 && before < float.MaxValue && after < float.MaxValue
            ? Math.Clamp(0.5f * (before - after) / curvature, -0.5f, 0.5f)
            : 0;
    }

    private (int Left, int Top, int Right, int Bottom) Excluded(int level, (int Left, int Top, int Right, int Bottom) exclude)
    {
        var cell = _step << level;
        return (exclude.Left / cell - 1, exclude.Top / cell - 1,
            (exclude.Right + cell - 1) / cell + 1, (exclude.Bottom + cell - 1) / cell + 1);
    }

    /// <summary>
    /// The whole-cell shift within <paramref name="reach"/> of (<paramref name="cx"/>, <paramref name="cy"/>)
    /// with the least mean difference, with every score kept in <paramref name="scores"/>; nothing when
    /// too little background is visible.
    /// </summary>
    private (int X, int Y, float Mean)? SearchShift(int level, (int Left, int Top, int Right, int Bottom) exclude,
        int cx, int cy, int reach, Span<float> scores)
    {
        var (w, h) = (_widths[level], _heights[level]);
        var (previous, current) = (_previous[level], _current[level]);
        var box = Excluded(level, exclude);
        var side = 2 * reach + 1;
        var best = float.MaxValue;
        (int X, int Y, float Mean)? found = null;
        for (var oy = -reach; oy <= reach; oy++)
        {
            for (var ox = -reach; ox <= reach; ox++)
            {
                var (dx, dy) = (cx + ox, cy + oy);
                var total = 0f;
                var count = 0;
                for (var y = Math.Max(0, -dy); y < Math.Min(h, h - dy); y++)
                {
                    for (var x = Math.Max(0, -dx); x < Math.Min(w, w - dx); x++)
                    {
                        if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
                        {
                            continue;
                        }

                        total += Math.Abs(previous[(y + dy) * w + x + dx] - current[y * w + x]);
                        count++;
                    }
                }

                var mean = count < MinimumSamples ? float.MaxValue : total / count;
                scores[(oy + reach) * side + ox + reach] = mean;
                // On the coarsest level a slight preference for no movement keeps a flat scene still.
                var score = mean + (reach == Search ? 0.05f * (Math.Abs(dx) + Math.Abs(dy)) : 0);
                if (score < best)
                {
                    best = score;
                    found = (dx, dy, mean);
                }
            }
        }

        return found;
    }

    /// <summary>Solves the symmetric three-by-three system, or only its shift part when the zoom is undetermined.</summary>
    internal static (double A, double U, double V) Solve(double h00, double h01, double h02,
        double h11, double h12, double h22, double b0, double b1, double b2)
    {
        var c00 = h11 * h22 - h12 * h12;
        var c01 = h02 * h12 - h01 * h22;
        var c02 = h01 * h12 - h02 * h11;
        var determinant = h00 * c00 + h01 * c01 + h02 * c02;
        if (Math.Abs(determinant) > 1e-9 * Math.Max(1, h00 * h11 * h22))
        {
            var c11 = h00 * h22 - h02 * h02;
            var c12 = h01 * h02 - h00 * h12;
            var c22 = h00 * h11 - h01 * h01;
            return ((c00 * b0 + c01 * b1 + c02 * b2) / determinant,
                (c01 * b0 + c11 * b1 + c12 * b2) / determinant,
                (c02 * b0 + c12 * b1 + c22 * b2) / determinant);
        }

        var shift = h11 * h22 - h12 * h12;
        return Math.Abs(shift) > 1e-9
            ? (0, (h22 * b1 - h12 * b2) / shift, (h11 * b2 - h12 * b1) / shift)
            : (0, 0, 0);
    }

    /// <summary>
    /// The bilinear interpolant at (x, y) and its exact derivatives. Using the interpolant's own
    /// gradient makes each Gauss-Newton step descend the very error being minimised, so the fit
    /// settles at its minimum rather than where a smoothed gradient balances.
    /// </summary>
    internal static (float Value, float X, float Y) Interpolate(float[] values, int width, float x, float y)
    {
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = x - x0;
        var fy = y - y0;
        var at = y0 * width + x0;
        var (a, b, c, d) = (values[at], values[at + 1], values[at + width], values[at + width + 1]);
        var top = a + (b - a) * fx;
        var bottom = c + (d - c) * fx;
        return (top + (bottom - top) * fy, (b - a) * (1 - fy) + (d - c) * fy, bottom - top);
    }
}
