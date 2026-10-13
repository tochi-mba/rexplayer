namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The camera as a slit-scan mirror (AU-18): each row of the picture (or column, or ring) shows a
/// different moment of the last second or two, so every movement smears and folds through time. The
/// bass deepens the delay, each kick sends a ripple of time through the picture, and the palette
/// tints it the further back it reaches.
/// </summary>
public sealed class TimeWarpScene : VisualScene
{
    private const int Kept = 64;

    private readonly byte[][] _frames = new byte[Kept][];
    private int _newest = -1;
    private int _count;
    private double _ripple = 10;

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
        var (pw, ph) = (camera.Width, camera.Height);

        // Keep this frame as the newest of the last few seconds' pictures.
        _newest = (_newest + 1) % Kept;
        if (_frames[_newest] is not { } slot || slot.Length != camera.Bgra.Length)
        {
            (_frames[_newest], _count) = (new byte[camera.Bgra.Length], 0);
            slot = _frames[_newest];
        }

        camera.Bgra.AsSpan(0, slot.Length).CopyTo(slot);
        _count = Math.Min(Kept, _count + 1);
        _ripple = pulse.Kick.Hit ? 0 : _ripple + context.Dt;

        var reach = context.Number("depth");
        var depth = reach * (0.35 + (0.65 * pulse.Bass));
        var bands = pulse.Bands;
        var way = context.Pick("direction");
        var tint = (float)context.Number("tint");
        var (scale, left, top) = CameraSight.Cover(w, h, pw, ph);
        var frames = _frames;
        var (newest, count, ripple) = (_newest, _count, _ripple);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // How far back this point looks: by its row, its column or its distance from the middle.
                var (u, v) = ((double)x / w, (double)y / h);
                var along = way switch
                {
                    1 => u,
                    2 => Math.Min(1, Math.Sqrt(((u - 0.5) * (u - 0.5)) + ((v - 0.5) * (v - 0.5))) * 1.6),
                    _ => v,
                };
                var wave = ripple < 1 ? Math.Exp(-Math.Pow((along - (ripple * 1.5)) / 0.08, 2)) * 8 : 0;
                var back = (int)Math.Clamp((along * depth) + wave, 0, count - 1);
                var frame = frames[(newest - back + Kept) % Kept];
                var (px, py) = ((int)Math.Clamp((x - left) / scale, 0, pw - 1), (int)Math.Clamp((y - top) / scale, 0, ph - 1));
                var at = ((py * pw) + px) * 4;
                var color = new Rgb(frame[at + 2] / 255f, frame[at + 1] / 255f, frame[at] / 255f);
                // The further back a point looks, the more the palette colours it; its part of the
                // spectrum lights it, and the kick's ripple flashes as it passes.
                var shade = context.Paint(along).Times(color.Luma);
                color = color.Toward(shade, Math.Min(1, tint * (0.3f + (float)(back / Math.Max(1, reach)))));
                var band = bands[(int)(along * (bands.Count - 1))];
                color = color.Times(0.7f + (0.6f * band) + (float)(wave * 0.08));
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.6f, 0.3f + (0.5f * pulse.Punch), 4);
    }
}
