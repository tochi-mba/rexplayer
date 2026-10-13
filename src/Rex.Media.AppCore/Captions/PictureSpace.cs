using Rex.Media.AppCore.Player;

namespace Rex.Media.AppCore.Captions;

/// <summary>How a word is kept readable over the picture behind it: an outline's strength (0 to 1) and a dark plate's opacity (0 for none).</summary>
public readonly record struct Backing(double Outline, double Plate);

/// <summary>
/// What a picture holds, cell by cell, for putting captions where they cover nothing that matters
/// and stay readable (OSD-11). The picture is cut into a grid of near-square cells; each cell has
/// its brightness, how busy it is (how much the brightness changes from one sample to the next),
/// how much of it is skin (a face or hands, by the skin colours' Cb and Cr range), whether it looks
/// like text already in the picture (busy with hard light-on-dark contrast and little colour), and
/// how much it moved since the picture before. Its importance is the most of these, with faces
/// widened by a cell all round for hair and chin, and the tracked subject, when one is given, as
/// important as anything.
/// </summary>
public sealed class PictureSpace
{
    /// <summary>Cells across the picture.</summary>
    public const int Columns = 32;

    private const double Margin = 0.04;

    private readonly float[] _luma;
    private readonly float[] _busy;
    private readonly float[] _skin;
    private readonly float[] _text;
    private readonly float[] _motion;
    private readonly float[] _importance;

    private PictureSpace(int columns, int rows)
    {
        (ColumnCount, RowCount) = (columns, rows);
        var cells = columns * rows;
        (_luma, _busy, _skin, _text, _motion, _importance) = (new float[cells], new float[cells], new float[cells], new float[cells], new float[cells], new float[cells]);
    }

    public int ColumnCount { get; }

    public int RowCount { get; }

    /// <summary>How much each cell matters, 0 to 1, row by row.</summary>
    public IReadOnlyList<float> Importance => _importance;

    /// <summary>Each cell's brightness, 0 to 1 (relative luminance, BT.709).</summary>
    public IReadOnlyList<float> Luma => _luma;

    /// <summary>Each cell's busyness, 0 to 1.</summary>
    public IReadOnlyList<float> Busy => _busy;

    /// <summary>A picture with nothing in it that matters, as when the video has not been read yet.</summary>
    public static PictureSpace Empty(int rows = 18) => new(Columns, Math.Clamp(rows, 1, 64));

    /// <summary>
    /// Reads a BGRA picture <paramref name="width"/> by <paramref name="height"/>, its rows
    /// <paramref name="stride"/> bytes apart. <paramref name="before"/>, the picture read before,
    /// gives the motion; <paramref name="subject"/>, as shares of the picture, is what Subject
    /// Lock follows.
    /// </summary>
    public static PictureSpace Read(ReadOnlySpan<byte> bgra, int width, int height, int stride, PictureSpace? before = null, Area? subject = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (bgra.Length < (stride * (height - 1)) + (width * 4))
        {
            throw new ArgumentException("The picture is smaller than its size says.", nameof(bgra));
        }

        var rows = Math.Clamp((int)Math.Round(Columns * (double)height / width), 1, 64);
        var space = new PictureSpace(Columns, rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                space.Measure(bgra, width, height, stride, column, row);
            }
        }

        if (before is not null && before.ColumnCount == space.ColumnCount && before.RowCount == space.RowCount)
        {
            for (var i = 0; i < space._motion.Length; i++)
            {
                space._motion[i] = (float)Math.Clamp(Math.Abs(space._luma[i] - before._luma[i]) * 6, 0, 1);
            }
        }

        space.Weigh(subject);
        return space;
    }

    /// <summary>
    /// The picture that matters most over a stretch: each cell's importance the most of all
    /// <paramref name="spaces"/>, so a caption placed for the stretch stays clear of all of them.
    /// </summary>
    public static PictureSpace Most(IReadOnlyList<PictureSpace> spaces)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        if (spaces.Count == 0)
        {
            return Empty();
        }

        var first = spaces[0];
        var most = new PictureSpace(first.ColumnCount, first.RowCount);
        foreach (var space in spaces.Where(space => space.ColumnCount == first.ColumnCount && space.RowCount == first.RowCount))
        {
            for (var i = 0; i < most._importance.Length; i++)
            {
                most._importance[i] = Math.Max(most._importance[i], space._importance[i]);
                most._busy[i] = Math.Max(most._busy[i], space._busy[i]);
                most._luma[i] = Math.Max(most._luma[i], space._luma[i]);
            }
        }

        return most;
    }

    /// <summary>
    /// Where a cluster <paramref name="width"/> by <paramref name="height"/> (shares of the picture)
    /// covers least that matters, near <paramref name="preferred"/> (the share of the picture its
    /// top left would sit at): the top left of the best place, kept a little in from every edge.
    /// </summary>
    public (double X, double Y) Place(double width, double height, (double X, double Y) preferred)
    {
        width = Math.Clamp(double.IsFinite(width) ? width : 0, 0, 1 - (2 * Margin));
        height = Math.Clamp(double.IsFinite(height) ? height : 0, 0, 1 - (2 * Margin));
        var maxX = Math.Max(Margin, 1 - Margin - width);
        var maxY = Math.Max(Margin, 1 - Margin - height);
        var target = (X: Math.Clamp(preferred.X, Margin, maxX), Y: Math.Clamp(preferred.Y, Margin, maxY));
        var best = target;
        var bestCost = double.MaxValue;
        const int Steps = 24;
        for (var sy = 0; sy <= Steps; sy++)
        {
            var y = Margin + ((1 - (2 * Margin) - height) * sy / Steps);
            for (var sx = 0; sx <= Steps; sx++)
            {
                var x = Margin + ((1 - (2 * Margin) - width) * sx / Steps);
                var (mean, most) = Under(x, y, width, height, _importance);
                var distance = Math.Sqrt(Math.Pow(x - target.X, 2) + Math.Pow(y - target.Y, 2));
                var cost = (4 * mean) + (2 * most) + (0.6 * distance);
                if (cost < bestCost - 1e-9)
                {
                    (bestCost, best) = (cost, (x, y));
                }
            }
        }

        return best;
    }

    /// <summary>How much that matters lies under a box (shares of the picture): the mean and the most of the cells' importance.</summary>
    public (double Mean, double Most) Covered(double x, double y, double width, double height) => Under(x, y, width, height, _importance);

    /// <summary>
    /// What keeps white text readable over a box (shares of the picture): an outline always, the
    /// stronger the busier the picture there; and a dark plate wherever the picture is bright enough
    /// that white on it would fall short of a 4.5 to 1 contrast, or busy enough to break up the letters.
    /// </summary>
    public Backing Behind(double x, double y, double width, double height)
    {
        var (luma, brightest) = Under(x, y, width, height, _luma);
        var (busy, _) = Under(x, y, width, height, _busy);
        // White's contrast with the picture is (1 + 0.05) / (L + 0.05): at least 4.5 needs L at most 0.183.
        var light = Math.Max(luma, (luma + brightest) / 2);
        var plate = Math.Clamp(((light - 0.18) / 0.5) + ((busy - 0.35) * 0.8), 0, 0.75);
        var outline = Math.Clamp(0.4 + busy, 0.4, 1);
        return new Backing(outline, plate < 0.05 ? 0 : plate);
    }

    private (double Mean, double Most) Under(double x, double y, double width, double height, float[] values)
    {
        var left = Math.Clamp((int)Math.Floor(x * ColumnCount), 0, ColumnCount - 1);
        var top = Math.Clamp((int)Math.Floor(y * RowCount), 0, RowCount - 1);
        var right = Math.Clamp((int)Math.Ceiling((x + width) * ColumnCount) - 1, left, ColumnCount - 1);
        var bottom = Math.Clamp((int)Math.Ceiling((y + height) * RowCount) - 1, top, RowCount - 1);
        double sum = 0;
        double most = 0;
        for (var row = top; row <= bottom; row++)
        {
            for (var column = left; column <= right; column++)
            {
                var value = values[(row * ColumnCount) + column];
                sum += value;
                most = Math.Max(most, value);
            }
        }

        return (sum / ((right - left + 1) * (bottom - top + 1)), most);
    }

    private void Measure(ReadOnlySpan<byte> bgra, int width, int height, int stride, int column, int row)
    {
        var x0 = column * width / ColumnCount;
        var x1 = Math.Max(x0 + 1, (column + 1) * width / ColumnCount);
        var y0 = row * height / RowCount;
        var y1 = Math.Max(y0 + 1, (row + 1) * height / RowCount);
        var step = Math.Max(1, Math.Min(x1 - x0, y1 - y0) / 8);
        double sum = 0;
        double change = 0;
        double chroma = 0;
        var skin = 0;
        var count = 0;
        var changes = 0;
        var (darkest, brightest) = (1.0, 0.0);
        for (var y = y0; y < y1; y += step)
        {
            for (var x = x0; x < x1; x += step)
            {
                var (luma, cb, cr) = Sample(bgra, stride, x, y);
                sum += luma;
                count++;
                darkest = Math.Min(darkest, luma);
                brightest = Math.Max(brightest, luma);
                chroma += Math.Abs(cb - 128) + Math.Abs(cr - 128);
                if (cb is >= 77 and <= 127 && cr is >= 133 and <= 173 && luma > 0.12)
                {
                    skin++;
                }

                if (x + step < x1)
                {
                    change += Math.Abs(Sample(bgra, stride, x + step, y).Luma - luma);
                    changes++;
                }

                if (y + step < y1)
                {
                    change += Math.Abs(Sample(bgra, stride, x, y + step).Luma - luma);
                    changes++;
                }
            }
        }

        var cell = (row * ColumnCount) + column;
        _luma[cell] = (float)(sum / count);
        var busy = changes == 0 ? 0 : Math.Clamp(change / changes / 0.15, 0, 1);
        _busy[cell] = (float)busy;
        _skin[cell] = (float)skin / count;
        var colourless = chroma / count < 30;
        _text[cell] = busy > 0.45 && brightest - darkest > 0.55 && colourless ? 1 : 0;
    }

    private static (double Luma, double Cb, double Cr) Sample(ReadOnlySpan<byte> bgra, int stride, int x, int y)
    {
        var at = (y * stride) + (x * 4);
        var (b, g, r) = (bgra[at], bgra[at + 1], bgra[at + 2]);
        var luma = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255;
        var cb = 128 - (0.168736 * r) - (0.331264 * g) + (0.5 * b);
        var cr = 128 + (0.5 * r) - (0.418688 * g) - (0.081312 * b);
        return (luma, cb, cr);
    }

    private void Weigh(Area? subject)
    {
        for (var row = 0; row < RowCount; row++)
        {
            for (var column = 0; column < ColumnCount; column++)
            {
                var cell = (row * ColumnCount) + column;
                var face = 0.0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (r, c) = (row + dy, column + dx);
                        if (r >= 0 && r < RowCount && c >= 0 && c < ColumnCount && _skin[(r * ColumnCount) + c] >= 0.35)
                        {
                            face = dx == 0 && dy == 0 ? 1 : Math.Max(face, 0.8);
                        }
                    }
                }

                var held = subject is { } box
                    && (column + 1.0) / ColumnCount > box.X && (double)column / ColumnCount < box.X + box.Width
                    && (row + 1.0) / RowCount > box.Y && (double)row / RowCount < box.Y + box.Height ? 1 : 0;
                _importance[cell] = (float)Math.Max(Math.Max(face, held), Math.Max(_text[cell] * 0.9, Math.Max(_busy[cell] * 0.5, _motion[cell] * 0.6)));
            }
        }
    }
}
