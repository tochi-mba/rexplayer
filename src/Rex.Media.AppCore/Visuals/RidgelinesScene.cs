namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The sound as a range of ridges (AU-18): every moment a new line of the spectrum is laid down at
/// the front, swelling in the middle where the music is, and the older ones climb away up the
/// stage, each hidden where a nearer one rises in front of it, as a mountain range is. The kick lifts
/// the newest ridge; the colours run from the front to the back.
/// </summary>
public sealed class RidgelinesScene : VisualScene
{
    private const int Points = 96;

    private readonly List<float[]> _ridges = [];
    private readonly List<(double, double)> _line = [];
    private double _owed;
    private float[] _horizon = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var lines = (int)context.Number("lines");
        var height = context.Number("height");

        // A new ridge every so often: as many a second as fit the stage in about four seconds.
        _owed += context.Dt * lines / 4;
        while (_owed >= 1)
        {
            _owed--;
            _ridges.Insert(0, Profile(pulse));
        }

        if (_ridges.Count == 0)
        {
            _ridges.Add(Profile(pulse));
        }

        while (_ridges.Count > lines)
        {
            _ridges.RemoveAt(_ridges.Count - 1);
        }

        // The front ridge rides the music now: the kick lifts it.
        var front = Profile(pulse);
        var lift = 1 + (0.8 * pulse.Punch);
        canvas.Clear(Rgb.Black);
        if (_horizon.Length != w)
        {
            _horizon = new float[w];
        }

        Array.Fill(_horizon, h);
        var gap = h * 0.8 / lines;
        for (var r = 0; r < _ridges.Count; r++)
        {
            var profile = r == 0 ? front : _ridges[r];
            var baseline = (h * 0.92) - (r * gap);
            var amplitude = h * 0.22 * height * (r == 0 ? lift : 1);
            var color = context.Paint((double)r / lines);
            var fade = (float)(1 - (0.75 * r / lines));
            _line.Clear();
            for (var x = 0; x < w; x++)
            {
                var at = (double)x / Math.Max(1, w - 1) * (Points - 1);
                var below = (int)at;
                var value = profile[below] + ((profile[Math.Min(Points - 1, below + 1)] - profile[below]) * (float)(at - below));
                var y = (float)(baseline - (value * amplitude));

                // Only what rises above every nearer ridge shows.
                if (y < _horizon[x])
                {
                    _horizon[x] = y;
                    _line.Add((x, y));
                }
                else if (_line.Count > 0)
                {
                    Stroke(canvas, color, fade);
                }
            }

            Stroke(canvas, color, fade);
        }

        canvas.Bloom(0.6f, 0.4f, 3);
    }

    private void Stroke(Raster canvas, Rgb color, float fade)
    {
        if (_line.Count > 1)
        {
            canvas.Path(_line, 1.6, color, fade, 1.5);
        }

        _line.Clear();
    }

    /// <summary>The spectrum across the ridge, mirrored, and swelling toward the middle as the famous plots do.</summary>
    private static float[] Profile(MusicPulse pulse)
    {
        var profile = new float[Points];
        for (var p = 0; p < Points; p++)
        {
            var across = (double)p / (Points - 1);
            var from = Math.Abs((across * 2) - 1);
            var band = pulse.Bands[(int)Math.Min(pulse.Bands.Count - 1, from * (pulse.Bands.Count - 1))];
            var swell = Math.Exp(-Math.Pow((across - 0.5) / 0.22, 2));
            var texture = 0.25 + (0.75 * Noise.At((p * 0.37) + (pulse.Beats * 3.1)));
            profile[p] = (float)(band * (0.15 + (0.85 * swell)) * texture);
        }

        return profile;
    }
}
