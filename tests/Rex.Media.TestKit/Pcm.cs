using System.Buffers.Binary;

namespace Rex.Media.TestKit;

/// <summary>Turns float samples into interleaved PCM bytes the way an encoder would, for fixtures.</summary>
public static class Pcm
{
    /// <summary>Interleaves channels of float samples as 16-bit little-endian integers.</summary>
    public static byte[] Int16(params float[][] channels)
    {
        var frames = channels[0].Length;
        var bytes = new byte[frames * channels.Length * 2];
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels.Length; c++)
            {
                var value = (short)Math.Clamp(Math.Round(channels[c][i] * 32768.0), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(((i * channels.Length) + c) * 2), value);
            }
        }

        return bytes;
    }

    /// <summary>A WAV file holding a 16-bit sine wave on every channel.</summary>
    public static byte[] SineWav(double frequency, int sampleRate, int channels, double seconds, double amplitude = 0.5)
    {
        var count = (int)(sampleRate * seconds);
        var tone = Signals.Sine(frequency, sampleRate, count, amplitude);
        var planes = Enumerable.Repeat(tone, channels).ToArray();
        return WavBuilder.Pcm(sampleRate, channels, 16, Int16(planes)).Build();
    }

    /// <summary>A WAV whose left channel counts samples (value i / 32768 at sample i, wrapping) for exact-position checks.</summary>
    public static byte[] RampWav(int sampleRate, int samples)
    {
        var ramp = new float[samples];
        for (var i = 0; i < samples; i++)
        {
            ramp[i] = (short)(i & 0x7FFF) / 32768f;
        }

        return WavBuilder.Pcm(sampleRate, 1, 16, Int16(ramp)).Build();
    }
}
