namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener scanned to the beat (AU-18): a bar of light sweeps across the picture once a beat,
/// and the picture shows only where the bar has just passed, fading behind it like a scanner's
/// trace, in the palette's colours or the camera's own; the bar flares on the kick and its light
/// crackles with the hi-hats.
/// </summary>
public sealed class ScannerScene : VisualScene
{
    private double _sweep;
    private float[] _seen = [];

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
        if (_seen.Length != w * h)
        {
            _seen = new float[w * h];
        }

        // The bar goes once across each beat, by the clock when it is sure; otherwise it waits.
        var across = context.Pick("direction") == 1;
        var length = across ? w : h;
        var position = pulse.Locked || pulse.Tempo > 0 ? pulse.Phase * length : _sweep;
        var width = Math.Max(2, length * 0.02 * context.Number("width") * (1 + pulse.Punch));
        var natural = context.Pick("colour") == 1;
        var hold = (float)Math.Pow(context.Number("hold"), context.Dt * 30);
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        var bar = context.Paint(0.9).Times(0.8f + (1.4f * pulse.Punch) + (0.5f * pulse.Hat.Level));
        var seen = _seen;
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w) + x;
                var along = across ? x : y;
                var near = Math.Abs(along - position);
                var under = near < width;
                seen[i] = under ? 1 : seen[i] * hold;
                var color = seen[i] > 0.01f
                    ? CameraSight.Tone(context, camera, (x - left) / scale, (y - top) / scale, natural, (double)along / length).Times(seen[i])
                    : Rgb.Black;
                if (under)
                {
                    color = color.Plus(bar.Times((float)(1 - (near / width))));
                }

                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        _sweep = position;
        canvas.Bloom(0.5f, 0.7f, 4);
    }
}
