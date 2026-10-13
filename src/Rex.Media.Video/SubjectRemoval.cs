using System.Runtime.InteropServices;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>Where a removal preview's estimated pixels, those never seen in the video, come from.</summary>
public enum RemovalFillSource
{
    /// <summary>Synthesised from the texture around the hole by <see cref="SubjectRemovalFill"/>.</summary>
    Synthesised,

    /// <summary>Synthesised for now, while an inpainting model works on a fill of this paused picture.</summary>
    ModelWorking,

    /// <summary>Filled by an inpainting model.</summary>
    Model,
}

/// <summary>
/// Removes a tracked subject from the picture, as a preview: the subject's outline is found inside and
/// around its box (<see cref="SubjectRemovalMatte"/>), what the camera saw behind it in earlier
/// pictures is put back from a background that follows pans and zooms
/// (<see cref="SubjectRemovalMotion"/>, <see cref="SubjectRemovalPlate"/>), and only what was never
/// seen is synthesised from the texture around it (<see cref="SubjectRemovalFill"/>). Every edited
/// pixel is counted as observed or estimated, so the window can say which the preview shows.
/// <para>
/// The work is bounded to the subject's surroundings and runs on reduced grids where it has to, and
/// every buffer is kept from picture to picture, so a steady preview allocates nothing.
/// </para>
/// </summary>
public sealed class SubjectRemoval
{
    // The fill's grid holds at most this many cells, and its hole at most a quarter as many.
    private const int FillBudget = 8_000;
    private const int HoleBudget = 2_500;
    // A picture this long after the previous one is not its neighbour; nothing carries across.
    private const double LongestGap = 0.5;

    private const byte Outside = 0;
    private const byte Seen = 1;
    private const byte Seeded = 2;
    private const byte Unseen = 3;

    private readonly SubjectRemovalMotion _motion = new();
    private readonly SubjectRemovalPlate _plate = new();
    private readonly SubjectRemovalMatte _matte = new();
    private readonly SubjectRemovalFill _fill = new();
    private readonly byte[] _opacities = new byte[2 * SubjectRemovalMatte.SaturatedDistance + 1];
    private byte[] _alpha = [];
    private byte[] _kind = [];
    private uint[] _colour = [];
    private int[] _previousSource = [];
    private int[] _jumps = [];
    private int[] _columns = [];
    private int[] _weights = [];
    private (int Left, int Top, int Scale, int Width, int Height) _previousGrid;
    private bool _hasPreviousSource;
    private (int Left, int Top, int Right, int Bottom)? _previousBox;
    private CameraMotion _cameraMotion = CameraMotion.Still;
    // Where the selection moved since the previous picture; a blend moves with it.
    private (float X, float Y) _boxShift;
    // Never copied from: the box and a band around the subject's outline, which may hold parts of
    // the subject the outline missed (a shoe, a hand) or its blurred edge.
    private (int Left, int Top, int Right, int Bottom) _protected;
    private int _guard;
    private RemovalMode _mode;
    private byte[]? _shape;
    private (int Width, int Height, MediaTime Pts) _shapePicture;
    private MediaTime _lastPts = MediaTime.Unknown;
    private uint _pictures;
    private int _boxSpeed;
    private int _unseen;

    /// <summary>Edited pixels whose background the video showed in an earlier picture.</summary>
    public int ObservedPixels { get; private set; }

    /// <summary>Edited pixels whose background was never seen and was synthesised.</summary>
    public int EstimatedPixels { get; private set; }

    /// <summary>The share of the box the last preview treated as the subject, from 0 to 1.</summary>
    public float Coverage => _matte.Coverage;

    /// <summary>The camera's motion into the last picture observed.</summary>
    public CameraMotion CameraMotion => _cameraMotion;

    /// <summary>Whether the last picture began a new shot, so the background seen before was dropped.</summary>
    public bool CutDetected => _motion.Cut;

    /// <summary>Whether the last preview found the subject at the picture's pixel (x, y), its outline not widened.</summary>
    public bool IsSubject(int x, int y)
    {
        var area = _matte.Area;
        return !_matte.IsEmpty && x >= area.Left && y >= area.Top && x < area.Right && y < area.Bottom
            && _matte.Distance[(y - area.Top) * (area.Right - area.Left) + x - area.Left] <= 0;
    }

    /// <summary>Whether the last preview's never-seen pixels came from a given estimate rather than this class's own synthesis.</summary>
    public bool UsedEstimate { get; private set; }

    /// <summary>
    /// The subject's exact outline in one picture (one byte a pixel, non-zero inside), drawn by a
    /// segmentation model: every preview of that picture uses it as the matte, and the pictures after
    /// it start from it. The outline is copied.
    /// </summary>
    public void UseShape(ReadOnlySpan<byte> outline, int width, int height, MediaTime pts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (outline.Length != width * height)
        {
            throw new ArgumentException("An outline has one value for each pixel of its picture.", nameof(outline));
        }

        _shape = outline.ToArray();
        _shapePicture = (width, height, pts);
    }

    /// <summary>Forget everything learnt and the memory it used.</summary>
    public void Release()
    {
        _plate.Release();
        _shape = null;
        Forget();
    }

    /// <summary>
    /// Marks, in <paramref name="hole"/> (one byte a pixel of the picture), every pixel the last
    /// preview replaced with an estimate rather than with background the video showed: 255 there, 0
    /// elsewhere. Returns how many there are. This is the hole an inpainting model is asked to fill.
    /// </summary>
    public int EstimatedHole(Span<byte> hole, int width)
    {
        hole.Clear();
        if (_matte.IsEmpty)
        {
            return 0;
        }

        var area = _matte.Area;
        var areaWidth = area.Right - area.Left;
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var kind = _kind[(y - area.Top) * areaWidth + x - area.Left];
                if (kind is Seeded or Unseen)
                {
                    hole[y * width + x] = 255;
                    count++;
                }
            }
        }

        return count;
    }

    private void Forget()
    {
        _motion.Reset();
        _matte.Reset();
        _hasPreviousSource = false;
        _previousBox = null;
        _boxSpeed = 0;
        _cameraMotion = CameraMotion.Still;
        _lastPts = MediaTime.Unknown;
        ObservedPixels = EstimatedPixels = 0;
    }

    /// <summary>
    /// Process one picture (BGRA) with the subject at <paramref name="box"/>. With
    /// <paramref name="advance"/> it is a new picture: the camera's motion is followed and the
    /// background it shows is recorded. With <paramref name="output"/> the preview is drawn there
    /// (the same size as the picture): <paramref name="feather"/> is the soft edge in pixels, and
    /// <paramref name="coverage"/> (0 to 100) how much of the box counts as the subject. With
    /// <paramref name="estimate"/> (the picture's size, such as an inpainting model's fill of
    /// <see cref="EstimatedHole"/>), pixels never seen take its colours instead of being
    /// synthesised. In <see cref="RemovalMode.Blend"/> nothing seen behind the subject is used:
    /// every replaced pixel is copied from the surface just around it. Returns whether any pixel was
    /// replaced. The picture itself is never changed.
    /// </summary>
    public bool Apply(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box, bool advance,
        int feather, int coverage, VideoFrame? output, VideoFrame? estimate = null, RemovalMode mode = RemovalMode.Remove)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if ((output is not null && (output.Width != frame.Width || output.Height != frame.Height))
            || (estimate is not null && (estimate.Width != frame.Width || estimate.Height != frame.Height)))
        {
            throw new ArgumentException("The preview and the estimate must be the picture's size.", nameof(output));
        }

        feather = Math.Clamp(feather, 0, SubjectRemovalMatte.MaximumFeather);
        ObservedPixels = EstimatedPixels = 0;
        UsedEstimate = false;
        _mode = mode;
        if (_plate.Width != frame.Width || _plate.Height != frame.Height)
        {
            _plate.Prepare(frame.Width, frame.Height);
            Forget();
        }

        if (advance)
        {
            Follow(frame, box);
        }

        if (output is not null)
        {
            var shaped = _shape is not null && _shapePicture == (frame.Width, frame.Height, frame.Pts);
            _matte.Compute(frame, box, _plate, coverage, remember: advance, shaped ? _shape : default);
        }

        if (advance)
        {
            Record(frame, box, output is not null, feather);
        }


        if (output is null)
        {
            return false;
        }

        frame.Plane(0).CopyTo(output.Plane(0));
        if (_matte.IsEmpty)
        {
            return false;
        }

        _guard = (int)(_matte.Grow + Math.Max(feather, 3) + 4);
        _protected = Expand(box, 0, 0, _guard, frame);

        var hole = Classify(frame, feather);
        if (estimate is not null)
        {
            TakeEstimate(estimate);
        }
        else if (hole.Right > hole.Left)
        {
            Synthesise(frame, hole, advance);
        }

        Composite(frame, output);
        return true;
    }

    /// <summary>Follows the camera into this picture, starting afresh after a cut or a gap.</summary>
    private void Follow(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        if (_lastPts.IsKnown && frame.Pts.IsKnown
            && (frame.Pts <= _lastPts || (frame.Pts - _lastPts).TotalSeconds > LongestGap))
        {
            _plate.Clear();
            Forget();
        }

        _lastPts = frame.Pts;
        _pictures++;
        var exclude = Expand(Union(box, _previousBox ?? box), 0.1f, 0.05f, 4, frame);
        var before = _previousBox ?? box;
        _boxShift = ((box.Left + box.Right - before.Left - before.Right) / 2f, (box.Top + box.Bottom - before.Top - before.Bottom) / 2f);
        _boxSpeed = Math.Max(Math.Max(Math.Abs(box.Left - before.Left), Math.Abs(box.Right - before.Right)),
            Math.Max(Math.Abs(box.Top - before.Top), Math.Abs(box.Bottom - before.Bottom)));
        _previousBox = box;
        _cameraMotion = _motion.Estimate(frame, exclude, Interest(frame, box));
        if (_motion.Cut)
        {
            _plate.Clear();
            _matte.Reset();
            _hasPreviousSource = false;
            _cameraMotion = CameraMotion.Still;
        }
        else
        {
            _plate.Advance(_cameraMotion);
        }

        _plate.Keep(Interest(frame, box));
    }

    /// <summary>
    /// The area around the subject whose background is worth recording: the matte's reach, and
    /// as far again as the camera or the subject moved last time, which is where it may go next.
    /// </summary>
    private (int Left, int Top, int Right, int Bottom) Interest(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var pan = Math.Max(Math.Abs(_cameraMotion.ShiftX), Math.Abs(_cameraMotion.ShiftY));
        // Record far enough ahead of the moving silhouette for its confirmed sightings before it
        // passes over a pixel, including the guard around the feathered outline.
        return Expand(box, 0.18f, 0.14f, 44 + 2 * (int)Math.Max(pan, _boxSpeed), frame);
    }

    private void Record(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box, bool hasMatte, int feather)
    {
        var interest = Interest(frame, box);
        if (hasMatte)
        {
            // Only background clear of the subject's soft edge is recorded, so its blur never is.
            var clearance = (int)((_matte.Grow + Math.Max(feather, 4) + 2) * SubjectRemovalMatte.DistanceUnit);
            _plate.Learn(frame, interest, _matte.Area, _matte.Distance, clearance);
        }
        else
        {
            _plate.Learn(frame, interest, Expand(box, 0.1f, 0.05f, 4, frame), default, 0);
        }
    }

    private static (int Left, int Top, int Right, int Bottom) Union(
        (int Left, int Top, int Right, int Bottom) a, (int Left, int Top, int Right, int Bottom) b) =>
        (Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    private static (int Left, int Top, int Right, int Bottom) Expand((int Left, int Top, int Right, int Bottom) box,
        float acrossShare, float downShare, int pixels, VideoFrame frame)
    {
        var dx = (int)(acrossShare * (box.Right - box.Left)) + pixels;
        var dy = (int)(downShare * (box.Bottom - box.Top)) + pixels;
        return (Math.Max(0, box.Left - dx), Math.Max(0, box.Top - dy),
            Math.Min(frame.Width, box.Right + dx), Math.Min(frame.Height, box.Bottom + dy));
    }

    /// <summary>The soft edge: fully replaced within <paramref name="grow"/> pixels of the outline, then fading over <paramref name="feather"/>.</summary>
    internal static byte Opacity(float distance, float grow, int feather)
    {
        if (distance <= grow)
        {
            return 255;
        }

        if (feather == 0)
        {
            return 0;
        }

        var t = (distance - grow) / feather;
        if (t >= 1)
        {
            return 0;
        }

        return (byte)(255 * (1 - t * t * (3 - 2 * t)) + 0.5f);
    }

    /// <summary>
    /// Finds each edited pixel's opacity and where its fill comes from: observed background, an
    /// estimate made in an earlier picture (kept, so the fill holds still), or nothing yet. Returns
    /// the bounds of the pixels with nothing yet.
    /// </summary>
    private (int Left, int Top, int Right, int Bottom) Classify(VideoFrame frame, int feather)
    {
        var area = _matte.Area;
        var width = area.Right - area.Left;
        var pixels = width * (area.Bottom - area.Top);
        SubjectRemovalMatte.Ensure(ref _alpha, pixels);
        SubjectRemovalMatte.Ensure(ref _kind, pixels);
        SubjectRemovalMatte.Ensure(ref _colour, pixels);
        var distance = _matte.Distance;
        var grow = _matte.Grow;
        // Distances are capped, so every opacity there can be is worked out once per picture.
        for (var d = 0; d < _opacities.Length; d++)
        {
            _opacities[d] = Opacity((d - SubjectRemovalMatte.SaturatedDistance) / (float)SubjectRemovalMatte.DistanceUnit, grow, feather);
        }

        var (left, top, right, bottom) = (frame.Width, frame.Height, 0, 0);
        _unseen = 0;
        uint seen = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var at = (y - area.Top) * width + x - area.Left;
                var alpha = _opacities[distance[at] + SubjectRemovalMatte.SaturatedDistance];
                _alpha[at] = alpha;
                if (alpha == 0)
                {
                    _kind[at] = Outside;
                    continue;
                }

                // A blend copies only the surface around the selection, never the scene seen behind it.
                var state = _mode == RemovalMode.Blend ? SubjectRemovalPlate.Unknown : _plate.Sample(x, y, out seen);
                _colour[at] = seen;
                if (state == SubjectRemovalPlate.Observed)
                {
                    _kind[at] = Seen;
                    ObservedPixels++;
                    continue;
                }

                EstimatedPixels++;
                if (state == SubjectRemovalPlate.Estimated)
                {
                    _kind[at] = Seeded;
                    continue;
                }

                _kind[at] = Unseen;
                _unseen++;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x + 1), Math.Max(bottom, y + 1));
            }
        }

        return (left, top, right, bottom);
    }

    /// <summary>
    /// Every estimated pixel takes the given estimate's colour, which is also kept on the plate as
    /// this place's estimate, so the fill does not change when the video plays on from here.
    /// </summary>
    private void TakeEstimate(VideoFrame estimate)
    {
        var area = _matte.Area;
        var width = area.Right - area.Left;
        var pixels = MemoryMarshal.Cast<byte, uint>(estimate.Plane(0));
        var stride = estimate.Stride(0) / 4;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var at = (y - area.Top) * width + x - area.Left;
                if (_kind[at] is Seeded or Unseen)
                {
                    var colour = pixels[y * stride + x] & 0xFFFFFF;
                    _colour[at] = colour;
                    _plate.Estimate(x, y, colour);
                }
            }
        }

        UsedEstimate = true;
    }

    /// <summary>Whether the background at the picture's pixel (x, y) is known, seen or estimated before, and its colour if so.</summary>
    private bool Background(ReadOnlySpan<byte> plane, int stride, int x, int y, out uint colour)
    {
        var area = _matte.Area;
        if (x < area.Left || y < area.Top || x >= area.Right || y >= area.Bottom)
        {
            colour = Pixel(plane, stride, x, y);
            return true;
        }

        var at = (y - area.Top) * (area.Right - area.Left) + x - area.Left;
        switch (_kind[at])
        {
            case Outside:
                colour = Pixel(plane, stride, x, y);
                return true;
            case Seen or Seeded:
                colour = _colour[at];
                return true;
            default:
                colour = 0;
                return false;
        }
    }

    /// <summary>
    /// Whether the picture's pixel (x, y) may hold the subject: inside its box, or within the guard
    /// band beyond its outline. Fill is never copied from there, so a shoe or a hand the outline
    /// missed cannot reappear inside the hole.
    /// </summary>
    private bool Guarded(int x, int y)
    {
        if (x >= _protected.Left && x < _protected.Right && y >= _protected.Top && y < _protected.Bottom)
        {
            return true;
        }

        var area = _matte.Area;
        return x >= area.Left && y >= area.Top && x < area.Right && y < area.Bottom
            && _matte.Distance[(y - area.Top) * (area.Right - area.Left) + x - area.Left] <= _guard * SubjectRemovalMatte.DistanceUnit;
    }

    private static uint Pixel(ReadOnlySpan<byte> plane, int stride, int x, int y)
    {
        var at = y * stride + x * 4;
        return plane[at] | ((uint)plane[at + 1] << 8) | ((uint)plane[at + 2] << 16);
    }

    /// <summary>Synthesises the never-seen pixels inside <paramref name="hole"/> on a reduced grid, then copies the texture back at full size.</summary>
    private void Synthesise(VideoFrame frame, (int Left, int Top, int Right, int Bottom) hole, bool advance)
    {
        var holeWidth = hole.Right - hole.Left;
        var holeHeight = hole.Bottom - hole.Top;
        var margin = Math.Min(holeWidth, holeHeight) / 2 + 8;
        var grid = (Left: Math.Max(0, hole.Left - margin), Top: Math.Max(0, hole.Top - margin),
            Right: Math.Min(frame.Width, hole.Right + margin), Bottom: Math.Min(frame.Height, hole.Bottom + margin));
        var gridArea = (double)(grid.Right - grid.Left) * (grid.Bottom - grid.Top);
        var scale = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(gridArea / FillBudget, (double)_unseen / HoleBudget))));
        var width = (grid.Right - grid.Left + scale - 1) / scale;
        var height = (grid.Bottom - grid.Top + scale - 1) / scale;
        _fill.Prepare(width, height);
        ReadGrid(frame, grid, scale, width, height);
        if (advance)
        {
            // A paused redraw starts from the same seed and known pixels as the first render, and
            // never from a match recorded from the very same picture, so it draws the same fill.
            GuessFromPrevious(grid, scale, width, height, advance);
        }

        _fill.Solve(_pictures * 2654435761u);
        if (advance)
        {
            SubjectRemovalMatte.Ensure(ref _previousSource, width * height);
            _fill.Source.CopyTo(_previousSource);
            _previousGrid = (grid.Left, grid.Top, scale, width, height);
            _hasPreviousSource = true;
        }

        WriteBack(frame, hole, grid, scale, width, height);
    }

    private void ReadGrid(VideoFrame frame, (int Left, int Top, int Right, int Bottom) grid, int scale, int width, int height)
    {
        var colour = _fill.Colour;
        var state = _fill.State;
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        // A cell is read from at most four by four of its pixels.
        var step = Math.Max(1, scale / 4);
        for (var j = 0; j < height; j++)
        {
            for (var i = 0; i < width; i++)
            {
                long blue = 0, green = 0, red = 0;
                int known = 0, missing = 0;
                var guarded = false;
                for (var y = grid.Top + j * scale; y < Math.Min(grid.Bottom, grid.Top + (j + 1) * scale); y += step)
                {
                    for (var x = grid.Left + i * scale; x < Math.Min(grid.Right, grid.Left + (i + 1) * scale); x += step)
                    {
                        guarded |= Guarded(x, y);
                        if (Background(plane, stride, x, y, out var value))
                        {
                            (blue, green, red) = (blue + (value & 0xFF), green + ((value >> 8) & 0xFF), red + ((value >> 16) & 0xFF));
                            known++;
                        }
                        else
                        {
                            missing++;
                        }
                    }
                }

                var cell = j * width + i;
                _fill.Guess[cell] = -1;
                _fill.Avoid[cell] = guarded;
                state[cell] = missing == 0 ? SubjectRemovalFill.Known : SubjectRemovalFill.Missing;
                if (missing == 0)
                {
                    (colour[cell * 3], colour[cell * 3 + 1], colour[cell * 3 + 2]) = ((byte)(blue / known), (byte)(green / known), (byte)(red / known));
                }
            }
        }
    }

    /// <summary>
    /// Suggests, for each hole cell, the match the previous picture used at the same place in the
    /// scene, carried along by the camera's motion, so the fill keeps its texture from one picture to
    /// the next.
    /// </summary>
    private void GuessFromPrevious((int Left, int Top, int Right, int Bottom) grid, int scale, int width, int height, bool advance)
    {
        if (!_hasPreviousSource)
        {
            return;
        }

        // A blend is part of the selection's surface, so it moves with the selection, not the camera.
        var motion = !advance ? CameraMotion.Still
            : _mode == RemovalMode.Blend ? new CameraMotion(1, -_boxShift.X, -_boxShift.Y)
            : _cameraMotion;
        var previous = _previousGrid;
        var state = _fill.State;
        for (var cell = 0; cell < width * height; cell++)
        {
            if (state[cell] == SubjectRemovalFill.Known)
            {
                continue;
            }

            var x = grid.Left + (cell % width + 0.5f) * scale;
            var y = grid.Top + (cell / width + 0.5f) * scale;
            var (px, py) = motion.Previous(x, y);
            var mx = (int)MathF.Floor((px - previous.Left) / previous.Scale);
            var my = (int)MathF.Floor((py - previous.Top) / previous.Scale);
            if (mx < 0 || my < 0 || mx >= previous.Width || my >= previous.Height)
            {
                continue;
            }

            var source = _previousSource[my * previous.Width + mx];
            if (source < 0)
            {
                continue;
            }

            var dx = (source % previous.Width - mx) * previous.Scale / motion.Scale;
            var dy = (source / previous.Width - my) * previous.Scale / motion.Scale;
            var sx = (int)MathF.Floor((x + dx - grid.Left) / scale);
            var sy = (int)MathF.Floor((y + dy - grid.Top) / scale);
            if (sx >= 0 && sy >= 0 && sx < width && sy < height)
            {
                _fill.Guess[cell] = sy * width + sx;
            }
        }
    }

    /// <summary>
    /// Gives each never-seen pixel the full-size pixel its grid cell's match points to, blending the
    /// four nearest cells' matches where they disagree, so the copied texture keeps its own fine
    /// detail instead of the grid's blur.
    /// </summary>
    private void WriteBack(VideoFrame frame, (int Left, int Top, int Right, int Bottom) hole,
        (int Left, int Top, int Right, int Bottom) grid, int scale, int width, int height)
    {
        var area = _matte.Area;
        var areaWidth = area.Right - area.Left;
        var colour = _fill.Colour;
        var source = _fill.Source;
        var correction = _fill.Correction;
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var cells = width * height;
        SubjectRemovalMatte.Ensure(ref _jumps, cells);
        for (var cell = 0; cell < cells; cell++)
        {
            // A cell's match as a jump in picture pixels, packed in one integer; none for known cells.
            var match = source[cell];
            _jumps[cell] = match < 0 ? NoJump
                : Jump((match % width - cell % width) * scale, (match / width - cell / width) * scale);
        }

        var holeWidth = hole.Right - hole.Left;
        SubjectRemovalMatte.Ensure(ref _columns, holeWidth);
        SubjectRemovalMatte.Ensure(ref _weights, holeWidth);
        for (var x = 0; x < holeWidth; x++)
        {
            var gx = Math.Clamp((hole.Left + x - grid.Left + 0.5f) / scale - 0.5f, 0, width - 1);
            _columns[x] = (int)gx;
            _weights[x] = (int)((gx - (int)gx) * 256);
        }

        for (var y = hole.Top; y < hole.Bottom; y++)
        {
            var gy = Math.Clamp((y - grid.Top + 0.5f) / scale - 0.5f, 0, height - 1);
            var j0 = (int)gy;
            var j1 = Math.Min(j0 + 1, height - 1);
            var wy = (int)((gy - j0) * 256);
            for (var x = hole.Left; x < hole.Right; x++)
            {
                var at = (y - area.Top) * areaWidth + x - area.Left;
                if (_kind[at] != Unseen)
                {
                    continue;
                }

                var i0 = _columns[x - hole.Left];
                var i1 = Math.Min(i0 + 1, width - 1);
                var wx = _weights[x - hole.Left];
                var (c00, c10, c01, c11) = (j0 * width + i0, j0 * width + i1, j1 * width + i0, j1 * width + i1);
                uint filled;
                if (_jumps[c00] == _jumps[c10] && _jumps[c00] == _jumps[c01] && _jumps[c00] == _jumps[c11] && _jumps[c00] != NoJump)
                {
                    // The usual case inside a copied patch: all four cells agree, one read does.
                    filled = Copy(frame, plane, stride, x, y, _jumps[c00], Cell(colour, c00));
                }
                else
                {
                    filled = SubjectRemovalPlate.Mix(
                        Copy(frame, plane, stride, x, y, _jumps[c00], Cell(colour, c00)),
                        Copy(frame, plane, stride, x, y, _jumps[c10], Cell(colour, c10)),
                        Copy(frame, plane, stride, x, y, _jumps[c01], Cell(colour, c01)),
                        Copy(frame, plane, stride, x, y, _jumps[c11], Cell(colour, c11)), wx, wy);
                }

                filled = Correct(filled, correction, c00, c10, c01, c11, wx, wy);
                _colour[at] = filled;
                if (_mode == RemovalMode.Remove)
                {
                    // Kept where the scene is, so the fill holds still as the subject moves on.
                    _plate.Estimate(x, y, filled);
                }
            }
        }
    }

    /// <summary>
    /// A copied pixel moved by the fill's fusion, the four nearest cells' corrections blended as the
    /// copies were, so the texture keeps its detail while its tone meets the picture around the hole.
    /// </summary>
    private static uint Correct(uint pixel, ReadOnlySpan<float> correction, int c00, int c10, int c01, int c11, int wx, int wy)
    {
        var (fx, fy) = (wx / 256f, wy / 256f);
        var result = 0u;
        for (var c = 0; c < 3; c++)
        {
            var top = correction[c00 * 3 + c] + (correction[c10 * 3 + c] - correction[c00 * 3 + c]) * fx;
            var bottom = correction[c01 * 3 + c] + (correction[c11 * 3 + c] - correction[c01 * 3 + c]) * fx;
            var value = (int)((pixel >> (8 * c)) & 0xFF) + (int)MathF.Round(top + (bottom - top) * fy);
            result |= (uint)Math.Clamp(value, 0, 255) << (8 * c);
        }

        return result;
    }

    private const int NoJump = int.MinValue;

    private static int Jump(int dx, int dy) => (dy << 16) | (dx & 0xFFFF);

    /// <summary>The background colour at (x, y) moved by <paramref name="jump"/>, or <paramref name="fallback"/> when that is not background.</summary>
    private uint Copy(VideoFrame frame, ReadOnlySpan<byte> plane, int stride, int x, int y, int jump, uint fallback)
    {
        if (jump == NoJump)
        {
            return fallback;
        }

        var tx = x + (short)(jump & 0xFFFF);
        var ty = y + (jump >> 16);
        return tx >= 0 && ty >= 0 && tx < frame.Width && ty < frame.Height && !Guarded(tx, ty) && Background(plane, stride, tx, ty, out var value)
            ? value
            : fallback;
    }

    private static uint Cell(Span<byte> colour, int cell) =>
        colour[cell * 3] | ((uint)colour[cell * 3 + 1] << 8) | ((uint)colour[cell * 3 + 2] << 16);

    private void Composite(VideoFrame frame, VideoFrame output)
    {
        var area = _matte.Area;
        var width = area.Right - area.Left;
        var source = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var target = MemoryMarshal.Cast<byte, uint>(output.Plane(0));
        var (inStride, outStride) = (frame.Stride(0) / 4, output.Stride(0) / 4);
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var row = (y - area.Top) * width - area.Left;
            for (var x = area.Left; x < area.Right; x++)
            {
                var alpha = _alpha[row + x];
                if (alpha == 0)
                {
                    continue;
                }

                var original = source[y * inStride + x];
                var fill = _colour[row + x];
                // Blue and red blend together in one integer, green in another, with the opacity
                // in 256ths so each lane divides by a shift; the picture's alpha is kept.
                var over = alpha + (uint)(alpha >> 7);
                var keep = 256 - over;
                var blueRed = (((original & 0xFF00FF) * keep + (fill & 0xFF00FF) * over + 0x800080) >> 8) & 0xFF00FF;
                var green = (((original & 0xFF00) * keep + (fill & 0xFF00) * over + 0x8000) >> 8) & 0xFF00;
                target[y * outStride + x] = (original & 0xFF000000) | blueRed | green;
            }
        }
    }
}
