namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A lava lamp the music heats (AU-18): soft blobs, each carrying its own part of the spectrum
/// (the bass the biggest, low down; the treble small, high up), drift and swell as their part is
/// loud; where they meet they melt together. Every kick draws them all toward the middle, so the
/// lamp gathers into one on the beat and drifts apart between.
/// </summary>
public sealed class LavaScene : VisualScene
{
    private double _time;
    private float _gather;
    private (double X, double Y, double R, float Share)[] _blobs = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var count = (int)context.Number("blobs");
        _time += context.Dt * (0.15 + (0.6 * pulse.Loudness));
        _gather = context.Toggle("merge") ? Math.Max(pulse.Punch, _gather - (float)(context.Dt * 2.5)) : 0;
        if (_blobs.Length != count)
        {
            _blobs = new (double, double, double, float)[count];
        }

        for (var b = 0; b < count; b++)
        {
            var share = count == 1 ? 0.5 : (double)b / (count - 1);
            var band = Level(pulse, share);
            var wander = (Noise.Fractal(_time + (b * 17.3), 3) - 0.5) * 1.6;
            var rise = (Noise.Fractal((_time * 0.7) + (b * 5.1) + 40, 3) - 0.5) * 1.2;
            var x = (w / 2.0) + (wander * w * 0.4 * (1 - _gather));
            var y = (h * (0.75 - (0.5 * share))) + (rise * h * 0.3 * (1 - _gather)) - ((h * (0.75 - (0.5 * share)) - (h / 2.0)) * _gather);
            _blobs[b] = (x, y, size * (0.04 + (0.09 * band) + (0.02 * (1 - share))), (float)share);
        }

        var glow = (float)context.Number("glow");
        var blobs = _blobs;
        Raster.Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                // The field: each blob's pull, falling with the square of the distance.
                double field = 0, weight = 0, share = 0;
                foreach (var (bx, by, br, bs) in blobs)
                {
                    var (dx, dy) = (x - bx, y - by);
                    var pull = br * br / ((dx * dx) + (dy * dy) + 1);
                    field += pull;
                    weight += pull * pull;
                    share += pull * pull * bs;
                }

                // Inside where the field passes one, with a soft lit edge; a faint glow outside.
                var inside = (float)Math.Clamp((field - 0.8) * 2.5, 0, 1);
                var halo = (float)Math.Min(1, field) * glow * 0.25f;
                var color = context.Paint(share / weight).Times((inside * 0.85f) + halo);
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.6f, 0.5f * glow, 5);
    }

    /// <summary>The level of the spectrum <paramref name="share"/> of the way up it, over a few bands.</summary>
    private static float Level(MusicPulse pulse, double share)
    {
        var at = (int)(share * (pulse.Bands.Count - 4));
        return Math.Max(Math.Max(pulse.Bands[at], pulse.Bands[at + 1]), Math.Max(pulse.Bands[at + 2], pulse.Bands[at + 3]));
    }
}
