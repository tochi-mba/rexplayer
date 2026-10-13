namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener in a hall of mirrors (AU-18): the camera's picture folded into a wedge that mirrors
/// round the middle of the stage, so they appear as many as the mirrors, meeting themselves. The
/// hall turns a step on every beat, each bar it can gain or lose a mirror, the bass draws the picture
/// in toward the middle, and every kick lights it up.
/// </summary>
public sealed class HallOfMirrorsScene : VisualScene
{
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
        var size = Math.Min(w, h);
        var (cx, cy) = (w / 2.0, h / 2.0);
        var turning = context.Toggle("turn");
        _turnTo += turning && pulse.Beat ? Math.PI / 10 : 0;
        _turn += (_turnTo - _turn) * (1 - Math.Exp(-context.Dt / 0.1));
        var mirrors = Math.Max(2, (int)context.Number("mirrors") + (turning ? (int)(pulse.Beats / 4 % 3) - 1 : 0));
        var wedge = Math.PI / mirrors;
        var zoom = 1 / (1 + (context.Number("zoom") * 0.5 * pulse.Bass));
        var natural = context.Pick("colour") == 1;
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        var (pcx, pcy) = (CameraSight.Centre(context.Mask, context.MaskWidth, context.MaskHeight));
        var (fx, fy) = context.Mask is null ? (camera.Width / 2.0, camera.Height / 2.0) : (pcx * camera.Width / Math.Max(1, context.MaskWidth), pcy * camera.Height / Math.Max(1, context.MaskHeight));
        var bright = 0.75f + (0.9f * pulse.Punch);
        var turn = _turn;
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // Folded into the first wedge, then looked up in the picture about the listener.
                var (dx, dy) = ((x - cx) * zoom, (y - cy) * zoom);
                var r = Math.Sqrt((dx * dx) + (dy * dy));
                var a = Math.Atan2(dy, dx) + turn;
                a = ((a % (2 * wedge)) + (2 * wedge)) % (2 * wedge);
                a = a > wedge ? (2 * wedge) - a : a;
                var (u, v) = (Math.Cos(a) * r, Math.Sin(a) * r);
                var (px, py) = (fx + (u / scale), fy + (v / scale));
                var color = CameraSight.Tone(context, camera, px, py, natural, Math.Min(1, r / size)).Times(bright);
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.6f, 0.5f, 4);
    }
}
