namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener on a wall of lights (AU-18): the camera's picture made of round LEDs, each lit as
/// bright as the picture there, but only as far as its column's part of the spectrum allows, so the
/// picture of them is an equaliser at the same time, the bass on the left. The listener shines brighter
/// than the room behind them; every kick snaps the wall to coarse pixels for a moment.
/// </summary>
public sealed class LedWallScene : VisualScene
{
    private float _snap;

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
        _snap = pulse.Kick.Hit ? pulse.Kick.Strength : Math.Max(0, _snap - (float)(context.Dt * 4));

        // A kick halves the columns, for a coarse moment.
        var columns = Math.Max(4, (int)(context.Number("tiles") * (1 - (0.5 * _snap))));
        var tile = (double)w / columns;
        var rows = Math.Max(2, (int)(h / tile));
        var gate = (float)context.Number("gate");
        var byColumn = context.Pick("colour") == 1;
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        var mask = context.Mask;
        canvas.Clear(Rgb.Black);
        for (var c = 0; c < columns; c++)
        {
            var band = pulse.Bands[c * (pulse.Bands.Count - 1) / Math.Max(1, columns - 1)];
            var open = 1 - gate + (gate * band);
            for (var r = 0; r < rows; r++)
            {
                var (x, y) = ((c + 0.5) * tile, (r + 0.5) * h / rows);
                var (px, py) = ((x - left) / scale, (y - top) / scale);
                var picture = new Rgb(camera.Sample(px, py, 2), camera.Sample(px, py, 1), camera.Sample(px, py, 0));
                var person = mask is not null && CameraSight.Inside(mask, context.MaskWidth, context.MaskHeight, px * context.MaskWidth / camera.Width, py * context.MaskHeight / camera.Height);
                var luma = picture.Luma;
                var color = context.Paint(byColumn ? (double)c / columns : luma);
                var light = luma * open * (person ? 1.4f : 0.7f) * (0.8f + (0.6f * _snap));
                canvas.Disc(x, y, Math.Max(0.6, tile * 0.42), color, light);
            }
        }

        canvas.Bloom(0.5f, 0.6f, 3);
    }
}
