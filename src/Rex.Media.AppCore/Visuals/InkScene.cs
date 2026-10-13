namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Ink in water (AU-18): every drum lets fall a drop of ink, the kick a large one low down, the
/// snare one in the middle, the hi-hats small ones above, each in the colour of the pitch it fell
/// on; the water carries them in a slow current that the bass stirs, curling them into each other,
/// and they fade as they spread.
/// </summary>
public sealed class InkScene : VisualScene
{
    private float[] _r = [];
    private float[] _g = [];
    private float[] _b = [];
    private float[] _r2 = [];
    private float[] _g2 = [];
    private float[] _b2 = [];
    private double _time;
    private int _width;
    private int _height;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        if (_r.Length != w * h)
        {
            (_r, _g, _b, _r2, _g2, _b2) = (new float[w * h], new float[w * h], new float[w * h], new float[w * h], new float[w * h], new float[w * h]);
        }

        (_width, _height) = (w, h);

        var current = context.Number("current") * size * (0.02 + (0.12 * pulse.Bass)) * context.Dt * 30;
        _time += context.Dt * (0.2 + (0.8 * pulse.Loudness));
        var keep = (float)Math.Pow(context.Number("fade"), context.Dt * 30);

        // The current: the curl of a gently moving stream function, so the water neither piles up nor drains.
        var (t, scale) = (_time, 3.0 / size);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var (u, v) = (x * scale, y * scale);
                var vx = (Math.Cos((v * 2.1) + t) * 1.3) + (Math.Cos(((u + v) * 1.4) - (t * 0.8)) * 0.8);
                var vy = (-Math.Cos((u * 1.7) - (t * 1.1)) * 1.3) - (Math.Cos(((u + v) * 1.4) - (t * 0.8)) * 0.8);
                var (sx, sy) = (Math.Clamp(x - (vx * current), 0, w - 1.001), Math.Clamp(y - (vy * current), 0, h - 1.001));
                var (x0, y0) = ((int)sx, (int)sy);
                var (fx, fy) = ((float)(sx - x0), (float)(sy - y0));
                var (a, b, c, d) = ((y0 * w) + x0, (y0 * w) + x0 + 1, ((y0 + 1) * w) + x0, ((y0 + 1) * w) + x0 + 1);
                var i = (y * w) + x;
                _r2[i] = Mix(_r, a, b, c, d, fx, fy) * keep;
                _g2[i] = Mix(_g, a, b, c, d, fx, fy) * keep;
                _b2[i] = Mix(_b, a, b, c, d, fx, fy) * keep;
            }
        });

        (_r, _r2, _g, _g2, _b, _b2) = (_r2, _r, _g2, _g, _b2, _b);
        var drops = context.Number("drops");
        foreach (var (onset, height, reach) in new[] { (pulse.Kick, 0.7, 0.09), (pulse.Snare, 0.45, 0.06), (pulse.Hat, 0.2, 0.025) })
        {
            if (onset.Hit)
            {
                Drop(w * (0.15 + (0.7 * random.NextDouble())), h * height, size * reach * drops * (0.5 + (0.5 * onset.Strength)), context.Paint(pulse.Pitch));
            }
        }

        Array.Copy(_r, canvas.R, _r.Length);
        Array.Copy(_g, canvas.G, _g.Length);
        Array.Copy(_b, canvas.B, _b.Length);
        canvas.Bloom(0.7f, 0.3f, 4);
    }

    private static float Mix(float[] plane, int a, int b, int c, int d, float fx, float fy) =>
        (plane[a] * (1 - fx) * (1 - fy)) + (plane[b] * fx * (1 - fy)) + (plane[c] * (1 - fx) * fy) + (plane[d] * fx * fy);

    /// <summary>A drop of ink: dense in the middle, thinning to its edge.</summary>
    private void Drop(double cx, double cy, double radius, Rgb color)
    {
        var w = _width;
        for (var y = Math.Max(0, (int)(cy - radius)); y < Math.Min(_height, (int)(cy + radius) + 1); y++)
        {
            for (var x = Math.Max(0, (int)(cx - radius)); x < Math.Min(w, (int)(cx + radius) + 1); x++)
            {
                var (dx, dy) = ((x - cx) / radius, (y - cy) / radius);
                var near = 1 - ((dx * dx) + (dy * dy));
                if (near > 0)
                {
                    var i = (y * w) + x;
                    var ink = (float)(near * near);
                    (_r[i], _g[i], _b[i]) = (_r[i] + (color.R * ink), _g[i] + (color.G * ink), _b[i] + (color.B * ink));
                }
            }
        }
    }
}
