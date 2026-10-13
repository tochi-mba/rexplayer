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

    // A zoom this far from where the scene was first recorded makes old cells too coarse to use.
    private const float MaximumZoom = 0.2f;

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
    /// <paramref name="clearance"/>; with no distances the whole guard is left alone.
    /// </summary>
    public void Learn(VideoFrame frame, (int Left, int Top, int Right, int Bottom) area,
        (int Left, int Top, int Right, int Bottom) guard, ReadOnlySpan<short> distance, int clearance)
    {
        ArgumentNullException.ThrowIfNull(frame);
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
            LearnUnzoomed(pixels, stride, frame.Width, (left, top, right, bottom), guard, distance, clearance);
            return;
        }

        var inverse = 1 / _scale;
        for (var v = v0; v < v1; v++)
        {
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
        ReadOnlySpan<short> distance, int clearance)
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
            for (var u = u0; u < u1; u++)
            {
                var x0 = u - baseX;
                var nearestX = x0 + nearX;
                if (guarded && nearestX >= guard.Left && nearestX < guard.Right
                    && (guardRow.IsEmpty || guardRow[nearestX - guard.Left] <= clearance))
                {
                    continue;
                }

                See(ref plate[u], ref sightings[u], (wx | wy) == 0 ? row0[x0] : Mix(row0[x0], row0[x0 + stepX], row1[x0], row1[x0 + stepX], wx, wy));
            }
        }
    }

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
