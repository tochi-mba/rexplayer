namespace Rex.Media.Vision;

/// <summary>
/// The matte without a model: where the picture differs from the room, with the room learned as
/// the running median of each pixel over the last while, so there is no empty-room step. A person
/// who moves at all uncovers the wall behind them more often than not, and the median takes the
/// wall; what moves now is marked at once from the frame difference, so the first seconds show
/// motion while the room is still being learned. The result is soft (0 to 1), smoothed once.
/// </summary>
public sealed class MotionMatte
{
    /// <summary>How many past pictures the room's median is taken over.</summary>
    public const int Memory = 24;

    /// <summary>One picture in every this many is remembered for the median, so the memory spans a while.</summary>
    public const int Stride = 8;

    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _luma;
    private readonly byte[] _previous;
    private readonly byte[][] _remembered;
    private readonly byte[] _sorted = new byte[Memory];
    private readonly float[] _raw;
    private int _seen;
    private int _frames;

    public MotionMatte(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 3);
        (_width, _height) = (width, height);
        _luma = new byte[width * height];
        _previous = new byte[width * height];
        _raw = new float[width * height];
        _remembered = new byte[Memory][];
        for (var i = 0; i < Memory; i++)
        {
            _remembered[i] = new byte[width * height];
        }
    }

    /// <summary>How different from the room a pixel must be to count as the listener, 0 to 1.</summary>
    public float Threshold { get; set; } = 0.14f;

    /// <summary>The luminance of the last picture, as the flow and the hands read it.</summary>
    public ReadOnlySpan<byte> Luma => _luma;

    /// <summary>Whether enough pictures have been remembered for the room to be known.</summary>
    public bool RoomKnown => _seen >= 3;

    /// <summary>
    /// Takes the next camera picture, shrunk to the matte's size by area averaging and mirrored when
    /// asked, and writes the matte into <paramref name="matte"/> (the matte's size, 0 to 1).
    /// </summary>
    public void Update(BgraPicture picture, bool mirror, Span<float> matte)
    {
        if (matte.Length != _luma.Length)
        {
            throw new ArgumentException("The matte is the size the matte was made for.", nameof(matte));
        }

        _luma.CopyTo(_previous, 0);
        Shrink(picture, _width, _height, mirror, _luma);
        if (_frames == 0)
        {
            // Nothing has moved yet: the first picture is only remembered.
            _luma.CopyTo(_previous, 0);
        }

        if (_frames++ % Stride == 0)
        {
            _luma.CopyTo(_remembered[_seen % Memory], 0);
            _seen++;
        }

        var limit = Math.Clamp(Threshold, 0.02f, 1) * 255;
        var remembered = Math.Min(_seen, Memory);
        for (var i = 0; i < _luma.Length; i++)
        {
            // What moved since the last picture is the listener whatever the room is.
            var moved = Math.Abs(_luma[i] - _previous[i]) / limit;
            float still = 0;
            if (remembered >= 3)
            {
                for (var k = 0; k < remembered; k++)
                {
                    _sorted[k] = _remembered[k][i];
                }

                _sorted.AsSpan(0, remembered).Sort();
                still = Math.Abs(_luma[i] - _sorted[remembered / 2]) / limit;
            }

            _raw[i] = Math.Clamp(Math.Max(moved, still) - 0.5f, 0, 1);
        }

        // One pass of a 3 by 3 box softens the edge and fills pinholes.
        for (var y = 0; y < _height; y++)
        {
            var up = Math.Max(0, y - 1) * _width;
            var row = y * _width;
            var down = Math.Min(_height - 1, y + 1) * _width;
            for (var x = 0; x < _width; x++)
            {
                var left = Math.Max(0, x - 1);
                var right = Math.Min(_width - 1, x + 1);
                var sum = _raw[up + left] + _raw[up + x] + _raw[up + right]
                    + _raw[row + left] + _raw[row + x] + _raw[row + right]
                    + _raw[down + left] + _raw[down + x] + _raw[down + right];
                matte[row + x] = Math.Min(1, sum / 6);
            }
        }
    }

    /// <summary>Forgets the room, as when the camera moves.</summary>
    public void Reset()
    {
        (_seen, _frames) = (0, 0);
        Array.Clear(_luma);
        Array.Clear(_previous);
    }

    /// <summary>The picture's luminance at <paramref name="width"/> by <paramref name="height"/>, each pixel the average of those it covers.</summary>
    public static void Shrink(BgraPicture picture, int width, int height, bool mirror, Span<byte> luma)
    {
        for (var y = 0; y < height; y++)
        {
            var top = y * picture.Height / height;
            var bottom = Math.Max(top + 1, (y + 1) * picture.Height / height);
            for (var x = 0; x < width; x++)
            {
                var left = x * picture.Width / width;
                var right = Math.Max(left + 1, (x + 1) * picture.Width / width);
                var sum = 0;
                var count = 0;
                for (var sy = top; sy < Math.Min(picture.Height, bottom); sy++)
                {
                    var row = picture.Row(sy);
                    for (var sx = left; sx < Math.Min(picture.Width, right); sx++)
                    {
                        var at = sx * 4;
                        sum += ((row[at + 2] * 77) + (row[at + 1] * 150) + (row[at] * 29)) >> 8;
                        count++;
                    }
                }

                luma[(y * width) + (mirror ? width - 1 - x : x)] = (byte)(sum / count);
            }
        }
    }
}

/// <summary>
/// How the picture moves, without a model: for each cell of the flow field, the shift of its block
/// of pixels between the last picture and this one that matches best (the least sum of absolute
/// differences), searched a few pixels either way. Coarse and cheap, which is what the effects need:
/// a field to push particles and ink with.
/// </summary>
public sealed class BlockFlow
{
    /// <summary>How far a block is looked for, in pixels either way.</summary>
    public const int Search = 4;

    /// <summary>The block compared, in pixels each way: a little larger than the cell, so edges match.</summary>
    public const int Block = 6;

    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _previous;
    private bool _primed;

    public BlockFlow(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, SceneSense.FlowStep);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, SceneSense.FlowStep);
        (_width, _height) = (width, height);
        _previous = new byte[width * height];
    }

    /// <summary>
    /// Takes the next luminance picture (the matte's size) and writes the flow of each cell into
    /// <paramref name="flowX"/> and <paramref name="flowY"/> (<see cref="SceneSense.FlowStep"/> cells
    /// apart), in pixels. Cells whose block is flat (nothing to match) are still.
    /// </summary>
    public void Update(ReadOnlySpan<byte> luma, Span<float> flowX, Span<float> flowY)
    {
        if (luma.Length != _previous.Length)
        {
            throw new ArgumentException("The picture is the size the flow was made for.", nameof(luma));
        }

        var cellsAcross = _width / SceneSense.FlowStep;
        var cellsDown = _height / SceneSense.FlowStep;
        if (flowX.Length != cellsAcross * cellsDown || flowY.Length != flowX.Length)
        {
            throw new ArgumentException("The flow field has one cell per block of the picture.", nameof(flowX));
        }

        if (!_primed)
        {
            luma.CopyTo(_previous);
            _primed = true;
            flowX.Clear();
            flowY.Clear();
            return;
        }

        for (var cy = 0; cy < cellsDown; cy++)
        {
            for (var cx = 0; cx < cellsAcross; cx++)
            {
                var centreX = (cx * SceneSense.FlowStep) + (SceneSense.FlowStep / 2);
                var centreY = (cy * SceneSense.FlowStep) + (SceneSense.FlowStep / 2);
                var (bestX, bestY, best) = (0, 0, int.MaxValue);
                var flat = true;
                for (var dy = -Search; dy <= Search; dy++)
                {
                    for (var dx = -Search; dx <= Search; dx++)
                    {
                        var cost = Cost(luma, centreX, centreY, dx, dy, ref flat);
                        // Of equal costs the smaller shift wins, so a flat block stays still.
                        if (cost < best || (cost == best && (dx * dx) + (dy * dy) < (bestX * bestX) + (bestY * bestY)))
                        {
                            (bestX, bestY, best) = (dx, dy, cost);
                        }
                    }
                }

                var cell = (cy * cellsAcross) + cx;
                (flowX[cell], flowY[cell]) = flat ? (0, 0) : (bestX, bestY);
            }
        }

        luma.CopyTo(_previous);
    }

    public void Reset() => _primed = false;

    /// <summary>The difference between the block around a point in the last picture and the block shifted by (<paramref name="dx"/>, <paramref name="dy"/>) in this one.</summary>
    private int Cost(ReadOnlySpan<byte> luma, int centreX, int centreY, int dx, int dy, ref bool flat)
    {
        var cost = 0;
        var first = -1;
        for (var y = centreY - Block; y < centreY + Block; y++)
        {
            var fromY = Math.Clamp(y, 0, _height - 1);
            var toY = Math.Clamp(y + dy, 0, _height - 1);
            for (var x = centreX - Block; x < centreX + Block; x++)
            {
                var before = _previous[(fromY * _width) + Math.Clamp(x, 0, _width - 1)];
                var after = luma[(toY * _width) + Math.Clamp(x + dx, 0, _width - 1)];
                cost += Math.Abs(before - after);
                if (first < 0)
                {
                    first = before;
                }
                else if (flat && before != first)
                {
                    flat = false;
                }
            }
        }

        return cost;
    }
}

/// <summary>
/// Fills a <see cref="SceneSense"/> without any model: the matte from <see cref="MotionMatte"/>, the
/// flow from <see cref="BlockFlow"/>, and a hand guessed at each upper end of the outline (the
/// highest marked point left and right of the listener's middle), given as an open hand so the
/// gestures that need only a position (hovering, swiping, raising) work and those that need fingers
/// (a pinch, a fist) cannot fire by accident. No body is claimed.
/// </summary>
public sealed class HandWrittenPerceiver
{
    /// <summary>A guessed hand is this sure of itself.</summary>
    public const float GuessConfidence = 0.3f;

    private readonly MotionMatte _matte;
    private readonly BlockFlow _flow;

    public HandWrittenPerceiver(int width = SceneSense.DefaultWidth, int height = SceneSense.DefaultHeight)
    {
        _matte = new MotionMatte(width, height);
        _flow = new BlockFlow(width, height);
        (Width, Height) = (width, height);
    }

    public int Width { get; }

    public int Height { get; }

    public float Threshold
    {
        get => _matte.Threshold;
        set => _matte.Threshold = value;
    }

    /// <summary>Fills <paramref name="scene"/> (the perceiver's size) from the next camera picture.</summary>
    public void Perceive(BgraPicture picture, bool mirror, SceneSense scene, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Width != Width || scene.Height != Height)
        {
            throw new ArgumentException("The scene is the size the perceiver was made for.", nameof(scene));
        }

        _matte.Update(picture, mirror, scene.Matte);
        _flow.Update(_matte.Luma, scene.FlowX, scene.FlowY);
        GuessHands(scene);
        scene.Body.Clear();
        scene.Timestamp = timestamp;
        scene.Source = SenseSource.HandWritten;
    }

    public void Reset()
    {
        _matte.Reset();
        _flow.Reset();
    }

    private void GuessHands(SceneSense scene)
    {
        // The listener's middle, then the highest marked point on each side of it.
        var (sumX, count) = (0.0, 0);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (scene.Matte[(y * Width) + x] > 0.5f)
                {
                    (sumX, count) = (sumX + x, count + 1);
                }
            }
        }

        if (count < Width * Height / 200)
        {
            scene.Hands[0].Clear();
            scene.Hands[1].Clear();
            return;
        }

        var middle = (int)(sumX / count);
        Guess(scene, scene.Hands[0], 0, middle);
        Guess(scene, scene.Hands[1], middle, Width);
    }

    private void Guess(SceneSense scene, HandSense hand, int from, int to)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = from; x < to; x++)
            {
                if (scene.Matte[(y * Width) + x] > 0.5f)
                {
                    // Up to a third of the way down: an arm raised or out. Lower is the shoulders, not a hand.
                    if (y > Height / 3)
                    {
                        hand.Clear();
                        return;
                    }

                    OpenHandAt(hand, (x + 0.5f) / Width, (y + 0.5f) / Height, 0.08f);
                    return;
                }
            }
        }

        hand.Clear();
    }

    /// <summary>Lays out an open hand's points around a position: palm below it, fingers reaching above.</summary>
    internal static void OpenHandAt(HandSense hand, float x, float y, float palm)
    {
        hand.Present = true;
        hand.Confidence = GuessConfidence;
        for (var i = 0; i < HandSense.Points; i++)
        {
            hand.Landmarks[i] = new Landmark(x, y + palm, 0, GuessConfidence);
        }

        hand.Landmarks[HandSense.IndexBase] = new Landmark(x - (palm * 0.3f), y, 0, GuessConfidence);
        hand.Landmarks[HandSense.MiddleBase] = new Landmark(x, y, 0, GuessConfidence);
        hand.Landmarks[HandSense.PinkyBase] = new Landmark(x + (palm * 0.3f), y, 0, GuessConfidence);
        hand.Landmarks[HandSense.ThumbTip] = new Landmark(x - palm, y, 0, GuessConfidence);
        hand.Landmarks[HandSense.IndexTip] = new Landmark(x - (palm * 0.3f), y - palm, 0, GuessConfidence);
        hand.Landmarks[HandSense.MiddleTip] = new Landmark(x, y - palm, 0, GuessConfidence);
        hand.Landmarks[HandSense.RingTip] = new Landmark(x + (palm * 0.2f), y - palm, 0, GuessConfidence);
        hand.Landmarks[HandSense.PinkyTip] = new Landmark(x + (palm * 0.4f), y - palm, 0, GuessConfidence);
    }
}
