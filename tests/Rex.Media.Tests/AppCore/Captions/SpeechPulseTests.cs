using Rex.Media.AppCore.Captions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class SpeechPulseTests
{
    private const int Rate = 48000;
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(SpeechPulse.Size / (double)Rate);

    /// <summary>A voice-like buzz: a tone with its harmonics, at <paramref name="amplitude"/>, continuing from <paramref name="phaseFrames"/> frames before.</summary>
    private static float[] Voice(double hz, double amplitude, int phaseFrames)
    {
        var samples = new float[SpeechPulse.Size];
        var offset = phaseFrames * SpeechPulse.Size;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (offset + i) / (double)Rate;
            samples[i] = (float)(amplitude * ((0.6 * Math.Sin(2 * Math.PI * hz * t)) + (0.3 * Math.Sin(4 * Math.PI * hz * t)) + (0.1 * Math.Sin(6 * Math.PI * hz * t))));
        }

        return samples;
    }

    /// <summary>Speaks <paramref name="syllables"/> syllables of <paramref name="hz"/> at the given amplitudes, each held some frames and parted by near-silence.</summary>
    private static List<SpeechFrame> Speak(SpeechPulse pulse, double hz, IEnumerable<double> syllables, int hold = 4, int gap = 2)
    {
        var frames = new List<SpeechFrame>();
        var frame = 0;
        foreach (var amplitude in syllables)
        {
            for (var i = 0; i < hold; i++)
            {
                frames.Add(pulse.Update(Voice(hz, amplitude, frame++), Rate, Dt));
            }

            for (var i = 0; i < gap; i++)
            {
                frames.Add(pulse.Update(Voice(hz, amplitude * 0.02, frame++), Rate, Dt));
            }
        }

        return frames;
    }

    [Fact]
    [Capability("OSD-11")]
    public void EachSyllableIsAnOnsetAndThePaceCountsThem()
    {
        var pulse = new SpeechPulse();
        var frames = Speak(pulse, 120, Enumerable.Repeat(0.3, 12));

        Assert.InRange(pulse.Onsets, 10, 12);
        Assert.Equal(pulse.Onsets, frames.Count(frame => frame.Onset));
        // Six frames a syllable at 43 ms is about 3.9 syllables a second.
        Assert.InRange(frames[^1].Pace, 3, 5);
        Assert.All(frames.Where(frame => frame.Onset), frame => Assert.False(frame.Silent));
    }

    [Fact]
    [Capability("OSD-11")]
    public void LoudnessIsAgainstTheSpeakersOwnRangeAndStressRanksAWordAmongTheirLastOnes()
    {
        var pulse = new SpeechPulse();
        var soft = Enumerable.Repeat(0.05, 8).ToList();
        Speak(pulse, 110, soft);
        var softFrames = Speak(pulse, 110, [0.05]);
        var shout = Speak(pulse, 110, [0.6]);

        var softLoudness = softFrames.Where(frame => frame.Onset).Select(frame => frame.Loudness).Single();
        var shoutLoudness = shout.Where(frame => frame.Onset).Select(frame => frame.Loudness).Single();
        Assert.True(shoutLoudness > softLoudness + 0.3, $"{shoutLoudness} should be far above {softLoudness}");
        Assert.Equal(Stress.Peak, pulse.StressOf(shoutLoudness));
        Assert.Equal(Stress.Hush, pulse.StressOf(0));
        Assert.Equal(Stress.Plain, pulse.StressOf((softLoudness + shoutLoudness) / 2));
        Assert.Equal(Stress.Shout, pulse.StressOf(shoutLoudness - 0.001f));
    }

    [Fact]
    public void BeforeEnoughWordsAreHeardStressIsPlainUnlessVeryLoud()
    {
        var pulse = new SpeechPulse();
        Assert.Equal(Stress.Plain, pulse.StressOf(0.5f));
        Assert.Equal(Stress.Shout, pulse.StressOf(0.9f));
    }

    [Fact]
    [Capability("OSD-11")]
    public void PitchIsToldAgainstTheSpeakersUsualAndSettlesBetweenVoicedFrames()
    {
        var pulse = new SpeechPulse();
        Speak(pulse, 120, Enumerable.Repeat(0.3, 6), hold: 6, gap: 0);
        Assert.InRange(pulse.UsualHertz, 110, 130);
        Assert.InRange(pulse.Frame.Pitch, 0.4, 0.6);

        var lifted = pulse.Update(Voice(200, 0.3, 0), Rate, Dt);
        Assert.InRange(pulse.Hertz, 190, 210);
        Assert.True(lifted.Pitch > 0.8, $"{lifted.Pitch}");

        var dropped = pulse.Update(Voice(80, 0.3, 0), Rate, Dt);
        Assert.True(dropped.Pitch < 0.3, $"{dropped.Pitch}");

        // Noise has no clear repetition: no pitch, and the reading settles back toward usual.
        var noise = new float[SpeechPulse.Size];
        var random = new Random(7);
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = (float)((random.NextDouble() * 0.6) - 0.3);
        }

        var unvoiced = pulse.Update(noise, Rate, Dt);
        Assert.Equal(0, pulse.Hertz);
        Assert.True(unvoiced.Pitch > dropped.Pitch && unvoiced.Pitch < 0.5, $"{unvoiced.Pitch}");
    }

    [Fact]
    [Capability("OSD-11")]
    public void SilenceIsSilentForAsLongAsItLastsAndHasNoPitch()
    {
        var pulse = new SpeechPulse();
        Speak(pulse, 120, [0.3, 0.3]);
        var quiet = new float[SpeechPulse.Size];
        SpeechFrame frame = default;
        for (var i = 0; i < 10; i++)
        {
            frame = pulse.Update(quiet, Rate, Dt);
        }

        Assert.True(frame.Silent);
        Assert.InRange(frame.SilentFor, 9 * Dt.TotalSeconds, 10 * Dt.TotalSeconds + 0.001);
        Assert.Equal(0, pulse.Hertz);
        Assert.Equal(0, frame.Loudness);
    }

    [Fact]
    public void ATailOnlySignalIsNotVoicedAndATinySampleRateFindsNoPitch()
    {
        var pulse = new SpeechPulse();
        var tail = new float[SpeechPulse.Size];
        for (var i = tail.Length - 200; i < tail.Length; i++)
        {
            tail[i] = 0.5f;
        }

        pulse.Update(tail, Rate, Dt);
        Assert.Equal(0, pulse.Hertz);
        Assert.Equal(0, new SpeechPulse().Update(Voice(120, 0.3, 0), 100, Dt).Pitch - 0.5f, 3);
    }

    [Fact]
    public void ResetForgetsTheSpeakerAndWrongInputIsRefused()
    {
        var pulse = new SpeechPulse();
        Speak(pulse, 120, [0.3, 0.3, 0.3]);
        pulse.Reset();
        Assert.Equal(0, pulse.Onsets);
        Assert.Equal(0, pulse.UsualHertz);
        Assert.Equal(default, pulse.Frame);
        Assert.Throws<ArgumentException>(() => pulse.Update(new float[10], Rate, Dt));
        Assert.Throws<ArgumentOutOfRangeException>(() => pulse.Update(new float[SpeechPulse.Size], 0, Dt));
    }
}
