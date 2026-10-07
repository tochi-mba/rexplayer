using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class AudioEffectsTests
{
    private const int Rate = 48_000;

    /// <summary>A second of a tone through an equaliser, and its level afterwards in decibels relative to before.</summary>
    private static double GainAt(EqualizerSettings settings, double frequency)
    {
        var tone = Signals.Sine(frequency, Rate, Rate, amplitude: 0.1);
        using var frame = AudioFrame.Rent(Rate, 1, tone.Length);
        tone.CopyTo(frame.Channel(0));
        new Equalizer(settings, Rate, 1).Process(frame);

        // The first tenth of a second is left out: a filter takes a moment to settle.
        var settled = frame.Channel(0)[(Rate / 10)..frame.SampleCount];
        return 20 * Math.Log10(Signals.Amplitude(settled, Rate, frequency) / 0.1);
    }

    private static EqualizerSettings Band(int band, double gain)
    {
        var gains = new double[10];
        gains[band] = gain;
        return new EqualizerSettings(true, 0, gains);
    }

    [Theory]
    [InlineData(3, 250.0)]
    [InlineData(5, 1000.0)]
    [InlineData(7, 4000.0)]
    [Capability("AU-07")]
    public void ABandRaisesAndLowersItsOwnFrequencyByItsGain(int band, double frequency)
    {
        Assert.InRange(GainAt(Band(band, 12), frequency), 11.5, 12.5);
        Assert.InRange(GainAt(Band(band, -12), frequency), -12.5, -11.5);
    }

    [Fact]
    public void ABandLeavesFrequenciesFarFromItAlone()
    {
        Assert.InRange(GainAt(Band(5, 12), 125), -0.5, 0.5);
        Assert.InRange(GainAt(Band(5, 12), 8000), -0.5, 0.5);
    }

    [Fact]
    public void TheShelvesLiftEverythingWellBeyondThem()
    {
        Assert.InRange(GainAt(Band(0, 10), 8), 9, 10.5);
        Assert.InRange(GainAt(Band(9, 10), 22000), 9, 10.5);
        Assert.InRange(GainAt(Band(9, 10), 1000), -0.5, 0.5);
    }

    [Fact]
    public void ThePreampMovesEverything()
    {
        Assert.InRange(GainAt(new EqualizerSettings(true, -6, new double[10]), 1000), -6.1, -5.9);
    }

    [Fact]
    public void FlatOffOrUnshapeableTheSoundIsUntouched()
    {
        var tone = Signals.Sine(1000, Rate, 4800);
        foreach (var (settings, rate) in new[] { (EqualizerSettings.Off, Rate), (EqualizerSettings.Preset("Flat")!, Rate), (Band(9, 12) with { Enabled = false }, Rate), (Band(9, 12), 22_050) })
        {
            using var frame = AudioFrame.Rent(rate, 1, tone.Length);
            tone.CopyTo(frame.Channel(0));
            new Equalizer(settings, rate, 1).Process(frame);
            Assert.Equal(tone, frame.Channel(0)[..tone.Length].ToArray());
        }
    }

    [Fact]
    public void SettingsAreKeptInsideTheLimitsAndThePresetsAreThere()
    {
        var wild = new EqualizerSettings(true, 50, [99, -99, double.NaN]).Normalize();

        Assert.Equal(20, wild.Preamp);
        Assert.Equal([20, -20, 0, 0, 0, 0, 0, 0, 0, 0], wild.Gains);
        Assert.Equal(18, EqualizerSettings.Presets.Count);
        Assert.All(EqualizerSettings.Presets, preset => Assert.Equal(10, preset.Gains.Length));
        Assert.True(EqualizerSettings.Preset("Rock")!.IsAudible);
        Assert.Null(EqualizerSettings.Preset("Polka"));
        Assert.Equal(0, new EqualizerSettings(true, 0, null!).Normalize().Gains.Sum());
    }

    [Fact]
    public void AnEqualizerServesOnlyItsOwnFormatAndForgetsOnReset()
    {
        var equalizer = new Equalizer(Band(5, 12), Rate, 2);

        Assert.True(equalizer.Fits(Rate, 2));
        Assert.False(equalizer.Fits(44_100, 2));
        Assert.False(equalizer.Fits(Rate, 1));
        using var impulse = AudioFrame.Rent(Rate, 2, 4);
        impulse.Channel(0).Clear();
        impulse.Channel(1).Clear();
        impulse.Channel(0)[0] = 1;
        equalizer.Process(impulse);
        equalizer.Reset();
        using var silence = AudioFrame.Rent(Rate, 2, 4);
        silence.Channel(0).Clear();
        silence.Channel(1).Clear();
        equalizer.Process(silence);
        Assert.All(silence.Channel(0)[..4].ToArray(), sample => Assert.Equal(0f, sample));
        Assert.Throws<ArgumentNullException>(() => equalizer.Process(null!));
        Assert.Throws<ArgumentNullException>(() => new Equalizer(null!, Rate, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Equalizer(EqualizerSettings.Off, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Equalizer(EqualizerSettings.Off, Rate, 0));
    }

    [Theory]
    [InlineData(StereoMode.Stereo, 0.2f, 0.6f)]
    [InlineData(StereoMode.Mono, 0.4f, 0.4f)]
    [InlineData(StereoMode.Left, 0.2f, 0.2f)]
    [InlineData(StereoMode.Right, 0.6f, 0.6f)]
    [InlineData(StereoMode.Reverse, 0.6f, 0.2f)]
    [Capability("AU-11")]
    public void StereoModesMixTheFrontChannels(StereoMode mode, float left, float right)
    {
        using var frame = AudioFrame.Rent(Rate, 2, 1);
        (frame.Channel(0)[0], frame.Channel(1)[0]) = (0.2f, 0.6f);

        StereoModes.Apply(frame, mode);

        Assert.Equal(left, frame.Channel(0)[0], 6);
        Assert.Equal(right, frame.Channel(1)[0], 6);
    }

    [Fact]
    public void MonoMediaHasNoSidesToMix()
    {
        using var frame = AudioFrame.Rent(Rate, 1, 1);
        frame.Channel(0)[0] = 0.3f;

        StereoModes.Apply(frame, StereoMode.Reverse);

        Assert.Equal(0.3f, frame.Channel(0)[0]);
        Assert.Throws<ArgumentNullException>(() => StereoModes.Apply(null!, StereoMode.Mono));
    }

    private static readonly Dictionary<string, string> Tagged = new()
    {
        [MetadataKeys.ReplayGainTrackGain] = "-6.02 dB",
        [MetadataKeys.ReplayGainTrackPeak] = "0.9",
        [MetadataKeys.ReplayGainAlbumGain] = "+3.00 dB",
        [MetadataKeys.ReplayGainAlbumPeak] = "0.95",
    };

    [Fact]
    [Capability("AU-13")]
    public void ReplayGainUsesTheTagsTheModeAsksFor()
    {
        Assert.Equal(1f, ReplayGain.Factor(Tagged, ReplayGainSettings.Off));
        Assert.Equal(0.5f, ReplayGain.Factor(Tagged, new ReplayGainSettings(ReplayGainMode.Track)), 3);
        Assert.Equal(1 / 0.95f, ReplayGain.Factor(Tagged, new ReplayGainSettings(ReplayGainMode.Album)), 4);
        Assert.Equal(1.413f, ReplayGain.Factor(Tagged, new ReplayGainSettings(ReplayGainMode.Album, PreventClipping: false)), 3);
        Assert.Equal(1f, ReplayGain.Factor(Tagged, new ReplayGainSettings(ReplayGainMode.Track, Preamp: 6.02)), 3);
    }

    [Fact]
    public void UntaggedOrOddlyTaggedMediaGetsTheUntaggedGain()
    {
        Assert.Equal(0.5f, ReplayGain.Factor(new Dictionary<string, string>(), new ReplayGainSettings(ReplayGainMode.Track, UntaggedGain: -6.02)), 3);
        Assert.Equal(1f, ReplayGain.Factor(new Dictionary<string, string> { [MetadataKeys.ReplayGainTrackGain] = "loud" }, new ReplayGainSettings(ReplayGainMode.Album)));
        Assert.Equal(1f, ReplayGain.Factor(new Dictionary<string, string> { [MetadataKeys.ReplayGainTrackGain] = "Infinity" }, new ReplayGainSettings(ReplayGainMode.Track)));
        Assert.Equal(0.5f, ReplayGain.Factor(new Dictionary<string, string> { [MetadataKeys.ReplayGainTrackGain] = "-6.02", [MetadataKeys.ReplayGainTrackPeak] = "0" }, new ReplayGainSettings(ReplayGainMode.Track)), 3);
        Assert.Throws<ArgumentNullException>(() => ReplayGain.Factor(null!, ReplayGainSettings.Off));
        Assert.Throws<ArgumentNullException>(() => ReplayGain.Factor(Tagged, null!));
    }

    [Fact]
    public void ThePipelineAppliesTheEffectsBeforeTheVolume()
    {
        var pipeline = new AudioPipeline(new AudioFormat(Rate, 2, SampleFormat.F32, ChannelLayout.Stereo))
        {
            Effects = new AudioEffects(EqualizerSettings.Off, StereoMode.Left, 0.5f),
        };
        using var input = AudioFrame.Rent(Rate, 2, 4, ChannelLayout.Stereo);
        input.Channel(0).Fill(0.4f);
        input.Channel(1).Fill(0.8f);

        using var output = pipeline.Process(input)!;
        pipeline.Effects = null!;
        Assert.Same(AudioEffects.None, pipeline.Effects);

        Assert.All(output.Channel(1)[..4].ToArray(), sample => Assert.Equal(0.2f, sample, 6));
        pipeline.Reset(MediaTime.Zero);
    }
}
