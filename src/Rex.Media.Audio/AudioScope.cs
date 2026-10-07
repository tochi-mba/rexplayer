using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// The sound most recently sent to the speakers, kept for visualisations (AU-18): the last
/// <see cref="Length"/> samples of the front left and right channels (mono feeds both). The audio
/// thread writes; the window reads, asking for the samples just being heard rather than the ones
/// still waiting in the device's buffer.
/// </summary>
public sealed class AudioScope
{
    /// <summary>Samples kept per channel: over half a second at 48 kHz, more than a device buffers.</summary>
    public const int Length = 32768;

    private readonly float[] _left = new float[Length];
    private readonly float[] _right = new float[Length];
    private readonly object _gate = new();
    private long _written;

    /// <summary>The sample rate of what was last written, or 0 before anything was.</summary>
    public int SampleRate { get; private set; }

    /// <summary>Keeps <paramref name="frame"/>'s samples as the newest.</summary>
    public void Write(AudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.SampleCount == 0 || frame.Channels == 0)
        {
            return;
        }

        var left = frame.Channel(0)[..frame.SampleCount];
        var right = frame.Channels > 1 ? frame.Channel(1)[..frame.SampleCount] : left;
        lock (_gate)
        {
            SampleRate = frame.SampleRate;

            // Only the newest Length samples can be kept.
            var skip = Math.Max(0, left.Length - Length);
            for (var i = skip; i < left.Length; i++)
            {
                var at = (int)((_written + i) % Length);
                _left[at] = left[i];
                _right[at] = right[i];
            }

            _written += left.Length;
        }
    }

    /// <summary>
    /// Copies the samples that end <paramref name="behind"/> samples before the newest (those the
    /// device holds but has not played) into <paramref name="left"/> and <paramref name="right"/>,
    /// oldest first, with silence for any not yet written. False when nothing has been written.
    /// </summary>
    public bool Read(Span<float> left, Span<float> right, long behind = 0)
    {
        if (left.Length != right.Length || left.Length > Length)
        {
            throw new ArgumentException($"Read up to {Length} samples, the same number for each channel.");
        }

        lock (_gate)
        {
            if (_written == 0)
            {
                return false;
            }

            var end = _written - Math.Clamp(behind, 0, Length - left.Length);
            for (var i = 0; i < left.Length; i++)
            {
                var sample = end - left.Length + i;
                var kept = sample >= 0 && sample >= _written - Length;
                var at = (int)(((sample % Length) + Length) % Length);
                left[i] = kept ? _left[at] : 0;
                right[i] = kept ? _right[at] : 0;
            }

            return true;
        }
    }

    /// <summary>Forgets everything, as after a seek: what was kept is no longer what will be heard.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_left);
            Array.Clear(_right);
            _written = 0;
        }
    }
}
