using Rex.Media.AppCore.Player;
using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

/// <summary>What the visualisations show: the sound being heard, its spectrum, and its levels (AU-18).</summary>
public sealed class VisualizationTests
{
    private static AudioFrame Frame(int sampleRate, params float[][] channels)
    {
        var frame = AudioFrame.Rent(sampleRate, channels.Length, channels[0].Length);
        for (var channel = 0; channel < channels.Length; channel++)
        {
            channels[channel].CopyTo(frame.Channel(channel));
        }

        return frame;
    }

    private static float[] Sine(double hertz, int sampleRate, int count, double amplitude = 1) =>
        [.. Enumerable.Range(0, count).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hertz * i / sampleRate)))];

    [Fact]
    [Capability("AU-18")]
    public void TheScopeGivesTheSoundBeingHeardNotTheSoundStillQueued()
    {
        var scope = new AudioScope();
        var (left, right) = (new float[4], new float[4]);
        Assert.False(scope.Read(left, right));
        Assert.Equal(0, scope.SampleRate);

        using (var stereo = Frame(48000, [1, 2, 3, 4, 5, 6], [-1, -2, -3, -4, -5, -6]))
        {
            scope.Write(stereo);
        }

        Assert.Equal(48000, scope.SampleRate);
        Assert.True(scope.Read(left, right));
        Assert.Equal([3f, 4, 5, 6], left);
        Assert.Equal([-3f, -4, -5, -6], right);

        // Two samples still wait in the device: the four before them are being heard.
        Assert.True(scope.Read(left, right, behind: 2));
        Assert.Equal([1f, 2, 3, 4], left);

        // Asking for more than was written gives silence before the start.
        Assert.True(scope.Read(left, right, behind: 4));
        Assert.Equal([0f, 0, 1, 2], left);

        // Mono feeds both sides; an empty frame changes nothing.
        using (var mono = Frame(44100, [7, 8]))
        {
            scope.Write(mono);
        }

        using (var empty = AudioFrame.Rent(44100, 1, 0))
        {
            scope.Write(empty);
        }

        Assert.True(scope.Read(left, right));
        Assert.Equal([5f, 6, 7, 8], left);
        Assert.Equal([-5f, -6, 7, 8], right);

        scope.Clear();
        Assert.False(scope.Read(left, right));
        Assert.Throws<ArgumentNullException>(() => scope.Write(null!));
        Assert.Throws<ArgumentException>(() => scope.Read(new float[2], new float[3]));
        Assert.Throws<ArgumentException>(() => scope.Read(new float[AudioScope.Length + 1], new float[AudioScope.Length + 1]));
    }

    [Fact]
    public void TheScopeKeepsOnlyTheNewestSound()
    {
        var scope = new AudioScope();
        var many = Enumerable.Range(0, AudioScope.Length + 10).Select(i => (float)i).ToArray();
        using (var frame = Frame(48000, many))
        {
            scope.Write(frame);
        }

        var (left, right) = (new float[AudioScope.Length], new float[AudioScope.Length]);
        Assert.True(scope.Read(left, right, behind: 5));
        Assert.Equal(10f, left[0]);
        Assert.Equal(AudioScope.Length + 9f, left[^1]);

        // Further back than is kept: the oldest kept, then nothing older.
        var (two, other) = (new float[2], new float[2]);
        Assert.True(scope.Read(two, other, behind: long.MaxValue));
        Assert.Equal([10f, 11], two);
    }

    [Fact]
    public void TheTransformFindsASineInItsBin()
    {
        var magnitudes = new float[512];
        Fft.Magnitudes(Sine(48000.0 * 32 / 1024, 48000, 1024, 0.5), magnitudes);

        Assert.Equal(0.5, magnitudes[32], 2);
        Assert.True(magnitudes.Where((_, bin) => Math.Abs(bin - 32) > 2).All(m => m < 0.01));
        Assert.Throws<ArgumentException>(() => Fft.Magnitudes(new float[1024], new float[100]));
        Assert.Throws<ArgumentException>(() => Fft.Transform(new float[6], new float[6]));
        Assert.Throws<ArgumentException>(() => Fft.Transform(new float[8], new float[4]));
        Assert.Throws<ArgumentException>(() => Fft.Transform([], []));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheSpectrumRisesWhereTheToneIsAndFallsSlowlyAfter()
    {
        var spectrum = new SpectrumAnalyzer(16);
        spectrum.Update(Sine(1000, 48000, SpectrumAnalyzer.Size), 48000, TimeSpan.FromMilliseconds(30));

        var loudest = spectrum.Levels.ToList().IndexOf(spectrum.Levels.Max());
        Assert.InRange(spectrum.Levels[loudest], 0.95f, 1f);
        Assert.Equal(spectrum.Levels[loudest], spectrum.Peaks[loudest]);
        Assert.True(spectrum.Levels[0] < 0.5f, "the lowest band hears little of a 1 kHz tone");

        // Silence: the bar falls 1.5 heights a second and the peak 0.4.
        spectrum.Update(new float[SpectrumAnalyzer.Size], 48000, TimeSpan.FromMilliseconds(200));
        Assert.Equal(spectrum.Peaks[loudest] + 0.08f - 0.3f, spectrum.Levels[loudest], 2);
        Assert.InRange(spectrum.Peaks[loudest], 0.87f, 0.93f);

        spectrum.Update(new float[SpectrumAnalyzer.Size], 48000, TimeSpan.FromHours(1));
        Assert.All(spectrum.Peaks, peak => Assert.Equal(0f, peak));
        spectrum.Update(Sine(1000, 48000, SpectrumAnalyzer.Size), 48000, TimeSpan.FromSeconds(-1));
        spectrum.Reset();
        Assert.All(spectrum.Levels, level => Assert.Equal(0f, level));

        Assert.Throws<ArgumentException>(() => spectrum.Update(new float[10], 48000, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.Update(new float[SpectrumAnalyzer.Size], 0, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(8, lowHz: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(8, lowHz: 100, highHz: 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(8, floorDb: 0));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheMetersReadPeakAverageAndHold()
    {
        var meter = new LevelMeter();
        Assert.Equal(LevelMeter.FloorDb, meter.PeakDb);

        meter.Update(Sine(1000, 48000, 4800), TimeSpan.FromMilliseconds(30));
        Assert.Equal(0, meter.PeakDb, 2);
        Assert.Equal(-3.01, meter.RmsDb, 1);
        Assert.Equal(0, meter.HoldDb, 2);

        // A quarter of a second of silence: peak and average fall 24 dB a second, the hold 8.
        meter.Update(new float[480], TimeSpan.FromMilliseconds(250));
        Assert.Equal(-6, meter.PeakDb, 2);
        Assert.Equal(-9.01, meter.RmsDb, 1);
        Assert.Equal(-2, meter.HoldDb, 2);

        meter.Update([], TimeSpan.FromHours(1));
        Assert.Equal(LevelMeter.FloorDb, meter.PeakDb);
        Assert.Equal(LevelMeter.FloorDb, meter.HoldDb);
        meter.Update([2f], TimeSpan.FromSeconds(-1));
        Assert.Equal(0, meter.PeakDb);
        meter.Reset();
        Assert.Equal(LevelMeter.FloorDb, meter.RmsDb);

        Assert.Equal(0, LevelMeter.Position(-90));
        Assert.Equal(0.5, LevelMeter.Position(-30));
        Assert.Equal(1, LevelMeter.Position(3));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheVisualisationsComeInTurnAndTheSpectrogramWarmsWithLoudness()
    {
        Assert.Equal(VisualizerChoice.Oscilloscope, Visualizers.Next(VisualizerChoice.Spectrum));
        Assert.Equal(VisualizerChoice.Vinyl, Visualizers.Next(VisualizerChoice.Spectrogram));
        Assert.Equal(VisualizerChoice.Halo, Visualizers.Next(VisualizerChoice.Vinyl));
        Assert.Equal(VisualizerChoice.Strobe, Visualizers.Next(VisualizerChoice.Ripples));
        Assert.Equal(VisualizerChoice.Silhouette, Visualizers.Next(VisualizerChoice.Strobe));
        Assert.Equal(VisualizerChoice.BeatEdit, Visualizers.Next(VisualizerChoice.Silhouette));
        Assert.Equal(VisualizerChoice.Resonance, Visualizers.Next(VisualizerChoice.BeatEdit));
        Assert.Equal(VisualizerChoice.Off, Visualizers.Next(VisualizerChoice.Resonance));
        Assert.Equal(VisualizerChoice.Spectrum, Visualizers.Next(VisualizerChoice.Off));
        Assert.Equal(VisualizerChoice.Off, Visualizers.Next((VisualizerChoice)42));
        Assert.Equal(
            ["No visualisation", "Spectrum", "Oscilloscope", "Level meters", "Spectrogram", "Vinyl", "Halo", "Mirror wave", "Aurora", "Embers", "Ripples", "Strobe", "Camera silhouette", "Beat edit", "Resonance"],
            Enum.GetValues<VisualizerChoice>().Select(Visualizers.Name));
        Assert.Equal(VisualizerChoice.Spectrum, new PlayerSettings { Visualizer = (VisualizerChoice)99 }.Normalize().Visualizer);

        Assert.Equal(new Argb(255, 255, 0, 0), Visualizers.Hsv(0, 1, 1));
        Assert.Equal(new Argb(255, 0, 255, 0), Visualizers.Hsv(480, 1, 1));
        Assert.Equal(new Argb(255, 0, 0, 255), Visualizers.Hsv(-120, 1, 1));
        Assert.Equal(new Argb(255, 255, 255, 0), Visualizers.Hsv(60, 1, 1));
        Assert.Equal(new Argb(255, 0, 255, 255), Visualizers.Hsv(180, 1, 1));
        Assert.Equal(new Argb(255, 255, 0, 255), Visualizers.Hsv(300, 1, 1));
        Assert.Equal(new Argb(255, 128, 128, 128), Visualizers.Hsv(90, 0, 0.5));
        Assert.Equal(new Argb(255, 0, 0, 0), Visualizers.Hsv(double.NaN, double.PositiveInfinity, double.NaN));
        Assert.Equal(2, Visualizers.DominantBand([0.1f, 0.3f, 0.9f, 0.2f]));
        Assert.Equal(0, Visualizers.DominantBand(Array.Empty<float>()));
        Assert.Throws<ArgumentNullException>(() => Visualizers.DominantBand(null!));

        Assert.Equal(new Argb(255, 0, 0, 0), Visualizers.Heat(0));
        Assert.Equal(new Argb(255, 0, 0, 0), Visualizers.Heat(float.NaN));
        Assert.Equal(new Argb(255, 0, 0, 140), Visualizers.Heat(0.25f));
        Assert.Equal(new Argb(255, 90, 0, 150), Visualizers.Heat(0.375f));
        Assert.Equal(new Argb(255, 255, 255, 255), Visualizers.Heat(2));
    }
}
