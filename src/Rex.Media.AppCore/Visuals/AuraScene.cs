namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener's aura (AU-18): a field of light around them, softened from the camera's outline,
/// that breathes out with the bass; every kick sends a shockwave rippling outward from their body
/// along the field's contours; the hi-hats strike sparks along their edge. Inside, they are drawn as
/// heat, hottest at their core, or as a dark figure against their own glow.
/// </summary>
public sealed class AuraScene : VisualScene
{
    private const int Passes = 4;

    private readonly List<double> _waves = [];
    private float[] _field = [];
    private float[] _spare = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Mask is not { } mask || context.MaskWidth < 3 || context.MaskHeight < 3)
        {
            CameraSight.Waiting(context);
            return;
        }

        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h, mw, mh) = (canvas.Width, canvas.Height, context.MaskWidth, context.MaskHeight);
        Soften(mask, mw, mh);
        if (pulse.Kick.Hit && context.Toggle("rings"))
        {
            _waves.Add(0);
        }

        for (var i = 0; i < _waves.Count; i++)
        {
            _waves[i] += context.Dt * 1.6;
        }

        _waves.RemoveAll(wave => wave > 1);
        var (scale, left, top) = SilhouetteScene.Fit(w, h, mw, mh);
        var glow = (float)context.Number("glow") * (0.35f + (0.9f * pulse.Bass) + (0.5f * pulse.Punch));
        var heat = context.Toggle("heat");
        var (field, waves) = (_field, _waves.ToArray());
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var (mx, my) = ((x - left) / scale, (y - top) / scale);
                var near = Sample(field, mw, mh, mx, my);
                var inside = CameraSight.Inside(mask, mw, mh, mx, my);

                // The aura falls away from the body; the shockwaves ride its contours outward, a wave
                // being where the field has fallen to the level its age has reached.
                var light = context.Paint(0.15 + (0.5 * (1 - near))).Times(near * near * glow);
                foreach (var wave in waves)
                {
                    var level = 0.95 - wave;
                    var on = (float)Math.Exp(-Math.Pow((near - level) / 0.04, 2) * 1) * (float)(1 - wave);
                    light = light.Plus(context.Paint(0.8).Times(on * 0.9f));
                }

                if (inside)
                {
                    light = heat ? context.Paint(Math.Min(1, near * 1.1)).Times(0.5f + (0.6f * near) + (0.4f * pulse.Punch)) : light.Times(0.15f);
                }

                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (light.R, light.G, light.B);
            }
        });

        Sparks(context, mask, scale, left, top);
        canvas.Bloom(0.4f, 0.8f, 5);
    }

    /// <summary>The outline softened outward: a box blur passed over it a few times.</summary>
    private void Soften(byte[] mask, int w, int h)
    {
        if (_field.Length != w * h)
        {
            (_field, _spare) = (new float[w * h], new float[w * h]);
        }

        for (var i = 0; i < _field.Length; i++)
        {
            _field[i] = mask[i] > 0 ? 1 : 0;
        }

        var radius = Math.Max(2, w / 24);
        for (var pass = 0; pass < Passes; pass++)
        {
            // Across, then down, each a running average.
            for (var y = 0; y < h; y++)
            {
                float sum = 0;
                for (var x = -radius; x < w; x++)
                {
                    sum += x + radius < w ? _field[(y * w) + Math.Min(w - 1, x + radius)] : 0;
                    sum -= x - radius - 1 >= 0 ? _field[(y * w) + (x - radius - 1)] : 0;
                    if (x >= 0)
                    {
                        _spare[(y * w) + x] = sum / ((2 * radius) + 1);
                    }
                }
            }

            for (var x = 0; x < w; x++)
            {
                float sum = 0;
                for (var y = -radius; y < h; y++)
                {
                    sum += y + radius < h ? _spare[(Math.Min(h - 1, y + radius) * w) + x] : 0;
                    sum -= y - radius - 1 >= 0 ? _spare[((y - radius - 1) * w) + x] : 0;
                    if (y >= 0)
                    {
                        _field[(y * w) + x] = Math.Min(1, sum / ((2 * radius) + 1) * 1.6f);
                    }
                }
            }
        }
    }

    private static float Sample(float[] field, int w, int h, double x, double y)
    {
        var (u, v) = ((int)Math.Clamp(x, 0, w - 1), (int)Math.Clamp(y, 0, h - 1));
        return x < 0 || y < 0 || x > w - 1 || y > h - 1 ? 0 : field[(v * w) + u];
    }

    /// <summary>On each hi-hat, sparks at points along the body's edge.</summary>
    private static void Sparks(VisualContext context, byte[] mask, double scale, double left, double top)
    {
        if (!context.Pulse.Hat.Hit)
        {
            return;
        }

        var (mw, mh) = (context.MaskWidth, context.MaskHeight);
        var size = Math.Min(context.Canvas.Width, context.Canvas.Height);
        var found = 0;
        for (var tries = 0; tries < 400 && found < 14; tries++)
        {
            var (x, y) = (context.Random.Next(1, mw - 1), context.Random.Next(1, mh - 1));
            var edge = mask[(y * mw) + x] > 0 && (mask[(y * mw) + x - 1] == 0 || mask[(y * mw) + x + 1] == 0 || mask[((y - 1) * mw) + x] == 0 || mask[((y + 1) * mw) + x] == 0);
            if (edge)
            {
                found++;
                context.Canvas.Glow(left + (x * scale), top + (y * scale), size * 0.02, context.Paint(0.9), 1.2f);
            }
        }
    }
}
