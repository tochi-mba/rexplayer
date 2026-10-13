using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Separates the subject from its background inside and just around the tracked box, and gives
/// every pixel of the area its signed distance from the subject's outline, from which the removal's
/// soft edge is drawn.
/// <para>
/// Method: the colour of the box is compared with the colour of a ring around it, as in GrabCut
/// (Rother, Kolmogorov and Blake, "GrabCut: Interactive Foreground Extraction using Iterated Graph
/// Cuts", SIGGRAPH 2004): a pixel is scored by how much likelier its colour is under the subject's
/// model than the background's, the models are histograms as in Boykov and Jolly ("Interactive
/// Graph Cuts for Optimal Boundary and Region Segmentation", ICCV 2001), and they are estimated
/// again from the first labelling. Where a background seen in earlier pictures exists, its
/// difference from the picture is a second, stronger cue (background subtraction). Neighbouring
/// pixels of similar colour are pulled towards the same label by a few mean-field updates of the
/// same contrast-sensitive smoothness GrabCut uses, the result is snapped to the picture's edges with
/// a guided filter (He, Sun and Tang, "Guided Image Filtering", ECCV 2010), and it is blended with
/// the previous picture's result, carried along with the box, so it does not flicker. Pieces not
/// joined to the middle of the box are dropped and holes inside the subject are closed.
/// </para>
/// All of this runs on a grid of at most <see cref="GridBudget"/> cells, so its cost does not
/// grow with the size of the picture or of the subject.
/// </summary>
public sealed partial class SubjectRemovalMatte
{
    /// <summary>The most cells the analysis grid has, whatever the area's size.</summary>
    public const int GridBudget = 10_000;

    /// <summary>The widest soft edge the removal draws, in pixels.</summary>
    public const int MaximumFeather = 12;

    /// <summary>Distances are kept in eighths of a pixel.</summary>
    public const int DistanceUnit = 8;

    /// <summary>Distances are capped at 48 pixels either way, which never changes an opacity or a recording decision.</summary>
    public const int SaturatedDistance = 48 * DistanceUnit;

    private const int Saturated = SaturatedDistance;

    private const int Levels = 16;
    private const int BinCount = Levels * Levels * Levels;
    private const float Far = 4000;
    // A background histogram bin this unlikely holds a colour the ring around the box never shows.
    private const float Unseen = 2e-4f;
    private const byte Inside = 0;
    private const byte Band = 1;
    private const byte Beyond = 2;
    // Growth needs this many cells of the outer ring to know what the background looks like.
    private const int MinimumRing = 12;
    // A colour holding less than this many of the ring's cells' worth of its histogram is rare there.
    private const float RareInRing = 0.25f;

    private readonly float[] _subjectHistogram = new float[BinCount];
    private readonly float[] _backgroundHistogram = new float[BinCount];
    private readonly float[] _subjectMemory = new float[BinCount];
    private readonly float[] _backgroundMemory = new float[BinCount];
    private readonly float[] _scratch = new float[BinCount];
    private readonly float[] _ratio = new float[BinCount];
    private readonly float[] _ringHistogram = new float[BinCount];
    private bool _hasModels;

    private byte[] _colour = [];
    private float[] _grey = [];
    private short[] _bin = [];
    private byte[] _region = [];
    private bool[] _middle = [];
    private float[] _cue = [];
    private bool[] _observed = [];
    private float[] _prior = [];
    private float[] _unary = [];
    private float[] _probability = [];
    private float[] _next = [];
    private float[] _weights = [];
    private float[] _filterA = [];
    private float[] _filterB = [];
    private float[] _filterC = [];
    private float[] _filterD = [];
    private float[] _filterE = [];
    private byte[] _label = [];
    private int[] _queue = [];
    private float[] _outside = [];
    private float[] _inside = [];
    private short[] _distance = [];
    private int[] _columns = [];
    private float[] _fractions = [];
    private float[] _blend = [];

    private float[] _memory = [];
    private bool _hasMemory;
    private (int Left, int Top, int Right, int Bottom) _memoryBox;
    private (int Left, int Top) _memoryOrigin;
    private int _memoryScale;
    private int _memoryWidth;
    private int _memoryHeight;

    private int _gridWidth;
    private int _gridHeight;
    // How many cells the outline may grow outwards.
    private int _growth;
    private (int Width, int Height) _picture;

    /// <summary>The picture area the matte covers; the subject can extend past the box into it.</summary>
    public (int Left, int Top, int Right, int Bottom) Area { get; private set; }

    /// <summary>How many picture pixels one analysis cell spans across.</summary>
    public int Scale { get; private set; } = 1;

    /// <summary>
    /// How far beyond the subject's outline, in pixels, the removal stays fully opaque: enough to take
    /// the subject's blurred edge and the grid's own coarseness with it.
    /// </summary>
    public float Grow => 3 + 0.5f * Scale;

    /// <summary>The share of the box found to be subject, from 0 to 1.</summary>
    public float Coverage { get; private set; }

    /// <summary>Whether nothing in the box could be told apart from its surroundings.</summary>
    public bool IsEmpty { get; private set; } = true;

    /// <summary>
    /// The signed distance of each pixel of <see cref="Area"/> from the subject's outline, row by row,
    /// in units of <see cref="DistanceUnit"/> per pixel: negative inside the subject, positive outside.
    /// </summary>
    public ReadOnlySpan<short> Distance =>
        _distance.AsSpan(0, (Area.Right - Area.Left) * (Area.Bottom - Area.Top));

    /// <summary>Forget the colour models, the previous picture's matte and the last result.</summary>
    public void Reset()
    {
        _hasModels = false;
        _hasMemory = false;
        IsEmpty = true;
        Coverage = 0;
    }

    /// <summary>
    /// Find the subject around <paramref name="box"/> in <paramref name="frame"/> (BGRA).
    /// <paramref name="coverage"/> runs from 0 (only what is clearly the subject) to 100 (the whole
    /// box); 50 is balanced. <paramref name="plate"/>, when given, is the background seen before.
    /// With <paramref name="remember"/> false (a paused redraw) the models and the previous matte are
    /// used but not changed. <paramref name="shape"/>, when given, is the subject's outline in this
    /// very picture (one byte a pixel, non-zero inside, the picture's size), drawn by a segmentation
    /// model: it is taken as the matte, widened or narrowed by up to five pixels by
    /// <paramref name="coverage"/>, and the colour models and the remembered matte are learnt from it,
    /// so the pictures after it start from the true outline.
    /// </summary>
    public void Compute(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box,
        SubjectRemovalPlate? plate, int coverage, bool remember, ReadOnlySpan<byte> shape = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        box = (Math.Clamp(box.Left, 0, frame.Width - 1), Math.Clamp(box.Top, 0, frame.Height - 1),
            Math.Clamp(box.Right, 1, frame.Width), Math.Clamp(box.Bottom, 1, frame.Height));
        box = (box.Left, box.Top, Math.Max(box.Right, box.Left + 1), Math.Max(box.Bottom, box.Top + 1));
        var boxWidth = box.Right - box.Left;
        var boxHeight = box.Bottom - box.Top;
        // A tracking box follows the subject's texture, not its outline: an arm, a coat tail, the top
        // of a head or a pair of shoes often sticks out of it.
        var reachX = (int)(0.18f * boxWidth) + 4;
        var reachY = (int)(0.14f * boxHeight) + 4;
        var margin = MaximumFeather + 12;
        Area = (Math.Max(0, box.Left - reachX - margin), Math.Max(0, box.Top - reachY - margin),
            Math.Min(frame.Width, box.Right + reachX + margin), Math.Min(frame.Height, box.Bottom + reachY + margin));
        var areaWidth = Area.Right - Area.Left;
        var areaHeight = Area.Bottom - Area.Top;
        Scale = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)areaWidth * areaHeight / GridBudget)));
        _gridWidth = (areaWidth + Scale - 1) / Scale;
        _gridHeight = (areaHeight + Scale - 1) / Scale;
        var cells = _gridWidth * _gridHeight;
        Reserve(cells, areaWidth * areaHeight);
        _growth = (Math.Max(reachX, reachY) + MaximumFeather) / Scale + 1;
        _picture = (frame.Width, frame.Height);

        Read(frame, box, reachX, reachY, plate);
        if (!shape.IsEmpty)
        {
            if (shape.Length != frame.Width * frame.Height)
            {
                throw new ArgumentException("An outline has one value for each pixel of the picture.", nameof(shape));
            }

            Trace(shape, frame.Width, box, remember);
            KeepConnected();
            MeasureDistances((Math.Clamp(coverage, 0, 100) - 50) / 10f);
            return;
        }

        var bias = (Math.Clamp(coverage, 0, 100) - 50) / 50f * 2.5f;
        var wholeBox = coverage >= 100;
        // GrabCut's iteration: label with models from the box and the ring, then estimate the models
        // again from that labelling and label once more.
        BuildModels(firstPass: true, remember);
        Weigh();
        Score(bias);
        Smooth(1);
        Label(wholeBox);
        BuildModels(firstPass: false, remember);
        Score(bias);
        Smooth(3);
        Carry(box, remember);
        GuidedFilter();
        Label(wholeBox);
        Tidy();
        KeepConnected();
        Extend();
        MeasureDistances(0);
    }

    /// <summary>
    /// Takes a given outline as the matte: each cell is the share of its pixels inside it. The colour
    /// models are learnt from it alone and, with <paramref name="remember"/>, kept with it as the
    /// previous picture's matte.
    /// </summary>
    private void Trace(ReadOnlySpan<byte> shape, int pictureWidth, (int Left, int Top, int Right, int Bottom) box, bool remember)
    {
        var step = Math.Max(1, Scale / 4);
        for (var j = 0; j < _gridHeight; j++)
        {
            var top = Area.Top + j * Scale;
            var bottom = Math.Min(Area.Bottom, top + Scale);
            for (var i = 0; i < _gridWidth; i++)
            {
                var left = Area.Left + i * Scale;
                var right = Math.Min(Area.Right, left + Scale);
                int inside = 0, count = 0;
                for (var y = top; y < bottom; y += step)
                {
                    for (var x = left; x < right; x += step)
                    {
                        inside += shape[y * pictureWidth + x] != 0 ? 1 : 0;
                        count++;
                    }
                }

                var cell = j * _gridWidth + i;
                _probability[cell] = _region[cell] == Beyond ? 0 : inside / (float)count;
            }
        }

        Label(wholeBox: false);
        _hasModels = false;
        BuildModels(firstPass: false, remember);
        if (remember)
        {
            Remember(box);
        }
    }

    private void Reserve(int cells, int pixels)
    {
        Ensure(ref _colour, cells * 3);
        Ensure(ref _grey, cells);
        Ensure(ref _bin, cells);
        Ensure(ref _region, cells);
        Ensure(ref _middle, cells);
        Ensure(ref _cue, cells);
        Ensure(ref _observed, cells);
        Ensure(ref _prior, cells);
        Ensure(ref _unary, cells);
        Ensure(ref _probability, cells);
        Ensure(ref _next, cells);
        Ensure(ref _weights, cells * 4);
        Ensure(ref _filterA, cells);
        Ensure(ref _filterB, cells);
        Ensure(ref _filterC, cells);
        Ensure(ref _filterD, cells);
        Ensure(ref _filterE, cells);
        Ensure(ref _label, cells);
        Ensure(ref _queue, cells);
        Ensure(ref _outside, cells);
        Ensure(ref _inside, cells);
        Ensure(ref _distance, pixels);
    }

    internal static void Ensure<T>(ref T[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            // A quarter more than asked for, so an area that grows a little from picture to picture
            // as a subject moves does not allocate again each time.
            buffer = new T[length + length / 4];
        }
    }

    /// <summary>Averages each cell's pixels and notes where the cell lies relative to the box.</summary>
    private void Read(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box, int reachX, int reachY,
        SubjectRemovalPlate? plate)
    {
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var step = Math.Max(1, Scale / 4);
        var middleX = (box.Left + box.Right) / 2f;
        var middleY = (box.Top + box.Bottom) / 2f;
        for (var j = 0; j < _gridHeight; j++)
        {
            var top = Area.Top + j * Scale;
            var bottom = Math.Min(Area.Bottom, top + Scale);
            for (var i = 0; i < _gridWidth; i++)
            {
                var left = Area.Left + i * Scale;
                var right = Math.Min(Area.Right, left + Scale);
                int blue = 0, green = 0, red = 0, count = 0;
                for (var y = top; y < bottom; y += step)
                {
                    var row = y * stride;
                    for (var x = left; x < right; x += step)
                    {
                        var at = row + x * 4;
                        blue += plane[at];
                        green += plane[at + 1];
                        red += plane[at + 2];
                        count++;
                    }
                }

                var cell = j * _gridWidth + i;
                var (b, g, r) = (blue / count, green / count, red / count);
                (_colour[cell * 3], _colour[cell * 3 + 1], _colour[cell * 3 + 2]) = ((byte)b, (byte)g, (byte)r);
                _grey[cell] = (0.114f * b + 0.587f * g + 0.299f * r) / 255f;
                _bin[cell] = (short)(((b >> 4) << 8) | ((g >> 4) << 4) | (r >> 4));
                var x0 = (left + right - 1) / 2;
                var y0 = (top + bottom - 1) / 2;
                var outX = Math.Max(0, Math.Max(box.Left - x0, x0 - box.Right + 1)) / (float)reachX;
                var outY = Math.Max(0, Math.Max(box.Top - y0, y0 - box.Bottom + 1)) / (float)reachY;
                var outside = Math.Max(outX, outY);
                _region[cell] = outside == 0 ? Inside : outside <= 1 ? Band : Beyond;
                _middle[cell] = Math.Abs(x0 - middleX) < (box.Right - box.Left) / 4f
                    && Math.Abs(y0 - middleY) < (box.Bottom - box.Top) / 4f;
                // Positional prior: the box is a hint, the band beyond it is doubtful, and beyond the
                // band nothing is the subject.
                _prior[cell] = outside == 0 ? (_middle[cell] ? 0.3f : 0) : outside <= 1 ? -1 - 2 * outside : -30;
                // The top middle of a tracking box is nearly always the subject, a head or the roof of
                // a car, even when its colour is shared with the background beside it; just above
                // the box there, the doubt is smaller than elsewhere around it.
                if (outY <= 0.5f && outX == 0 && Math.Abs(x0 - middleX) < (box.Right - box.Left) / 6f
                    && y0 < box.Top + 0.3f * (box.Bottom - box.Top))
                {
                    _prior[cell] = outside == 0 ? 0.9f : -0.4f;
                }
                _cue[cell] = 0;
                _observed[cell] = false;
                if (plate is not null && plate.Sample(x0, y0, out var seen) == SubjectRemovalPlate.Observed)
                {
                    _observed[cell] = true;
                    // Background subtraction: a pixel that looks as the background did is background.
                    var difference = (Math.Abs((int)(seen & 0xFF) - b) + Math.Abs((int)((seen >> 8) & 0xFF) - g)
                        + Math.Abs((int)((seen >> 16) & 0xFF) - r)) / 3f;
                    _cue[cell] = Math.Clamp((difference - 16) / 6, -3.5f, 2.5f);
                }
            }
        }
    }

    private void BuildModels(bool firstPass, bool remember)
    {
        Array.Clear(_subjectHistogram);
        Array.Clear(_backgroundHistogram);
        var cells = _gridWidth * _gridHeight;
        for (var cell = 0; cell < cells; cell++)
        {
            var bin = _bin[cell];
            var region = _region[cell];
            if (firstPass)
            {
                if (region == Inside && _cue[cell] > -1)
                {
                    _subjectHistogram[bin] += _middle[cell] ? 1 : 0.6f;
                }

                if (region != Inside || _cue[cell] < -1)
                {
                    _backgroundHistogram[bin] += region == Band ? 0.5f : 1;
                }
            }
            else if (_label[cell] != 0)
            {
                _subjectHistogram[bin] += 1;
            }
            else
            {
                _backgroundHistogram[bin] += region == Inside ? 0.5f : 1;
            }
        }

        Normalise(_subjectHistogram);
        Normalise(_backgroundHistogram);
        if (_hasModels)
        {
            // Colour models change slowly; half of each comes from the pictures before.
            for (var bin = 0; bin < BinCount; bin++)
            {
                _subjectHistogram[bin] = 0.5f * (_subjectHistogram[bin] + _subjectMemory[bin]);
                _backgroundHistogram[bin] = 0.5f * (_backgroundHistogram[bin] + _backgroundMemory[bin]);
            }
        }

        if (!firstPass && remember)
        {
            _subjectHistogram.CopyTo(_subjectMemory, 0);
            _backgroundHistogram.CopyTo(_backgroundMemory, 0);
            _hasModels = true;
        }

        for (var bin = 0; bin < BinCount; bin++)
        {
            _ratio[bin] = Math.Clamp(MathF.Log(_subjectHistogram[bin] + 1e-4f) - MathF.Log(_backgroundHistogram[bin] + 1e-4f), -6, 6);
        }
    }

    /// <summary>Spreads each histogram over neighbouring bins, then scales it to sum to one.</summary>
    private void Normalise(float[] histogram)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            var stride = 1 << (4 * axis);
            for (var bin = 0; bin < BinCount; bin++)
            {
                var level = (bin >> (4 * axis)) & (Levels - 1);
                var before = level > 0 ? histogram[bin - stride] : histogram[bin];
                var after = level < Levels - 1 ? histogram[bin + stride] : histogram[bin];
                _scratch[bin] = 0.25f * before + 0.5f * histogram[bin] + 0.25f * after;
            }

            _scratch.CopyTo(histogram, 0);
        }

        var total = 0f;
        foreach (var value in histogram)
        {
            total += value;
        }

        if (total > 0)
        {
            for (var bin = 0; bin < BinCount; bin++)
            {
                histogram[bin] /= total;
            }
        }
    }

    /// <summary>
    /// Each cell's evidence for being the subject. The colour models are histograms of 4096 bins, a
    /// model with as many components as the subject has colours, so a black jacket and a face are
    /// both the subject's. A colour the background around the box never shows is the subject's
    /// whatever the rest of the subject looks like: a face above a dark coat, a hand, bright shoes.
    /// </summary>
    private void Score(float bias)
    {
        var cells = _gridWidth * _gridHeight;
        for (var cell = 0; cell < cells; cell++)
        {
            var bin = _bin[cell];
            var novel = _region[cell] != Beyond && _backgroundHistogram[bin] < Unseen
                ? (_region[cell] == Inside ? 2f : 1f)
                : 0;
            _unary[cell] = 0.8f * _ratio[bin] + novel + _prior[cell] + _cue[cell] + bias;
            _probability[cell] = Sigmoid(_unary[cell]);
        }
    }

    private static float Sigmoid(float value) => 1 / (1 + MathF.Exp(-value));

    /// <summary>
    /// Mean-field updates of a contrast-sensitive Potts model over eight neighbours, GrabCut's
    /// smoothness term with its automatically chosen contrast scale.
    /// </summary>
    private void Weigh()
    {
        var (w, h) = (_gridWidth, _gridHeight);
        var cells = w * h;
        // Right, down, down-right and down-left; each pair of neighbours is stored once, at the first.
        Span<int> offsets = [1, w, w + 1, w - 1];
        var total = 0.0;
        var pairs = 0;
        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                var cell = j * w + i;
                for (var direction = 0; direction < 4; direction++)
                {
                    var valid = direction == 0 ? i + 1 < w
                        : j + 1 < h && (direction == 1 || (direction == 2 ? i + 1 < w : i > 0));
                    var distance = valid ? ColourDistance(cell, cell + offsets[direction]) : -1;
                    _weights[cell * 4 + direction] = distance;
                    if (valid)
                    {
                        total += distance;
                        pairs++;
                    }
                }
            }
        }

        var beta = total > 0 ? (float)(pairs / (2 * total)) : 0;
        for (var at = 0; at < cells * 4; at++)
        {
            var distance = _weights[at];
            _weights[at] = distance < 0 ? 0 : MathF.Exp(-beta * distance) * ((at & 3) < 2 ? 1 : 0.7071f);
        }
    }

    private void Smooth(int iterations)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        var cells = w * h;
        Span<int> offsets = [1, w, w + 1, w - 1];
        const float Strength = 0.45f;
        var spin = _filterE;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var cell = 0; cell < cells; cell++)
            {
                spin[cell] = 2 * _probability[cell] - 1;
            }

            for (var cell = 0; cell < cells; cell++)
            {
                var pull = 0f;
                var at = cell * 4;
                for (var direction = 0; direction < 4; direction++)
                {
                    var offset = offsets[direction];
                    var forward = cell + offset;
                    if (forward < cells)
                    {
                        pull += _weights[at + direction] * spin[forward];
                    }

                    var backward = cell - offset;
                    if (backward >= 0)
                    {
                        pull += _weights[backward * 4 + direction] * spin[backward];
                    }
                }

                _probability[cell] = Sigmoid(_unary[cell] + Strength * pull);
            }
        }
    }

    private float ColourDistance(int a, int b)
    {
        var db = _colour[a * 3] - _colour[b * 3];
        var dg = _colour[a * 3 + 1] - _colour[b * 3 + 1];
        var dr = _colour[a * 3 + 2] - _colour[b * 3 + 2];
        return db * db + dg * dg + dr * dr;
    }

    private void Label(bool wholeBox)
    {
        var cells = _gridWidth * _gridHeight;
        for (var cell = 0; cell < cells; cell++)
        {
            _label[cell] = (byte)(_region[cell] == Beyond ? 0
                : wholeBox && _region[cell] == Inside ? 1
                : _probability[cell] > 0.51f ? 1 : 0);
        }
    }

    /// <summary>
    /// Blends in the previous picture's matte, read at the same place relative to the box, which moves
    /// with the subject; then remembers this one.
    /// </summary>
    private void Carry((int Left, int Top, int Right, int Bottom) box, bool remember)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        if (_hasMemory)
        {
            var oldWidth = _memoryBox.Right - _memoryBox.Left;
            var oldHeight = _memoryBox.Bottom - _memoryBox.Top;
            var newWidth = box.Right - box.Left;
            var newHeight = box.Bottom - box.Top;
            // A box enlarged for reacquisition is not evidence that the subject grew: when it changes
            // size a lot, the remembered matte stays in picture coordinates about the box's centre;
            // only a modest change is taken as the subject's change of scale.
            var loose = Math.Abs(newWidth - oldWidth) > oldWidth * 0.2f || Math.Abs(newHeight - oldHeight) > oldHeight * 0.2f;
            var scaleX = loose ? 1 : oldWidth / (float)newWidth;
            var scaleY = loose ? 1 : oldHeight / (float)newHeight;
            var offsetX = loose ? (_memoryBox.Left + _memoryBox.Right - box.Left - box.Right) / 2f : 0;
            var offsetY = loose ? (_memoryBox.Top + _memoryBox.Bottom - box.Top - box.Bottom) / 2f : 0;
            for (var j = 0; j < h; j++)
            {
                var y = Area.Top + (j + 0.5f) * Scale;
                var then = loose ? y + offsetY : _memoryBox.Top + (y - box.Top) * scaleY;
                var gy = (then - _memoryOrigin.Top) / _memoryScale - 0.5f;
                for (var i = 0; i < w; i++)
                {
                    var x = Area.Left + (i + 0.5f) * Scale;
                    var gx = ((loose ? x + offsetX : _memoryBox.Left + (x - box.Left) * scaleX) - _memoryOrigin.Left) / _memoryScale - 0.5f;
                    if (gx < 0 || gy < 0 || gx > _memoryWidth - 1 || gy > _memoryHeight - 1)
                    {
                        continue;
                    }

                    var x0 = Math.Min((int)gx, Math.Max(0, _memoryWidth - 2));
                    var y0 = Math.Min((int)gy, Math.Max(0, _memoryHeight - 2));
                    var x1 = Math.Min(x0 + 1, _memoryWidth - 1);
                    var y1 = Math.Min(y0 + 1, _memoryHeight - 1);
                    var fx = Math.Clamp(gx - x0, 0, 1);
                    var fy = Math.Clamp(gy - y0, 0, 1);
                    var top = _memory[y0 * _memoryWidth + x0] * (1 - fx) + _memory[y0 * _memoryWidth + x1] * fx;
                    var bottom = _memory[y1 * _memoryWidth + x0] * (1 - fx) + _memory[y1 * _memoryWidth + x1] * fx;
                    var cell = j * w + i;
                    _probability[cell] = 0.65f * _probability[cell] + 0.35f * (top + (bottom - top) * fy);
                }
            }
        }

        if (remember)
        {
            Remember(box);
        }
    }

    private void Remember((int Left, int Top, int Right, int Bottom) box)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        Ensure(ref _memory, w * h);
        _probability.AsSpan(0, w * h).CopyTo(_memory);
        (_memoryBox, _memoryOrigin, _memoryScale) = (box, (Area.Left, Area.Top), Scale);
        (_memoryWidth, _memoryHeight) = (w, h);
        _hasMemory = true;
    }

    /// <summary>He, Sun and Tang's guided filter with the grey picture as the guide, radius two cells.</summary>
    private void GuidedFilter()
    {
        const int Radius = 2;
        const float Epsilon = 0.003f;
        var cells = _gridWidth * _gridHeight;
        // filterA: mean guide, filterB: mean input, filterC: mean guide*input, filterD: mean guide*guide.
        for (var cell = 0; cell < cells; cell++)
        {
            _filterC[cell] = _grey[cell] * _probability[cell];
            _filterD[cell] = _grey[cell] * _grey[cell];
        }

        BoxFilter(_grey, _filterA, Radius);
        BoxFilter(_probability, _filterB, Radius);
        BoxFilter(_filterC, _filterC, Radius);
        BoxFilter(_filterD, _filterD, Radius);
        for (var cell = 0; cell < cells; cell++)
        {
            var variance = _filterD[cell] - _filterA[cell] * _filterA[cell];
            var a = (_filterC[cell] - _filterA[cell] * _filterB[cell]) / (variance + Epsilon);
            _filterC[cell] = a;
            _filterD[cell] = _filterB[cell] - a * _filterA[cell];
        }

        BoxFilter(_filterC, _filterC, Radius);
        BoxFilter(_filterD, _filterD, Radius);
        for (var cell = 0; cell < cells; cell++)
        {
            _probability[cell] = Math.Clamp(_filterC[cell] * _grey[cell] + _filterD[cell], 0, 1);
        }
    }

    /// <summary>A mean over a square window, clipped at the grid's edges; the output may be the input.</summary>
    private void BoxFilter(float[] input, float[] output, int radius)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        for (var j = 0; j < h; j++)
        {
            var row = j * w;
            var sum = 0f;
            for (var i = 0; i < Math.Min(radius, w); i++)
            {
                sum += input[row + i];
            }

            for (var i = 0; i < w; i++)
            {
                if (i + radius < w)
                {
                    sum += input[row + i + radius];
                }

                if (i - radius - 1 >= 0)
                {
                    sum -= input[row + i - radius - 1];
                }

                _filterE[row + i] = sum / (Math.Min(w - 1, i + radius) - Math.Max(0, i - radius) + 1);
            }
        }

        for (var i = 0; i < w; i++)
        {
            var sum = 0f;
            for (var j = 0; j < Math.Min(radius, h); j++)
            {
                sum += _filterE[j * w + i];
            }

            for (var j = 0; j < h; j++)
            {
                if (j + radius < h)
                {
                    sum += _filterE[(j + radius) * w + i];
                }

                if (j - radius - 1 >= 0)
                {
                    sum -= _filterE[(j - radius - 1) * w + i];
                }

                output[j * w + i] = sum / (Math.Min(h - 1, j + radius) - Math.Max(0, j - radius) + 1);
            }
        }
    }

    /// <summary>
    /// One pass of a majority filter over each cell's eight neighbours: a notch bitten into the
    /// outline, where a shadowed fold of clothing looked like the background, is closed, and a stray
    /// cell of background colour that looked like the subject is dropped.
    /// </summary>
    private void Tidy()
    {
        var (w, h) = (_gridWidth, _gridHeight);
        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                var cell = j * w + i;
                var around = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (x, y) = (i + dx, j + dy);
                        around += (dx | dy) != 0 && x >= 0 && y >= 0 && x < w && y < h ? _label[y * w + x] : 0;
                    }
                }

                _next[cell] = _region[cell] == Beyond ? 0 : around >= 6 ? 1 : around <= 1 ? 0 : _label[cell];
            }
        }

        for (var cell = 0; cell < w * h; cell++)
        {
            _label[cell] = (byte)_next[cell];
        }
    }
}
