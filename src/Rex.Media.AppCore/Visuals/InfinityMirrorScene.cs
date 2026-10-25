namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener in an infinity mirror (AU-18): the camera's picture holds a smaller copy of itself
/// about the listener, and that another, down and down, each level further back in time by a beat,
/// so a move ripples inward level by level. The levels breathe in and out with the bass, turn a
/// step on every beat, and each kick flashes down through them.
/// </summary>
public sealed class InfinityMirrorScene : VisualScene
{
    private const int Kept = 16;

    private readonly byte[][] _frames = new byte[Kept][];
    private readonly List<int> _beats = [];
    private int _newest = -1;
    private double _turn;
    private double _turnTo;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Camera is not { } camera)
        {
            CameraSight.Waiting(context);
            return;
        }

        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var (pw, ph) = (camera.Width, camera.Height);

        // A frame is kept on every beat: each level shows the frame one more beat back.
        if (pulse.Beat || _newest < 0)
        {
            _newest = (_newest + 1) % Kept;
            if (_frames[_newest] is not { } slot || slot.Length != camera.Bgra.Length)
            {
                _frames[_newest] = new byte[camera.Bgra.Length];
                _beats.Clear();
            }

            camera.Bgra.AsSpan(0, _frames[_newest].Length).CopyTo(_frames[_newest]);
            _beats.Insert(0, _newest);
            while (_beats.Count > Kept)
            {
                _beats.RemoveAt(_beats.Count - 1);
            }
        }

        var levels = (int)context.Number("levels");
        var shrink = context.Number("shrink") * (1 - (0.12 * pulse.Bass));
        _turnTo += context.Toggle("turn") && pulse.Beat ? 0.12 : 0;
        _turn += (_turnTo - _turn) * (1 - Math.Exp(-context.Dt / 0.12));
        var natural = context.Pick("colour") == 1;
        var (mcx, mcy) = CameraSight.Centre(context.Mask, context.MaskWidth, context.MaskHeight);
        var (fx, fy) = context.Mask is null ? (pw / 2.0, ph / 2.0) : (mcx * pw / Math.Max(1, context.MaskWidth), mcy * ph / Math.Max(1, context.MaskHeight));
        var (scale, left, top) = CameraSight.Cover(w, h, pw, ph);
        var (cx, cy) = (left + (fx * scale), top + (fy * scale));
        var flash = pulse.Punch;
        var (frames, beats, turn) = (_frames, _beats.ToArray(), _turn);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // The deepest level whose frame still holds this point: unwind it level by level.
                var (dx, dy) = ((double)x - cx, (double)y - cy);
                var level = 0;
                while (level < levels - 1)
                {
                    var (ux, uy) = (dx / shrink, dy / shrink);
                    var (cos, sin) = (Math.Cos(-turn), Math.Sin(-turn));
                    var (rx, ry) = ((ux * cos) - (uy * sin), (ux * sin) + (uy * cos));
                    if (cx + rx < 0 || cy + ry < 0 || cx + rx >= w || cy + ry >= h)
                    {
                        break;
                    }

                    (dx, dy) = (rx, ry);
                    level++;
                }

                var frame = frames[beats[Math.Min(beats.Length - 1, level)]];
                var (px, py) = ((int)Math.Clamp((cx + dx - left) / scale, 0, pw - 1), (int)Math.Clamp((cy + dy - top) / scale, 0, ph - 1));
                var at = ((py * pw) + px) * 4;
                var color = new Rgb(frame[at + 2] / 255f, frame[at + 1] / 255f, frame[at] / 255f);
                var deep = (float)level / Math.Max(1, levels - 1);
                color = natural ? color : context.Paint(deep).Times(color.Luma);
                color = color.Times((float)Math.Pow(0.85, level) * (1 + (flash * (1 - deep))));
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.6f, 0.4f, 4);
    }
}
