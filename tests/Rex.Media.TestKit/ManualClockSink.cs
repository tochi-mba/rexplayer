using Rex.Media.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>
/// A real-time audio sink whose clock the test moves by hand: it accepts audio at once, and reports
/// as played exactly the samples the test says, so video pacing can be checked without racing a timer.
/// </summary>
public sealed class ManualClockSink : IAudioSink
{
    private long _played;

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

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Flush() => PlayedSamples = 0;

    public void Drain(CancellationToken cancellationToken)
    {
    }

    public void Dispose()
    {
    }
}
