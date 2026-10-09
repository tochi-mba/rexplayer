namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener as the camera sees them (AU-18), filling the stage: a glowing outline that flares
/// on the beat, a figure filled with the spectrum (bass at the feet), sparks thrown off the outline,
/// echoes of every move trailing away in changing colours, or a neon outline split three ways that
/// spreads with the bass. The room can show dimly behind. The camera's picture never leaves the
/// computer, and is not kept.
/// </summary>
public sealed class SilhouetteScene : VisualScene
{
    private readonly Particles _sparks = new(400);
    private float[] _fill = [];
    private float[] _edge = [];
    private float[] _soft = [];
    private double _flash;

    public override bool UsesCamera => true;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var style = context.Pick("style");
        _flash = pulse.Drop ? 1 : Math.Max(0, _flash - (context.Dt / 0.6));
        if (style == 3)
        {
            canvas.Feedback(0.86f, 1.012, 0, 0, 0);
        }
        else
        {
            canvas.Clear(Rgb.Black);
        }

        if (context.Mask is not { } mask || context.MaskWidth < 3 || context.MaskHeight < 3)
        {
            // No outline yet (the camera is starting, or watching the room): a slow breath of light.
            canvas.Glow(canvas.Width / 2.0, canvas.Height / 2.0, Math.Min(canvas.Width, canvas.Height) * (0.3 + (0.1 * pulse.Bass)), context.Paint(0.5), 0.25f);
            return;
        }

        Outline(mask, context.MaskWidth, context.MaskHeight);
        var (scale, left, top) = Cover(canvas.Width, canvas.Height, context.MaskWidth, context.MaskHeight);
        if (context.Pick("background") == 1 && context.Camera is { } room)
        {
            // The room, dimmed and greyed, behind.
            var (roomScale, roomLeft, roomTop) = Cover(canvas.Width, canvas.Height, room.Width, room.Height);
            Raster.Rows(canvas.Height, y =>
            {
                for (var x = 0; x < canvas.Width; x++)
                {
                    var (sx, sy) = ((x - roomLeft) / roomScale, (y - roomTop) / roomScale);
                    var grey = ((room.Sample(sx, sy, 0) * 0.1f) + (room.Sample(sx, sy, 1) * 0.6f) + (room.Sample(sx, sy, 2) * 0.3f)) * 0.14f;
                    var i = (y * canvas.Width) + x;
                    canvas.R[i] += grey;
                    canvas.G[i] += grey;
                    canvas.B[i] += grey * 1.1f;
                }
            });
        }

        var glow = (float)(0.5 + (1.0 * pulse.Loudness) + (1.6 * pulse.Kick.Level) + (0.6 * pulse.Snare.Level));
        var spread = 3 + (pulse.Bass * canvas.Width * 0.03);
        var echo = context.Paint((context.Seconds * 0.12) % 1);
        var (w, h) = (canvas.Width, canvas.Height);
        Raster.Rows(h, y =>
        {
            var row = Math.Clamp(1 - ((double)y / h), 0, 1);
            var color = context.Paint(row);
            var band = pulse.Bands[Math.Min(pulse.Bands.Count - 1, (int)((1 - ((double)y / h)) * pulse.Bands.Count))];
            for (var x = 0; x < w; x++)
            {
                var (mx, my) = ((x - left) / scale, (y - top) / scale);
                var edge = Sample(_edge, context.MaskWidth, context.MaskHeight, mx, my);
                var fill = Sample(_fill, context.MaskWidth, context.MaskHeight, mx, my);
                var i = (y * w) + x;
                Rgb light = style switch
                {
                    1 => color.Times((fill * (0.15f + (0.95f * band))) + (edge * 0.7f)),
                    2 => color.Times((edge * 0.55f) + (fill * 0.05f)),
                    3 => echo.Times(edge * 1.3f),
                    4 => Neon(context, mx, my, spread / scale, glow),
                    _ => color.Times((edge * glow) + (fill * (0.1f + (0.15f * (float)_flash)))),
                };
                canvas.R[i] += light.R + (float)(_flash * fill * 0.5);
                canvas.G[i] += light.G + (float)(_flash * fill * 0.5);
                canvas.B[i] += light.B + (float)(_flash * fill * 0.5);
            }
        });

        if (style == 2)
        {
            ThrowSparks(context, scale, left, top);
        }

        canvas.Bloom(0.35f, 0.9f, 5);
    }

    /// <summary>Where a picture of <paramref name="across"/> by <paramref name="down"/> sits scaled to fill the stage, cut at its edges.</summary>
    internal static (double Scale, double Left, double Top) Cover(int width, int height, int across, int down)
    {
        var scale = Math.Max((double)width / across, (double)height / down);
        return (scale, (width - (across * scale)) / 2, (height - (down * scale)) / 2);
    }

    /// <summary>The outline in three colours, side by side, drawn apart by <paramref name="apart"/> (in outline pixels).</summary>
    private Rgb Neon(VisualContext context, double x, double y, double apart, float glow)
    {
        var (w, h) = (context.MaskWidth, context.MaskHeight);
        var light = Rgb.Black;
        for (var copy = 0; copy < 3; copy++)
        {
            var edge = Sample(_edge, w, h, x + ((copy - 1) * apart), y);
            light = light.Plus(context.Paint(copy / 3.0).Times(edge * glow * 0.8f));
        }

        return light;
    }

    /// <summary>
    /// The figure (1 inside) and its outline, from the camera's mask: the mask is softened first, so
    /// the figure's edge is smooth rather than stepped, and the outline is where that soft edge falls
    /// fastest (its gradient), a line that fades either side as a drawn line of light would.
    /// </summary>
    private void Outline(byte[] mask, int w, int h)
    {
        if (_fill.Length != w * h)
        {
            (_fill, _edge, _soft) = (new float[w * h], new float[w * h], new float[w * h]);
        }

        for (var i = 0; i < mask.Length && i < _fill.Length; i++)
        {
            _fill[i] = mask[i] > 0 ? 1 : 0;
        }

        Soften(_fill, _soft, w, h);
        Soften(_soft, _fill, w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var gx = _fill[(y * w) + Math.Min(w - 1, x + 1)] - _fill[(y * w) + Math.Max(0, x - 1)];
                var gy = _fill[(Math.Min(h - 1, y + 1) * w) + x] - _fill[(Math.Max(0, y - 1) * w) + x];
                _edge[(y * w) + x] = Math.Min(1, MathF.Sqrt((gx * gx) + (gy * gy)) * 1.6f);
            }
        }
    }

    /// <summary>A three-by-three average of <paramref name="from"/> into <paramref name="to"/>.</summary>
    private static void Soften(float[] from, float[] to, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                float sum = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var row = Math.Clamp(y + dy, 0, h - 1) * w;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        sum += from[row + Math.Clamp(x + dx, 0, w - 1)];
                    }
                }

                to[(y * w) + x] = sum / 9;
            }
        }
    }

    /// <summary>A value of <paramref name="plane"/> between its pixels; 0 outside it.</summary>
    private static float Sample(float[] plane, int w, int h, double x, double y)
    {
        if (x < 0 || y < 0 || x > w - 1 || y > h - 1)
        {
            return 0;
        }

        var (x0, y0) = ((int)x, (int)y);
        var (x1, y1) = (Math.Min(x0 + 1, w - 1), Math.Min(y0 + 1, h - 1));
        var (fx, fy) = ((float)(x - x0), (float)(y - y0));
        var top = plane[(y0 * w) + x0] + ((plane[(y0 * w) + x1] - plane[(y0 * w) + x0]) * fx);
        var bottom = plane[(y1 * w) + x0] + ((plane[(y1 * w) + x1] - plane[(y1 * w) + x0]) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>Sparks off points of the outline, more the louder it is.</summary>
    private void ThrowSparks(VisualContext context, double scale, double left, double top)
    {
        var (w, h) = (context.MaskWidth, context.MaskHeight);
        var size = Math.Min(context.Canvas.Width, context.Canvas.Height);
        var wanted = (int)(context.Dt * 300 * context.Pulse.Loudness) + (context.Pulse.Kick.Hit ? 40 : 0) + (context.Pulse.Hat.Hit ? 10 : 0);
        for (var tries = 0; tries < wanted * 8 && wanted > 0; tries++)
        {
            var i = context.Random.Next(_edge.Length);
            if (_edge[i] > 0)
            {
                wanted--;
                var (x, y) = (left + ((i % w) * scale), top + ((i / w) * scale));
                _sparks.Add(x, y, (context.Random.NextDouble() - 0.5) * size * 0.3, -size * (0.1 + (0.4 * context.Random.NextDouble())), size * 0.012, 1 - ((double)(i / w) / h), 1.4);
            }
        }

        _sparks.Step(context.Dt, 0, -size * 0.1, 0.8);
        _sparks.Draw(context, 1, 0.03);
    }
}

