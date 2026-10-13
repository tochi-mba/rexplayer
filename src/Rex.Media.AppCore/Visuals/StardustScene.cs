namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The listener made of stardust (AU-18): thousands of motes hold the shape of their body as the
/// camera sees it, each drawn back to its own place in the outline. Every kick blasts them out from
/// the body's middle, and they stream back to re-form it before the next; the hi-hats make them
/// shimmer; the drop scatters the figure entirely. Each mote keeps the colour of where it belongs, the
/// feet one end of the palette and the head the other.
/// </summary>
public sealed class StardustScene : VisualScene
{
    private (double X, double Y, double Vx, double Vy, int Home)[] _motes = [];
    private int[] _homes = [];
    private int _homeCount;

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
        var random = context.Random;
        var (w, h, mw, mh) = (canvas.Width, canvas.Height, context.MaskWidth, context.MaskHeight);
        var size = Math.Min(w, h);
        var count = (int)context.Number("particles");
        var (scale, left, top) = SilhouetteScene.Fit(w, h, mw, mh);
        Homes(mask, mw, mh);
        if (_motes.Length != count)
        {
            _motes = [.. Enumerable.Range(0, count).Select(n => (w / 2.0, h / 2.0, 0.0, 0.0, n))];
        }

        // Where the body's middle is, for the blasts to come from.
        var (cx, cy) = CameraSight.Centre(mask, mw, mh);
        (cx, cy) = (left + (cx * scale), top + (cy * scale));
        var blast = context.Number("blast") * size * (pulse.Drop ? 6 : pulse.Kick.Hit ? 1.6 * Math.Max(pulse.Kick.Strength, pulse.Punch) : 0);
        var glow = 0.55f + (0.6f * pulse.Loudness);
        var shimmer = pulse.Hat.Level * size * 0.6;
        var dt = context.Dt;
        canvas.Feedback((float)context.Number("trails"), 1, 0, 0, 0);
        for (var m = 0; m < count; m++)
        {
            var (x, y, vx, vy, home) = _motes[m];
            var spot = _homes[home % Math.Max(1, _homeCount)];
            var (hx, hy) = _homeCount > 0 ? (left + (((spot % mw) + 0.5) * scale), top + (((spot / mw) + 0.5) * scale)) : (cx, cy);
            if (blast > 0)
            {
                var (dx, dy) = (x - cx + ((random.NextDouble() - 0.5) * 2), y - cy + ((random.NextDouble() - 0.5) * 2));
                var reach = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
                (vx, vy) = (vx + (dx / reach * blast * (0.5 + random.NextDouble())), vy + (dy / reach * blast * (0.5 + random.NextDouble())));
            }

            // A spring home, damped, with a shimmer on the hats.
            var (ax, ay) = (((hx - x) * 18) + ((random.NextDouble() - 0.5) * shimmer), ((hy - y) * 18) + ((random.NextDouble() - 0.5) * shimmer));
            (vx, vy) = ((vx + (ax * dt)) * Math.Exp(-dt * 5), (vy + (ay * dt)) * Math.Exp(-dt * 5));
            (x, y) = (x + (vx * dt), y + (vy * dt));
            _motes[m] = (x, y, vx, vy, home);
            var share = _homeCount > 0 ? 1 - ((double)(spot / mw) / mh) : 0.5;
            canvas.Disc(x, y, Math.Max(0.7, size * 0.0035), context.Paint(share), glow);
        }

        canvas.Bloom(0.4f, 0.8f, 4);
    }

    /// <summary>The outline's points, gathered afresh each frame, for the motes to belong to.</summary>
    private void Homes(byte[] mask, int w, int h)
    {
        if (_homes.Length != w * h)
        {
            _homes = new int[w * h];
        }

        _homeCount = 0;
        for (var i = 0; i < w * h; i++)
        {
            if (mask[i] > 0)
            {
                _homes[_homeCount++] = i;
            }
        }

        // Spread the motes over the whole body rather than its first rows.
        if (_homeCount > 1)
        {
            var stride = Math.Max(1, _homeCount / 997);
            for (var i = 0; i < _homeCount; i++)
            {
                var j = (int)(((long)i * stride * 7919) % _homeCount);
                (_homes[i], _homes[j]) = (_homes[j], _homes[i]);
            }
        }
    }
}
