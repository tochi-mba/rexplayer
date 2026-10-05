using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Decides how the engine talks to an audio endpoint, given the endpoint's mix format. Pure policy,
/// so every rule is tested without a device (ADR-008): the adapters only report what the device said.
/// </summary>
public static class DeviceFormatPolicy
{
    /// <summary>
    /// The format frames must arrive in (always float in the engine) and how they are stored in the
    /// device buffer. A mask that does not match the channel count is ignored in favour of the
    /// default layout for that count.
    /// </summary>
    public static (AudioFormat Frames, SampleFormat Storage) FromMixFormat(int sampleRate, int channels, int bitsPerSample, uint channelMask, bool isFloat)
    {
        var storage = isFloat
            ? bitsPerSample == 32 ? SampleFormat.F32 : throw new NotSupportedException($"The audio device mixes in {bitsPerSample}-bit floats, which rexplayer cannot write.")
            : bitsPerSample switch
            {
                16 => SampleFormat.S16,
                24 => SampleFormat.S24,
                32 => SampleFormat.S32,
                _ => throw new NotSupportedException($"The audio device mixes in {bitsPerSample}-bit integers, which rexplayer cannot write."),
            };
        var layout = (ChannelLayout)channelMask;
        var frames = new AudioFormat(sampleRate, channels, SampleFormat.F32, layout.ChannelCount() == channels ? layout : ChannelLayouts.Default(channels));
        return (frames, storage);
    }
}
