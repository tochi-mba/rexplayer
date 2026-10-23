namespace Rex.Media.AppCore.Library;

/// <summary>
/// Bounded, sampled waveform geometry for a local recording. A long file's preview samples
/// evenly spaced short regions rather than pretending that it scanned all hours of audio.
/// </summary>
public static class RecordingWaveform
{
    /// <summary>Largest absolute finite sample in one sampled region, constrained to 0–1.</summary>
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            if (float.IsFinite(sample))
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    /// <summary>The start of a preview bin, clamped to the file's known length.</summary>
    public static TimeSpan At(TimeSpan duration, int index, int buckets)
    {
        if (buckets <= 0 || duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)(duration.Ticks * (Math.Clamp(index, 0, buckets) / (double)buckets)));
    }

    /// <summary>The bin corresponding to playback position; never indexes beyond the preview.</summary>
    public static int Current(TimeSpan duration, TimeSpan position, int buckets)
    {
        if (buckets <= 0 || duration <= TimeSpan.Zero)
        {
            return -1;
        }

        return Math.Clamp((int)(Math.Clamp(position.Ticks / (double)duration.Ticks, 0, 1) * buckets), 0, buckets - 1);
    }
}
