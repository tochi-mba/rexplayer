namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A colour strobe (AU-18): light that flashes on the beat in a pattern (the whole stage, halves
/// that trade places, bars that step along, rays from the middle, or a chequerboard that swaps),
/// coloured by the pitch or stepping through the palette beat by beat, fading between flashes to a
/// glow that follows the loudness. It never flashes more often than its setting allows (three a
/// second at first, the limit guidelines give for people with photosensitive epilepsy).
/// </summary>
public sealed class StrobeScene : VisualScene
{
    private double _flash;
    private double _sinceFlash = 10;
    private long _flashes;

    /// <summary>How many flashes the scene has made: what its tests count.</summary>
    public long Flashes => _flashes;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var dt = context.Dt;
        _sinceFlash += dt;
        _flash = Math.Max(0, _flash - (dt / context.Number("fade")));
        if (pulse.Beat && _sinceFlash >= 1 / context.Number("flashes"))
        {
            _flash = 1;
            _sinceFlash = 0;
            _flashes++;
        }

        var glow = pulse.Loudness * context.Number("glow");
        var level = (float)Math.Max(_flash, glow);
        var share = context.Pick("step") == 1 ? (_flashes * 0.137) % 1 : pulse.Pitch;
        var color = context.Paint(share).Times(level);
        var other = context.Paint((share + 0.5) % 1).Times(level);
        var (w, h) = (canvas.Width, canvas.Height);
        canvas.Clear(Rgb.Black);
        switch (context.Pick("pattern"))
        {
            case 1:
                // Halves that trade places on each flash.
                var left = _flashes % 2 == 0;
                canvas.Fill(left ? 0 : w / 2, 0, w - (w / 2), h, color);
                canvas.Fill(left ? w / 2 : 0, 0, w - (w / 2), h, other, 0.25f);
                break;
            case 2:
                // Bars, one lit brightly and stepping along each flash.
                const int Bars = 6;
                for (var bar = 0; bar < Bars; bar++)
                {
                    var lit = bar == (int)(_flashes % Bars);
                    canvas.Fill(bar * w / Bars, 0, (w / Bars) - 2, h, lit ? color : other, lit ? 1 : 0.15f);
                }

                break;
            case 3:
                // Rays from the middle, turning a step each flash.
                var size = Math.Max(w, h);
                const int Rays = 12;
                for (var ray = 0; ray < Rays; ray++)
                {
                    var angle = ((double)ray / Rays * Math.Tau) + (_flashes * 0.2);
                    canvas.Line(w / 2.0, h / 2.0, (w / 2.0) + (Math.Cos(angle) * size), (h / 2.0) + (Math.Sin(angle) * size), size * 0.06, ray % 2 == 0 ? color : other, 1, 1.5);
                }

                break;
            case 4:
                // A chequerboard that swaps on each flash.
                var cell = Math.Max(1, h / 4);
                for (var row = 0; row * cell < h; row++)
                {
                    for (var column = 0; column * cell < w; column++)
                    {
                        var on = (row + column + _flashes) % 2 == 0;
                        canvas.Fill(column * cell, row * cell, cell - 1, cell - 1, on ? color : other, on ? 1 : 0.12f);
                    }
                }

                break;
            default:
                canvas.Fill(0, 0, w, h, color);
                break;
        }

        canvas.Bloom(0.6f, 0.4f, 6);
    }
}

