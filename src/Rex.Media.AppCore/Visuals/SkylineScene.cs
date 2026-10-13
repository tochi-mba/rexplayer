namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A city at night that the music runs (AU-18): its towers rise and settle with the spectrum, the
/// bass downtown in the middle, the treble out at the edges; their windows go on and off with the
/// hi-hats; two searchlights sweep the sky and cross on each beat, flaring with the kick; the haze
/// over the city glows with the bass.
/// </summary>
public sealed class SkylineScene : VisualScene
{
    private float[] _heights = [];
    private double _beam;
    private double _beamTo;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var count = (int)context.Number("buildings");
        if (_heights.Length != count)
        {
            _heights = new float[count];
        }

        // The night, and the glow of the city in the haze over it.
        for (var y = 0; y < h; y++)
        {
            var down = (float)y / h;
            var sky = context.Paint(0.8 - (0.3 * down)).Times(0.02f + (0.1f * down * down));
            Array.Fill(canvas.R, sky.R, y * w, w);
            Array.Fill(canvas.G, sky.G, y * w, w);
            Array.Fill(canvas.B, sky.B, y * w, w);
        }

        canvas.Glow(w / 2.0, h, w * 0.6, context.Paint(0.35), 0.1f + (0.35f * pulse.Bass));

        // The searchlights: swinging to a new angle on each beat, crossing in the middle.
        _beamTo = pulse.Beat ? (pulse.Beats % 2 == 0 ? 0.35 : -0.35) : _beamTo;
        _beam += (_beamTo - _beam) * (1 - Math.Exp(-context.Dt / 0.18));
        if (context.Toggle("lights"))
        {
            foreach (var (foot, side) in new[] { (0.3, 1.0), (0.7, -1.0) })
            {
                var angle = (-Math.PI / 2) + (_beam * side);
                var (x0, y0) = (w * foot, h * 1.0);
                canvas.Line(x0, y0, x0 + (Math.Cos(angle) * h * 1.4), y0 + (Math.Sin(angle) * h * 1.4), size * 0.05, context.Paint(0.6), 0.08f + (0.3f * pulse.Punch), 2);
            }
        }

        // The towers: the bass in the middle of town, the treble at its edges.
        var windows = context.Toggle("windows");
        var slot = (double)w / count;
        for (var b = 0; b < count; b++)
        {
            var from = Math.Abs(((b + 0.5) / count * 2) - 1);
            var band = pulse.Bands[(int)(from * (pulse.Bands.Count - 1))];
            _heights[b] = Math.Max(band, _heights[b] - (float)(context.Dt * 0.8));
            var tower = (0.12 + (0.25 * Noise.Hash(b * 11)) + (0.45 * _heights[b])) * h;
            var (left, width) = ((int)(b * slot) + 1, Math.Max(2, (int)slot - 2));
            var top = (int)(h - tower);
            for (var y = top; y < h; y++)
            {
                var row = y * w;
                for (var x = left; x < Math.Min(w, left + width); x++)
                {
                    canvas.R[row + x] = canvas.G[row + x] = canvas.B[row + x] = 0;
                }
            }

            // Its windows: a grid, each lit as the hats' count and its own luck say.
            if (windows)
            {
                var pane = Math.Max(1, (int)(size * 0.012));
                var inset = width > pane * 3 ? pane : 0;
                var color = context.Paint(0.15 + (0.7 * (1 - from)));
                for (var y = top + pane; y < h - pane; y += pane * 2)
                {
                    for (var x = left + inset; x + pane <= left + width - inset; x += pane * 2)
                    {
                        var luck = Noise.Hash((b * 7919) + (y * 31) + x + (pulse.Hat.Count * 104729));
                        if (luck < 0.25 + (0.5 * pulse.Treble))
                        {
                            canvas.Fill(x, y, pane, pane, color, 0.5f + (0.5f * (float)luck));
                        }
                    }
                }
            }

            canvas.Line(left, top, left + width, top, 1, context.Paint(0.5), 0.3f + pulse.Punch);
        }

        canvas.Bloom(0.4f, 0.7f, 4);
    }
}
