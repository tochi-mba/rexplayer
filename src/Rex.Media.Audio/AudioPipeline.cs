using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Everything between a decoder and a sink, in a fixed order: change rate and speaker layout to what
/// the sink takes, apply the effects (loudness gain, stereo mode, equaliser), then the user's
/// volume, then soft-clip. Rate and layout changes are linear, so
/// they commute; the pipeline remixes first when that leaves fewer channels to resample. A stream
/// whose format changes mid-way (a chained internet radio stream) rebuilds the stages on the fly.
/// </summary>
public sealed class AudioPipeline
{
    private readonly AudioFormat _target;
    private readonly ResamplerQuality _quality;
    private Resampler? _resampler;
    private ChannelMixer? _mixer;
    private int _sourceRate;
    private ChannelLayout _sourceLayout;
    private bool _mixFirst;

    public AudioPipeline(AudioFormat target, ResamplerQuality quality = ResamplerQuality.Normal)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _quality = quality;
    }

    public VolumeProcessor Volume { get; } = new();

    /// <summary>The effects in use; the window may replace them at any moment, and the next frame takes them up.</summary>
    public AudioEffects Effects
    {
        get => Volatile.Read(ref _effects);
        set => Volatile.Write(ref _effects, value ?? AudioEffects.None);
    }

    private AudioEffects _effects = AudioEffects.None;
    private Equalizer? _equalizer;
    private EqualizerSettings? _equalizerSource;

    public AudioFormat Target => _target;

    /// <summary>Converts one decoded frame. The caller keeps the input; the result (if any) is the caller's to dispose.</summary>
    public AudioFrame? Process(AudioFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Configure(input);
        AudioFrame? frame;
        if (_mixFirst)
        {
            var mixed = Mix(input);
            frame = Resample(mixed);
            if (!ReferenceEquals(mixed, input) && !ReferenceEquals(mixed, frame))
            {
                mixed.Dispose();
            }
        }
        else
        {
            var resampled = Resample(input);
            frame = resampled is null ? null : Mix(resampled);
            if (resampled is not null && !ReferenceEquals(resampled, input) && !ReferenceEquals(resampled, frame))
            {
                resampled.Dispose();
            }
        }

        if (frame is null)
        {
            return null;
        }

        if (ReferenceEquals(frame, input))
        {
            frame = Copy(input);
        }

        return Finish(frame);
    }

    /// <summary>Releases what the resampler still holds at the end of a stream.</summary>
    public AudioFrame? Drain()
    {
        if (_resampler is null)
        {
            return null;
        }

        var layout = _mixFirst ? _target.Layout : _sourceLayout;
        var tail = _resampler.Drain(layout);
        if (tail is null)
        {
            return null;
        }

        if (!_mixFirst && _mixer is not null)
        {
            var mixed = _mixer.Process(tail);
            tail.Dispose();
            tail = mixed;
        }

        return Finish(tail);
    }

    /// <summary>Forgets buffered audio after a seek; output resumes at <paramref name="pts"/>.</summary>
    public void Reset(MediaTime pts)
    {
        _resampler?.Reset(pts);
        _equalizer?.Reset();
    }

    private AudioFrame Finish(AudioFrame frame)
    {
        var effects = Effects;
        if (effects.Gain != 1)
        {
            for (var channel = 0; channel < frame.Channels; channel++)
            {
                foreach (ref var sample in frame.Channel(channel)[..frame.SampleCount])
                {
                    sample *= effects.Gain;
                }
            }
        }

        StereoModes.Apply(frame, effects.StereoMode);
        // Rebuilt only when the settings or the format change: rebuilding loses the filters' memory.
        if (_equalizer is null || !ReferenceEquals(_equalizerSource, effects.Equalizer) || !_equalizer.Fits(frame.SampleRate, frame.Channels))
        {
            _equalizer = new Equalizer(effects.Equalizer, frame.SampleRate, frame.Channels);
            _equalizerSource = effects.Equalizer;
        }

        _equalizer.Process(frame);
        Volume.Process(frame);
        SoftClipper.Process(frame);
        return frame;
    }

    private void Configure(AudioFrame input)
    {
        if (input.SampleRate == _sourceRate && input.Layout == _sourceLayout)
        {
            return;
        }

        _sourceRate = input.SampleRate;
        _sourceLayout = input.Layout;
        _mixer = input.Layout == _target.Layout ? null : new ChannelMixer(input.Layout, _target.Layout);
        _mixFirst = _mixer is not null && _target.Channels < input.Channels;
        var resampleChannels = _mixFirst ? _target.Channels : input.Channels;
        _resampler = input.SampleRate == _target.SampleRate ? null : new Resampler(input.SampleRate, _target.SampleRate, resampleChannels, _quality);
        _resampler?.Reset(input.Pts);
    }

    private AudioFrame Mix(AudioFrame frame) => _mixer is null ? frame : _mixer.Process(frame);

    private AudioFrame? Resample(AudioFrame frame) => _resampler is null ? frame : _resampler.Process(frame);

    private static AudioFrame Copy(AudioFrame input)
    {
        var copy = AudioFrame.Rent(input.SampleRate, input.Channels, input.SampleCount, input.Layout);
        copy.Pts = input.Pts;
        copy.Generation = input.Generation;
        for (var channel = 0; channel < input.Channels; channel++)
        {
            input.Channel(channel).CopyTo(copy.Channel(channel));
        }

        return copy;
    }
}
