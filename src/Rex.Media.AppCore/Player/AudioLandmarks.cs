using Rex.Media.Audio;

namespace Rex.Media.AppCore.Player;

/// <summary>A pair of prominent spectral peaks, anchored at a 32 ms analysis hop.</summary>
public readonly record struct AudioLandmark(uint Hash, int Frame);

/// <summary>
/// Time-frequency constellation fingerprints based on Wang, ISMIR 2003, sections 2.1–2.3.
/// Accepts mono PCM at 8 kHz; resampling and channel mixing belong at the decoder boundary.
/// </summary>
public static class AudioLandmarks
{
    public const int Rate = 8000;
    public const int Window = 1024;
    public const int Hop = 256;
    public const double SecondsPerFrame = Hop / (double)Rate;

    /// <summary>Extracts up to four target pairs from each audible time-frequency anchor.</summary>
    public static IReadOnlyList<AudioLandmark> Fingerprint(ReadOnlySpan<float> mono, int frameOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameOffset);
        if (mono.Length < Window)
        {
            return [];
        }

        var frames = 1 + (mono.Length - Window) / Hop;
        var peaks = new List<(int Time, int Frequency, float Energy)>(frames * 2);
        var real = new float[Window];
        var imaginary = new float[Window];
        for (var frame = 0; frame < frames; frame++)
        {
            var slice = mono.Slice(frame * Hop, Window);
            for (var i = 0; i < Window; i++)
            {
                var sample = float.IsFinite(slice[i]) ? slice[i] : 0;
                real[i] = sample * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Window));
            }

            Array.Clear(imaginary);
            Fft.Transform(real, imaginary);
            var candidates = new List<(int Frequency, float Energy)>();
            // Avoid speech DC drift and upper Nyquist noise; peaks must exceed a modest floor.
            for (var k = 7; k < 440; k++)
            {
                var energy = real[k] * real[k] + imaginary[k] * imaginary[k];
                var previous = real[k - 1] * real[k - 1] + imaginary[k - 1] * imaginary[k - 1];
                var next = real[k + 1] * real[k + 1] + imaginary[k + 1] * imaginary[k + 1];
                if (energy > 0.01f && energy > previous && energy >= next)
                {
                    candidates.Add((k, energy));
                }
            }

            foreach (var peak in candidates.OrderByDescending(item => item.Energy).Take(2))
            {
                peaks.Add((frame + frameOffset, peak.Frequency, peak.Energy));
            }
        }

        var byTime = peaks.GroupBy(p => p.Time).ToDictionary(g => g.Key, g => g.ToArray());
        var hashes = new List<AudioLandmark>();
        foreach (var anchor in peaks)
        {
            var added = 0;
            for (var gap = 6; gap <= 54 && added < 4; gap += 8)
            {
                if (!byTime.TryGetValue(anchor.Time + gap, out var targets))
                {
                    continue;
                }

                foreach (var target in targets)
                {
                    // The 9-bit peak frequency bins and 6-bit separation are stable under
                    // gain changes. The anchor frame is stored separately for offset matching.
                    var hash = ((uint)anchor.Frequency << 15) | ((uint)target.Frequency << 6) | (uint)gap;
                    hashes.Add(new AudioLandmark(hash, anchor.Time));
                    if (++added == 4)
                    {
                        break;
                    }
                }
            }
        }

        return hashes;
    }
}
