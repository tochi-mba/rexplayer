namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A mandala that blooms with the music (AU-18): rings of petals round a bright heart, the inner
/// ring opened by the bass, the middle by the middle of the spectrum, the outer by the treble; the
/// whole flower turns a step on each beat, each ring the other way from the one inside it, and its
/// heart pulses with the kick. With each bar it can change how many petals it has.
/// </summary>
public sealed class MandalaScene : VisualScene
{
    private readonly List<(double, double)> _petal = [];
    private double _turn;
    private double _turnTo;
    private float[] _open = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var (cx, cy) = (w / 2.0, h / 2.0);
        var layers = (int)context.Number("layers");
        var petals = (int)context.Number("petals") + (context.Toggle("step") ? (int)(pulse.Beats / 4 % 3) * 2 : 0);
        if (_open.Length != layers)
        {
            _open = new float[layers];
        }

        _turnTo += pulse.Beat ? Math.PI / petals : 0;
        _turn += (_turnTo - _turn) * (1 - Math.Exp(-context.Dt / 0.15));
        canvas.Feedback(0.35f, 1.0 + (0.01 * pulse.Punch), 0, 0, 0);

        for (var layer = layers - 1; layer >= 0; layer--)
        {
            var share = layers == 1 ? 0.5 : (double)layer / (layers - 1);
            var band = pulse.Bands[(int)(share * (pulse.Bands.Count - 1))];
            _open[layer] = Math.Max(band, _open[layer] - (float)(context.Dt * 2));
            var inner = size * (0.06 + (0.08 * layer));
            var length = size * (0.06 + (0.16 * _open[layer]));
            var spin = (_turn * (layer % 2 == 0 ? 1 : -1)) + (layer * Math.PI / petals);
            var color = context.Paint(share);
            for (var p = 0; p < petals; p++)
            {
                Petal(cx, cy, spin + (p * Math.Tau / petals), inner, length, size * (0.25 / petals) * (1 + (layer * 0.4)));
                canvas.Path(_petal, Math.Max(1, size * 0.004), color, 0.5f + _open[layer], 1.5, closed: true);
            }
        }

        canvas.Glow(cx, cy, size * (0.05 + (0.06 * pulse.Punch)), context.Paint(pulse.Pitch), 0.8f + (1.4f * pulse.Punch));
        canvas.Bloom(0.4f, 0.8f, 5);
    }

    /// <summary>A petal: a pointed oval from <paramref name="inner"/> out along <paramref name="angle"/>.</summary>
    private void Petal(double cx, double cy, double angle, double inner, double length, double width)
    {
        _petal.Clear();
        var (ux, uy) = (Math.Cos(angle), Math.Sin(angle));
        for (var k = 0; k <= 16; k++)
        {
            var t = k / 16.0;
            var across = Math.Sin(t * Math.PI) * width;
            var along = inner + (t * length);
            _petal.Add((cx + (ux * along) - (uy * across), cy + (uy * along) + (ux * across)));
        }

        for (var k = 15; k > 0; k--)
        {
            var t = k / 16.0;
            var across = -Math.Sin(t * Math.PI) * width;
            var along = inner + (t * length);
            _petal.Add((cx + (ux * along) - (uy * across), cy + (uy * along) + (ux * across)));
        }
    }
}
