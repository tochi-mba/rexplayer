namespace Rex.Media.Video;

/// <summary>
/// Synthesises the background that was never seen, on a small grid, by copying texture from the
/// cells around the hole so that every small patch of the result looks like a patch of the picture.
/// <para>
/// Method: exemplar-based region filling (Criminisi, Perez and Toyama, "Region Filling and Object
/// Removal by Exemplar-Based Image Inpainting", IEEE TIP 13(9), 2004). The hole is filled from its
/// edge inwards, cells whose patch is mostly known first and, among those, cells where an edge meets
/// the hole, so lines such as a skirting board or a horizon are carried across before the flat
/// areas either side close over them. Each cell takes the colour of the known patch that best
/// matches what is already known around it (Efros and Leung, "Texture Synthesis by Non-parametric
/// Sampling", ICCV 1999). The candidates are the continuation of each filled neighbour's match
/// (Ashikhmin, "Synthesizing Natural Textures", I3D 2001), which copies whole stretches of texture
/// rather than a mosaic of single cells; a match suggested for the cell, such as the one the
/// previous picture used at the same place in the scene, which keeps the fill still from picture
/// to picture; and a random search near the hole that narrows around the best so far, as in
/// PatchMatch (Barnes et al., SIGGRAPH 2009). Sources are searched near the hole and nearer ones
/// are preferred, so the grass beside a person continues into the hole rather than the gravel
/// further away. A last pass matches every filled cell again against its now complete patch,
/// which removes the seams where fronts from different sides met.
/// </para>
/// When no whole patch is known anywhere, the hole is blended in from its edges instead, by
/// Gortler et al.'s pull-push ("The Lumigraph", SIGGRAPH 1996).
/// </summary>
public sealed class SubjectRemovalFill
{
    /// <summary>A cell whose colour is known.</summary>
    public const byte Known = 0;

    /// <summary>A hole cell to synthesise.</summary>
    public const byte Missing = 1;

    /// <summary>Patches are this many cells either side of their centre.</summary>
    public const int Radius = 2;

    private const int Side = 2 * Radius + 1;
    // Random candidates tried for every cell, spread over the search window.
    private const int Samples = 18;
    // How strongly a far source is penalised, relative to a typical patch difference.
    private const float Nearness = 0.15f;

    private byte[] _colour = [];
    private byte[] _state = [];
    private int[] _guess = [];
    private int[] _source = [];
    private bool[] _sourceOk = [];
    private float[] _confidence = [];
    private float[] _gradientX = [];
    private float[] _gradientY = [];
    private int[] _sums = [];
    private int[] _heapCells = [];
    private float[] _heapKeys = [];
    private int _heapCount;
    private float[] _pullColour = [];
    private float[] _pullWeight = [];
    private readonly int[] _pullOffsets = new int[32];
    private readonly int[] _pullSizes = new int[64];
    private uint _random = 2463534242;
    private int _window;

    /// <summary>The grid's width in cells.</summary>
    public int Width { get; private set; }

    /// <summary>The grid's height in cells.</summary>
    public int Height { get; private set; }

    /// <summary>Three bytes a cell, blue, green and red: the picture where known, the fill after <see cref="Solve"/>.</summary>
    public Span<byte> Colour => _colour.AsSpan(0, Width * Height * 3);

    /// <summary>Each cell's state: <see cref="Known"/> or <see cref="Missing"/>.</summary>
    public Span<byte> State => _state.AsSpan(0, Width * Height);

    /// <summary>For hole cells, a suggested source cell (an index), or -1 for none.</summary>
    public Span<int> Guess => _guess.AsSpan(0, Width * Height);

    /// <summary>For hole cells after <see cref="Solve"/>, the centre of the patch copied there, or -1.</summary>
    public ReadOnlySpan<int> Source => _source.AsSpan(0, Width * Height);

    /// <summary>Whether the last solve could only blend the hole in from its edges, having nothing whole to copy.</summary>
    public bool Blended { get; private set; }

    /// <summary>Set the grid's size; the caller then fills <see cref="Colour"/>, <see cref="State"/> and <see cref="Guess"/>.</summary>
    public void Prepare(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        (Width, Height) = (width, height);
        var cells = width * height;
        SubjectRemovalMatte.Ensure(ref _colour, cells * 3);
        SubjectRemovalMatte.Ensure(ref _state, cells);
        SubjectRemovalMatte.Ensure(ref _guess, cells);
        SubjectRemovalMatte.Ensure(ref _source, cells);
        SubjectRemovalMatte.Ensure(ref _sourceOk, cells);
        SubjectRemovalMatte.Ensure(ref _confidence, cells);
        SubjectRemovalMatte.Ensure(ref _gradientX, cells);
        SubjectRemovalMatte.Ensure(ref _gradientY, cells);
        SubjectRemovalMatte.Ensure(ref _sums, (width + 1) * (height + 1));
        // A cell enters the queue when it first borders the fill and again each time a neighbour is filled.
        SubjectRemovalMatte.Ensure(ref _heapCells, cells * 9);
        SubjectRemovalMatte.Ensure(ref _heapKeys, cells * 9);
    }

    /// <summary>Fill every hole cell. <paramref name="seed"/> makes the random search repeatable.</summary>
    public void Solve(uint seed)
    {
        _random = seed | 1;
        var cells = Width * Height;
        int holes = 0, left = Width, right = -1, upper = Height, lower = -1;
        for (var cell = 0; cell < cells; cell++)
        {
            _source[cell] = -1;
            var hole = _state[cell] != Known;
            _confidence[cell] = hole ? 0 : 1;
            if (hole)
            {
                holes++;
                var (x, y) = (cell % Width, cell / Width);
                (left, right, upper, lower) = (Math.Min(left, x), Math.Max(right, x), Math.Min(upper, y), Math.Max(lower, y));
            }
        }

        Blended = false;
        if (holes == 0)
        {
            return;
        }

        if (!FindSources())
        {
            PullPush();
            Blended = true;
            return;
        }

        // Sources are looked for within about the hole's own size of the cell being filled.
        _window = Math.Max(Math.Max(right - left, lower - upper) + 1, 3 * Side);
        MeasureEdges();
        _heapCount = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            if (_state[cell] != Known && Borders(cell))
            {
                Push(cell, Priority(cell));
            }
        }

        while (_heapCount > 0)
        {
            var cell = Pop();
            if (_source[cell] >= 0)
            {
                continue;
            }

            FillCell(cell);
            var (x, y) = (cell % Width, cell / Width);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var (nx, ny) = (x + dx, y + dy);
                    if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && _state[ny * Width + nx] != Known && _source[ny * Width + nx] < 0)
                    {
                        Push(ny * Width + nx, Priority(ny * Width + nx));
                    }
                }
            }
        }

        Polish(forward: true);
        Polish(forward: false);
        Polish(forward: true);
    }

    /// <summary>Whether a hole cell touches a known or filled cell.</summary>
    private bool Borders(int cell)
    {
        var (x, y) = (cell % Width, cell / Width);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var (nx, ny) = (x + dx, y + dy);
                if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && _confidence[ny * Width + nx] > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Criminisi's priority: how much of the cell's patch is already known, its confidence term, times
    /// how strongly a line in the known part runs straight into the hole, its data term: the known
    /// cells' isophotes (brightness gradients turned a right angle), added up so that the scattered
    /// gradients of grass or gravel cancel and only a coherent edge, such as a horizon or a skirting
    /// board, counts, then projected on the direction from the known side into the hole.
    /// </summary>
    private float Priority(int cell)
    {
        var (x, y) = (cell % Width, cell / Width);
        float confidence = 0, isoX = 0, isoY = 0, normalX = 0, normalY = 0;
        var known = 0;
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var py = y + dy;
            if (py < 0 || py >= Height)
            {
                continue;
            }

            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var px = x + dx;
                if (px < 0 || px >= Width)
                {
                    continue;
                }

                var at = py * Width + px;
                confidence += _confidence[at];
                if (_confidence[at] > 0)
                {
                    normalX -= dx;
                    normalY -= dy;
                }

                if (_state[at] == Known)
                {
                    isoX -= _gradientY[at];
                    isoY += _gradientX[at];
                    known++;
                }
            }
        }

        var length = MathF.Sqrt(normalX * normalX + normalY * normalY);
        var data = known == 0 || length == 0 ? 0 : Math.Min(1, MathF.Abs(isoX * normalX + isoY * normalY) / (length * known * 32));
        return confidence / (Side * Side) * (0.2f + data);
    }

    /// <summary>The summed confidence of a cell's patch: how much of it is known, and how surely.</summary>
    private float Confidence(int cell)
    {
        var (x, y) = (cell % Width, cell / Width);
        float confidence = 0;
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var py = y + dy;
            if (py < 0 || py >= Height)
            {
                continue;
            }

            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var px = x + dx;
                if (px >= 0 && px < Width)
                {
                    confidence += _confidence[py * Width + px];
                }
            }
        }

        return confidence;
    }

    /// <summary>Each known cell's brightness gradient, from its known neighbours either side; zero across the hole.</summary>
    private void MeasureEdges()
    {
        for (var cell = 0; cell < Width * Height; cell++)
        {
            var (x, y) = (cell % Width, cell / Width);
            _gradientX[cell] = _gradientY[cell] = 0;
            if (_state[cell] != Known)
            {
                continue;
            }

            if (x > 0 && x + 1 < Width && _state[cell - 1] == Known && _state[cell + 1] == Known)
            {
                _gradientX[cell] = (Grey(cell + 1) - Grey(cell - 1)) * 0.5f;
            }

            if (y > 0 && y + 1 < Height && _state[cell - Width] == Known && _state[cell + Width] == Known)
            {
                _gradientY[cell] = (Grey(cell + Width) - Grey(cell - Width)) * 0.5f;
            }
        }
    }

    private int Grey(int cell) => (_colour[cell * 3] + 2 * _colour[cell * 3 + 1] + _colour[cell * 3 + 2]) >> 2;

    /// <summary>Gives a hole cell the colour of the best of its candidate sources.</summary>
    private void FillCell(int cell)
    {
        var (x, y) = (cell % Width, cell / Width);
        var best = -1;
        var bestCost = float.MaxValue;
        var known = Confidence(cell);
        void Consider(int candidate, float discount)
        {
            if (candidate < 0 || candidate == best || !_sourceOk[candidate])
            {
                return;
            }

            var cost = Cost(cell, candidate, known, bestCost / discount) * discount;
            if (cost < bestCost)
            {
                (best, bestCost) = (candidate, cost);
            }
        }

        Consider(_guess[cell], 0.85f);
        // Continuing a neighbour's match copies whole stretches of texture.
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var (nx, ny) = (x + dx, y + dy);
                if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                {
                    continue;
                }

                var source = _source[ny * Width + nx];
                if (source >= 0)
                {
                    var (sx, sy) = (source % Width - dx, source / Width - dy);
                    if (sx >= 0 && sy >= 0 && sx < Width && sy < Height)
                    {
                        Consider(sy * Width + sx, 0.9f);
                    }
                }
            }
        }

        // Along-image candidates preserve a wall's mortar lines and the direction of
        // a floor's grain. Probe a few distances on both axes before random search;
        // random candidates alone can miss the matching side of a narrow occlusion.
        for (var distance = Side; distance <= _window; distance *= 2)
        {
            if (x - distance >= 0)
            {
                Consider(y * Width + x - distance, 1);
            }
            if (x + distance < Width)
            {
                Consider(y * Width + x + distance, 1);
            }
            if (y - distance >= 0)
            {
                Consider((y - distance) * Width + x, 1);
            }
            if (y + distance < Height)
            {
                Consider((y + distance) * Width + x, 1);
            }
        }

        for (var sample = 0; sample < Samples; sample++)
        {
            Consider(Near(x, y, _window), 1);
        }

        // PatchMatch's search: around the best so far at halving radii.
        for (var radius = _window / 2; radius >= 1 && best >= 0; radius /= 2)
        {
            Consider(Near(best % Width, best / Width, radius), 1);
        }

        if (best < 0)
        {
            // Every candidate drawn missed the sources; any source will do as a start.
            for (var candidate = 0; best < 0; candidate++)
            {
                best = _sourceOk[candidate] ? candidate : -1;
            }
        }

        _source[cell] = best;
        // Criminisi's confidence: a filled cell is as sure as the patch it was matched on.
        _confidence[cell] = Math.Max(0.05f, known / (Side * Side));
        CopyColour(cell, best);
    }

    private void CopyColour(int cell, int source)
    {
        _colour[cell * 3] = _colour[source * 3];
        _colour[cell * 3 + 1] = _colour[source * 3 + 1];
        _colour[cell * 3 + 2] = _colour[source * 3 + 2];
    }

    /// <summary>A random cell within <paramref name="reach"/> of (x, y), kept inside the grid.</summary>
    private int Near(int x, int y, int reach)
    {
        var sx = Math.Clamp(x + (int)(Next() % (uint)(2 * reach + 1)) - reach, 0, Width - 1);
        var sy = Math.Clamp(y + (int)(Next() % (uint)(2 * reach + 1)) - reach, 0, Height - 1);
        return sy * Width + sx;
    }

    /// <summary>
    /// The mean squared colour difference between the known and filled cells of a cell's patch and the
    /// same places around a source, weighted by how sure each is, plus a cost for a far source; stops
    /// early once past <paramref name="limit"/>. <paramref name="weight"/> is the patch's summed
    /// confidence, which bounds the mean from below while the sum is still being added up.
    /// </summary>
    private float Cost(int cell, int source, float weight, float limit)
    {
        var (x, y) = (cell % Width, cell / Width);
        var offset = source - cell;
        float total = 0;
        weight = Math.Max(weight, 1e-3f);
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var py = y + dy;
            if (py < 0 || py >= Height)
            {
                continue;
            }

            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var px = x + dx;
                var at = py * Width + px;
                if (px < 0 || px >= Width || _confidence[at] <= 0)
                {
                    continue;
                }

                var c = at * 3;
                var s = (at + offset) * 3;
                var b = _colour[c] - _colour[s];
                var g = _colour[c + 1] - _colour[s + 1];
                var r = _colour[c + 2] - _colour[s + 2];
                total += _confidence[at] * (b * b + g * g + r * r);
            }

            if (total / weight > limit)
            {
                return float.MaxValue;
            }
        }

        var (sx, sy) = (source % Width - x, source / Width - y);
        var distance = (sx * sx + sy * sy) / (float)(_window * _window);
        return total / weight * (1 + Nearness * distance) + 40 * distance;
    }

    /// <summary>One sweep matching every filled cell again over its whole patch, keeping a neighbour's continuation when it fits better.</summary>
    private void Polish(bool forward)
    {
        var cells = Width * Height;
        for (var n = 0; n < cells; n++)
        {
            var cell = forward ? n : cells - 1 - n;
            var current = _source[cell];
            if (current < 0)
            {
                continue;
            }

            var (x, y) = (cell % Width, cell / Width);
            var best = current;
            var known = Confidence(cell);
            var bestCost = Cost(cell, current, known, float.MaxValue);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var (nx, ny) = (x + dx, y + dy);
                    if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= Width || ny >= Height || _source[ny * Width + nx] < 0)
                    {
                        continue;
                    }

                    var source = _source[ny * Width + nx];
                    var (sx, sy) = (source % Width - dx, source / Width - dy);
                    if (sx < 0 || sy < 0 || sx >= Width || sy >= Height || !_sourceOk[sy * Width + sx] || sy * Width + sx == best)
                    {
                        continue;
                    }

                    var cost = Cost(cell, sy * Width + sx, known, bestCost);
                    if (cost < bestCost)
                    {
                        (best, bestCost) = (sy * Width + sx, cost);
                    }
                }
            }

            if (best != current)
            {
                _source[cell] = best;
                CopyColour(cell, best);
            }
        }
    }

    private uint Next()
    {
        // Marsaglia's xorshift: fast, repeatable and good enough to scatter search candidates.
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return _random;
    }

    private void Push(int cell, float key)
    {
        var at = _heapCount++;
        while (at > 0)
        {
            var parent = (at - 1) / 2;
            if (_heapKeys[parent] >= key)
            {
                break;
            }

            (_heapCells[at], _heapKeys[at]) = (_heapCells[parent], _heapKeys[parent]);
            at = parent;
        }

        (_heapCells[at], _heapKeys[at]) = (cell, key);
    }

    private int Pop()
    {
        var top = _heapCells[0];
        var (cell, key) = (_heapCells[--_heapCount], _heapKeys[_heapCount]);
        var at = 0;
        while (true)
        {
            var child = at * 2 + 1;
            if (child >= _heapCount)
            {
                break;
            }

            if (child + 1 < _heapCount && _heapKeys[child + 1] > _heapKeys[child])
            {
                child++;
            }

            if (_heapKeys[child] <= key)
            {
                break;
            }

            (_heapCells[at], _heapKeys[at]) = (_heapCells[child], _heapKeys[child]);
            at = child;
        }

        if (_heapCount > 0)
        {
            (_heapCells[at], _heapKeys[at]) = (cell, key);
        }

        return top;
    }

    /// <summary>Marks the cells whose whole patch is known and inside the grid; false when there are none.</summary>
    private bool FindSources()
    {
        var stride = Width + 1;
        // The buffer may be reused at another width, so the zero row and column are set each time.
        Array.Clear(_sums, 0, stride);
        for (var y = 0; y < Height; y++)
        {
            _sums[(y + 1) * stride] = 0;
            var run = 0;
            for (var x = 0; x < Width; x++)
            {
                run += _state[y * Width + x] != Known ? 1 : 0;
                _sums[(y + 1) * stride + x + 1] = _sums[y * stride + x + 1] + run;
            }
        }

        var any = false;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var ok = x >= Radius && y >= Radius && x + Radius < Width && y + Radius < Height
                    && _sums[(y + Radius + 1) * stride + x + Radius + 1] - _sums[(y - Radius) * stride + x + Radius + 1]
                        - _sums[(y + Radius + 1) * stride + x - Radius] + _sums[(y - Radius) * stride + x - Radius] == 0;
                _sourceOk[y * Width + x] = ok;
                any |= ok;
            }
        }

        return any;
    }

    /// <summary>
    /// Gortler's pull-push: known colours are averaged up a pyramid to the top, then pushed back down
    /// into every cell with too little weight of its own, giving a smooth fill with no gaps.
    /// </summary>
    private void PullPush()
    {
        var (w, h) = (Width, Height);
        var levels = 0;
        var total = 0;
        for (var (lw, lh) = (w, h); ; (lw, lh) = ((lw + 1) / 2, (lh + 1) / 2))
        {
            _pullOffsets[levels] = total;
            (_pullSizes[levels * 2], _pullSizes[levels * 2 + 1]) = (lw, lh);
            total += lw * lh;
            levels++;
            if (lw == 1 && lh == 1)
            {
                break;
            }
        }

        SubjectRemovalMatte.Ensure(ref _pullColour, total * 3);
        SubjectRemovalMatte.Ensure(ref _pullWeight, total);
        for (var cell = 0; cell < w * h; cell++)
        {
            var known = _state[cell] == Known;
            _pullWeight[cell] = known ? 1 : 0;
            for (var c = 0; c < 3; c++)
            {
                _pullColour[cell * 3 + c] = known ? _colour[cell * 3 + c] : 0;
            }
        }

        for (var k = 1; k < levels; k++)
        {
            var (fw, fh, fo) = (_pullSizes[(k - 1) * 2], _pullSizes[(k - 1) * 2 + 1], _pullOffsets[k - 1]);
            var (cw, ch, co) = (_pullSizes[k * 2], _pullSizes[k * 2 + 1], _pullOffsets[k]);
            for (var y = 0; y < ch; y++)
            {
                for (var x = 0; x < cw; x++)
                {
                    float weight = 0, blue = 0, green = 0, red = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var (fx, fy) = (x * 2 + dx, y * 2 + dy);
                            if (fx >= fw || fy >= fh)
                            {
                                continue;
                            }

                            var child = fo + fy * fw + fx;
                            var wt = _pullWeight[child];
                            weight += wt;
                            blue += wt * _pullColour[child * 3];
                            green += wt * _pullColour[child * 3 + 1];
                            red += wt * _pullColour[child * 3 + 2];
                        }
                    }

                    var at = co + y * cw + x;
                    var norm = weight > 0 ? 1 / weight : 0;
                    (_pullColour[at * 3], _pullColour[at * 3 + 1], _pullColour[at * 3 + 2]) = (blue * norm, green * norm, red * norm);
                    _pullWeight[at] = Math.Min(1, weight);
                }
            }
        }

        for (var k = levels - 2; k >= 0; k--)
        {
            var (fw, fh, fo) = (_pullSizes[k * 2], _pullSizes[k * 2 + 1], _pullOffsets[k]);
            var (cw, ch, co) = (_pullSizes[(k + 1) * 2], _pullSizes[(k + 1) * 2 + 1], _pullOffsets[k + 1]);
            for (var y = 0; y < fh; y++)
            {
                var gy = Math.Clamp((y + 0.5f) / 2 - 0.5f, 0, ch - 1);
                var y0 = (int)gy;
                var y1 = Math.Min(y0 + 1, ch - 1);
                var ty = gy - y0;
                for (var x = 0; x < fw; x++)
                {
                    var at = fo + y * fw + x;
                    var own = _pullWeight[at];
                    if (own >= 1)
                    {
                        continue;
                    }

                    var gx = Math.Clamp((x + 0.5f) / 2 - 0.5f, 0, cw - 1);
                    var x0 = (int)gx;
                    var x1 = Math.Min(x0 + 1, cw - 1);
                    var tx = gx - x0;
                    for (var c = 0; c < 3; c++)
                    {
                        var upper = _pullColour[(co + y0 * cw + x0) * 3 + c] * (1 - tx) + _pullColour[(co + y0 * cw + x1) * 3 + c] * tx;
                        var lower = _pullColour[(co + y1 * cw + x0) * 3 + c] * (1 - tx) + _pullColour[(co + y1 * cw + x1) * 3 + c] * tx;
                        var parent = upper + (lower - upper) * ty;
                        _pullColour[at * 3 + c] = own * _pullColour[at * 3 + c] + (1 - own) * parent;
                    }

                    _pullWeight[at] = 1;
                }
            }
        }

        for (var cell = 0; cell < w * h; cell++)
        {
            if (_state[cell] != Known)
            {
                for (var c = 0; c < 3; c++)
                {
                    _colour[cell * 3 + c] = (byte)Math.Clamp(_pullColour[cell * 3 + c] + 0.5f, 0, 255);
                }
            }
        }
    }
}
