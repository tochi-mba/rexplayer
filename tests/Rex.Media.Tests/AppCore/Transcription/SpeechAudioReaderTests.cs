using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class SpeechAudioReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-speech-reader-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static TimeRange R(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    private static DecoderRegistry Decoders => new DecoderRegistry().Add(new PcmDecoderFactory());

    private static float Ramp(int sample) => (short)(sample & 0x7FFF) / 32768f;

    private string File(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    [Capability("SUB-21")]
    public void ItReadsStretchesInOrderWithoutSeekingAndSeeksForAnyOther()
    {
        var token = TestContext.Current.CancellationToken;
        using var reader = new SpeechAudioReader(File("ramp.wav", Pcm.RampWav(16000, 16000 * 30)), Decoders, token);
        Assert.True(reader.HasSound);
        Assert.Equal(TimeSpan.FromSeconds(30), reader.Duration);

        var first = reader.Read(R(1, 2), token);
        Assert.Equal(16000, first.Length);
        Assert.Equal(Ramp(16000), first[0]);
        Assert.Equal(Ramp(31999), first[^1]);

        // Overlapping the last: read on.
        var next = reader.Read(R(1.5, 3), token);
        Assert.Equal(24000, next.Length);
        Assert.Equal(Ramp(24000), next[0]);

        // Far ahead, then back: sought.
        Assert.Equal(Ramp(16000 * 15), reader.Read(R(15, 16), token)[0]);
        Assert.Equal(Ramp(8000), reader.Read(R(0.5, 1), token)[0]);

        // Past the end: what there is, and the reader says the sound ended.
        var tail = reader.Read(R(29, 33), token);
        Assert.Equal(16000, tail.Length);
        Assert.True(reader.Ended);
        Assert.Empty(reader.Read(R(31, 32), token));
        Assert.Empty(reader.Read(R(5, 5), token));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ChannelsAreAveragedAndTheRateBroughtTo16Kilohertz()
    {
        var token = TestContext.Current.CancellationToken;
        var left = Enumerable.Repeat(0.5f, 8000 * 4).ToArray();
        var right = Enumerable.Repeat(-0.25f, 8000 * 4).ToArray();
        using var stereo = new SpeechAudioReader(File("stereo.wav", WavBuilder.Pcm(8000, 2, 16, Pcm.Int16(left, right)).Build()), Decoders, token);
        var heard = stereo.Read(R(1, 2), token);
        Assert.Equal(16000, heard.Length);
        Assert.All(heard, sample => Assert.Equal(0.125f, sample, 3));
    }

    [Fact]
    [Capability("SUB-21")]
    public void MonoAndMultichannelSilentRecordingsRemainSafeToSample()
    {
        var token = TestContext.Current.CancellationToken;
        foreach (var channels in new[] { 1, 2, 3 })
        {
            var sound = Enumerable.Range(0, channels)
                .Select(_ => new float[16000 * 2]).ToArray();
            var path = File($"silent-{channels}.wav",
                WavBuilder.Pcm(16000, channels, 16, Pcm.Int16(sound)).Build());
            using var reader = new SpeechAudioReader(path, Decoders, token);
            Assert.True(reader.HasSound);
            Assert.Equal(16000, reader.Read(R(0, 1), token).Length);
            Assert.All(reader.Read(R(1, 2), token), sample => Assert.Equal(0f, sample));
            Assert.Empty(reader.Read(R(2, 2), token));
        }
    }

    [Fact]
    [Capability("SUB-21")]
    public void ClippedAudioAndZeroLengthWavDoNotProduceInvalidWaveformSamples()
    {
        var token = TestContext.Current.CancellationToken;
        using (var reader = new SpeechAudioReader(
            File("clipped.wav", WavBuilder.Pcm(16000, 1, 16,
                Pcm.Int16(Enumerable.Repeat(1f, 16000).ToArray())).Build()),
            Decoders, token))
        {
            var samples = reader.Read(R(0, 1), token);
            Assert.Equal(16000, samples.Length);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) <= 1f));
        }

        using var empty = new SpeechAudioReader(File("empty.wav",
            WavBuilder.Pcm(16000, 1, 16, Pcm.Int16([])).Build()), Decoders, token);
        Assert.Empty(empty.Read(R(0, 1), token));
        Assert.Empty(empty.Read(R(1, 1), token));
    }

    [Fact]
    [Capability("SUB-21")]
    public void AFileWithoutSoundGivesNothingAndOneThatIsNotMediaFails()
    {
        var token = TestContext.Current.CancellationToken;
        using (var silent = new SpeechAudioReader(File("tone.wav", Pcm.SineWav(220, 16000, 1, 1)), new DecoderRegistry(), token))
        {
            Assert.False(silent.HasSound);
            Assert.Empty(silent.Read(R(0, 1), token));
        }

        Assert.ThrowsAny<IOException>(() => new SpeechAudioReader(File("noise.bin", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]), Decoders, token));
        Assert.Throws<ArgumentNullException>(() => new SpeechAudioReader(null!, Decoders, token));
        Assert.Throws<ArgumentNullException>(() => new SpeechAudioReader("x", null!, token));
    }
}
