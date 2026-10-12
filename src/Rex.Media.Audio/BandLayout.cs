namespace Rex.Media.Audio;

/// <summary>
/// Which transform bins make each band of a spectrum visualisation (AU-18): the range from a low
/// frequency to a high one shared out by ratio, as octaves are, each band at least one bin wide.
/// </summary>
public static class BandLayout
{
    /// <summary>
    /// The first and last bin of band <paramref name="band"/> of <paramref name="bands"/> between
    /// <paramref name="lowHz"/> and <paramref name="highHz"/>, for a transform of
    /// <paramref name="size"/> samples at <paramref name="sampleRate"/>.
    /// </summary>
    public static (int From, int To) Bins(int band, int bands, double lowHz, double highHz, int sampleRate, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bands, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 4);
        var lastBin = (size / 2) - 1;
        var from = Bin(lowHz * Math.Pow(highHz / lowHz, (double)band / bands), sampleRate, size, lastBin);
        var to = Math.Max(from, Bin(lowHz * Math.Pow(highHz / lowHz, (double)(band + 1) / bands), sampleRate, size, lastBin) - 1);
        return (from, to);
    }

    /// <summary>The lowest and highest frequency band <paramref name="band"/> covers.</summary>
    public static (double Low, double High) Edges(int band, int bands, double lowHz, double highHz) =>
        (lowHz * Math.Pow(highHz / lowHz, (double)band / bands), lowHz * Math.Pow(highHz / lowHz, (double)(band + 1) / bands));

    /// <summary>The summed power of bins <paramref name="from"/> to <paramref name="to"/>: how much sound the band holds, however wide.</summary>
    public static double Power(ReadOnlySpan<float> magnitudes, int from, int to)
    {
        double power = 0;
        for (var bin = from; bin <= to; bin++)
        {
            power += magnitudes[bin] * (double)magnitudes[bin];
        }

        return power;
    }

    private static int Bin(double hertz, int sampleRate, int size, int lastBin) => (int)Math.Clamp(Math.Round(hertz * size / sampleRate), 1, lastBin);
}
