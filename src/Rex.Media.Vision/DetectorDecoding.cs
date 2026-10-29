using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rex.Media.Vision;

/// <summary>
/// The anchors of a single-shot detector (ADR-0022): one anchor per cell of each output grid,
/// repeated a few times per cell, listed grid by grid, row by row, across then down, as the
/// MediaPipe detectors expect them. The palm detector at 192 has grids of stride 8 (two a cell)
/// and 16 (six a cell), 2016 anchors; the person detector at 224 has strides 8, 16 and 32 (two,
/// two and six a cell), 2254. The tables the converters publish are checked against
/// <see cref="Fingerprint"/> in tests rather than copied.
/// </summary>
public sealed class AnchorGrid
{
    public static AnchorGrid Palm { get; } = new(192, [(8, 2), (16, 6)]);

    public static AnchorGrid Person { get; } = new(224, [(8, 2), (16, 2), (32, 6)]);

    public AnchorGrid(int inputSide, IReadOnlyList<(int Stride, int PerCell)> layers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputSide);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0 || layers.Any(layer => layer.Stride <= 0 || layer.PerCell <= 0 || inputSide % layer.Stride != 0))
        {
            throw new ArgumentException("Each layer's stride divides the input side and repeats its anchor at least once.", nameof(layers));
        }

        InputSide = inputSide;
        Layers = layers;
        var count = layers.Sum(layer => (inputSide / layer.Stride) * (inputSide / layer.Stride) * layer.PerCell);
        Anchors = new float[count * 2];
        var at = 0;
        foreach (var (stride, perCell) in layers)
        {
            var cells = inputSide / stride;
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                {
                    for (var repeat = 0; repeat < perCell; repeat++)
                    {
                        Anchors[at++] = (x + 0.5f) / cells;
                        Anchors[at++] = (y + 0.5f) / cells;
                    }
                }
            }
        }
    }

    /// <summary>The square the detector takes, in pixels.</summary>
    public int InputSide { get; }

    public IReadOnlyList<(int Stride, int PerCell)> Layers { get; }

    /// <summary>Every anchor's centre as shares of the square, across then down, one pair after another.</summary>
    public float[] Anchors { get; }

    public int Count => Anchors.Length / 2;

    /// <summary>The SHA-256 of the anchors written one a line as "x y" to six places: what a published table hashes to.</summary>
    public string Fingerprint()
    {
        var text = new StringBuilder();
        for (var i = 0; i < Anchors.Length; i += 2)
        {
            if (i > 0)
            {
                text.Append('\n');
            }

            text.Append(Anchors[i].ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                .Append(Anchors[i + 1].ToString("F6", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(text.ToString())));
    }
}

/// <summary>
/// One thing a detector found: its box and its key points as shares of the detector's square
/// (which the caller maps back through the letterbox), and its score. The points are the model's
/// own: seven palm points, or the body's hips, shoulders and the like.
/// </summary>
public sealed class Detection
{
    public Detection(int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        Points = new float[points * 2];
    }

    public float Left { get; internal set; }

    public float Top { get; internal set; }

    public float Right { get; internal set; }

    public float Bottom { get; internal set; }

    public float Score { get; internal set; }

    /// <summary>The key points, across then down, one pair after another.</summary>
    public float[] Points { get; }

    public float Width => Right - Left;

    public float Height => Bottom - Top;

    public float CentreX => (Left + Right) / 2;

    public float CentreY => (Top + Bottom) / 2;

    public float Area => Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>The share of the two boxes' union that they share.</summary>
    public float OverlapWith(Detection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var width = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var height = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var shared = width * height;
        var union = Area + other.Area - shared;
        return union > 0 ? shared / union : 0;
    }

    internal void CopyFrom(Detection other)
    {
        (Left, Top, Right, Bottom, Score) = (other.Left, other.Top, other.Right, other.Bottom, other.Score);
        other.Points.CopyTo(Points, 0);
    }
}

/// <summary>
/// Turns a single-shot detector's raw output into a few detections: the score through a sigmoid,
/// the box as an offset from its anchor in pixels of the square, the points likewise, then
/// non-maximum suppression keeps the best of each cluster. Made once for a model, with every
/// buffer it needs, so a frame allocates nothing.
/// </summary>
public sealed class DetectorDecoder
{
    /// <summary>The most candidates above the score threshold considered for suppression.</summary>
    public const int Candidates = 32;

    private readonly AnchorGrid _grid;
    private readonly int _points;
    private readonly Detection[] _candidates;
    private readonly bool[] _dropped = new bool[Candidates];
    private int _found;

    /// <param name="grid">The detector's anchors.</param>
    /// <param name="points">How many key points each detection carries.</param>
    /// <param name="keep">The most detections given back: hands are at most two, bodies one.</param>
    public DetectorDecoder(AnchorGrid grid, int points, int keep)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keep);
        _points = points;
        _candidates = new Detection[Candidates];
        for (var i = 0; i < Candidates; i++)
        {
            _candidates[i] = new Detection(points);
        }

        Detections = new Detection[keep];
        for (var i = 0; i < keep; i++)
        {
            Detections[i] = new Detection(points);
        }
    }

    /// <summary>The detections of the last <see cref="Decode"/>, best first; only the first <see cref="Count"/> mean anything.</summary>
    public Detection[] Detections { get; }

    public int Count { get; private set; }

    /// <summary>How many values each anchor's row of <c>boxes</c> holds: the box, then the points.</summary>
    public int RowLength => 4 + (_points * 2);

    /// <summary>
    /// Decodes <paramref name="boxes"/> (one row of <see cref="RowLength"/> per anchor) and
    /// <paramref name="scores"/> (one logit per anchor), keeping what scores at least
    /// <paramref name="threshold"/> after the sigmoid and overlaps a better detection by less than
    /// <paramref name="overlap"/>.
    /// </summary>
    public void Decode(ReadOnlySpan<float> boxes, ReadOnlySpan<float> scores, float threshold, float overlap)
    {
        if (scores.Length != _grid.Count || boxes.Length != _grid.Count * RowLength)
        {
            throw new ArgumentException("The output has one row of boxes and one score per anchor.", nameof(boxes));
        }

        _found = 0;
        Count = 0;
        var side = _grid.InputSide;
        for (var i = 0; i < scores.Length; i++)
        {
            var score = 1 / (1 + MathF.Exp(-scores[i]));
            if (!(score >= threshold))
            {
                continue;
            }

            // Kept in score order; a full list drops its worst.
            var slot = _found;
            while (slot > 0 && _candidates[slot - 1].Score < score)
            {
                slot--;
            }

            if (slot >= Candidates)
            {
                continue;
            }

            var last = Math.Min(_found, Candidates - 1);
            for (var k = last; k > slot; k--)
            {
                _candidates[k].CopyFrom(_candidates[k - 1]);
            }

            var row = boxes.Slice(i * RowLength, RowLength);
            var anchorX = _grid.Anchors[i * 2];
            var anchorY = _grid.Anchors[(i * 2) + 1];
            var centreX = (row[0] / side) + anchorX;
            var centreY = (row[1] / side) + anchorY;
            var width = row[2] / side;
            var height = row[3] / side;
            var candidate = _candidates[slot];
            (candidate.Left, candidate.Top, candidate.Right, candidate.Bottom, candidate.Score) =
                (centreX - (width / 2), centreY - (height / 2), centreX + (width / 2), centreY + (height / 2), score);
            for (var p = 0; p < _points; p++)
            {
                candidate.Points[p * 2] = (row[4 + (p * 2)] / side) + anchorX;
                candidate.Points[(p * 2) + 1] = (row[5 + (p * 2)] / side) + anchorY;
            }

            _found = Math.Min(Candidates, _found + 1);
        }

        Array.Clear(_dropped);
        for (var i = 0; i < _found && Count < Detections.Length; i++)
        {
            if (_dropped[i])
            {
                continue;
            }

            Detections[Count++].CopyFrom(_candidates[i]);
            for (var k = i + 1; k < _found; k++)
            {
                if (!_dropped[k] && _candidates[i].OverlapWith(_candidates[k]) >= overlap)
                {
                    _dropped[k] = true;
                }
            }
        }
    }
}
