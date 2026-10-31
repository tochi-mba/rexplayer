using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>How captured sound is laid out, as the device or the app's stream gives it.</summary>
public readonly record struct CaptureFormat(int SampleRate, int Channels, int BitsPerSample, bool IsFloat)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
}

/// <summary>
/// Turns sound captured from another app or the whole computer (interleaved 32-bit float or 16-bit
/// integer, any number of channels) into what the visualisations read: an <see cref="AudioScope"/>
/// of front left and right. Mono feeds both sides; a packet the system marks silent is written as
/// silence, so the visualisation settles rather than freezing. One frame is kept and grown as
/// needed, so steady capture allocates nothing.
/// </summary>
public sealed class CaptureFeed : IDisposable
{
    private AudioFrame? _frame;

    public CaptureFeed(AudioScope scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public AudioScope Scope { get; }

    /// <summary>Takes <paramref name="frames"/> frames of <paramref name="format"/> from <paramref name="interleaved"/>, or silence for them.</summary>
    public void Write(ReadOnlySpan<byte> interleaved, int frames, CaptureFormat format, bool silent = false)
    {
        if (frames <= 0)
        {
            return;
        }

        if (format.Channels < 1 || format.SampleRate < 1 || !(format.IsFloat ? format.BitsPerSample == 32 : format.BitsPerSample == 16))
        {
            throw new NotSupportedException("Captured sound is read as 32-bit float or 16-bit integer.");
        }

        if (!silent && interleaved.Length < frames * format.BlockAlign)
        {
            throw new ArgumentException("The captured bytes are shorter than the frames they hold.", nameof(interleaved));
        }

        if (_frame is null || _frame.Capacity < frames || _frame.SampleRate != format.SampleRate)
        {
            _frame?.Dispose();
            _frame = AudioFrame.Rent(format.SampleRate, 2, Math.Max(frames, 1024));
        }

        _frame.SetSampleCount(frames);
        var left = _frame.Channel(0);
        var right = _frame.Channel(1);
        if (silent)
        {
            left[..frames].Clear();
            right[..frames].Clear();
        }
        else
        {
            var second = format.Channels > 1 ? 1 : 0;
            for (var i = 0; i < frames; i++)
            {
                var at = i * format.BlockAlign;
                (left[i], right[i]) = (Sample(interleaved, at, format.IsFloat), Sample(interleaved, at + (second * format.BitsPerSample / 8), format.IsFloat));
            }
        }

        Scope.Write(_frame);
    }

    public void Dispose()
    {
        _frame?.Dispose();
        _frame = null;
    }

    private static float Sample(ReadOnlySpan<byte> bytes, int at, bool isFloat) => isFloat
        ? BitConverter.ToSingle(bytes.Slice(at, 4))
        : BitConverter.ToInt16(bytes.Slice(at, 2)) / 32768f;
}
