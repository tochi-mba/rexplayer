using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Deliberately changes pitch with playback speed (AU-15's chipmunk mode). Adjacent
/// samples are interpolated at speed-spaced source positions; one sample of history joins
/// decoder frames without clicks or a reset of the sample phase. Output keeps the source
/// sample rate, so the audio sink hears higher pitch when playing faster and lower when slower.
/// This is independent of WSOLA's pitch-preserving path.
/// </summary>
public sealed class PitchSpeed
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _previous;
    private double _phase;
    private MediaTime _start = MediaTime.Unknown;
    private long _emitted;
    private bool _hasPrevious;

    public PitchSpeed(int sampleRate, int channels, double speed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (!double.IsFinite(speed) || speed < TimeStretch.MinimumRate || speed > TimeStretch.MaximumRate)
            throw new ArgumentOutOfRangeException(nameof(speed));
        _sampleRate = sampleRate;
        _channels = channels;
        Speed = speed;
        _previous = new float[channels];
    }

    public double Speed { get; }

    public bool Fits(int sampleRate, int channels) =>
        sampleRate == _sampleRate && channels == _channels;

    public AudioFrame? Process(AudioFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Fits(input.SampleRate, input.Channels))
            throw new InvalidOperationException("The frame does not match the speed processor.");

        if (!_start.IsKnown && input.Pts.IsKnown)
            _start = input.Pts;
        var length = input.SampleCount;
        if (length == 0) return null;

        // First output position refers to the current frame; subsequent frames can
        // interpolate from the previous frame's final sample at position -1.
        if (!_hasPrevious) _phase = Math.Max(0, _phase);
        var from = _phase;
        var ready = 0;
        while (from < length - 1)
        {
            ready++;
            from += Speed;
        }

        AudioFrame? output = null;
        if (ready > 0)
        {
            output = AudioFrame.Rent(_sampleRate, _channels, ready, input.Layout);
            output.Pts = _start.IsKnown
                ? _start + MediaTime.FromSeconds(_emitted * Speed / _sampleRate)
                : MediaTime.Unknown;
            output.Generation = input.Generation;

            for (var channel = 0; channel < _channels; channel++)
            {
                var samples = input.Channel(channel);
                var dest = output.Channel(channel);
                var position = _phase;
                for (var sample = 0; sample < ready; sample++)
                {
                    var leftIndex = (int)Math.Floor(position);
                    var share = (float)(position - leftIndex);
                    var left = leftIndex < 0 ? _previous[channel] : samples[leftIndex];
                    var right = samples[leftIndex + 1];
                    dest[sample] = left + ((right - left) * share);
                    position += Speed;
                }
            }
            _emitted += ready;
        }

        for (var channel = 0; channel < _channels; channel++)
            _previous[channel] = input.Channel(channel)[length - 1];
        _hasPrevious = true;
        _phase = from - length;
        return output;
    }

    public void Reset(MediaTime pts)
    {
        _start = pts;
        _phase = 0;
        _emitted = 0;
        _hasPrevious = false;
        Array.Clear(_previous);
    }
}
