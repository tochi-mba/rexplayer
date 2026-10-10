namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A woven soundscape: each of the spectrum's 64 bands bends a glowing filament. The low end
/// rolls the whole field, midrange shapes the folds, treble picks out fine ridges, and drum onsets
/// launch concentric pressure waves. Unlike Embers, this is a continuous, spectrally mapped field,
/// not independent particles. All motion comes from local audio analysis and elapsed time.
/// </summary>
public sealed class ResonanceScene : VisualScene
{
    private readonly List<(double Radius, double X, double Y, float Strength)> _waves = [];
    private double _phase;
    private float _surge;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var size = Math.Min(w, h);
        var depth = context.Number("depth");
        var flow = context.Number("flow");
        var density = (int)context.Number("density");
        var contrast = context.Number("contrast");
        var trails = context.Number("trails");
        var mirror = context.Toggle("mirror");
        var rings = context.Toggle("rings");
        var dt = Math.Clamp(context.Dt, 0, 0.25);

        // The slowly drifting phase only accelerates with music, so silence doesn't resemble a
        // song. The faster onset envelopes govern sharp movement independently of the phase.
        _phase += dt * flow * (0.15 + (1.5 * pulse.Loudness));
        _surge = pulse.Drop ? 1 : Math.Max(0, _surge - (float)(dt * 1.6));
        canvas.Feedback((float)(0.10 + (trails * 0.72)), 1.002, 0, 0, 0);

        // A dim, spatially broad bed keeps the artwork legible in quiet sections without burning
        // out the highlights on loud material. The centre follows the dominant frequency.
        var focusX = w * (0.22 + (0.56 * pulse.Pitch));
        var focusY = h * (0.52 - (0.10 * pulse.Mid));
        var bed = (float)(0.035 + (0.28 * pulse.Loudness) + (0.12 * _surge));
        canvas.Glow(focusX, focusY, size * 0.87, context.Paint(0.54), bed);
        canvas.Glow(w * 0.5, h * 0.75, size * (0.42 + (0.18 * pulse.Bass)),
            context.Paint(0.10), (float)(0.055 + (0.35 * pulse.Bass)));

        // The same logarithmically distributed band positions are used on every filament. Smooth
        // neighbours suppress individual-bin flicker but preserve distinct tonal peaks.
        var samples = Math.Clamp(w / 7, 28, 132);
        for (var lane = 0; lane < density; lane++)
        {
            var share = density == 1 ? 0.5 : lane / (double)(density - 1);
            var baseY = h * (0.16 + (share * 0.7));
            var bandDepth = depth * h * (0.055 + (share * 0.025));
            var hue = (share * 0.72 + (pulse.Pitch * 0.28) + (_phase * 0.025)) % 1;
            var color = context.Paint(hue);
            var light = (float)(contrast * (0.36 + (pulse.Loudness * 0.65) + (pulse.Bass * 0.2)));
            var thickness = Math.Clamp(size * (0.0035 + (0.0025 * pulse.Mid)) * (1.15 - (share * 0.3)), 1, 4);
            var previous = Point(0, lane, samples, baseY, bandDepth, share, mirror, pulse);
            for (var x = 1; x <= samples; x++)
            {
                var current = Point(x, lane, samples, baseY, bandDepth, share, mirror, pulse);
                canvas.Line(previous.X, previous.Y, current.X, current.Y, thickness, color, light, 1.5);
                previous = current;
            }
        }

        if (rings)
        {
            if (pulse.Kick.Hit || pulse.Snare.Hit)
            {
                _waves.Add((size * 0.06, focusX, focusY, Math.Clamp(pulse.Kick.Strength + (pulse.Snare.Strength * 0.5f), 0.2f, 1f)));
                if (_waves.Count > 5)
                {
                    _waves.RemoveAt(0);
                }
            }

            for (var i = _waves.Count - 1; i >= 0; i--)
            {
                var wave = _waves[i];
                var radius = wave.Radius + (dt * size * (0.38 + (pulse.Bass * 0.45)));
                var strength = wave.Strength * (float)Math.Exp(-dt * 1.4);
                if (radius > size * 0.85 || strength < 0.06f)
                {
                    _waves.RemoveAt(i);
                    continue;
                }

                _waves[i] = (radius, wave.X, wave.Y, strength);
                canvas.Ring(wave.X, wave.Y, radius, Math.Max(1, size * 0.006),
                    context.Paint((pulse.Pitch + 0.25) % 1), strength * (float)contrast);
            }
        }
        else
        {
            _waves.Clear();
        }

        if (_surge > 0)
        {
            canvas.Glow(focusX, focusY, size * 0.4, context.Paint(pulse.Pitch),
                _surge * (float)contrast * 0.22f);
        }

        canvas.Bloom(0.42f, 0.54f, 3);
    }

    private (double X, double Y) Point(int position, int lane, int samples,
        double baseY, double depth, double share, bool mirror, MusicPulse pulse)
    {
        var x = position / (double)samples;
        var frequency = mirror ? Math.Abs(2 * x - 1) : x;
        var at = Math.Clamp((int)(Math.Pow(frequency, 0.8) * (pulse.Bands.Count - 1)), 0, pulse.Bands.Count - 1);
        var a = pulse.Bands[Math.Max(0, at - 1)];
        var b = pulse.Bands[at];
        var c = pulse.Bands[Math.Min(pulse.Bands.Count - 1, at + 1)];
        var energy = (a + (b * 2) + c) * 0.25;
        var wave = Math.Sin((x * (3.5 + share * 3.0) * Math.Tau) - _phase * (0.9 + share * 0.4) + lane * 0.75);
        var sub = Math.Cos((x * 1.2 * Math.Tau) + _phase * 0.33 + share * 2);
        var bass = pulse.Bass * sub * (1 - share) * 1.6;
        var treble = pulse.Treble * wave * share * 0.48;
        var push = (energy * (0.42 + (share * 1.35))) * wave;
        var kick = pulse.Kick.Level * (1 - share) * Math.Sin((x * 2 + _phase * 0.1) * Math.Tau);
        var snare = pulse.Snare.Level * share * Math.Sin((x * 5 + _phase) * Math.Tau);
        var y = baseY - (depth * (bass + treble + push + (kick * 0.5) + (snare * 0.5)));
        return (x, y);
    }
}
