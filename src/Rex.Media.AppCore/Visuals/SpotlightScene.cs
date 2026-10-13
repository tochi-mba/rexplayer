namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener under stage lights (AU-18): a dark stage, and spotlights from above that follow
/// them, each a cone of coloured light through haze that shows the picture only where it falls.
/// Every kick swings the lights to new colours and widens them; the bass opens the beams; the
/// hi-hats flicker them; a drop brings every light up full.
/// </summary>
public sealed class SpotlightScene : VisualScene
{
    private int _cue;
    private float _full;

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
        var lights = (int)context.Number("lights");
        _cue += pulse.Kick.Hit ? 1 : 0;
        _full = pulse.Drop ? 1 : Math.Max(0, _full - (float)(context.Dt / 1.5));
        var (mcx, mcy) = CameraSight.Centre(context.Mask, context.MaskWidth, context.MaskHeight);
        var (tx, ty) = context.Mask is null ? (w / 2.0, h * 0.5) : (mcx * w / Math.Max(1, context.MaskWidth), mcy * h / Math.Max(1, context.MaskHeight));
        var beam = context.Number("beam") * (0.1 + (0.12 * pulse.Bass) + (0.1 * pulse.Punch));
        var haze = (float)context.Number("haze");
        var natural = context.Pick("colour") == 1;
        var flicker = 1 - (0.5f * pulse.Hat.Level);
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        var cue = _cue;
        var full = _full;
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var light = Rgb.Black;
                for (var l = 0; l < lights; l++)
                {
                    // Each light hangs along the top, aimed at the listener; its colour steps with the kicks.
                    var (lx, ly) = (w * ((l + 0.5) / lights), -h * 0.1);
                    var (ax, ay) = (tx - lx, ty - ly);
                    var (px, py) = (x - lx, y - ly);
                    var along = ((px * ax) + (py * ay)) / Math.Max(1, (ax * ax) + (ay * ay));
                    var (nx, ny) = (px - (ax * along), py - (ay * along));
                    var off = Math.Sqrt((nx * nx) + (ny * ny)) / (size * beam * Math.Max(0.2, along));
                    var cone = (float)Math.Exp(-off * off) * (along > 0 ? 1 : 0);
                    var color = context.Paint(((l * 0.37) + (cue * 0.23)) % 1);
                    light = light.Plus(color.Times((cone * flicker) + (full * 0.6f)));
                }

                var picture = CameraSight.Tone(context, camera, (x - left) / scale, (y - top) / scale, natural, 0.5);
                var lit = new Rgb(picture.R * light.R, picture.G * light.G, picture.B * light.B).Times(1.2f).Plus(light.Times(haze * 0.25f));
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (lit.R, lit.G, lit.B);
            }
        });

        canvas.Bloom(0.5f, 0.6f, 5);
    }
}
