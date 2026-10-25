namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener's picture shattered by the kick (AU-18): the picture is cut into tiles, and every
/// kick blasts them outward from the listener, spinning, as hard as the kick was; between kicks
/// they spring back together, so the picture breaks and mends with the beat. The drop blows it
/// apart entirely. Each tile is lit by its part of the spectrum.
/// </summary>
public sealed class ShatterScene : VisualScene
{
    private (double X, double Y, double Vx, double Vy, double Spin, double Angle)[] _tiles = [];

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
        var random = context.Random;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var columns = (int)context.Number("tiles");
        var rows = Math.Max(2, (int)Math.Round(columns * h / (double)w));
        if (_tiles.Length != columns * rows)
        {
            _tiles = new (double, double, double, double, double, double)[columns * rows];
        }

        var (mcx, mcy) = CameraSight.Centre(context.Mask, context.MaskWidth, context.MaskHeight);
        var (cx, cy) = context.Mask is null ? (w / 2.0, h / 2.0) : (mcx * w / Math.Max(1, context.MaskWidth), mcy * h / Math.Max(1, context.MaskHeight));
        var blast = context.Number("blast") * size * (pulse.Drop ? 5 : pulse.Kick.Hit ? 1.2 * Math.Max(pulse.Kick.Strength, pulse.Punch) : 0);
        var spinning = context.Toggle("spin");
        var natural = context.Pick("colour") == 1;
        var dt = context.Dt;
        var (tw, th) = ((double)w / columns, (double)h / rows);
        var (scale, left, top) = CameraSight.Cover(w, h, camera.Width, camera.Height);
        canvas.Clear(Rgb.Black);
        for (var t = 0; t < _tiles.Length; t++)
        {
            var (ox, oy, vx, vy, spin, angle) = _tiles[t];
            var (c, r) = (t % columns, t / columns);
            var (hx, hy) = (((c + 0.5) * tw) - cx, ((r + 0.5) * th) - cy);
            if (blast > 0)
            {
                var reach = Math.Max(1, Math.Sqrt((hx * hx) + (hy * hy)));
                (vx, vy) = (vx + (hx / reach * blast * (0.6 + (0.8 * random.NextDouble()))), vy + (hy / reach * blast * (0.6 + (0.8 * random.NextDouble()))));
                spin += spinning ? (random.NextDouble() - 0.5) * blast / size * 6 : 0;
            }

            // A spring back to its place, damped.
            (vx, vy) = ((vx - (ox * 30 * dt)) * Math.Exp(-dt * 4), (vy - (oy * 30 * dt)) * Math.Exp(-dt * 4));
            (spin, angle) = ((spin - (angle * 30 * dt)) * Math.Exp(-dt * 4), angle + (spin * dt));
            (ox, oy) = (ox + (vx * dt), oy + (vy * dt));
            _tiles[t] = (ox, oy, vx, vy, spin, angle);

            // The tile, drawn at its place, turned about its middle.
            var band = pulse.Bands[t * (pulse.Bands.Count - 1) / Math.Max(1, _tiles.Length - 1)];
            var light = 0.65f + (0.6f * band) + (0.3f * pulse.Punch);
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            var (tcx, tcy) = (((c + 0.5) * tw) + ox, ((r + 0.5) * th) + oy);
            var gap = 1 - Math.Min(0.3, Math.Sqrt((ox * ox) + (oy * oy)) / size);
            for (var y = (int)(r * th); y < (int)((r + 1) * th); y++)
            {
                for (var x = (int)(c * tw); x < (int)((c + 1) * tw); x++)
                {
                    var (dx, dy) = ((x - ((c + 0.5) * tw)) * gap, (y - ((r + 0.5) * th)) * gap);
                    var (sx, sy) = ((int)(tcx + (dx * cos) - (dy * sin)), (int)(tcy + (dx * sin) + (dy * cos)));
                    if (sx >= 0 && sy >= 0 && sx < w && sy < h)
                    {
                        var color = CameraSight.Tone(context, camera, (x - left) / scale, (y - top) / scale, natural, (double)c / columns).Times(light);
                        var i = (sy * w) + sx;
                        (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
                    }
                }
            }
        }

        canvas.Bloom(0.6f, 0.4f, 3);
    }
}
