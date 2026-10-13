namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The music laid out as it goes by (AU-18), so its shape can be read and not only felt. The last
/// few seconds scroll in from the right, where "now" is: underneath, the spectrum in time (the bass
/// at the bottom), so a bass line, a melody or a drop shows as a shape; over it, the beat clock's
/// grid, every fourth beat brighter; along the top, three lanes with each kick, snare and hi-hat
/// placed where it struck, as large as it was hard. The edge where now is flares with every kick.
/// </summary>
public sealed class GrooveScene : VisualScene
{
    /// <summary>How far across the stage now is: the rest, to the right, is where the music comes in.</summary>
    private const double Now = 0.88;

    /// <summary>The share of the stage the drum lanes take at the top.</summary>
    private const double Lanes = 0.24;

    private readonly List<(double At, int Lane, float Strength)> _hits = [];
    private readonly List<(double At, bool Bar, bool Filled)> _beats = [];
    private float[] _r = [];
    private float[] _g = [];
    private float[] _b = [];
    private int _width;
    private int _height;
    private double _owed;
    private double _clock;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var seconds = context.Number("seconds");
        var now = (int)(w * Now);
        var perSecond = now / seconds;
        _clock += context.Dt;
        if (w != _width || h != _height)
        {
            (_r, _g, _b, _width, _height, _owed) = (new float[w * h], new float[w * h], new float[w * h], w, h, 0);
        }

        // The spectrum in time: move what has gone by to the left, and paint the newest columns.
        _owed += perSecond * context.Dt;
        var shift = Math.Min(now, (int)_owed);
        _owed -= shift;
        var top = (int)(h * Lanes);
        if (shift > 0)
        {
            for (var y = top; y < h; y++)
            {
                var row = y * w;
                foreach (var plane in new[] { _r, _g, _b })
                {
                    Array.Copy(plane, row + shift, plane, row, now - shift);
                }

                var (red, green, blue) = Column(context, y, top, h);
                for (var x = now - shift; x < now; x++)
                {
                    (_r[row + x], _g[row + x], _b[row + x]) = (red, green, blue);
                }
            }
        }

        Array.Copy(_r, canvas.R, _r.Length);
        Array.Copy(_g, canvas.G, _g.Length);
        Array.Copy(_b, canvas.B, _b.Length);
        Remember(pulse, seconds);
        if (context.Toggle("grid"))
        {
            foreach (var (at, bar, filled) in _beats)
            {
                var x = now - ((_clock - at) * perSecond);
                canvas.Line(x, top, x, h, bar ? 2.5 : 1.2, context.Paint(bar ? 0.65 : 0.45), (bar ? 0.55f : 0.25f) * (filled ? 0.5f : 1), 2);
            }
        }

        if (context.Toggle("lanes"))
        {
            DrawLanes(context, now, perSecond, top);
        }

        // Now: a line that flares with the kick, and a glow the bass swells.
        var color = context.Paint(pulse.Pitch);
        canvas.Line(now, 0, now, h, 1.5 + (size * 0.02 * pulse.Punch), color, 0.3f + (1.3f * pulse.Punch), 3);
        canvas.Glow(now, h * 0.62, size * (0.12 + (0.2 * pulse.Bass) + (0.15 * pulse.Punch)), color, (0.05f + (0.5f * pulse.Punch)) * (pulse.Loudness > 0 ? 1 : 0));
        canvas.Bloom(0.45f, 0.6f, 4);
    }

    /// <summary>One row of the spectrum's newest column: the band at that height, lit as loud as it is, in its colour.</summary>
    private static (float Red, float Green, float Blue) Column(VisualContext context, int y, int top, int h)
    {
        if (!context.Toggle("spectrum"))
        {
            return (0, 0, 0);
        }

        var bands = context.Pulse.Bands;
        var up = (double)(h - 1 - y) / Math.Max(1, h - 1 - top);
        var at = up * (bands.Count - 1);
        var below = (int)at;
        var above = Math.Min(bands.Count - 1, below + 1);
        var level = bands[below] + ((bands[above] - bands[below]) * (float)(at - below));
        var light = level * level * 0.85f;
        var color = context.Paint(up);
        return (color.R * light, color.G * light, color.B * light);
    }

    /// <summary>Keeps the drum hits and beats heard, and lets go of those that have scrolled off the stage.</summary>
    private void Remember(MusicPulse pulse, double seconds)
    {
        foreach (var (onset, lane) in new[] { (pulse.Kick, 0), (pulse.Snare, 1), (pulse.Hat, 2) })
        {
            if (onset.Hit)
            {
                _hits.Add((_clock, lane, onset.Strength));
            }
        }

        if (pulse.Beat)
        {
            _beats.Add((_clock, pulse.Beats % 4 == 0, pulse.Filled));
        }

        var oldest = _clock - seconds - 0.5;
        _hits.RemoveAll(hit => hit.At < oldest);
        _beats.RemoveAll(beat => beat.At < oldest);
    }

    /// <summary>The kick, snare and hi-hat lanes: a faint guide each, and every hit a light where it struck, swelling for a moment as it lands.</summary>
    private void DrawLanes(VisualContext context, int now, double perSecond, int top)
    {
        var canvas = context.Canvas;
        var size = Math.Min(canvas.Width, canvas.Height);
        double[] heights = [0.75, 0.47, 0.2];
        double[] shares = [0.05, 0.5, 0.9];
        double[] sizes = [0.035, 0.026, 0.016];
        for (var lane = 0; lane < 3; lane++)
        {
            var y = top * heights[lane];
            canvas.Line(0, y, now, y, 1, context.Paint(shares[lane]), 0.12f);
        }

        foreach (var (at, lane, strength) in _hits)
        {
            var age = _clock - at;
            var (x, y) = (now - (age * perSecond), top * heights[lane]);
            var landing = (float)Math.Max(0, 1 - (age / 0.2));
            var radius = size * sizes[lane] * (0.45 + (0.55 * strength)) * (1 + landing);
            var color = context.Paint(shares[lane]);
            canvas.Glow(x, y, radius * 2.2, color, (0.35f * strength) + landing);
            canvas.Disc(x, y, radius * 0.55, color, 0.6f + (0.4f * strength));
        }
    }
}
