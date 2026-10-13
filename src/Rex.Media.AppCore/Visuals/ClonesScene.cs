namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener's dance, frozen beat by beat (AU-18): on every beat the camera's outline of them is
/// kept as a clone in the colour of that moment's pitch, and the clones drift away to either side,
/// growing and fading, so the last bars of the dance stand behind the dancer like a stop-motion
/// trail. The dancer themselves glows on top, flaring with every kick.
/// </summary>
public sealed class ClonesScene : VisualScene
{
    private readonly List<Clone> _clones = [];

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
        var keep = (int)context.Number("clones");
        if (pulse.Beat)
        {
            _clones.Add(new Clone([.. mask], pulse.Pitch, _clones.Count % 2 == 0 ? 1 : -1));
            while (_clones.Count > keep)
            {
                _clones.RemoveAt(0);
            }
        }

        var beat = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        foreach (var clone in _clones)
        {
            clone.Age += context.Dt / beat;
        }

        var (scale, left, top) = SilhouetteScene.Fit(w, h, mw, mh);
        var drift = context.Number("drift") * w * 0.035;
        var (cx, cy) = CameraSight.Centre(mask, mw, mh);
        var colours = _clones.Select(clone => context.Paint(clone.Pitch)).ToArray();
        var live = context.Paint(pulse.Pitch);
        var flare = 0.5f + (0.9f * pulse.Punch);
        var clones = _clones.ToArray();
        canvas.Clear(Rgb.Black);
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var (mx, my) = ((x - left) / scale, (y - top) / scale);
                var light = Rgb.Black;

                // The clones, oldest first: each moved out to its side and grown about the dancer's middle.
                for (var c = 0; c < clones.Length; c++)
                {
                    var clone = clones[c];
                    var grow = 1 + (0.08 * clone.Age);
                    var (ox, oy) = (cx + ((mx - cx - (clone.Side * clone.Age * drift / scale)) / grow), cy + ((my - cy) / grow));
                    if (CameraSight.Inside(clone.Mask, mw, mh, ox, oy))
                    {
                        light = colours[c].Times((float)(0.55 * Math.Max(0, 1 - (clone.Age / (keep + 1)))));
                    }
                }

                if (CameraSight.Inside(mask, mw, mh, mx, my))
                {
                    light = live.Times(flare);
                }

                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (light.R, light.G, light.B);
            }
        });

        canvas.Bloom(0.45f, 0.7f, 5);
    }

    private sealed class Clone(byte[] mask, float pitch, int side)
    {
        public byte[] Mask { get; } = mask;

        public float Pitch { get; } = pitch;

        public int Side { get; } = side;

        public double Age { get; set; }
    }
}
