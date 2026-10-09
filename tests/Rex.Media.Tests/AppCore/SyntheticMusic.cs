using Rex.Media.Audio;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Music made for the visualisation tests: a kick on every beat, a snare on two and four, quiet
/// hi-hats between and a held chord, at a known tempo; optionally a quiet stretch (the chord alone)
/// and the drop back in after it. Every sample is known, so where its beats fall is known too.
/// </summary>
internal sealed class SyntheticMusic
{
    public const int Rate = 48_000;
    public const int Fps = 30;

    private readonly float[] _left;
    private readonly float[] _right;

    /// <param name="bpm">Beats a minute.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="quietFrom">Where the quiet stretch starts, or a negative number for none.</param>
    /// <param name="quietUntil">Where the drop comes back in.</param>
    /// <param name="silence">Nothing at all, for comparing.</param>
    public SyntheticMusic(double bpm, double seconds, double quietFrom = -1, double quietUntil = -1, bool silence = false)
    {
        Bpm = bpm;
        var count = (int)(seconds * Rate);
        (_left, _right) = (new float[count], new float[count]);
        if (silence)
        {
            return;
        }

        var random = new Random(7);
        var beat = 60 / bpm;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / Rate;
            var quiet = t >= quietFrom && t < quietUntil;
            var sinceBeat = t % beat;
            var beatIndex = (int)(t / beat);

            // The chord: three soft sines, a little apart in the two channels.
            var pad = 0.06 * (Math.Sin(Math.Tau * 220 * t) + Math.Sin(Math.Tau * 277.2 * t) + Math.Sin(Math.Tau * 329.6 * t));
            double drums = 0;
            if (!quiet)
            {
                // The kick: a falling sine, fast decay.
                var kickPitch = 50 + (90 * Math.Exp(-sinceBeat * 30));
                drums += 0.8 * Math.Sin(Math.Tau * kickPitch * sinceBeat) * Math.Exp(-sinceBeat * 12);
                if (beatIndex % 2 == 1)
                {
                    drums += 0.25 * ((random.NextDouble() * 2) - 1) * Math.Exp(-sinceBeat * 25);
                }

                var sinceHat = t % (beat / 2);
                drums += 0.03 * ((random.NextDouble() * 2) - 1) * Math.Exp(-sinceHat * 80);
            }

            _left[i] = (float)Math.Clamp(drums + pad, -1, 1);
            _right[i] = (float)Math.Clamp(drums + (pad * 0.8), -1, 1);
        }
    }

    public double Bpm { get; }

    public int Frames => _left.Length * Fps / Rate;

    /// <summary>The <see cref="SpectrumAnalyzer.Size"/> samples of each channel heard by frame <paramref name="frame"/>, silence before the start.</summary>
    public (float[] Left, float[] Right) At(int frame)
    {
        var end = (int)((long)frame * Rate / Fps);
        var (left, right) = (new float[SpectrumAnalyzer.Size], new float[SpectrumAnalyzer.Size]);
        for (var i = 0; i < SpectrumAnalyzer.Size; i++)
        {
            var at = end - SpectrumAnalyzer.Size + i;
            if (at >= 0 && at < _left.Length)
            {
                (left[i], right[i]) = (_left[at], _right[at]);
            }
        }

        return (left, right);
    }

    public static TimeSpan FrameTime => TimeSpan.FromSeconds(1.0 / Fps);
}
