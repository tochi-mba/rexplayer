namespace Rex.Media.Video;

/// <summary>
/// The matte's outline: the subject grown into what joins it, pieces apart from it dropped, holes
/// in it closed, and every pixel's signed distance from the result.
/// </summary>
public sealed partial class SubjectRemovalMatte
{
    /// <summary>
    /// Grows the subject outwards from its outline into cells that join it and look like nothing the
    /// outer ring around the area shows, or, where the background was seen before, unlike what was
    /// seen there: a hand, a bag strap, a shoe or a jacket's edge just past the box, or a face whose
    /// colour the first labelling doubted. It is region growing (Adams and Bischof, "Seeded Region
    /// Growing", IEEE TPAMI 16(6), 1994) with the background model as the test, taken from the outer
    /// ring alone so that the parts of the subject in the band beside the box cannot teach the model
    /// that they are background. It is bounded: a few cells beyond the band's reach, never the
    /// area's edge, and at most half as many cells again as the subject had, nearest first.
    /// </summary>
    private void Extend()
    {
        var (w, h) = (_gridWidth, _gridHeight);
        var cells = w * h;
        Array.Clear(_ringHistogram);
        var ring = 0;
        var subject = 0;
        var count = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            if (_label[cell] == 1)
            {
                subject++;
                _next[cell] = 0;
                _queue[count++] = cell;
            }
            else if (_region[cell] == Beyond)
            {
                // Only the outer edge of the analysis area is a background sample. The
                // nearer Beyond cells may still contain an outstretched hand or shoe.
                var (x, y) = (cell % w, cell / w);
                if (x < 3 || x >= w - 3 || y < 3 || y >= h - 3)
                {
                    _ringHistogram[_bin[cell]] += 1;
                    ring++;
                }
            }
        }

        if (subject == 0 || ring < MinimumRing)
        {
            return;
        }

        Normalise(_ringHistogram);
        // A colour shown by fewer than a couple of the ring's cells is not the background's.
        var rare = RareInRing / ring;
        var budget = subject / 2;
        var depth = _growth;
        // The area's outermost two cells stay background, so the outline always closes, except
        // where the area ends at the picture's edge, which the subject may reach.
        var (first, top) = (Area.Left == 0 ? 0 : 2, Area.Top == 0 ? 0 : 2);
        var (end, bottom) = (Area.Right == _picture.Width ? w : w - 2, Area.Bottom == _picture.Height ? h : h - 2);
        for (var head = 0; head < count && budget > 0; head++)
        {
            var cell = _queue[head];
            var (i, j) = (cell % w, cell / w);
            var reached = _next[cell] + 1;
            for (var dy = -1; dy <= 1 && budget > 0; dy++)
            {
                for (var dx = -1; dx <= 1 && budget > 0; dx++)
                {
                    var (x, y) = (i + dx, j + dy);
                    if (x < first || y < top || x >= end || y >= bottom)
                    {
                        continue;
                    }

                    var next = y * w + x;
                    // Grow only beyond the chosen rectangle: the normal matte has already
                    // settled the pixels inside it. Relabelling those pixels here weakens
                    // its background-model evidence and worsens the moving-picture mask.
                    if (reached > depth || _region[next] == Inside || _label[next] != 0 || !Unlike(next, rare))
                    {
                        continue;
                    }

                    _label[next] = 1;
                    _next[next] = reached;
                    _queue[count++] = next;
                    budget--;
                }
            }
        }
    }

    /// <summary>Whether a cell differs from the background seen there before or, where none was seen, from every colour the outer ring shows.</summary>
    private bool Unlike(int cell, float rare) =>
        _observed[cell] ? _cue[cell] > 2f : _ringHistogram[_bin[cell]] < rare;

    /// <summary>Keeps only what joins the middle of the box and fills holes enclosed by the subject.</summary>
    private void KeepConnected()
    {
        var (w, h) = (_gridWidth, _gridHeight);
        var cells = w * h;
        var count = 0;
        for (var pass = 0; pass < 2 && count == 0; pass++)
        {
            for (var cell = 0; cell < cells; cell++)
            {
                if (_label[cell] == 1 && _region[cell] == Inside && (pass == 1 || _middle[cell]))
                {
                    _label[cell] = 2;
                    _queue[count++] = cell;
                }
            }
        }

        Flood(count, from: 1, to: 2, diagonal: true);
        // Everything still marked 1 was not reached from the middle: background that looked alike.
        count = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            if (_label[cell] == 1)
            {
                _label[cell] = 0;
            }

            var (i, j) = (cell % w, cell / w);
            if (_label[cell] == 0 && (i == 0 || j == 0 || i == w - 1 || j == h - 1))
            {
                _label[cell] = 3;
                _queue[count++] = cell;
            }
        }

        Flood(count, from: 0, to: 3, diagonal: false);
        var inside = 0;
        var subject = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            // Background reached from the edge is background; any left unreached is a hole inside
            // the subject and is removed with it.
            _label[cell] = (byte)(_label[cell] == 3 ? 0 : 1);
            if (_region[cell] == Inside)
            {
                inside++;
                subject += _label[cell];
            }
        }

        Coverage = inside > 0 ? subject / (float)inside : 0;
        IsEmpty = subject == 0;
    }

    private void Flood(int count, byte from, byte to, bool diagonal)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        for (var head = 0; head < count; head++)
        {
            var cell = _queue[head];
            var (i, j) = (cell % w, cell / w);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diagonal && dx != 0 && dy != 0))
                    {
                        continue;
                    }

                    var (x, y) = (i + dx, j + dy);
                    if (x < 0 || y < 0 || x >= w || y >= h || _label[y * w + x] != from)
                    {
                        continue;
                    }

                    _label[y * w + x] = to;
                    _queue[count++] = y * w + x;
                }
            }
        }
    }

    /// <summary>
    /// Chamfer distances on the grid (Borgefors, "Distance Transformations in Digital Images",
    /// CVGIP 34, 1986), turned into a signed distance and interpolated to every pixel of the area. The
    /// outline is moved <paramref name="widen"/> pixels outwards (inwards when negative).
    /// </summary>
    private void MeasureDistances(float widen)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        Chamfer(_outside, subject: true);
        Chamfer(_inside, subject: false);
        var cells = w * h;
        for (var cell = 0; cell < cells; cell++)
        {
            // The outline lies halfway between a subject cell and its background neighbour.
            _next[cell] = (_label[cell] == 1 ? 0.5f - _inside[cell] : _outside[cell] - 0.5f) * Scale - widen;
        }

        var areaWidth = Area.Right - Area.Left;
        var areaHeight = Area.Bottom - Area.Top;
        Ensure(ref _columns, areaWidth);
        Ensure(ref _fractions, areaWidth);
        Ensure(ref _blend, w + 1);
        for (var x = 0; x < areaWidth; x++)
        {
            var gx = Math.Clamp((x + 0.5f) / Scale - 0.5f, 0, w - 1);
            _columns[x] = (int)gx;
            _fractions[x] = gx - (int)gx;
        }

        for (var y = 0; y < areaHeight; y++)
        {
            var gy = Math.Clamp((y + 0.5f) / Scale - 0.5f, 0, h - 1);
            var y0 = (int)gy;
            var y1 = Math.Min(y0 + 1, h - 1);
            var fy = gy - y0;
            // Blend the two grid rows once, then each pixel only blends two neighbours of that row.
            for (var i = 0; i < w; i++)
            {
                _blend[i] = (_next[y0 * w + i] + (_next[y1 * w + i] - _next[y0 * w + i]) * fy) * DistanceUnit;
            }

            _blend[w] = _blend[w - 1];
            var row = _distance.AsSpan(y * areaWidth, areaWidth);
            var x = 0;
            for (var i = 0; i < w && x < areaWidth; i++)
            {
                // Between two cells both well inside the subject or both far outside it, every pixel
                // gets the same far value: only the band around the outline needs interpolating.
                var end = i + 1 < w ? Math.Min(areaWidth, (int)((i + 1.5f) * Scale)) : areaWidth;
                var (a, b) = (_blend[i], _blend[i + 1]);
                if ((a > Saturated && b > Saturated) || (a < -Saturated && b < -Saturated))
                {
                    row[x..end].Fill(a > 0 ? (short)Saturated : (short)-Saturated);
                    x = end;
                    continue;
                }

                for (; x < end; x++)
                {
                    var c = _columns[x];
                    row[x] = (short)Math.Clamp(_blend[c] + (_blend[c + 1] - _blend[c]) * _fractions[x], -Saturated, Saturated);
                }
            }
        }
    }

    /// <summary>Distance, in cells, from each cell to the nearest cell whose label is not <paramref name="subject"/>'s opposite.</summary>
    private void Chamfer(float[] distance, bool subject)
    {
        var (w, h) = (_gridWidth, _gridHeight);
        const float Diagonal = 1.4142f;
        for (var cell = 0; cell < w * h; cell++)
        {
            distance[cell] = (_label[cell] == 1) == subject ? 0 : Far;
        }

        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                var cell = j * w + i;
                var d = distance[cell];
                if (i > 0) d = Math.Min(d, distance[cell - 1] + 1);
                if (j > 0) d = Math.Min(d, distance[cell - w] + 1);
                if (i > 0 && j > 0) d = Math.Min(d, distance[cell - w - 1] + Diagonal);
                if (i + 1 < w && j > 0) d = Math.Min(d, distance[cell - w + 1] + Diagonal);
                distance[cell] = d;
            }
        }

        for (var j = h - 1; j >= 0; j--)
        {
            for (var i = w - 1; i >= 0; i--)
            {
                var cell = j * w + i;
                var d = distance[cell];
                if (i + 1 < w) d = Math.Min(d, distance[cell + 1] + 1);
                if (j + 1 < h) d = Math.Min(d, distance[cell + w] + 1);
                if (i + 1 < w && j + 1 < h) d = Math.Min(d, distance[cell + w + 1] + Diagonal);
                if (i > 0 && j + 1 < h) d = Math.Min(d, distance[cell + w - 1] + Diagonal);
                distance[cell] = d;
            }
        }
    }
}
