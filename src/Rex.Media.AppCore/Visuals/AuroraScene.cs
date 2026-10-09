namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The northern lights (AU-18): curtains of light that drift and fold across a night sky, each
/// rising higher where its part of the spectrum is loud, bright at the hem and fading upward, with
/// stars that twinkle in the treble.
/// </summary>
public sealed class AuroraScene : VisualScene
{
    private const int StarCount = 140;

    private (double X, double Y, double Phase)[]? _stars;
    private float[] _levels = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var t = context.Seconds;
        var pulse = context.Pulse;

        // The sky: deep blue overhead to black at the horizon.
        for (var y = 0; y < h; y++)
        {
            var sky = (float)(1 - ((double)y / h));
            Array.Fill(canvas.R, 0.004f * sky, y * w, w);
            Array.Fill(canvas.G, 0.012f * sky, y * w, w);
            Array.Fill(canvas.B, 0.05f * sky, y * w, w);
        }

        _stars ??= [.. Enumerable.Range(0, StarCount).Select(_ => (context.Random.NextDouble(), context.Random.NextDouble() * 0.7, context.Random.NextDouble() * Math.Tau))];
        foreach (var (x, y, phase) in _stars)
        {
            var twinkle = 0.25 + (0.35 * Math.Sin((t * 2.3) + phase)) + (pulse.Treble * 0.8);
            canvas.Glow(x * w, y * h, 1.6, Rgb.White, (float)Math.Max(0.05, twinkle) * 0.5f);
        }

        // The spectrum spread across the width, smoothed so the curtains move like cloth.
        if (_levels.Length != w)
        {
            _levels = new float[w];
        }

        for (var x = 0; x < w; x++)
        {
            var band = pulse.Bands[Math.Min(pulse.Bands.Count - 1, x * pulse.Bands.Count / w)];
            _levels[x] += (band - _levels[x]) * (float)Math.Min(1, context.Dt * 6);
        }

        var curtains = (int)context.Number("curtains");
        var height = context.Number("height");
        for (var layer = 0; layer < curtains; layer++)
        {
            var phase = layer * 2.1;
            for (var x = 0; x < w; x++)
            {
                var u = (double)x / w;
                var hem = h * (0.62 + (0.1 * Math.Sin((u * Math.Tau * 1.3) + (t * 0.21 * (layer + 1)) + phase))
                    + (0.05 * Math.Sin((u * Math.Tau * 3.7) - (t * 0.37) + phase)) - (layer * 0.06));
                var rise = h * height * (0.12 + (0.75 * _levels[x])) * (1 + (0.25 * pulse.Bass));
                var share = (layer / (double)Math.Max(1, curtains)) * 0.4;
                Curtain(canvas, context, x, hem, rise, share, (float)(0.35 + (0.4 * _levels[x])));
            }
        }

        canvas.Bloom(0.3f, 0.75f, 6);
    }

    /// <summary>One column of a curtain: a bright hem at <paramref name="hem"/>, its light fading over <paramref name="rise"/> above it.</summary>
    private static void Curtain(Raster canvas, VisualContext context, int x, double hem, double rise, double share, float strength)
    {
        var top = Math.Max(0, (int)(hem - rise));
        var bottom = Math.Min(canvas.Height - 1, (int)hem + 3);
        var low = context.Paint(share);
        var high = context.Paint(share + 0.55);
        for (var y = top; y <= bottom; y++)
        {
            var up = (hem - y) / Math.Max(1, rise);
            var light = up < 0 ? Math.Max(0, 1 + (up * rise / 3)) : Math.Pow(1 - up, 1.6);
            var color = low.Toward(high, (float)Math.Clamp(up, 0, 1));
            var i = (y * canvas.Width) + x;
            var amount = (float)light * strength;
            canvas.R[i] += color.R * amount;
            canvas.G[i] += color.G * amount;
            canvas.B[i] += color.B * amount;
        }
    }
}

