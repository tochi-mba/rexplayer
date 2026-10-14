using System.Numerics;
using System.Runtime.InteropServices;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// The background as it has been seen, kept in the coordinates of the scene rather than of the
/// latest picture, so what the camera saw before a pan is still in the right place after it.
/// <para>
/// Method: a background mosaic (Irani, Anandan and Hsu, "Mosaic Based Representations of Video
/// Sequences and Their Applications", ICCV 1995), as used for video object removal by Patwardhan,
/// Sapiro and Bertalmio ("Video Inpainting Under Constrained Camera Motion", IEEE TIP 16(2), 2007)
/// and by Granados et al. ("Background Inpainting for Videos with Dynamic Objects and a
/// Free-Moving Camera", ECCV 2012). Each plate cell takes the picture's colour at exactly its own
/// scene point (a backward warp, bilinear between the four nearest pixels), so cells written from
/// different pictures agree with each other to a fraction of a pixel; a filled pixel is read back
/// from its scene point the same way.
/// </para>
/// Every cell records whether it was observed, estimated by the fill, or never seen, so the preview
/// can say truthfully which of its pixels the video really showed.
/// </summary>
public sealed class SubjectRemovalPlate
{
    /// <summary>Nothing is known about the cell.</summary>
    public const byte Unknown = 0;

    /// <summary>The cell holds a synthesised estimate, never a pixel the video showed.</summary>
    public const byte Estimated = 1;

    /// <summary>The cell holds a background pixel the video showed.</summary>
    public const byte Observed = 255;

    /// <summary>How many sightings in a row must agree before a cell counts as observed background.</summary>
    public const int Confirmations = 3;

    // Two sightings agree when their colours differ by at most this much, summed over the channels:
    // a little more than compression noise and a fraction of a pixel's misalignment leave.
    private const int Agreement = 42;

    // A zoom this far from where the scene was first recorded makes old cells too coarse to use. Up
    // to it a cell read back is a little soft, which is far less noticeable than the whole fill
    // turning into estimates at once.
    private const float MaximumZoom = 0.4f;

    private uint[] _cells = [];
    // Each cell's latest sighting, with how many sightings in a row agreed with it in the top byte.
    private uint[] _sightings = [];
    private float _scale = 1;
    private float _offsetX;
    private float _offsetY;
    // With no zoom every pixel lands the same fraction of a cell from a whole cell, so that whole
    // offset and fraction are worked out once per picture instead of once per pixel.
    private bool _unzoomed = true;
    private int _wholeX;
    private int _wholeY;
    private int _fractionX;
    private int _fractionY;

    /// <summary>The plate's width in cells, the width of the pictures it records.</summary>
    public int Width { get; private set; }

    /// <summary>The plate's height in cells.</summary>
    public int Height { get; private set; }

    /// <summary>Where the picture's point (x, y) is on the plate: (Scale * x + OffsetX, Scale * y + OffsetY).</summary>
    public (float Scale, float OffsetX, float OffsetY) Mapping => (_scale, _offsetX, _offsetY);

    /// <summary>Make room for pictures of this size; a new size forgets everything recorded.</summary>
    public void Prepare(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width != Width || height != Height)
        {
            (Width, Height) = (width, height);
            _cells = new uint[width * height];
            _sightings = new uint[width * height];
        }

        Clear();
    }

    /// <summary>Forget every cell and the memory it took.</summary>
    public void Release()
    {
        _cells = [];
        _sightings = [];
        Width = Height = 0;
        Map(1, 0, 0);
    }

    /// <summary>Forget every cell but keep the memory.</summary>
    public void Clear()
    {
        Array.Clear(_cells);
        Array.Clear(_sightings);
        Map(1, 0, 0);
    }

    private void Map(float scale, float offsetX, float offsetY)
    {
        (_scale, _offsetX, _offsetY) = (scale, offsetX, offsetY);
        _unzoomed = scale == 1;
        _wholeX = (int)MathF.Floor(offsetX);
        _wholeY = (int)MathF.Floor(offsetY);
        _fractionX = (int)((offsetX - _wholeX) * 256);
        _fractionY = (int)((offsetY - _wholeY) * 256);
    }

    /// <summary>Follow the camera from the previous picture to the next.</summary>
    public void Advance(CameraMotion motion)
    {
        var scale = _scale * motion.Scale;
        if (Math.Abs(MathF.Log(scale)) > MaximumZoom)
        {
            Clear();
            return;
        }

        Map(scale, _offsetX + _scale * motion.ShiftX, _offsetY + _scale * motion.ShiftY);
    }

    /// <summary>
    /// Keep the picture's area <paramref name="area"/> on the plate, moving what is recorded by whole
    /// cells when the camera has panned far enough to carry it past an edge. Cells pushed off the
    /// far side are forgotten.
    /// </summary>
    public void Keep((int Left, int Top, int Right, int Bottom) area)
    {
        var left = _scale * area.Left + _offsetX;
        var top = _scale * area.Top + _offsetY;
        var right = _scale * area.Right + _offsetX;
        var bottom = _scale * area.Bottom + _offsetY;
        if (left >= 0 && top >= 0 && right <= Width && bottom <= Height)
        {
            return;
        }

        var dx = (int)MathF.Round(Width / 2f - (left + right) / 2);
        var dy = (int)MathF.Round(Height / 2f - (top + bottom) / 2);
        Shift(_cells, dx, dy);
        Shift(_sightings, dx, dy);
        Map(_scale, _offsetX + dx, _offsetY + dy);
    }

    private void Shift(uint[] plate, int dx, int dy)
    {
        if (Math.Abs(dx) >= Width || Math.Abs(dy) >= Height)
        {
            Array.Clear(plate);
            return;
        }

        var cells = plate.AsSpan();
        var width = Width;
        var kept = width - Math.Abs(dx);
        var from = Math.Max(0, -dx);
        var to = Math.Max(0, dx);
        // Rows move in the direction that never overwrites a row still to be moved.
        for (var i = 0; i < Height; i++)
        {
            var y = dy > 0 ? Height - 1 - i : i;
            var row = cells.Slice(y * width, width);
            var source = y - dy;
            if (source < 0 || source >= Height)
            {
                row.Clear();
                continue;
            }

            cells.Slice(source * width + from, kept).CopyTo(row[to..]);
            row[..to].Clear();
            row[(to + kept)..].Clear();
        }
    }

    /// <summary>
    /// Record the background pixels of <paramref name="frame"/> (BGRA) inside <paramref name="area"/>.
    /// Inside <paramref name="guard"/> a pixel is recorded only where <paramref name="distance"/> (one
    /// value a pixel across the guard, in eighths of a pixel from the subject) exceeds
    /// <paramref name="clearance"/>; with no distances the whole guard is left alone. With a
    /// <paramref name="rowStep"/> above one, only every so many plate rows are recorded, those whose
    /// index plus <paramref name="rowPhase"/> it divides: a large area is recorded in turns over a few
    /// pictures instead of all at once.
    /// </summary>
    public void Learn(VideoFrame frame, (int Left, int Top, int Right, int Bottom) area,
        (int Left, int Top, int Right, int Bottom) guard, ReadOnlySpan<short> distance, int clearance,
        int rowStep = 1, int rowPhase = 0)
    {
        ArgumentNullException.ThrowIfNull(frame);
        rowStep = Math.Max(1, rowStep);
        var pixels = MemoryMarshal.Cast<byte, uint>(frame.Plane(0));
        var stride = frame.Stride(0) / 4;
        var (left, top) = (Math.Max(0, area.Left), Math.Max(0, area.Top));
        var (right, bottom) = (Math.Min(frame.Width, area.Right), Math.Min(frame.Height, area.Bottom));
        // The plate cells whose scene points fall inside the area.
        var u0 = Math.Max(0, (int)MathF.Ceiling(_scale * left + _offsetX));
        var u1 = Math.Min(Width, (int)MathF.Floor(_scale * (right - 1) + _offsetX) + 1);
        var v0 = Math.Max(0, (int)MathF.Ceiling(_scale * top + _offsetY));
        var v1 = Math.Min(Height, (int)MathF.Floor(_scale * (bottom - 1) + _offsetY) + 1);
        var guardWidth = guard.Right - guard.Left;
        if (_unzoomed)
        {
            LearnUnzoomed(pixels, stride, frame.Width, (left, top, right, bottom), guard, distance, clearance, rowStep, rowPhase);
            return;
        }

        var inverse = 1 / _scale;
        for (var v = v0; v < v1; v++)
        {
            if ((v + rowPhase) % rowStep != 0)
            {
                continue;
            }

            var y = Math.Clamp((v - _offsetY) * inverse, top, bottom - 1);
            var y0 = (int)y;
            var wy = (int)((y - y0) * 256);
            var y1 = Math.Min(y0 + 1, frame.Height - 1);
            var nearestY = wy < 128 ? y0 : y1;
            var guarded = nearestY >= guard.Top && nearestY < guard.Bottom;
            var guardRow = guarded && !distance.IsEmpty
                ? distance.Slice((nearestY - guard.Top) * guardWidth, guardWidth)
                : default;
            var row0 = pixels.Slice(y0 * stride, frame.Width);
            var row1 = pixels.Slice(y1 * stride, frame.Width);
            var plate = _cells.AsSpan(v * Width, Width);
            var sightings = _sightings.AsSpan(v * Width, Width);
            for (var u = u0; u < u1; u++)
            {
                var x = Math.Clamp((u - _offsetX) * inverse, left, right - 1);
                var x0 = (int)x;
                var wx = (int)((x - x0) * 256);
                var x1 = Math.Min(x0 + 1, frame.Width - 1);
                var nearestX = wx < 128 ? x0 : x1;
                if (guarded && nearestX >= guard.Left && nearestX < guard.Right
                    && (guardRow.IsEmpty || guardRow[nearestX - guard.Left] <= clearance))
                {
                    continue;
                }

                See(ref plate[u], ref sightings[u], (wx | wy) == 0 ? row0[x0] : Mix(row0[x0], row0[x1], row1[x0], row1[x1], wx, wy));
            }
        }
    }

    /// <summary>
    /// Recording with no zoom: every cell is the same fraction of a pixel from the picture's grid,
    /// so the blend weights and the whole-pixel offset are fixed for the whole picture.
    /// </summary>
    private void LearnUnzoomed(ReadOnlySpan<uint> pixels, int stride, int pictureWidth,
        (int Left, int Top, int Right, int Bottom) area, (int Left, int Top, int Right, int Bottom) guard,
        ReadOnlySpan<short> distance, int clearance, int rowStep, int rowPhase)
    {
        // Cell u shows the picture at x = u - offset: x0 = u - baseX, blended towards x0 + 1 by wx.
        var (wx, wy) = (_fractionX == 0 ? 0 : 256 - _fractionX, _fractionY == 0 ? 0 : 256 - _fractionY);
        var baseX = _wholeX + (wx > 0 ? 1 : 0);
        var baseY = _wholeY + (wy > 0 ? 1 : 0);
        var stepX = wx > 0 ? 1 : 0;
        var stepY = wy > 0 ? 1 : 0;
        var u0 = Math.Max(0, area.Left + baseX);
        var u1 = Math.Min(Width, area.Right - stepX + baseX);
        var v0 = Math.Max(0, area.Top + baseY);
        var v1 = Math.Min(Height, area.Bottom - stepY + baseY);
        var nearX = wx >= 128 ? 1 : 0;
        var nearY = wy >= 128 ? 1 : 0;
        var guardWidth = guard.Right - guard.Left;
        for (var v = v0; v < v1; v++)
        {
            if ((v + rowPhase) % rowStep != 0)
            {
                continue;
            }

            var y0 = v - baseY;
            var nearestY = y0 + nearY;
            var guarded = nearestY >= guard.Top && nearestY < guard.Bottom;
            var guardRow = guarded && !distance.IsEmpty
                ? distance.Slice((nearestY - guard.Top) * guardWidth, guardWidth)
                : default;
            var row0 = pixels.Slice(y0 * stride, pictureWidth);
            var row1 = pixels.Slice((y0 + stepY) * stride, pictureWidth);
            var plate = _cells.AsSpan(v * Width, Width);
            var sightings = _sightings.AsSpan(v * Width, Width);
            // The cells whose nearest pixel lies in the guard's columns, which only the guard's
            // distances may let through.
            var (g0, g1) = guarded
                ? (Math.Clamp(guard.Left - nearX + baseX, u0, u1), Math.Clamp(guard.Right - nearX + baseX, u0, u1))
                : (u1, u1);
            var row = new Run(plate, sightings, row0, row1, baseX, stepX, wx, wy);
            row.See(u0, g0, default, 0, 0);
            if (!guardRow.IsEmpty)
            {
                row.See(g0, g1, guardRow, g0 - baseX + nearX - guard.Left, clearance);
            }

            row.See(g1, u1, default, 0, 0);
        }
    }

    /// <summary>
    /// One plate row being recorded from one or two picture rows with no zoom: the same blend
    /// weights for every cell, so a run of cells is read, blended and counted at once with the
    /// processor's vector instructions.
    /// </summary>
    private readonly ref struct Run(Span<uint> plate, Span<uint> sightings, ReadOnlySpan<uint> row0, ReadOnlySpan<uint> row1,
        int baseX, int stepX, int wx, int wy)
    {
        private readonly Span<uint> _plate = plate;
        private readonly Span<uint> _sightings = sightings;
        private readonly ReadOnlySpan<uint> _row0 = row0;
        private readonly ReadOnlySpan<uint> _row1 = row1;

        /// <summary>
        /// Counts a sighting for the cells from <paramref name="from"/> to <paramref name="to"/>; with
        /// <paramref name="distances"/>, only those whose distance (from <paramref name="first"/> on)
        /// exceeds <paramref name="clearance"/>.
        /// </summary>
        public void See(int from, int to, ReadOnlySpan<short> distances, int first, int clearance)
        {
            var u = from;
            var guarded = !distances.IsEmpty;
            var (w00, w10, w01, w11) = Weights(wx, wy);
            Span<int> clear = stackalloc int[Vector<int>.Count];
            for (; u + Vector<uint>.Count <= to; u += Vector<uint>.Count)
            {
                var x0 = u - baseX;
                var colour = (wx | wy) == 0 ? new Vector<uint>(_row0[x0..])
                    : Mix(new Vector<uint>(_row0[x0..]), new Vector<uint>(_row0[(x0 + stepX)..]),
                        new Vector<uint>(_row1[x0..]), new Vector<uint>(_row1[(x0 + stepX)..]), w00, w10, w01, w11);
                var wanted = Vector<uint>.AllBitsSet;
                if (guarded)
                {
                    var at = first + u - from;
                    for (var lane = 0; lane < clear.Length; lane++)
                    {
                        clear[lane] = distances[at + lane];
                    }

                    wanted = Vector.AsVectorUInt32(Vector.GreaterThan(new Vector<int>(clear), new Vector<int>(clearance)));
                }

                SeeAll(_plate.Slice(u, Vector<uint>.Count), _sightings.Slice(u, Vector<uint>.Count), colour, wanted);
            }

            for (; u < to; u++)
            {
                if (guarded && distances[first + u - from] <= clearance)
                {
                    continue;
                }

                var x0 = u - baseX;
                SubjectRemovalPlate.See(ref _plate[u], ref _sightings[u], (wx | wy) == 0 ? _row0[x0]
                    : SubjectRemovalPlate.Mix(_row0[x0], _row0[x0 + stepX], _row1[x0], _row1[x0 + stepX], wx, wy));
            }
        }
    }

    /// <summary>The four bilinear weights in 256ths that <see cref="Mix(uint, uint, uint, uint, int, int)"/> uses.</summary>
    private static (uint W00, uint W10, uint W01, uint W11) Weights(int wx, int wy)
    {
        var w00 = (uint)((256 - wx) * (256 - wy)) >> 8;
        var w10 = (uint)(wx * (256 - wy)) >> 8;
        var w01 = (uint)((256 - wx) * wy) >> 8;
        return (w00, w10, w01, 256 - w00 - w10 - w01);
    }

    /// <summary><see cref="Mix(uint, uint, uint, uint, int, int)"/> for a run of pixels.</summary>
    private static Vector<uint> Mix(Vector<uint> a, Vector<uint> b, Vector<uint> c, Vector<uint> d, uint w00, uint w10, uint w01, uint w11)
    {
        var mask = new Vector<uint>(0xFF00FF);
        var greenMask = new Vector<uint>(0xFF00);
        var blueRed = ((a & mask) * w00 + (b & mask) * w10 + (c & mask) * w01 + (d & mask) * w11 + new Vector<uint>(0x800080)) >> 8;
        var green = ((a & greenMask) * w00 + (b & greenMask) * w10 + (c & greenMask) * w01 + (d & greenMask) * w11 + new Vector<uint>(0x8000)) >> 8;
        return (blueRed & mask) | (green & greenMask);
    }

    /// <summary><see cref="See(ref uint, ref uint, uint)"/> for a run of cells, only where <paramref name="record"/> is set.</summary>
    private static void SeeAll(Span<uint> cells, Span<uint> sightings, Vector<uint> colour, Vector<uint> record)
    {
        colour &= new Vector<uint>(0xFFFFFF);
        var sighting = new Vector<uint>(sightings);
        var count = sighting >> 24;
        var difference = Channel(sighting, colour, 0) + Channel(sighting, colour, 8) + Channel(sighting, colour, 16);
        var agrees = ~Vector.Equals(count, Vector<uint>.Zero) & Vector.AsVectorUInt32(Vector.LessThanOrEqual(difference, new Vector<int>(Agreement)));
        count = Vector.ConditionalSelect(agrees, Vector.Min(count + Vector<uint>.One, new Vector<uint>((uint)Confirmations)), Vector<uint>.One);
        Vector.ConditionalSelect(record, colour | (count << 24), sighting).CopyTo(sightings);
        var confirmed = record & Vector.Equals(count, new Vector<uint>((uint)Confirmations));
        Vector.ConditionalSelect(confirmed, colour | new Vector<uint>((uint)Observed << 24), new Vector<uint>(cells)).CopyTo(cells);
    }

    /// <summary>How far apart two runs of packed colours are in one channel.</summary>
    private static Vector<int> Channel(Vector<uint> a, Vector<uint> b, int shift) =>
        Vector.Abs(Vector.AsVectorInt32((a >> shift) & new Vector<uint>(0xFF)) - Vector.AsVectorInt32((b >> shift) & new Vector<uint>(0xFF)));

    /// <summary>
    /// Counts a sighting of a cell's background. Only when <see cref="Confirmations"/> sightings in a
    /// row agree does the cell become observed, so a person or a car passing through the area for a
    /// moment is never recorded as background (Granados et al. likewise trust only background that
    /// stays consistent over time); once observed, the cell follows the latest agreeing sightings, so
    /// a slow change of light is followed too.
    /// </summary>
    private static void See(ref uint cell, ref uint sighting, uint colour)
    {
        colour &= 0xFFFFFF;
        var count = sighting >> 24;
        count = count > 0 && Agrees(sighting, colour) ? Math.Min(count + 1, (uint)Confirmations) : 1;
        sighting = colour | (count << 24);
        if (count >= Confirmations)
        {
            cell = colour | ((uint)Observed << 24);
        }
    }

    private static bool Agrees(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF))
            + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)) <= Agreement;

    /// <summary>
    /// Bilinear blend of the colour of four packed BGRA pixels (alpha dropped), with weights in
    /// 256ths, blue and red computed together in one integer.
    /// </summary>
    internal static uint Mix(uint a, uint b, uint c, uint d, int wx, int wy)
    {
        var w00 = (uint)((256 - wx) * (256 - wy)) >> 8;
        var w10 = (uint)(wx * (256 - wy)) >> 8;
        var w01 = (uint)((256 - wx) * wy) >> 8;
        var w11 = 256 - w00 - w10 - w01;
        var blueRed = ((a & 0xFF00FF) * w00 + (b & 0xFF00FF) * w10 + (c & 0xFF00FF) * w01 + (d & 0xFF00FF) * w11 + 0x800080) >> 8;
        var green = ((a & 0xFF00) * w00 + (b & 0xFF00) * w10 + (c & 0xFF00) * w01 + (d & 0xFF00) * w11 + 0x8000) >> 8;
        return (blueRed & 0xFF00FF) | (green & 0xFF00);
    }

    /// <summary>Where the picture's point (x, y) falls on the plate.</summary>
    public (float U, float V) Locate(float x, float y) => (_scale * x + _offsetX, _scale * y + _offsetY);

    /// <summary>
    /// The plate's colour at the picture's pixel (x, y), packed as BGR with no alpha, blended from
    /// the four nearest cells, and how it is known: <see cref="Observed"/> only when all four were
    /// observed, <see cref="Estimated"/> when all four are known but some are estimates, else
    /// <see cref="Unknown"/>.
    /// </summary>
    public byte Sample(int x, int y, out uint colour)
    {
        colour = 0;
        int u0, v0, wx, wy;
        if (_unzoomed)
        {
            (u0, v0, wx, wy) = (x + _wholeX, y + _wholeY, _fractionX, _fractionY);
        }
        else
        {
            var (u, v) = Locate(x, y);
            if (u < 0 || v < 0)
            {
                return Unknown;
            }

            (u0, v0) = ((int)u, (int)v);
            (wx, wy) = ((int)((u - u0) * 256), (int)((v - v0) * 256));
        }

        if (u0 < 0 || v0 < 0 || u0 + (wx > 0 ? 1 : 0) >= Width || v0 + (wy > 0 ? 1 : 0) >= Height)
        {
            return Unknown;
        }

        var at = v0 * Width + u0;
        var c00 = _cells[at];
        if ((wx | wy) == 0)
        {
            colour = c00 & 0xFFFFFF;
            return (byte)(c00 >> 24);
        }

        var c10 = wx > 0 ? _cells[at + 1] : c00;
        var c01 = wy > 0 ? _cells[at + Width] : c00;
        var c11 = wx > 0 && wy > 0 ? _cells[at + Width + 1] : wx > 0 ? c10 : c01;
        var lowest = Math.Min(Math.Min(c00 >> 24, c10 >> 24), Math.Min(c01 >> 24, c11 >> 24));
        if (lowest == Unknown)
        {
            return Unknown;
        }

        colour = Mix(c00, c10, c01, c11, wx, wy);
        return lowest == Observed ? Observed : Estimated;
    }

    /// <summary>
    /// <see cref="Sample"/> for <paramref name="colours"/>.Length pixels of the picture's row
    /// <paramref name="y"/> from <paramref name="x"/> on: each one's colour and how it is known. With
    /// no zoom every pixel is the same fraction of a cell from the plate's grid, so a run of them is
    /// blended at once with the processor's vector instructions.
    /// </summary>
    public void SampleRow(int x, int y, Span<uint> colours, Span<byte> states)
    {
        var count = colours.Length;
        if (!_unzoomed)
        {
            for (var i = 0; i < count; i++)
            {
                states[i] = Sample(x + i, y, out colours[i]);
            }

            return;
        }

        // The pixels whose four cells all lie on the plate; the rest are unknown.
        var (wx, wy) = (_fractionX, _fractionY);
        var (stepX, stepY) = (wx > 0 ? 1 : 0, wy > 0 ? 1 : 0);
        var v0 = y + _wholeY;
        var first = Math.Clamp(-(x + _wholeX), 0, count);
        var last = v0 < 0 || v0 + stepY >= Height ? first : Math.Clamp(Width - stepX - x - _wholeX, first, count);
        colours[..first].Clear();
        states[..first].Clear();
        colours[last..].Clear();
        states[last..].Clear();
        var row = _cells.AsSpan(Math.Max(0, v0) * Width, Width);
        var below = _cells.AsSpan(Math.Min(Height - 1, Math.Max(0, v0 + stepY)) * Width, Width);
        var (w00, w10, w01, w11) = Weights(wx, wy);
        var i0 = first;
        for (; i0 + Vector<uint>.Count <= last; i0 += Vector<uint>.Count)
        {
            var u = x + i0 + _wholeX;
            var (c00, c10) = (new Vector<uint>(row[u..]), new Vector<uint>(row[(u + stepX)..]));
            var (c01, c11) = (new Vector<uint>(below[u..]), new Vector<uint>(below[(u + stepX)..]));
            var lowest = Vector.Min(Vector.Min(c00 >> 24, c10 >> 24), Vector.Min(c01 >> 24, c11 >> 24));
            var known = ~Vector.Equals(lowest, Vector<uint>.Zero);
            (Vector.ConditionalSelect(known, Mix(c00, c10, c01, c11, w00, w10, w01, w11), Vector<uint>.Zero)).CopyTo(colours[i0..]);
            for (var lane = 0; lane < Vector<uint>.Count; lane++)
            {
                states[i0 + lane] = (byte)lowest[lane];
            }
        }

        for (; i0 < last; i0++)
        {
            states[i0] = Sample(x + i0, y, out colours[i0]);
        }
    }

    /// <summary>Record a synthesised colour (packed BGR) for the picture's pixel (x, y), never over an observed cell.</summary>
    public void Estimate(int x, int y, uint colour)
    {
        var (u, v) = Locate(x, y);
        var cu = (int)MathF.Round(u);
        var cv = (int)MathF.Round(v);
        if ((uint)cu >= (uint)Width || (uint)cv >= (uint)Height)
        {
            return;
        }

        ref var cell = ref _cells[cv * Width + cu];
        if (cell >> 24 != Observed)
        {
            cell = (colour & 0xFFFFFF) | ((uint)Estimated << 24);
        }
    }
}
