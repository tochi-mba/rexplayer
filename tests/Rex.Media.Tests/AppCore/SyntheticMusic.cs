using Rex.Media.AppCore.Visuals;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Music made for the visualisation tests: a kick on every beat, a snare on two and four, quiet
/// hi-hats between and a held chord, at a known tempo; optionally a quiet stretch (the chord alone)
/// and the drop back in after it, a quieter or louder master, a busy groove (a shaker on every
/// sixteenth, a log drum on the off-beats and a held sub bass under it all), kicks left out, a change
/// of tempo, or a single held bass note and nothing else. Every sample is known, so where its beats
/// fall is known too (<see cref="Kicks"/>).
/// </summary>
internal sealed class SyntheticMusic
{
    public const int Rate = 48_000;
    public const int Fps = 30;

    private readonly float[] _left;
    private readonly float[] _right;
    private readonly List<double> _kicks = [];

    /// <param name="bpm">Beats a minute.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="quietFrom">Where the quiet stretch starts, or a negative number for none.</param>
    /// <param name="quietUntil">Where the drop comes back in.</param>
    /// <param name="silence">Nothing at all, for comparing.</param>
    /// <param name="gainDb">The master's level against the usual, in decibels.</param>
    /// <param name="busy">A shaker on every sixteenth, a log drum on the off-beats and a held sub bass.</param>
    /// <param name="missing">Beats (counting from 0) whose kick is left out; the rest of the beat plays.</param>
    /// <param name="changeAt">Where the tempo changes to <paramref name="newBpm"/>, or a negative number for never.</param>
    /// <param name="newBpm">The tempo after the change.</param>
    /// <param name="tone">A held 55 Hz note and nothing else.</param>
    /// <param name="fps">Frames a second it is heard at, as the window draws: thirty, or sixty when the stage draws quickly.</param>
    /// <param name="humanize">How far, in seconds, each beat after the first may fall early or late, as a drummer plays.</param>
    public SyntheticMusic(double bpm, double seconds, double quietFrom = -1, double quietUntil = -1, bool silence = false,
        double gainDb = 0, bool busy = false, int[]? missing = null, double changeAt = -1, double newBpm = 0, bool tone = false, int fps = Fps,
        double humanize = 0)
    {
        Bpm = bpm;
        FramesPerSecond = fps;
        var count = (int)(seconds * Rate);
        (_left, _right) = (new float[count], new float[count]);
        if (silence)
        {
            return;
        }

        var gain = Math.Pow(10, gainDb / 20);
        if (tone)
        {
            for (var i = 0; i < count; i++)
            {
                _left[i] = _right[i] = (float)(gain * 0.5 * Math.Sin(Math.Tau * 55 * i / Rate));
            }

            return;
        }

        // Where every beat starts, the tempo changing part way when asked.
        var beats = new List<double>();
        for (var t = 0.0; t < seconds; t += 60 / (changeAt >= 0 && t >= changeAt ? newBpm : bpm))
        {
            beats.Add(t);
        }

        var played = new Random(11);
        for (var b = 1; b < beats.Count; b++)
        {
            beats[b] += humanize * ((played.NextDouble() * 2) - 1);
        }

        var random = new Random(7);
        var beatIndex = 0;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / Rate;
            while (beatIndex + 1 < beats.Count && beats[beatIndex + 1] <= t)
            {
                beatIndex++;
            }

            var beat = (beatIndex + 1 < beats.Count ? beats[beatIndex + 1] : beats[beatIndex] + (60 / bpm)) - beats[beatIndex];
            var quiet = t >= quietFrom && t < quietUntil;
            var sinceBeat = t - beats[beatIndex];

            // The chord: three soft sines, a little apart in the two channels.
            var pad = 0.06 * (Math.Sin(Math.Tau * 220 * t) + Math.Sin(Math.Tau * 277.2 * t) + Math.Sin(Math.Tau * 329.6 * t));
            double drums = 0;
            if (!quiet)
            {
                // The kick: a falling sine, fast decay, hardest on the first beat of each bar.
                if (missing is null || !missing.Contains(beatIndex))
                {
                    var kickPitch = 50 + (90 * Math.Exp(-sinceBeat * 30));
                    var accent = beatIndex % 4 == 0 ? 0.85 : 0.5;
                    drums += accent * Math.Sin(Math.Tau * kickPitch * sinceBeat) * Math.Exp(-sinceBeat * 12);
                }

                if (beatIndex % 2 == 1)
                {
                    drums += 0.25 * ((random.NextDouble() * 2) - 1) * Math.Exp(-sinceBeat * 25);
                }

                var sinceHat = sinceBeat % (beat / 2);
                drums += 0.03 * ((random.NextDouble() * 2) - 1) * Math.Exp(-sinceHat * 80);
                if (busy)
                {
                    // A shaker every sixteenth, a log drum (a pitched knock) on each off-beat.
                    var sinceShake = sinceBeat % (beat / 4);
                    drums += 0.08 * ((random.NextDouble() * 2) - 1) * Math.Exp(-sinceShake * 60);
                    var sinceLog = sinceBeat - (beat / 2);
                    if (sinceLog >= 0)
                    {
                        drums += 0.3 * Math.Sin(Math.Tau * 110 * sinceLog) * Math.Exp(-sinceLog * 9);
                    }
                }
            }

            // The held sub bass of the busy groove carries on through the quiet stretch.
            var sub = busy ? 0.25 * Math.Sin(Math.Tau * 45 * t) : 0;
            _left[i] = (float)Math.Clamp(gain * (drums + pad + sub), -1, 1);
            _right[i] = (float)Math.Clamp(gain * (drums + (pad * 0.8) + sub), -1, 1);
        }

        for (var b = 0; b < beats.Count; b++)
        {
            var quiet = beats[b] >= quietFrom && beats[b] < quietUntil;
            if (!quiet && (missing is null || !missing.Contains(b)))
            {
                _kicks.Add(beats[b]);
            }
        }
    }

    public double Bpm { get; }

    /// <summary>Where every kick lands, in seconds.</summary>
    public IReadOnlyList<double> Kicks => _kicks;

    /// <summary>Frames a second this music is heard at.</summary>
    public int FramesPerSecond { get; }

    /// <summary>The time between two of this music's frames.</summary>
    public TimeSpan Step => TimeSpan.FromSeconds(1.0 / FramesPerSecond);

    public int Frames => (int)((long)_left.Length * FramesPerSecond / Rate);

    /// <summary>The <see cref="MusicPulse.AnalysisSize"/> samples of each channel heard by frame <paramref name="frame"/>, silence before the start.</summary>
    public (float[] Left, float[] Right) At(int frame)
    {
        var end = (int)((long)frame * Rate / FramesPerSecond);
        var (left, right) = (new float[MusicPulse.AnalysisSize], new float[MusicPulse.AnalysisSize]);
        for (var i = 0; i < left.Length; i++)
        {
            var at = end - left.Length + i;
            if (at >= 0 && at < _left.Length)
            {
                (left[i], right[i]) = (_left[at], _right[at]);
            }
        }

        return (left, right);
    }

    /// <summary>The newest mono samples heard by frame <paramref name="frame"/>.</summary>
    public float[] Mono(int frame)
    {
        var (left, right) = At(frame);
        return [.. left.Zip(right, (l, r) => (l + r) / 2)];
    }

    public static TimeSpan FrameTime => TimeSpan.FromSeconds(1.0 / Fps);

    /// <summary>The music as a 16-bit stereo WAV file.</summary>
    public byte[] Wav() => WavBuilder.Pcm(Rate, 2, 16, Pcm.Int16(_left, _right)).Build();
}
