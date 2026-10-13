namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener seen through water (AU-18): every kick drops a wave into the surface at the
/// listener, and the picture is bent through the rings as they spread, each as big as the kick
/// was hard; the bass heaves the whole surface in a slow swell, the hi-hats shimmer it, and the
/// light caught in the ripples glints in the palette's colours.
/// </summary>
public sealed class WaterScene : VisualScene
{
    private readonly List<(double X, double Y, double Age, double Strength)> _waves = [];
    private double _swell;

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
        var (mcx, mcy) = CameraSight.Centre(context.Mask, context.MaskWidth, context.MaskHeight);
        var (ox, oy) = context.Mask is null ? (w / 2.0, h / 2.0) : (mcx * w / Math.Max(1, context.MaskWidth), mcy * h / Math.Max(1, context.MaskHeight));
        if (pulse.Kick.Hit)
        {
            _waves.Add((ox, oy, 0, Math.Max(pulse.Kick.Strength, pulse.Punch)));
        }

        var lifetime = context.Number("lifetime");
        for (var i = 0; i < _waves.Count; i++)
        {
            _waves[i] = (_waves[i].X, _waves[i].Y, _waves[i].Age + context.Dt, _waves[i].Strength);
        }

        _waves.RemoveAll(wave => wave.Age > lifetime);
        _swell += context.Dt * (0.5 + (2 * pulse.Bass));
        var depth = context.Number("depth") * size * 0.04;
        var shimmer = context.Toggle("shimmer") ? pulse.Hat.Level * size * 0.004 : 0;
        var natural = context.Pick("colour") == 1;
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        var glint = context.Paint(0.85);
        var (waves, swell, bass) = (_waves.ToArray(), _swell, pulse.Bass);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // The surface's slope here: the rings' crests, the swell, and the shimmer.
                var (sx, sy, height) = (0.0, 0.0, 0.0);
                foreach (var (wx, wy, age, strength) in waves)
                {
                    var (dx, dy) = (x - wx, y - wy);
                    var r = Math.Sqrt((dx * dx) + (dy * dy));
                    var front = age * size * 0.5;
                    var ring = Math.Exp(-Math.Pow((r - front) / (size * 0.05), 2)) * strength * (1 - (age / lifetime));
                    var crest = Math.Sin((r - front) * 0.3) * ring;
                    (sx, sy, height) = (sx + (crest * dx / Math.Max(1, r)), sy + (crest * dy / Math.Max(1, r)), height + ring);
                }

                var heave = Math.Sin((x * 0.01) + swell) * Math.Cos((y * 0.013) - (swell * 0.7)) * bass * 0.5;
                var jitter = shimmer > 0 ? (Noise.Hash((x * 7) + (y * 131) + (long)(swell * 50)) - 0.5) * shimmer : 0;
                var (px, py) = ((x + (sx * depth) + (heave * depth * 0.5) + jitter - left) / scale, (y + (sy * depth) + (heave * depth * 0.5) - top) / scale);
                var color = CameraSight.Tone(context, camera, px, py, natural, Math.Min(1, height));
                color = color.Plus(glint.Times((float)(height * 0.5)));
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.55f, 0.5f, 4);
    }
}
