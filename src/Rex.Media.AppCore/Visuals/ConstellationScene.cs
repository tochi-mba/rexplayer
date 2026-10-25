namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The melody drawn in the stars (AU-18): a sky of stars, low notes to the left and high to the
/// right; each beat lights the star the pitch has reached and draws a line to it from the last, so
/// the tune traces a constellation that slowly fades as new lines are drawn. The chosen star flares
/// as hard as the beat; the others twinkle with the hi-hats; a nebula behind glows with the bass.
/// </summary>
public sealed class ConstellationScene : VisualScene
{
    private readonly List<(int From, int To, double Age)> _links = [];
    private (double X, double Y, double Size)[] _stars = [];
    private int _last = -1;
    private float _flare;
    private float _twinkle;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var count = (int)context.Number("stars");
        var memory = context.Number("memory");
        if (_stars.Length != count)
        {
            // Stars by height of note: placed left to right, at heights the hash scatters, as shares
            // of the stage, so they stay put when it is resized.
            _stars = [.. Enumerable.Range(0, count).Select(n => (0.05 + (0.9 * (n + Noise.Hash(n * 3)) / count), 0.1 + (0.8 * Noise.Hash((n * 3) + 1)), Noise.Hash((n * 3) + 2)))];
            _links.Clear();
            _last = -1;
        }

        if (pulse.Beat)
        {
            // The star nearest the pitch, high or low as the treble is against the bass.
            var target = (pulse.Pitch * 0.9) + 0.05;
            var lift = 0.85 - (0.7 * Math.Clamp(0.5 + pulse.Treble - pulse.Bass, 0, 1));
            var best = 0;
            for (var s = 1; s < count; s++)
            {
                if (Distance(_stars[s], target, lift) < Distance(_stars[best], target, lift))
                {
                    best = s;
                }
            }

            if (_last >= 0 && _last != best)
            {
                _links.Add((_last, best, 0));
            }

            (_last, _flare) = (best, Math.Max(0.5f, pulse.BeatStrength));
        }

        _flare = Math.Max(0, _flare - (float)(context.Dt * 1.5));
        _twinkle = pulse.Hat.Hit ? 1 : Math.Max(0, _twinkle - (float)(context.Dt * 6));
        canvas.Feedback(0.5f, 1, 0, 0, 0);
        canvas.Glow(w * (0.3 + (0.4 * pulse.Pitch)), h * 0.5, size * (0.6 + (0.3 * pulse.Bass)), context.Paint(0.3), 0.04f + (0.2f * pulse.Bass));

        for (var l = 0; l < _links.Count; l++)
        {
            var (from, to, age) = _links[l];
            _links[l] = (from, to, age + context.Dt);
            var light = (float)Math.Max(0, 1 - (age / memory));
            var ((x0, y0, _), (x1, y1, _)) = (_stars[from], _stars[to]);
            canvas.Line(x0 * w, y0 * h, x1 * w, y1 * h, Math.Max(1.3, size * 0.003), context.Paint(x1), light * 0.7f, 2);
        }

        _links.RemoveAll(link => link.Age > memory);
        for (var s = 0; s < count; s++)
        {
            var (x, y, star) = _stars[s];
            (x, y) = (x * w, y * h);
            var light = (float)(0.25 + (0.4 * star) + (_twinkle * 0.5 * Noise.Hash(s + (pulse.Hat.Count * 101)))) + (s == _last ? _flare * 2 : 0);
            canvas.Glow(x, y, size * (0.008 + (0.01 * star) + (s == _last ? 0.04 * _flare : 0)), context.Paint(x / w), light);
        }

        canvas.Bloom(0.4f, 0.8f, 4);
    }

    /// <summary>How far a star is from where the music points, the pitch counting most.</summary>
    private static double Distance((double X, double Y, double Size) star, double x, double y) =>
        Math.Abs(star.X - x) + (Math.Abs(star.Y - y) * 0.15);
}
