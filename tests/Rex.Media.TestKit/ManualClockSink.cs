using Rex.Media.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>
/// A real-time audio sink whose clock the test moves by hand: it accepts audio at once, reports as
/// played exactly the samples the test says, and drains once the test has played everything, so
/// video pacing and the end of playback can be checked without racing a timer.
/// </summary>
public sealed class ManualClockSink : IAudioSink
{
    private long _played;
    private long _written;

    public string Name => "manual clock";

    public bool IsRealTime => true;

    /// <summary>Samples heard so far, as the test sets them.</summary>
    public long PlayedSamples
    {
        get => Interlocked.Read(ref _played);
        set => Interlocked.Exchange(ref _played, value);
    }

    public long QueuedSamples => 0;

    public AudioFormat Open(AudioFormat preferred) => preferred;

    /// <summary>Samples written so far.</summary>
    public long WrittenSamples => Interlocked.Read(ref _written);

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Interlocked.Add(ref _written, frame.SampleCount);
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Flush()
    {
        PlayedSamples = 0;
        Interlocked.Exchange(ref _written, 0);
    }

    /// <summary>Waits, as a device does, until the test has moved the clock past everything written.</summary>
    public void Drain(CancellationToken cancellationToken)
    {
        while (PlayedSamples < WrittenSamples)
        {
            cancellationToken.WaitHandle.WaitOne(1);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
    }
}
