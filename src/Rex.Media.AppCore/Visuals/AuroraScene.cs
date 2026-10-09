namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The northern lights over mountains at night (AU-18). Each curtain is a ribbon that folds and
/// drifts across the sky, made of fine vertical rays: bright at its lower hem, where the light is
/// green, fading upward into violet and pink, as the real thing does. Where its part of the
/// spectrum is loud a curtain climbs higher and burns brighter; a beat sends a wave of light running
/// along it; stars twinkle with the treble.
/// </summary>
public sealed class AuroraScene : VisualScene
{
    private const int StarCount = 180;

    private (double X, double Y, double Size, double Phase)[]? _stars;
    private float[] _levels = [];
    private readonly List<double> _waves = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var t = context.Seconds;
        var pulse = context.Pulse;
        var drift = t * context.Number("drift");

        // The sky: deep blue overhead, lighter and greener toward the horizon.
        for (var y = 0; y < h; y++)
        {
            var down = (float)y / h;
            Array.Fill(canvas.R, 0.006f + (0.012f * down), y * w, w);
            Array.Fill(canvas.G, 0.012f + (0.03f * down), y * w, w);
            Array.Fill(canvas.B, 0.05f + (0.03f * down), y * w, w);
        }

        if (context.Toggle("stars"))
        {
            _stars ??= [.. Enumerable.Range(0, StarCount).Select(n => (Noise.Hash(n * 3), Noise.Hash((n * 3) + 1) * 0.75, 0.5 + Noise.Hash((n * 3) + 2), Noise.Hash(n + 999) * Math.Tau))];
            foreach (var (x, y, size, phase) in _stars)
            {
                var twinkle = (0.5 + (0.5 * Math.Sin((t * (1.5 + size)) + phase))) * (0.35 + (0.65 * size)) + (pulse.Treble * 0.6);
                canvas.Glow(x * w, y * h, 0.8 + size, new Rgb(0.85f, 0.9f, 1f), (float)twinkle * 0.7f);
            }
        }

        // The spectrum across the sky, smoothed so the curtains move like cloth, not like bars.
        if (_levels.Length != w)
        {
            _levels = new float[w];
        }

        for (var x = 0; x < w; x++)
        {
            // Between bands, blended: the curtains rise and fall in swells, never in steps.
            var at = Math.Pow((double)x / w, 1.4) * (pulse.Bands.Count - 1);
            var below = (int)at;
            var above = Math.Min(pulse.Bands.Count - 1, below + 1);
            var band = pulse.Bands[below] + ((pulse.Bands[above] - pulse.Bands[below]) * (float)(at - below));
            _levels[x] += (band - _levels[x]) * (float)Math.Min(1, context.Dt * 5);
        }

        Soften(_levels, Math.Max(2, w / 24));

        // A kick sends a wave of light along the curtains, left to right.
        if (pulse.Kick.Hit)
        {
            _waves.Add(0);
        }

        for (var i = 0; i < _waves.Count; i++)
        {
            _waves[i] += context.Dt * 1.3;
        }

        _waves.RemoveAll(wave => wave > 1.4);

        var curtains = (int)context.Number("curtains");
        var height = context.Number("height");
        for (var layer = 0; layer < curtains; layer++)
        {
            Curtain(context, layer, curtains, drift, height);
        }

        Mountains(canvas);
        canvas.Bloom(0.25f, 0.9f, 6);
    }

    /// <summary>A running average of <paramref name="radius"/> each side along <paramref name="levels"/>, twice: a soft swell.</summary>
    private static void Soften(float[] levels, int radius)
    {
        var copy = new float[levels.Length];
        for (var pass = 0; pass < 2; pass++)
        {
            Array.Copy(levels, copy, levels.Length);
            for (var x = 0; x < levels.Length; x++)
            {
                float sum = 0;
                for (var d = -radius; d <= radius; d++)
                {
                    sum += copy[Math.Clamp(x + d, 0, levels.Length - 1)];
                }

                levels[x] = sum / ((2 * radius) + 1);
            }
        }
    }

    /// <summary>One curtain: for each column, rays rising from a folding hem.</summary>
    private void Curtain(VisualContext context, int layer, int curtains, double drift, double height)
    {
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var pulse = context.Pulse;
        var seed = layer * 31.7;
        var depth = curtains == 1 ? 0 : (double)layer / (curtains - 1);
        var baseline = 0.42 + (0.22 * depth);
        var strength = 0.55 - (0.25 * depth);
        Raster.Rows(w, x =>
        {
            var u = (double)x / w;

            // The hem folds: slow broad swings, finer folds on them, drifting sideways.
            var fold = (Noise.Fractal((u * 2.2) + (drift * 0.05) + seed, 3) - 0.5) * 0.45;
            var hem = h * (baseline + fold);

            // How bright this part is: its own slow pattern, its part of the music, the beat waves.
            var glow = Noise.Fractal((u * 3.5) - (drift * 0.08) + seed + 50, 3);
            glow = Math.Max(0, (glow - 0.3) * 1.6);
            foreach (var wave in _waves)
            {
                var d = (u - (wave - 0.2)) / 0.12;
                glow += 0.9 * Math.Exp(-d * d) * (1.4 - wave);
            }

            var level = _levels[x];
            var intensity = glow * (0.35 + (1.4 * level)) * strength;
            if (intensity < 0.003)
            {
                return;
            }

            // Rays: the fine vertical striations along the curtain, flickering with the hi-hats.
            var ray = 0.55 + (0.45 * Noise.At((u * 140) + (drift * 0.6) + seed + (pulse.Hat.Count * 0.37)));
            var rise = h * height * (0.18 + (0.55 * level) + (0.2 * Noise.At((u * 9) + seed))) * (1 + (0.3 * pulse.Bass));
            Column(canvas, context, x, hem, rise, (float)(intensity * ray), depth);
        });
    }

    /// <summary>One column of a curtain: bright at the hem, fading upward and shifting from green to violet.</summary>
    private static void Column(Raster canvas, VisualContext context, int x, double hem, double rise, float intensity, double depth)
    {
        var top = Math.Max(0, (int)(hem - rise));
        var bottom = Math.Min(canvas.Height - 1, (int)(hem + 4));
        for (var y = top; y <= bottom; y++)
        {
            var up = (hem - y) / Math.Max(1, rise);
            var light = up < 0 ? Math.Exp(up * rise / 1.6) : Math.Exp(-up * 2.4) * (0.75 + (0.25 * Math.Exp(-up * 14)));
            var color = context.Paint(Math.Clamp((up * 0.85) + (depth * 0.15), 0, 1));
            var i = (y * canvas.Width) + x;
            var amount = (float)light * intensity;
            canvas.R[i] += color.R * amount;
            canvas.G[i] += color.G * amount;
            canvas.B[i] += color.B * amount;
        }
    }

    /// <summary>Black mountains along the bottom, in front of the sky.</summary>
    private static void Mountains(Raster canvas)
    {
        var (w, h) = (canvas.Width, canvas.Height);
        for (var x = 0; x < w; x++)
        {
            var u = (double)x / w;
            var ridge = h * (0.84 - (0.12 * Noise.Fractal((u * 3) + 7.1, 5)));
            for (var y = Math.Max(0, (int)ridge); y < h; y++)
            {
                // A pixel of soft edge, then the dark of the land.
                var cover = (float)Math.Clamp(y + 1 - ridge, 0, 1);
                var i = (y * w) + x;
                var keep = 1 - (cover * 0.97f);
                canvas.R[i] *= keep;
                canvas.G[i] *= keep;
                canvas.B[i] *= keep;
            }
        }
    }
}
