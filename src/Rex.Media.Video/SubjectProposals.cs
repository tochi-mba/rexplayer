using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>A box proposed around a click, in picture pixels, with how much it looks like one whole thing.</summary>
public readonly record struct SubjectProposal(int Left, int Top, int Width, int Height, double Score);

/// <summary>
/// The boxes a single click could mean, best first. Subject Lock follows a rectangle of texture,
/// and a click says only "this thing"; the box that tracks best is the one that holds the whole
/// thing and little else. This proposes such boxes from the evidence a picture gives about where
/// one object ends and its surroundings begin, the same evidence general object-proposal methods
/// use:
///
/// * Colour contrast with the surroundings. A box around a whole object holds colours that the
///   ring just outside it does not; a box inside the object shares its colours with that ring,
///   and a box that is too large shares them with the background. Measured as the histogram
///   intersection distance (Swain and Ballard, "Color indexing", IJCV 7(1), 1991) between the
///   interior and the ring, the colour-contrast cue of Alexe, Deselaers and Ferrari, "Measuring
///   the objectness of image windows" (IEEE TPAMI 34(11), 2012).
/// * Edges on the boundary rather than across it. A box fits an object when its sides lie along
///   the object's outline, and fits it badly when outlines cross its sides, the scoring rule of
///   Zitnick and Dollar, "Edge Boxes: Locating Object Proposals from Edges" (ECCV 2014), and the
///   edge-density cue of Alexe and colleagues. Along a vertical side an outline is a horizontal
///   change of light and an outline crossing it a vertical one, so the two gradient directions
///   are summed separately.
/// * Texture inside, which the tracker needs: a box of flat colour matches anywhere.
/// * Unlike its neighbourhood: a box that looks like the places beside it is the start of a
///   tracker sliding onto a lookalike. Only the tracker can say how alike two places look to it,
///   so the caller measures that with the tracker's own comparison and combines it through
///   <see cref="Rank"/>.
/// * The click well inside, since the user pointed at the thing itself.
///
/// The picture is first reduced to a grid of cells around the click, with summed-area tables
/// (Crow, "Summed-area tables for texture mapping", SIGGRAPH 1984) of brightness and both gradient
/// directions, so each box costs the same whatever its size. Boxes are tried at ten sizes from 4 to
/// 45 percent of the picture's shorter side, four shapes and nine placements around the click; the
/// region grown from the click by colour (Adams and Bischof, "Seeded region growing", IEEE TPAMI
/// 16(6), 1994) adds its own bounding box; and the best few are refined side by side. All buffers
/// are kept between clicks, so a click allocates nothing once the first has been served.
/// </summary>
public sealed class SubjectProposals
{
    /// <summary>The most boxes <see cref="Find"/> proposes.</summary>
    public const int Capacity = 6;

    /// <summary>Box sizes as the square root of their area, in shares of the picture's shorter side.</summary>
    private static readonly double[] Scales = [0.04, 0.055, 0.075, 0.1, 0.12, 0.155, 0.2, 0.26, 0.34, 0.45];

    /// <summary>Box shapes, width over height: square, one by two, two by three and three by two.</summary>
    private static readonly double[] Aspects = [1, 0.5, 2.0 / 3, 1.5];

    /// <summary>Where the click sits from the box's centre, in shares of its size.</summary>
    private static readonly double[] Offsets = [0, -0.25, 0.25];

    // Four chromaticity steps on each of two axes and four steps of brightness.
    private const int Bins = 64;
    private const int Smallest = 3;
    private const int Cells = 216;
    private const double Reach = 0.58;
    private const double Margin = 0.8;
    private const int Tolerance = 54;

    private readonly SubjectProposal[] _found = new SubjectProposal[Capacity];
    private readonly Box[] _pool = new Box[Scales.Length * Aspects.Length * Offsets.Length * Offsets.Length + 2];
    private readonly Box[] _kept = new Box[Capacity];
    private readonly int[] _inside = new int[Bins];
    private readonly int[] _outside = new int[Bins];
    private byte[] _blue = [];
    private byte[] _green = [];
    private byte[] _red = [];
    private byte[] _luma = [];
    private byte[] _bin = [];
    private int[] _queue = [];
    private int[] _seen = [];
    private long[] _sumLuma = [];
    private long[] _sumSquare = [];
    private long[] _sumAcross = [];
    private long[] _sumDown = [];
    private int _stamp;
    private int _pooled;
    private int _cell;
    private int _originX;
    private int _originY;
    private int _across;
    private int _down;
    private int _pictureWidth;
    private int _pictureHeight;
    private int _clickX;
    private int _clickY;
    private int _least;

    private readonly record struct Box(int Left, int Top, int Right, int Bottom, double Score);

    private readonly record struct Stats(double Mean, double Deviation, double Across, double Down);

    /// <summary>The boxes found by the last <see cref="Find"/>, best first.</summary>
    public ReadOnlySpan<SubjectProposal> Proposals => _found.AsSpan(0, Count);

    /// <summary>How many boxes the last <see cref="Find"/> proposed.</summary>
    public int Count { get; private set; }

    /// <summary>How many boxes the last <see cref="Find"/> scored, for cost diagnostics.</summary>
    public int Evaluated { get; private set; }

    /// <summary>
    /// Propose boxes around (<paramref name="x"/>, <paramref name="y"/>), in picture coordinates
    /// from 0 to 1, on a BGRA picture. False when the click is outside the picture or the picture
    /// is too small to hold a box.
    /// </summary>
    public bool Find(VideoFrame frame, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format != PixelFormat.Bgra32)
        {
            throw new ArgumentException("A BGRA picture is required.", nameof(frame));
        }

        Count = 0;
        Evaluated = 0;
        _pooled = 0;
        if (!(x >= 0 && x <= 1 && y >= 0 && y <= 1))
        {
            return false;
        }

        Build(frame, x, y);
        Scan();
        Grow();
        Choose();
        return Count > 0;
    }

    /// <summary>
    /// The rank of a proposal once the tracker has compared it with the places around it:
    /// <paramref name="resemblance"/> is the tracker's own similarity (0 to 1) to the most alike
    /// of them. Resemblance below two thirds is ordinary; above it the box costs more the closer
    /// its neighbour comes to being a twin.
    /// </summary>
    public static double Rank(double score, double resemblance) =>
        score - 2 * Math.Max(0, resemblance - 0.8);

    /// <summary>
    /// Whether a box's score says that something stands out from its surroundings there. Whole
    /// objects in film and television score from about a half upwards; a box over foliage, a
    /// tiled wall or a plain surface, where no edge or colour sets anything apart, scores about
    /// four tenths or less, and locking it would follow a patch of background.
    /// </summary>
    public static bool StandsOut(double score) => score >= 0.45;

    /// <summary>Whether a neighbour so alike that the tracker could not tell the two apart rules a box out.</summary>
    public static bool IsAmbiguous(double resemblance) => resemblance >= 0.9;

    private void Build(VideoFrame frame, float x, float y)
    {
        (_pictureWidth, _pictureHeight) = (frame.Width, frame.Height);
        var shorter = Math.Min(frame.Width, frame.Height);
        _cell = Math.Max(1, (shorter + Cells - 1) / Cells);
        var px = Math.Min(frame.Width - 1, (int)(x * frame.Width));
        var py = Math.Min(frame.Height - 1, (int)(y * frame.Height));
        var half = (int)(Reach * shorter);
        _originX = Math.Max(0, px - half);
        _originY = Math.Max(0, py - half);
        _across = Math.Max(1, (Math.Min(frame.Width, px + half + 1) - _originX) / _cell);
        _down = Math.Max(1, (Math.Min(frame.Height, py + half + 1) - _originY) / _cell);
        _clickX = Math.Min(_across - 1, (px - _originX) / _cell);
        _clickY = Math.Min(_down - 1, (py - _originY) / _cell);
        // No box side shorter than the narrow side of the smallest box tried.
        _least = Math.Max(Smallest, (int)Math.Round(Scales[0] * Math.Sqrt(Aspects[1]) * shorter / _cell));
        var cells = _across * _down;
        if (_luma.Length < cells)
        {
            _blue = new byte[cells];
            _green = new byte[cells];
            _red = new byte[cells];
            _luma = new byte[cells];
            _bin = new byte[cells];
            _queue = new int[cells];
            _seen = new int[cells];
            _stamp = 0;
        }

        var tables = (_across + 1) * (_down + 1);
        if (_sumLuma.Length < tables)
        {
            _sumLuma = new long[tables];
            _sumSquare = new long[tables];
            _sumAcross = new long[tables];
            _sumDown = new long[tables];
        }

        // Each cell is the mean of four taps spread over it, as the tracker reads its own grid.
        var near = _cell / 4;
        var far = _cell * 3 / 4;
        for (var j = 0; j < _down; j++)
        {
            var upper = frame.Row(0, _originY + j * _cell + near);
            var lower = frame.Row(0, _originY + j * _cell + far);
            for (var i = 0; i < _across; i++)
            {
                var a = (_originX + i * _cell + near) * 4;
                var c = (_originX + i * _cell + far) * 4;
                var b = (upper[a] + upper[c] + lower[a] + lower[c]) >> 2;
                var g = (upper[a + 1] + upper[c + 1] + lower[a + 1] + lower[c + 1]) >> 2;
                var r = (upper[a + 2] + upper[c + 2] + lower[a + 2] + lower[c + 2]) >> 2;
                var k = j * _across + i;
                var l = (b * 29 + g * 150 + r * 77) >> 8;
                (_blue[k], _green[k], _red[k], _luma[k]) = ((byte)b, (byte)g, (byte)r, (byte)l);
                _bin[k] = (byte)(Chroma(b, g, r) * 4 + l / 64);
            }
        }

        var stride = _across + 1;
        Array.Clear(_sumLuma, 0, stride);
        Array.Clear(_sumSquare, 0, stride);
        Array.Clear(_sumAcross, 0, stride);
        Array.Clear(_sumDown, 0, stride);
        for (var j = 0; j < _down; j++)
        {
            long luma = 0, square = 0, across = 0, down = 0;
            var row = (j + 1) * stride;
            _sumLuma[row] = _sumSquare[row] = _sumAcross[row] = _sumDown[row] = 0;
            var up = Math.Max(0, j - 1) * _across;
            var below = Math.Min(_down - 1, j + 1) * _across;
            for (var i = 0; i < _across; i++)
            {
                var k = j * _across + i;
                int value = _luma[k];
                luma += value;
                square += value * value;
                across += Math.Abs(_luma[j * _across + Math.Min(_across - 1, i + 1)] - _luma[j * _across + Math.Max(0, i - 1)]);
                down += Math.Abs(_luma[below + i] - _luma[up + i]);
                var at = row + i + 1;
                _sumLuma[at] = _sumLuma[at - stride] + luma;
                _sumSquare[at] = _sumSquare[at - stride] + square;
                _sumAcross[at] = _sumAcross[at - stride] + across;
                _sumDown[at] = _sumDown[at - stride] + down;
            }
        }
    }

    /// <summary>The chromaticity bin of a colour: which way its hue leans, whatever its brightness.</summary>
    private static int Chroma(int b, int g, int r)
    {
        var sum = b + g + r;
        // Near-black has no reliable colour; it shares the first bin, told apart only by brightness.
        if (sum < 36) return 0;
        var red = Math.Min(3, r * 4 / (sum * 3 / 5 + 1));
        var green = Math.Min(3, g * 4 / (sum * 3 / 5 + 1));
        return red * 4 + green;
    }

    private void Scan()
    {
        var shorter = Math.Min(_pictureWidth, _pictureHeight) / (double)_cell;
        foreach (var scale in Scales)
        {
            foreach (var aspect in Aspects)
            {
                var width = Math.Max(Smallest, (int)Math.Round(scale * shorter * Math.Sqrt(aspect)));
                var height = Math.Max(Smallest, (int)Math.Round(scale * shorter / Math.Sqrt(aspect)));
                if (width > _across || height > _down) continue;
                foreach (var dx in Offsets)
                {
                    foreach (var dy in Offsets)
                    {
                        // Moved inwards where it would leave the picture, as long as the click stays inside it.
                        var left = Math.Clamp((int)Math.Round(_clickX + 0.5 - width * (0.5 + dx)), 0, _across - width);
                        var top = Math.Clamp((int)Math.Round(_clickY + 0.5 - height * (0.5 + dy)), 0, _down - height);
                        Consider(left, top, left + width, top + height, Margin);
                    }
                }
            }
        }
    }

    private void Consider(int left, int top, int right, int bottom, double margin)
    {
        if (!Holds(left, top, right, bottom, margin)) return;
        _pool[_pooled++] = new Box(left, top, right, bottom, Evaluate(left, top, right, bottom));
    }

    /// <summary>Whether the click lies within <paramref name="margin"/> of the box's half-size from its centre.</summary>
    private bool Holds(int left, int top, int right, int bottom, double margin) =>
        Math.Abs(Offset(_clickX, left, right)) <= margin && Math.Abs(Offset(_clickY, top, bottom)) <= margin;

    private static double Offset(int click, int low, int high) => (2.0 * click + 1 - low - high) / (high - low);

    /// <summary>
    /// Grow a region of like colour outward from the click, comparing each neighbour with the
    /// region's running mean, and propose its bounding box. A region that floods past the largest
    /// box has leaked into the background and proposes nothing.
    /// </summary>
    private void Grow()
    {
        var shorter = Math.Min(_pictureWidth, _pictureHeight) / (double)_cell;
        var limit = (int)(0.45 * 0.45 * 2 * shorter * shorter);
        var stamp = ++_stamp;
        var seed = _clickY * _across + _clickX;
        long blue = _blue[seed], green = _green[seed], red = _red[seed];
        var count = 1;
        _seen[seed] = stamp;
        _queue[0] = seed;
        var (left, top, right, bottom) = (_clickX, _clickY, _clickX, _clickY);
        for (var head = 0; head < count; head++)
        {
            var at = _queue[head];
            var x = at % _across;
            var y = at / _across;
            (left, right) = (Math.Min(left, x), Math.Max(right, x));
            (top, bottom) = (Math.Min(top, y), Math.Max(bottom, y));
            if (count > limit) return;
            for (var side = 0; side < 4; side++)
            {
                var nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0);
                var ny = y + (side == 2 ? -1 : side == 3 ? 1 : 0);
                if ((uint)nx >= (uint)_across || (uint)ny >= (uint)_down) continue;
                var next = ny * _across + nx;
                if (_seen[next] == stamp) continue;
                var distance = Math.Abs(_blue[next] * count - blue) + Math.Abs(_green[next] * count - green)
                    + Math.Abs(_red[next] * count - red);
                if (distance > Tolerance * count) continue;
                _seen[next] = stamp;
                _queue[count++] = next;
                (blue, green, red) = (blue + _blue[next], green + _green[next], red + _red[next]);
            }
        }

        if (right - left + 1 < _least || bottom - top + 1 < _least) return;
        Consider(left, top, right + 1, bottom + 1, 1);
        Consider(Math.Max(0, left - 1), Math.Max(0, top - 1), Math.Min(_across, right + 2), Math.Min(_down, bottom + 2), 1);
    }

    /// <summary>Keep the best boxes that are not near-copies of a better one, refine each, and rank them again.</summary>
    private void Choose()
    {
        var pool = _pool.AsSpan(0, _pooled);
        pool.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        var kept = 0;
        foreach (var box in pool)
        {
            if (kept == Capacity) break;
            if (Overlaps(box, kept, 0.6)) continue;
            _kept[kept++] = Refine(box);
        }

        var chosen = _kept.AsSpan(0, kept);
        chosen.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        foreach (var box in chosen)
        {
            // Two starts can climb to the same box; the second adds nothing.
            if (Overlaps(box, Count, 0.85, _found)) continue;
            var left = _originX + box.Left * _cell;
            var top = _originY + box.Top * _cell;
            var right = box.Right == _across && _originX + _across * _cell + _cell > _pictureWidth ? _pictureWidth : _originX + box.Right * _cell;
            var bottom = box.Bottom == _down && _originY + _down * _cell + _cell > _pictureHeight ? _pictureHeight : _originY + box.Bottom * _cell;
            _found[Count++] = new SubjectProposal(left, top, right - left, bottom - top, box.Score);
        }
    }

    private bool Overlaps(Box box, int kept, double limit)
    {
        for (var i = 0; i < kept; i++)
        {
            if (Overlap(box.Left, box.Top, box.Right, box.Bottom, _kept[i].Left, _kept[i].Top, _kept[i].Right, _kept[i].Bottom) > limit) return true;
        }

        return false;
    }

    private bool Overlaps(Box box, int found, double limit, SubjectProposal[] proposals)
    {
        for (var i = 0; i < found; i++)
        {
            var p = proposals[i];
            if (Overlap(_originX + box.Left * _cell, _originY + box.Top * _cell, _originX + box.Right * _cell, _originY + box.Bottom * _cell,
                p.Left, p.Top, p.Left + p.Width, p.Top + p.Height) > limit) return true;
        }

        return false;
    }

    /// <summary>Intersection over union of two boxes given by their edges.</summary>
    public static double Overlap(int left, int top, int right, int bottom, int otherLeft, int otherTop, int otherRight, int otherBottom)
    {
        var width = Math.Max(0, Math.Min(right, otherRight) - Math.Max(left, otherLeft));
        var height = Math.Max(0, Math.Min(bottom, otherBottom) - Math.Max(top, otherTop));
        var shared = (double)width * height;
        var union = (double)(right - left) * (bottom - top) + (double)(otherRight - otherLeft) * (otherBottom - otherTop) - shared;
        return union > 0 ? shared / union : 0;
    }

    /// <summary>Move each side in or out while that improves the box, halving the step when nothing does.</summary>
    private Box Refine(Box box)
    {
        var step = Math.Max(1, Math.Min(box.Right - box.Left, box.Bottom - box.Top) / 8);
        for (var round = 0; round < 24 && step > 0; round++)
        {
            var improved = false;
            for (var move = 0; move < 8; move++)
            {
                var change = move % 2 == 0 ? -step : step;
                var (l, t, r, b) = (box.Left, box.Top, box.Right, box.Bottom);
                switch (move / 2)
                {
                    case 0: l += change; break;
                    case 1: r += change; break;
                    case 2: t += change; break;
                    default: b += change; break;
                }

                if (l < 0 || t < 0 || r > _across || b > _down || r - l < _least || b - t < _least
                    || !Holds(l, t, r, b, 1)) continue;
                var score = Evaluate(l, t, r, b);
                if (score <= box.Score) continue;
                box = new Box(l, t, r, b, score);
                improved = true;
            }

            if (!improved) step /= 2;
        }

        return box;
    }

    private Stats Measure(int left, int top, int right, int bottom)
    {
        double area = (right - left) * (bottom - top);
        var mean = Sum(_sumLuma, left, top, right, bottom) / area;
        var deviation = Math.Sqrt(Math.Max(0, Sum(_sumSquare, left, top, right, bottom) / area - mean * mean));
        return new Stats(mean, deviation, Sum(_sumAcross, left, top, right, bottom) / area,
            Sum(_sumDown, left, top, right, bottom) / area);
    }

    private double Sum(long[] table, int left, int top, int right, int bottom)
    {
        var stride = _across + 1;
        return table[bottom * stride + right] - table[top * stride + right] - table[bottom * stride + left] + table[top * stride + left];
    }

    /// <summary>How much the box at these cell edges looks like one whole object around the click.</summary>
    private double Evaluate(int left, int top, int right, int bottom)
    {
        Evaluated++;
        var width = right - left;
        var height = bottom - top;
        var inside = Measure(left, top, right, bottom);
        var inner = inside.Across + inside.Down;
        var ring = Math.Max(1, Math.Min(width, height) / 4);
        var (outerLeft, outerTop) = (Math.Max(0, left - ring), Math.Max(0, top - ring));
        var (outerRight, outerBottom) = (Math.Min(_across, right + ring), Math.Min(_down, bottom + ring));
        var ringArea = (double)(outerRight - outerLeft) * (outerBottom - outerTop) - width * height;
        var outer = ringArea > 0
            ? (Sum(_sumAcross, outerLeft, outerTop, outerRight, outerBottom) + Sum(_sumDown, outerLeft, outerTop, outerRight, outerBottom)
                - inner * width * height) / ringArea
            : inner;
        var colour = ringArea > 0 ? Contrast(left, top, right, bottom, outerLeft, outerTop, outerRight, outerBottom) : 0;
        var texture = Math.Abs(inner - outer) / (inner + outer + 4);
        var detail = Math.Min(1, inside.Deviation / 16);
        var centred = 1 - 0.5 * (Square(Offset(_clickX, left, right)) + Square(Offset(_clickY, top, bottom)));
        return 0.34 * colour + 0.30 * Edges(left, top, right, bottom, inner, outer) + 0.10 * texture
            + 0.10 * detail + 0.16 * centred;
    }

    private static double Square(double value) => value * value;

    /// <summary>
    /// Histogram intersection distance between the box's colours and the ring's. The interior and
    /// each side of the ring are sampled on a grid of at most sixteen steps across the box, so the
    /// cost of a box does not grow with its size, and every side of the ring is read at least once.
    /// </summary>
    private double Contrast(int left, int top, int right, int bottom, int outerLeft, int outerTop, int outerRight, int outerBottom)
    {
        Array.Clear(_inside);
        Array.Clear(_outside);
        var stepX = Math.Max(1, (right - left) / 16);
        var stepY = Math.Max(1, (bottom - top) / 16);
        var inside = Tally(_inside, left, top, right, bottom, stepX, stepY);
        var outside = Tally(_outside, outerLeft, outerTop, outerRight, top, stepX, stepY)
            + Tally(_outside, outerLeft, bottom, outerRight, outerBottom, stepX, stepY)
            + Tally(_outside, outerLeft, top, left, bottom, stepX, stepY)
            + Tally(_outside, right, top, outerRight, bottom, stepX, stepY);
        var shared = 0d;
        for (var i = 0; i < Bins; i++)
        {
            shared += Math.Min(_inside[i] / (double)inside, _outside[i] / (double)outside);
        }

        return 1 - shared;
    }

    private int Tally(int[] histogram, int left, int top, int right, int bottom, int stepX, int stepY)
    {
        var count = 0;
        for (var y = top; y < bottom; y += stepY)
        {
            for (var x = left; x < right; x += stepX)
            {
                histogram[_bin[y * _across + x]]++;
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// How strongly the box's sides follow outlines rather than cut across texture, from 0 to 1.
    /// Each side is scored on a thin band straddling it, and the weakest side counts as much as
    /// the average, because a box fits only when all its sides do. A side on the picture's own
    /// edge says nothing, since an object cut off by the frame has no outline there.
    /// </summary>
    private double Edges(int left, int top, int right, int bottom, double inner, double outer)
    {
        var band = Math.Max(1, Math.Min(right - left, bottom - top) / 8);
        var floor = 0.5 * inner + outer + 8;
        var total = 0d;
        var weakest = 1d;
        var sides = 0;
        if (_originX + left * _cell >= _cell)
        {
            Add(Side(_sumAcross, _sumDown, left - band, top, left + band, bottom, floor));
        }

        if (_originX + right * _cell + _cell <= _pictureWidth)
        {
            Add(Side(_sumAcross, _sumDown, right - band, top, right + band, bottom, floor));
        }

        if (_originY + top * _cell >= _cell)
        {
            Add(Side(_sumDown, _sumAcross, left, top - band, right, top + band, floor));
        }

        if (_originY + bottom * _cell + _cell <= _pictureHeight)
        {
            Add(Side(_sumDown, _sumAcross, left, bottom - band, right, bottom + band, floor));
        }

        return sides == 0 ? 0 : 0.5 * total / sides + 0.5 * weakest;

        void Add(double side)
        {
            total += side;
            weakest = Math.Min(weakest, side);
            sides++;
        }
    }

    private double Side(long[] along, long[] crossing, int left, int top, int right, int bottom, double floor)
    {
        (left, top) = (Math.Max(0, left), Math.Max(0, top));
        (right, bottom) = (Math.Min(_across, right), Math.Min(_down, bottom));
        double area = (right - left) * (bottom - top);
        var outline = Sum(along, left, top, right, bottom) / area;
        var cut = Sum(crossing, left, top, right, bottom) / area;
        return outline / (outline + cut + floor);
    }
}
