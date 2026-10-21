namespace Rex.Media.Audio;

/// <summary>
/// Interleaved float samples handed from the engine's audio thread to a device that pulls them on
/// its own thread (a CoreAudio render callback): one writer, one reader, no locks and no
/// allocation once made. The reader's count of frames actually taken is what the device has
/// played, so it serves as the master clock; when the reader asks for more than is queued it gets
/// silence for the rest, and that silence is not counted.
/// </summary>
public sealed class SampleRing
{
    private readonly float[] _samples;
    private long _written;
    private long _read;

    /// <param name="frames">How many frames the ring holds before writes must wait.</param>
    /// <param name="channels">Samples per frame.</param>
    public SampleRing(int frames, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        Channels = channels;
        CapacityFrames = frames;
        _samples = new float[frames * channels];
    }

    public int Channels { get; }

    public int CapacityFrames { get; }

    /// <summary>Frames the reader has taken since the ring was made or cleared: what has been played.</summary>
    public long FramesRead => Volatile.Read(ref _read) / Channels;

    /// <summary>Frames written and not yet taken.</summary>
    public int QueuedFrames => (int)((Volatile.Read(ref _written) - Volatile.Read(ref _read)) / Channels);

    /// <summary>Frames that fit now without overwriting anything unread.</summary>
    public int FreeFrames => CapacityFrames - QueuedFrames;

    /// <summary>
    /// Writes as many whole frames of <paramref name="interleaved"/> as fit, and says how many
    /// samples that was; the writer waits and offers the rest again.
    /// </summary>
    public int Write(ReadOnlySpan<float> interleaved)
    {
        var count = Math.Min(interleaved.Length / Channels, FreeFrames) * Channels;
        var start = (int)(Volatile.Read(ref _written) % _samples.Length);
        var first = Math.Min(count, _samples.Length - start);
        interleaved[..first].CopyTo(_samples.AsSpan(start));
        interleaved[first..count].CopyTo(_samples);
        Volatile.Write(ref _written, _written + count);
        return count;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with queued samples, then silence; says how many frames
    /// were real. Called on the device's thread.
    /// </summary>
    public int Read(Span<float> destination)
    {
        var wanted = destination.Length / Channels * Channels;
        var count = Math.Min(wanted, QueuedFrames * Channels);
        var start = (int)(Volatile.Read(ref _read) % _samples.Length);
        var first = Math.Min(count, _samples.Length - start);
        _samples.AsSpan(start, first).CopyTo(destination);
        _samples.AsSpan(0, count - first).CopyTo(destination[first..]);
        destination[count..].Clear();
        Volatile.Write(ref _read, _read + count);
        return count / Channels;
    }

    /// <summary>
    /// Forgets everything queued and starts the played count again, for a seek. Only while the
    /// reader is stopped: the device must not be pulling at the same moment.
    /// </summary>
    public void Clear()
    {
        Volatile.Write(ref _read, 0);
        Volatile.Write(ref _written, 0);
    }
}
