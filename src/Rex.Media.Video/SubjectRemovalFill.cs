namespace Rex.Media.Video;

/// <summary>
/// Synthesises the background that was never seen, on a small grid, by copying texture from the
/// pixels around the hole so that every small patch of the result looks like a patch of the picture.
/// <para>
/// Method: patch-based completion by expectation-maximisation (Wexler, Shechtman and Irani,
/// "Space-Time Completion of Video", IEEE TPAMI 29(3), 2007): each hole cell is given the source
/// patch that best matches the patch around it, and each hole colour is then the weighted vote of
/// every chosen patch that covers it. Matches are found with PatchMatch (Barnes, Shechtman,
/// Finkelstein and Goldman, "PatchMatch: A Randomized Correspondence Algorithm for Structural Image
/// Editing", SIGGRAPH 2009): good matches propagate to neighbours, and a random search at shrinking
/// radii improves them. The hole is solved coarse to fine, starting from a pull-push fill (Gortler,
/// Grzeszczuk, Szeliski and Cohen, "The Lumigraph", SIGGRAPH 1996). A match suggested for a cell,
/// such as the one the previous picture used at the same place in the scene, competes with the
/// search's own, so a fill that was good stays put instead of boiling from frame to frame.
/// </para>
/// </summary>
public sealed class SubjectRemovalFill
{
    /// <summary>A cell whose colour is known.</summary>
    public const byte Known = 0;

    /// <summary>A hole cell to synthesise.</summary>
    public const byte Missing = 1;

    /// <summary>Patches are this many cells either side of their centre.</summary>
    public const int Radius = 2;

    private const int MaximumLevels = 5;

    private readonly Level[] _levels = new Level[MaximumLevels];
    private float[] _pullColour = [];
    private float[] _pullWeight = [];
    private readonly int[] _pullOffsets = new int[32];
    private readonly int[] _pullSizes = new int[64];
    private float[] _vote = [];
    private uint _random = 2463534242;

    public SubjectRemovalFill()
    {
        for (var i = 0; i < MaximumLevels; i++)
        {
            _levels[i] = new Level();
        }
    }

    /// <summary>The finest grid's width in cells.</summary>
    public int Width { get; private set; }

    /// <summary>The finest grid's height in cells.</summary>
    public int Height { get; private set; }

    /// <summary>Three bytes a cell, blue, green and red: the picture where known, the fill after <see cref="Solve"/>.</summary>
    public Span<byte> Colour => _levels[0].Colour.AsSpan(0, Width * Height * 3);

    /// <summary>Each cell's state: <see cref="Known"/> or <see cref="Missing"/>.</summary>
    public Span<byte> State => _levels[0].State.AsSpan(0, Width * Height);

    /// <summary>For hole cells, a suggested source cell (an index), or -1 for none.</summary>
    public Span<int> Guess => _levels[0].Guess.AsSpan(0, Width * Height);

    /// <summary>For hole cells after <see cref="Solve"/>, the centre of the patch copied there, or -1.</summary>
    public ReadOnlySpan<int> Source => _levels[0].Source.AsSpan(0, Width * Height);

    /// <summary>How many coarse-to-fine levels the last solve used; 0 when it only blended from the edges.</summary>
    public int LevelsUsed { get; private set; }

    /// <summary>Set the grid's size; the caller then fills <see cref="Colour"/>, <see cref="State"/> and <see cref="Guess"/>.</summary>
    public void Prepare(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        (Width, Height) = (width, height);
        _levels[0].Reserve(width, height);
    }

    /// <summary>Fill every hole cell. <paramref name="seed"/> makes the random search repeatable.</summary>
    public void Solve(uint seed)
    {
        _random = seed | 1;
        var top = _levels[0];
        var cells = Width * Height;
        var holes = 0;
        int left = Width, right = -1, upper = Height, lower = -1;
        for (var cell = 0; cell < cells; cell++)
        {
            top.Source[cell] = -1;
            var hole = top.State[cell] != Known;
            top.Hole[cell] = hole;
            if (!hole)
            {
                continue;
            }

            holes++;
            var (x, y) = (cell % Width, cell / Width);
            (left, right, upper, lower) = (Math.Min(left, x), Math.Max(right, x), Math.Min(upper, y), Math.Max(lower, y));
        }

        LevelsUsed = 0;
        if (holes == 0)
        {
            return;
        }

        if (!top.FindSources())
        {
            // Nothing around the hole is big enough to copy from: blend in from its edges instead.
            PullPush(top);
            return;
        }

        var extent = Math.Max(right - left, lower - upper) + 1;
        var count = 1;
        while (count < MaximumLevels && extent >> count > 12
            && Math.Min(_levels[count - 1].Width, _levels[count - 1].Height) / 2 >= 4 * Radius + 2)
        {
            _levels[count].Shrink(_levels[count - 1]);
            if (!_levels[count].FindSources())
            {
                break;
            }

            count++;
        }

        LevelsUsed = count;
        var coarsest = _levels[count - 1];
        PullPush(coarsest);
        for (var cell = 0; cell < coarsest.Width * coarsest.Height; cell++)
        {
            if (!coarsest.Hole[cell])
            {
                continue;
            }

            var guess = count == 1 ? coarsest.Guess[cell] : -1;
            coarsest.Source[cell] = guess >= 0 && coarsest.SourceOk[guess] ? guess : RandomSource(coarsest);
            coarsest.Distance[cell] = Patch(coarsest, cell, coarsest.Source[cell], float.MaxValue);
        }

        Iterate(coarsest, 4, wide: true);
        for (var level = count - 2; level >= 0; level--)
        {
            var fine = _levels[level];
            Upsample(_levels[level + 1], fine, useGuess: level == 0);
            Iterate(fine, 2, wide: true);
        }
    }

    /// <summary>Carries a coarse level's matches to the next finer level, doubling every offset.</summary>
    private void Upsample(Level coarse, Level fine, bool useGuess)
    {
        for (var cell = 0; cell < fine.Width * fine.Height; cell++)
        {
            fine.Source[cell] = -1;
            if (!fine.Hole[cell])
            {
                continue;
            }

            var (x, y) = (cell % fine.Width, cell / fine.Width);
            var parent = Math.Min(y / 2, coarse.Height - 1) * coarse.Width + Math.Min(x / 2, coarse.Width - 1);
            var candidate = -1;
            if (coarse.Source[parent] >= 0)
            {
                var source = coarse.Source[parent];
                var sx = Math.Clamp(source % coarse.Width * 2 + (x & 1), 0, fine.Width - 1);
                var sy = Math.Clamp(source / coarse.Width * 2 + (y & 1), 0, fine.Height - 1);
                candidate = sy * fine.Width + sx;
            }

            fine.Source[cell] = candidate >= 0 && fine.SourceOk[candidate] ? candidate : RandomSource(fine);
            fine.Distance[cell] = 0;
        }

        // The patch distances need the fine level's colours, which the vote below sets from these
        // matches; the guess from the previous picture competes once they are known.
        Vote(fine);
        for (var cell = 0; cell < fine.Width * fine.Height; cell++)
        {
            if (!fine.Hole[cell])
            {
                continue;
            }

            fine.Distance[cell] = Patch(fine, cell, fine.Source[cell], float.MaxValue);
            var guess = useGuess ? fine.Guess[cell] : -1;
            if (guess >= 0 && fine.SourceOk[guess])
            {
                var distance = Patch(fine, cell, guess, fine.Distance[cell]);
                if (distance < fine.Distance[cell])
                {
                    (fine.Source[cell], fine.Distance[cell]) = (guess, distance);
                }
            }
        }
    }

    private int RandomSource(Level level)
    {
        while (true)
        {
            var cell = (int)(Next() % (uint)(level.Width * level.Height));
            if (level.SourceOk[cell])
            {
                return cell;
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

    private void Iterate(Level level, int passes, bool wide)
    {
        var (w, h) = (level.Width, level.Height);
        for (var pass = 0; pass < passes; pass++)
        {
            var forward = pass % 2 == 0;
            var step = forward ? 1 : -1;
            for (var n = 0; n < w * h; n++)
            {
                var cell = forward ? n : w * h - 1 - n;
                if (!level.Hole[cell])
                {
                    continue;
                }

                var (x, y) = (cell % w, cell / w);
                // Propagation: the neighbour already visited this pass suggests its own match, moved along.
                Try(level, cell, x - step, y, step, 0);
                Try(level, cell, x, y - step, 0, step);
                // Random search around the best match at halving radii, then once anywhere.
                var best = level.Source[cell];
                for (var radius = wide ? Math.Max(w, h) : 4; radius >= 1; radius /= 2)
                {
                    var sx = best % w + (int)(Next() % (uint)(2 * radius + 1)) - radius;
                    var sy = best / w + (int)(Next() % (uint)(2 * radius + 1)) - radius;
                    if (sx >= 0 && sy >= 0 && sx < w && sy < h)
                    {
                        Consider(level, cell, sy * w + sx);
                        best = level.Source[cell];
                    }
                }

                Consider(level, cell, (int)(Next() % (uint)(w * h)));
            }

            Vote(level);
        }
    }

    private static void Try(Level level, int cell, int nx, int ny, int dx, int dy)
    {
        var (w, h) = (level.Width, level.Height);
        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
        {
            return;
        }

        var neighbour = ny * w + nx;
        var source = level.Source[neighbour];
        if (source < 0)
        {
            return;
        }

        var sx = source % w + dx;
        var sy = source / w + dy;
        if (sx >= 0 && sy >= 0 && sx < w && sy < h)
        {
            Consider(level, cell, sy * w + sx);
        }
    }

    private static void Consider(Level level, int cell, int candidate)
    {
        if (!level.SourceOk[candidate] || candidate == level.Source[cell])
        {
            return;
        }

        var distance = Patch(level, cell, candidate, level.Distance[cell]);
        if (distance < level.Distance[cell])
        {
            (level.Source[cell], level.Distance[cell]) = (candidate, distance);
        }
    }

    /// <summary>Sum of squared colour differences between the patches at two cells, stopping once past <paramref name="limit"/>.</summary>
    private static float Patch(Level level, int cell, int source, float limit)
    {
        var (w, h) = (level.Width, level.Height);
        var (x, y) = (cell % w, cell / w);
        var offset = (source - cell) * 3;
        var colour = level.Colour;
        var total = 0;
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var py = y + dy;
            if (py < 0 || py >= h)
            {
                continue;
            }

            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var px = x + dx;
                if (px < 0 || px >= w)
                {
                    continue;
                }

                var at = (py * w + px) * 3;
                var b = colour[at] - colour[at + offset];
                var g = colour[at + 1] - colour[at + offset + 1];
                var r = colour[at + 2] - colour[at + offset + 2];
                total += b * b + g * g + r * r;
            }

            if (total > limit)
            {
                return total;
            }
        }

        return total;
    }

    /// <summary>Each hole colour becomes the weighted mean of what every match covering it would put there.</summary>
    private void Vote(Level level)
    {
        var (w, h) = (level.Width, level.Height);
        var cells = w * h;
        MatteEnsure(ref _vote, cells * 3);
        var spread = 0.0;
        var count = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            if (level.Hole[cell])
            {
                spread += level.Distance[cell];
                count++;
            }
        }

        // Wexler's weights favour the closer matches, on the scale of a typical match's distance.
        var scale = (float)(spread / Math.Max(1, count)) + 1;
        for (var cell = 0; cell < cells; cell++)
        {
            if (!level.Hole[cell])
            {
                continue;
            }

            var (x, y) = (cell % w, cell / w);
            float blue = 0, green = 0, red = 0, total = 0;
            for (var dy = -Radius; dy <= Radius; dy++)
            {
                for (var dx = -Radius; dx <= Radius; dx++)
                {
                    var (qx, qy) = (x + dx, y + dy);
                    if (qx < 0 || qy < 0 || qx >= w || qy >= h)
                    {
                        continue;
                    }

                    var q = qy * w + qx;
                    var source = level.Source[q];
                    if (!level.Hole[q] || source < 0)
                    {
                        continue;
                    }

                    var at = (source - dy * w - dx) * 3;
                    var weight = MathF.Exp(-Math.Min(30, level.Distance[q] / scale));
                    blue += weight * level.Colour[at];
                    green += weight * level.Colour[at + 1];
                    red += weight * level.Colour[at + 2];
                    total += weight;
                }
            }

            _vote[cell * 3] = blue / total;
            _vote[cell * 3 + 1] = green / total;
            _vote[cell * 3 + 2] = red / total;
        }

        for (var cell = 0; cell < cells; cell++)
        {
            if (level.Hole[cell])
            {
                level.Colour[cell * 3] = (byte)(_vote[cell * 3] + 0.5f);
                level.Colour[cell * 3 + 1] = (byte)(_vote[cell * 3 + 1] + 0.5f);
                level.Colour[cell * 3 + 2] = (byte)(_vote[cell * 3 + 2] + 0.5f);
            }
        }
    }

    private static void MatteEnsure(ref float[] buffer, int length) => SubjectRemovalMatte.Ensure(ref buffer, length);

    /// <summary>
    /// Gortler's pull-push: known colours are averaged up a pyramid to the top, then pushed back down
    /// into every cell with too little weight of its own, giving a smooth fill with no gaps.
    /// </summary>
    private void PullPush(Level level)
    {
        var (w, h) = (level.Width, level.Height);
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

        MatteEnsure(ref _pullColour, total * 3);
        MatteEnsure(ref _pullWeight, total);
        for (var cell = 0; cell < w * h; cell++)
        {
            var known = !level.Hole[cell];
            _pullWeight[cell] = known ? 1 : 0;
            for (var c = 0; c < 3; c++)
            {
                _pullColour[cell * 3 + c] = known ? level.Colour[cell * 3 + c] : 0;
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
            if (level.Hole[cell])
            {
                for (var c = 0; c < 3; c++)
                {
                    level.Colour[cell * 3 + c] = (byte)Math.Clamp(_pullColour[cell * 3 + c] + 0.5f, 0, 255);
                }
            }
        }
    }

    /// <summary>One level of the coarse-to-fine pyramid.</summary>
    private sealed class Level
    {
        public int Width;
        public int Height;
        public byte[] Colour = [];
        public byte[] State = [];
        public bool[] Hole = [];
        public bool[] SourceOk = [];
        public int[] Guess = [];
        public int[] Source = [];
        public float[] Distance = [];
        private int[] _unknown = [];

        public void Reserve(int width, int height)
        {
            (Width, Height) = (width, height);
            var cells = width * height;
            SubjectRemovalMatte.Ensure(ref Colour, cells * 3);
            SubjectRemovalMatte.Ensure(ref State, cells);
            SubjectRemovalMatte.Ensure(ref Hole, cells);
            SubjectRemovalMatte.Ensure(ref SourceOk, cells);
            SubjectRemovalMatte.Ensure(ref Guess, cells);
            SubjectRemovalMatte.Ensure(ref Source, cells);
            SubjectRemovalMatte.Ensure(ref Distance, cells);
            SubjectRemovalMatte.Ensure(ref _unknown, (width + 1) * (height + 1));
        }

        /// <summary>Half the size of <paramref name="fine"/>: a cell is a hole if any of its four is.</summary>
        public void Shrink(Level fine)
        {
            Reserve(fine.Width / 2, fine.Height / 2);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var cell = y * Width + x;
                    int blue = 0, green = 0, red = 0;
                    var hole = false;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var child = (y * 2 + dy) * fine.Width + x * 2 + dx;
                            hole |= fine.Hole[child];
                            blue += fine.Colour[child * 3];
                            green += fine.Colour[child * 3 + 1];
                            red += fine.Colour[child * 3 + 2];
                        }
                    }

                    Hole[cell] = hole;
                    State[cell] = hole ? Missing : Known;
                    Source[cell] = -1;
                    (Colour[cell * 3], Colour[cell * 3 + 1], Colour[cell * 3 + 2]) = ((byte)(blue / 4), (byte)(green / 4), (byte)(red / 4));
                }
            }
        }

        /// <summary>Marks the cells whose whole patch is known and inside the grid; false when there are none.</summary>
        public bool FindSources()
        {
            var stride = Width + 1;
            // The buffer may be reused at another width, so the zero row and column are set each time.
            Array.Clear(_unknown, 0, stride);
            for (var y = 0; y < Height; y++)
            {
                _unknown[(y + 1) * stride] = 0;
                var run = 0;
                for (var x = 0; x < Width; x++)
                {
                    run += Hole[y * Width + x] ? 1 : 0;
                    _unknown[(y + 1) * stride + x + 1] = _unknown[y * stride + x + 1] + run;
                }
            }

            var any = false;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var ok = x >= Radius && y >= Radius && x + Radius < Width && y + Radius < Height
                        && _unknown[(y + Radius + 1) * stride + x + Radius + 1] - _unknown[(y - Radius) * stride + x + Radius + 1]
                            - _unknown[(y + Radius + 1) * stride + x - Radius] + _unknown[(y - Radius) * stride + x - Radius] == 0;
                    SourceOk[y * Width + x] = ok;
                    any |= ok;
                }
            }

            return any;
        }
    }
}
