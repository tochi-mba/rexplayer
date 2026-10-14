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
/// <para>
/// A camera's motion changes little from one picture to the next, so the motion the last picture
/// found is tried first: refined in detail from there, it is kept when it fits as well as the last
/// picture's did, and the pyramid search runs only when it does not, as after a jolt or a cut. The
/// whole picture's pyramid is then needed only for that search, so it is built only for it, and at
/// least every few pictures so it never grows stale; the search compares with it however old it is
/// and allows for the motion since.
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
    // How many cells of a pyramid level score one shift.
    private const int ScoredCells = 3_000;
    // How many of the coarsest level's best shifts are followed down the pyramid.
    private const int Candidates = 3;
    private const float Huber = 8;
    // The detailed stage reads at most this many pixels and fits at most this many samples.
    private const int DetailBudget = 400_000;
    private const int DetailSamples = 4_000;
    // The most pictures the whole picture's pyramid may be older than the latest picture.
    private const int OldestPyramid = 6;
    // A predicted motion is kept when its detailed fit leaves at most this much more difference than
    // the last picture's did, in grey levels, and never more than this much in all.
    private const float Settling = 1.2f;
    private const float SettlingSlack = 1;
    private const float MostSettled = CutResidual / 2;

    private readonly float[][] _previous = [[], [], []];
    private readonly float[][] _current = [[], [], []];
    private readonly int[] _widths = new int[Levels];
    private readonly int[] _heights = new int[Levels];
    private readonly CameraMotion[] _candidates = new CameraMotion[Candidates + 4];
    private CameraMotion _lastMotion = CameraMotion.Still;
    private int _frameWidth;
    private int _frameHeight;
    private int _step = 1;
    private bool _hasPrevious;
    // How many pictures the pyramid in _previous is older than the latest picture, and the motion
    // from the latest picture back to it.
    private int _pyramidAge;
    private CameraMotion _sincePyramid = CameraMotion.Still;
    // The mean difference the latest picture's detailed fit left, or infinity when unknown.
    private float _settled = float.PositiveInfinity;
    private float[][] _detail = [[], [], []];
    private float[][] _detailPrevious = [[], [], []];
    private DetailLevel[] _detailArea = new DetailLevel[Levels];
    private DetailLevel[] _detailPreviousArea = new DetailLevel[Levels];

    /// <summary>The mean grey-level difference left after the last alignment, from 0 to 255.</summary>
    public float Residual { get; private set; }

    /// <summary>Whether the last picture belongs to a different shot from the one before it.</summary>
    public bool Cut { get; private set; }

    /// <summary>Forget the previous picture, so the next one starts a new sequence.</summary>
    public void Reset()
    {
        Forget();
        Cut = false;
        Residual = 0;
    }

    private void Forget()
    {
        _hasPrevious = false;
        _lastMotion = CameraMotion.Still;
        _settled = float.PositiveInfinity;
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

        ReadDetail(frame, detail);
        Cut = false;
        Residual = 0;
        var motion = CameraMotion.Still;
        // Only a fit the last picture settled gives something to compare with.
        var (predicted, error) = _hasPrevious && float.IsFinite(_settled) ? Predict(exclude) : (motion, float.PositiveInfinity);
        if (error <= Math.Min(MostSettled, _settled * Settling + SettlingSlack))
        {
            (motion, Residual, _settled) = (Snap(predicted), error, error);
            _sincePyramid = Compose(motion, _sincePyramid);
            if (++_pyramidAge >= OldestPyramid)
            {
                Shrink(frame);
                Advance();
            }
        }
        else
        {
            Shrink(frame);
            _settled = float.PositiveInfinity;
            if (_hasPrevious && Align(exclude) is > 0 and var count)
            {
                (motion, _settled) = Cut ? (Relative(_candidates[0]), float.PositiveInfinity) : Decide(count, exclude);
            }

            Advance();
        }

        motion = Snap(motion);
        _lastMotion = Cut ? CameraMotion.Still : motion;
        _hasPrevious = true;
        (_detail, _detailPrevious) = (_detailPrevious, _detail);
        (_detailArea, _detailPreviousArea) = (_detailPreviousArea, _detailArea);
        return motion;
    }

    /// <summary>The pyramid just built becomes the one later pictures are compared with.</summary>
    private void Advance()
    {
        for (var level = 0; level < Levels; level++)
        {
            (_previous[level], _current[level]) = (_current[level], _previous[level]);
        }

        (_pyramidAge, _sincePyramid) = (0, CameraMotion.Still);
    }

    /// <summary>
    /// The last picture's motion refined in detail, at half and then at full detail, and the mean
    /// difference it leaves (Bergen et al.'s model-based estimate started from a prediction).
    /// </summary>
    private (CameraMotion Motion, float Error) Predict((int Left, int Top, int Right, int Bottom) exclude)
    {
        var (coarse, _) = RefineDetail(1, _lastMotion, exclude, 1);
        return RefineDetail(0, coarse, exclude, 3);
    }

    /// <summary>Motion <paramref name="first"/> into a picture followed by <paramref name="then"/> out of it, as one motion.</summary>
    internal static CameraMotion Compose(CameraMotion first, CameraMotion then) =>
        new(then.Scale * first.Scale, then.Scale * first.ShiftX + then.ShiftX, then.Scale * first.ShiftY + then.ShiftY);

    /// <summary>
    /// A motion found against the pyramid, which may be older than the previous picture, as a
    /// motion into the previous picture: the motion since the pyramid's picture is taken off.
    /// </summary>
    private CameraMotion Relative(CameraMotion found) =>
        new(found.Scale / _sincePyramid.Scale, (found.ShiftX - _sincePyramid.ShiftX) / _sincePyramid.Scale,
            (found.ShiftY - _sincePyramid.ShiftY) / _sincePyramid.Scale);

    /// <summary>Noise in an unmoving shot must not make a recorded background creep: under half a pixel anywhere is still.</summary>
    private CameraMotion Snap(CameraMotion motion)
    {
        var reach = Math.Abs(motion.Scale - 1) * Math.Max(_frameWidth, _frameHeight) / 2;
        return reach < 0.5f && Math.Abs(motion.ShiftX) < 0.3f && Math.Abs(motion.ShiftY) < 0.3f
            ? CameraMotion.Still
            : motion;
    }

    /// <summary>
    /// The hypotheses (the camera keeping still, moving as it did last time, and what the pyramid
    /// found) are refined in the detail area coarse to fine, as Bergen et al. do: at a quarter and
    /// then half of the detail, where fine texture cannot trap the fit, and the two that match best
    /// there at full detail, which decides between them. Hypotheses that settle on the same motion
    /// are followed once.
    /// </summary>
    private (CameraMotion Motion, float Error) Decide(int count, (int Left, int Top, int Right, int Bottom) exclude)
    {
        for (var candidate = 0; candidate < count; candidate++)
        {
            _candidates[candidate] = Relative(_candidates[candidate]);
        }

        _candidates[count++] = CameraMotion.Still;
        _candidates[count++] = _lastMotion;
        Span<float> errors = stackalloc float[count];
        for (var candidate = 0; candidate < count; candidate++)
        {
            errors[candidate] = float.PositiveInfinity;
            if (Repeats(candidate))
            {
                continue;
            }

            var motion = RefineDetail(Levels - 1, _candidates[candidate], exclude, 3).Motion;
            _candidates[candidate] = motion;
            if (!Repeats(candidate))
            {
                (_candidates[candidate], errors[candidate]) = RefineDetail(1, motion, exclude, 2);
            }
        }

        var best = _candidates[0];
        var bestError = float.PositiveInfinity;
        for (var round = 0; round < 2; round++)
        {
            var pick = 0;
            for (var candidate = 1; candidate < count; candidate++)
            {
                pick = errors[candidate] < errors[pick] ? candidate : pick;
            }

            if (round > 0 && float.IsPositiveInfinity(errors[pick]))
            {
                break;
            }

            errors[pick] = float.PositiveInfinity;
            var (refined, error) = RefineDetail(0, _candidates[pick], exclude, 4);
            if (round == 0 || error < bestError)
            {
                (best, bestError) = (refined, error);
            }
        }

        return (best, bestError);
    }

    /// <summary>Whether a hypothesis lies within a pixel of an earlier one.</summary>
    private bool Repeats(int candidate)
    {
        var motion = _candidates[candidate];
        for (var earlier = 0; earlier < candidate; earlier++)
        {
            var other = _candidates[earlier];
            if (Math.Abs(other.ShiftX - motion.ShiftX) + Math.Abs(other.ShiftY - motion.ShiftY) < 1
                && Math.Abs(other.Scale - motion.Scale) < 0.002f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A grey copy of the detail area, two-by-two averaged when it is too big to read whole, and two
    /// halvings of it for the coarse-to-fine fit.
    /// </summary>
    private void ReadDetail(VideoFrame frame, (int Left, int Top, int Right, int Bottom) area)
    {
        var left = Math.Clamp(area.Left, 0, frame.Width - 1);
        var top = Math.Clamp(area.Top, 0, frame.Height - 1);
        var width = Math.Clamp(area.Right, left + 1, frame.Width) - left;
        var height = Math.Clamp(area.Bottom, top + 1, frame.Height) - top;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / DetailBudget)));
        var (w, h) = (Math.Max(1, width / step), Math.Max(1, height / step));
        // The area follows the subject and the camera's speed, so its size wavers: room for twice it,
        // within the budget, keeps that from needing more memory.
        var room = Math.Min(2 * w * h, DetailBudget + DetailBudget / 4);
        SubjectRemovalMatte.Ensure(ref _detail[0], w * h, room);
        var far = step / 2;
        // A sample averages the pixels at 0 and far along each axis, so its centre is far / 2 in.
        _detailArea[0] = new DetailLevel(left + far / 2f, top + far / 2f, w, h, step);
        var detail = _detail[0];
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        for (var y = 0; y < h; y++)
        {
            var row0 = (top + y * step) * stride;
            var row1 = (top + y * step + far) * stride;
            for (var x = 0; x < w; x++)
            {
                var a = (left + x * step) * 4;
                if (far == 0)
                {
                    // One pixel a sample: the four reads below would all be the same one.
                    detail[y * w + x] = Grey(plane, row0 + a);
                    continue;
                }

                var b = (left + x * step + far) * 4;
                detail[y * w + x] = (Grey(plane, row0 + a) + Grey(plane, row0 + b)
                    + Grey(plane, row1 + a) + Grey(plane, row1 + b)) * 0.25f;
            }
        }

        for (var level = 1; level < Levels; level++)
        {
            var fine = _detailArea[level - 1];
            var (cw, ch) = (Math.Max(1, fine.Width / 2), Math.Max(1, fine.Height / 2));
            SubjectRemovalMatte.Ensure(ref _detail[level], cw * ch, room >> (2 * level));
            // Two samples averaged lie halfway between them.
            _detailArea[level] = new DetailLevel(fine.Left + fine.Step / 2, fine.Top + fine.Step / 2, cw, ch, fine.Step * 2);
            var (source, target) = (_detail[level - 1], _detail[level]);
            for (var y = 0; y < ch; y++)
            {
                var y0 = Math.Min(fine.Height - 1, y * 2) * fine.Width;
                var y1 = Math.Min(fine.Height - 1, y * 2 + 1) * fine.Width;
                for (var x = 0; x < cw; x++)
                {
                    var x0 = Math.Min(fine.Width - 1, x * 2);
                    var x1 = Math.Min(fine.Width - 1, x * 2 + 1);
                    target[y * cw + x] = (source[y0 + x0] + source[y0 + x1] + source[y1 + x0] + source[y1 + x1]) * 0.25f;
                }
            }
        }
    }

    /// <summary>
    /// Gauss-Newton refinement of zoom and shift on samples of one level of the detail area, against
    /// the same level of the previous picture's, starting from <paramref name="motion"/>. Returns the
    /// refined motion and the mean difference left, or infinity when too little could be compared.
    /// </summary>
    private (CameraMotion Motion, float Error) RefineDetail(int level, CameraMotion motion,
        (int Left, int Top, int Right, int Bottom) exclude, int iterations)
    {
        var current = _detailArea[level];
        var previous = _detailPreviousArea[level];
        var (samplesNow, samplesThen) = (_detail[level], _detailPrevious[level]);
        // Coarser levels need fewer samples: each already averages many pixels.
        var budget = DetailSamples >> level;
        var sampleStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)current.Width * current.Height / budget)));
        // The zoom is fitted about the area's centre so its column of the system is well scaled.
        var cx = current.Left + current.Width * current.Step / 2f;
        var cy = current.Top + current.Height * current.Step / 2f;
        var (scale, u, v) = (motion.Scale, motion.ShiftX + (motion.Scale - 1) * cx, motion.ShiftY + (motion.Scale - 1) * cy);
        var error = float.PositiveInfinity;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            double h00 = 0, h01 = 0, h02 = 0, h11 = 0, h12 = 0, h22 = 0, b0 = 0, b1 = 0, b2 = 0, total = 0;
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
                    var x = current.Left + i * current.Step;
                    var y = current.Top + j * current.Step;
                    if (i >= current.Width || j >= current.Height
                        || (x >= exclude.Left && x < exclude.Right && y >= exclude.Top && y < exclude.Bottom))
                    {
                        continue;
                    }

                    var px = (scale * (x - cx) + cx + u - previous.Left) / previous.Step;
                    var py = (scale * (y - cy) + cy + v - previous.Top) / previous.Step;
                    if (px < 1 || py < 1 || px >= previous.Width - 2 || py >= previous.Height - 2)
                    {
                        continue;
                    }

                    var (value, gx, gy) = Interpolate(samplesThen, previous.Width, px, py);
                    var r = value - samplesNow[j * current.Width + i];
                    (gx, gy) = (gx / previous.Step, gy / previous.Step);
                    var magnitude = Math.Abs(r);
                    total += magnitude;
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
                return (motion, float.PositiveInfinity);
            }

            // The mean difference where this step started, which the step only improves on.
            error = (float)(total / samples);
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
        return (Math.Abs(scale - 1) * Math.Max(current.Width, current.Height) * current.Step / 2 < 0.25f
            ? new CameraMotion(1, u, v)
            : new CameraMotion(scale, u - (scale - 1) * cx, v - (scale - 1) * cy), error);
    }

    /// <summary>
    /// One level of the detail area's pyramid: where its first sample's centre lies in the picture,
    /// how many samples it has, and how far apart they are in picture pixels.
    /// </summary>
    private readonly record struct DetailLevel(float Left, float Top, int Width, int Height, float Step);

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

        Forget();
    }

    /// <summary>
    /// Reads a grey copy, each cell the mean of an evenly spread grid of up to four by four of its
    /// pixels, so fine texture is averaged rather than aliased; then halves it twice.
    /// </summary>
    private void Shrink(VideoFrame frame)
    {
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var (w, h) = (_widths[0], _heights[0]);
        var top = _current[0];
        var across = Math.Min(_step, 4);
        var spacing = _step / across;
        var first = (_step - spacing * (across - 1)) / 2;
        var weight = 1f / (across * across);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var total = 0f;
                for (var j = 0; j < across; j++)
                {
                    var row = (y * _step + first + j * spacing) * stride;
                    for (var i = 0; i < across; i++)
                    {
                        total += Grey(plane, row + (x * _step + first + i * spacing) * 4);
                    }
                }

                top[y * w + x] = total * weight;
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
    /// level's scores for the fraction of a cell. A repetitive wall can make a shift a pattern away
    /// look as good as the truth on the coarse levels, so several hypotheses are kept: the few best
    /// coarse shifts followed down, and shifts near no movement and near the last picture's motion
    /// searched on the finest level directly, since a camera's motion changes little from frame to
    /// frame. The detailed stage then decides between them.
    /// </summary>
    private int Align((int Left, int Top, int Right, int Bottom) exclude)
    {
        const int Side = 2 * Search + 1;
        Span<float> coarse = stackalloc float[Side * Side];
        if (SearchShift(Levels - 1, exclude, 0, 0, Search, coarse) is null)
        {
            return 0;
        }

        Span<int> starts = stackalloc int[Candidates];
        var count = 0;
        for (var pick = 0; pick < Candidates; pick++)
        {
            var best = -1;
            var bestScore = float.MaxValue;
            for (var at = 0; at < Side * Side; at++)
            {
                // A slight preference for no movement keeps a flat scene still.
                var score = coarse[at] + 0.05f * (Math.Abs(at % Side - Search) + Math.Abs(at / Side - Search));
                if (coarse[at] < float.MaxValue && score < bestScore && !starts[..count].Contains(at))
                {
                    (best, bestScore) = (at, score);
                }
            }

            if (best < 0)
            {
                break;
            }

            starts[count++] = best;
        }

        Span<float> means = stackalloc float[Candidates + 2];
        Span<(int X, int Y)> ends = stackalloc (int, int)[Candidates + 2];
        var found = 0;
        // With an older pyramid, keeping still means having moved as much as since it was built.
        var expected = Compose(_lastMotion, _sincePyramid);
        var (stillX, stillY) = ((int)MathF.Round(_sincePyramid.ShiftX / _step), (int)MathF.Round(_sincePyramid.ShiftY / _step));
        Keep(Finest(exclude, stillX, stillY, 1), means, ends, ref found);
        var (lastX, lastY) = ((int)MathF.Round(expected.ShiftX / _step), (int)MathF.Round(expected.ShiftY / _step));
        if (lastX != stillX || lastY != stillY)
        {
            Keep(Finest(exclude, lastX, lastY, 1), means, ends, ref found);
        }

        foreach (var start in starts[..count])
        {
            Keep(Descend(exclude, start % Side - Search, start / Side - Search), means, ends, ref found);
        }

        if (found > 0)
        {
            Residual = means[0];
            Cut = means[0] > CutResidual;
        }

        return found;
    }

    /// <summary>Adds a hypothesis to the candidates, best match first, once for each whole-cell shift.</summary>
    private void Keep((int X, int Y, float Mean, float FractionX, float FractionY)? shift,
        Span<float> means, Span<(int X, int Y)> ends, ref int found)
    {
        if (shift is not { } s || ends[..found].Contains((s.X, s.Y)))
        {
            return;
        }

        var at = found++;
        while (at > 0 && means[at - 1] > s.Mean)
        {
            (means[at], ends[at], _candidates[at]) = (means[at - 1], ends[at - 1], _candidates[at - 1]);
            at--;
        }

        // A cell's centre lies half a cell in from its corner, the same in both pictures.
        (means[at], ends[at], _candidates[at]) = (s.Mean, (s.X, s.Y),
            new CameraMotion(1, _step * (s.X + s.FractionX), _step * (s.Y + s.FractionY)));
    }

    /// <summary>Follows a coarse shift down the pyramid, searching the cell either side on each finer level.</summary>
    internal (int X, int Y, float Mean, float FractionX, float FractionY)? Descend(
        (int Left, int Top, int Right, int Bottom) exclude, int x, int y)
    {
        Span<float> scores = stackalloc float[9];
        for (var level = Levels - 2; level > 0; level--)
        {
            if (SearchShift(level, exclude, x * 2, y * 2, 1, scores) is not { } best)
            {
                return null;
            }

            (x, y) = (best.X, best.Y);
        }

        return Finest(exclude, x * 2, y * 2, 1);
    }

    /// <summary>
    /// The best whole-cell shift on the finest level within <paramref name="reach"/> of (cx, cy), and
    /// its fraction of a cell from a parabola through the scores either side. A best shift on the
    /// window's edge is searched around again, at most twice; if it is still on an edge it has no
    /// fraction.
    /// </summary>
    internal (int X, int Y, float Mean, float FractionX, float FractionY)? Finest(
        (int Left, int Top, int Right, int Bottom) exclude, int cx, int cy, int reach)
    {
        Span<float> scores = stackalloc float[25];
        (int X, int Y, float Mean) best = default;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (SearchShift(0, exclude, cx, cy, reach, scores) is not { } found)
            {
                return null;
            }

            best = found;
            var side = 2 * reach + 1;
            var (ox, oy) = (best.X - cx + reach, best.Y - cy + reach);
            if (ox > 0 && oy > 0 && ox < side - 1 && oy < side - 1)
            {
                var at = oy * side + ox;
                return (best.X, best.Y, best.Mean, Vertex(scores[at - 1], scores[at], scores[at + 1]),
                    Vertex(scores[at - side], scores[at], scores[at + side]));
            }

            (cx, cy, reach) = (best.X, best.Y, 1);
        }

        return (best.X, best.Y, best.Mean, 0, 0);
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
        // A few thousand cells score a shift as well as every cell would.
        var stride = Math.Max(1, (int)Math.Sqrt(w * h / (float)ScoredCells));
        (int X, int Y, float Mean)? found = null;
        for (var oy = -reach; oy <= reach; oy++)
        {
            for (var ox = -reach; ox <= reach; ox++)
            {
                var (dx, dy) = (cx + ox, cy + oy);
                var total = 0f;
                var count = 0;
                var (left, right) = (Math.Max(0, -dx), Math.Min(w, w - dx));
                for (var y = Math.Max(0, -dy); y < Math.Min(h, h - dy); y += stride)
                {
                    var now = current.AsSpan(y * w, w);
                    var then = previous.AsSpan((y + dy) * w, w);
                    var inside = y >= box.Top && y < box.Bottom;
                    for (var x = left; x < right; x += stride)
                    {
                        if (inside && x >= box.Left && x < box.Right)
                        {
                            x = box.Right - stride;
                            continue;
                        }

                        total += Math.Abs(then[x + dx] - now[x]);
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
