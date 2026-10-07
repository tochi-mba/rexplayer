namespace Rex.Media.Audio;

/// <summary>
/// A channel's loudness for the meters visualisation (AU-18), in decibels below full scale: the
/// peak, which jumps up and falls back steadily; the average (RMS) level, which falls the same
/// way; and a hold mark at the recent highest peak, which drifts down more slowly.
/// </summary>
public sealed class LevelMeter
{
    /// <summary>The quietest level shown; anything quieter reads as this.</summary>
    public const double FloorDb = -60;

    /// <summary>How fast the peak and average fall, in decibels per second.</summary>
    private const double Fall = 24;

    /// <summary>How fast the hold mark falls, in decibels per second.</summary>
    private const double HoldFall = 8;

    public double PeakDb { get; private set; } = FloorDb;

    public double RmsDb { get; private set; } = FloorDb;

    public double HoldDb { get; private set; } = FloorDb;

    /// <summary>Where <paramref name="decibels"/> sits on a meter, from 0 at the floor to 1 at full scale.</summary>
    public static double Position(double decibels) => Math.Clamp((decibels - FloorDb) / -FloorDb, 0, 1);

    /// <summary>Measures the newest <paramref name="samples"/>, <paramref name="elapsed"/> after the last time.</summary>
    public void Update(ReadOnlySpan<float> samples, TimeSpan elapsed)
    {
        var peak = 0f;
        var sum = 0.0;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
            sum += sample * sample;
        }

        var seconds = Math.Max(0, elapsed.TotalSeconds);
        var rms = samples.IsEmpty ? 0 : Math.Sqrt(sum / samples.Length);
        PeakDb = Math.Max(Decibels(peak), PeakDb - (Fall * seconds));
        RmsDb = Math.Max(Decibels(rms), RmsDb - (Fall * seconds));
        HoldDb = Math.Max(PeakDb, Math.Max(FloorDb, HoldDb - (HoldFall * seconds)));
    }

    public void Reset() => PeakDb = RmsDb = HoldDb = FloorDb;

    private static double Decibels(double amplitude) => Math.Clamp(20 * Math.Log10(Math.Max(amplitude, 1e-9)), FloorDb, 0);
}
