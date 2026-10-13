namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Painting with light by moving (AU-18): wherever the listener's outline moves, the edge it leaves
/// behind is laid down as a stroke of light in the colour of the moment's pitch, as bright as the
/// music is loud; the strokes linger and fade. Every kick flares the whole outline, so standing still
/// still paints the beat, and the body shows faintly inside.
/// </summary>
public sealed class LightPaintingScene : VisualScene
{
    private byte[] _before = [];

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
        var size = Math.Min(w, h);
        if (_before.Length != mask.Length)
        {
            _before = new byte[mask.Length];
        }

        canvas.Feedback((float)context.Number("fade"), 1.0, 0, 0, 0);
        var (scale, left, top) = SilhouetteScene.Fit(w, h, mw, mh);
        var brush = size * 0.006 * context.Number("brush");
        var ink = context.Paint(pulse.Pitch);
        var moving = 0.4f + (1.2f * pulse.Loudness);
        var flare = pulse.Punch;
        var body = context.Toggle("body") ? context.Paint(0.5).Times(0.08f + (0.1f * pulse.Bass)) : Rgb.Black;
        for (var y = 1; y < mh - 1; y++)
        {
            for (var x = 1; x < mw - 1; x++)
            {
                var i = (y * mw) + x;
                var inside = mask[i] > 0;
                var edge = inside && (mask[i - 1] == 0 || mask[i + 1] == 0 || mask[i - mw] == 0 || mask[i + mw] == 0);
                var (sx, sy) = (left + ((x + 0.5) * scale), top + ((y + 0.5) * scale));
                if (inside && context.Toggle("body"))
                {
                    canvas.Fill((int)sx, (int)sy, Math.Max(1, (int)scale + 1), Math.Max(1, (int)scale + 1), body);
                }

                // A stroke where the edge was not a frame ago (the body moved), and the whole edge on the kick.
                var changed = inside != (_before[i] > 0);
                if ((edge && changed) || (edge && flare > 0.3f))
                {
                    canvas.Glow(sx, sy, brush * (changed ? 1.6 : 1), ink, changed ? moving : flare * 0.9f);
                }
            }
        }

        mask.AsSpan(0, _before.Length).CopyTo(_before);
        canvas.Bloom(0.4f, 0.8f, 5);
    }
}
