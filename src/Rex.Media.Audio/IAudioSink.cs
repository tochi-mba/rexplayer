using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Where audio ends up: a WASAPI endpoint, a WAV file, or nowhere at real-time speed. The engine's
/// audio pump writes float frames in the sink's own format (it resamples and remixes first) and reads
/// <see cref="PlayedSamples"/> as the master clock, so a sink must count only what has actually been
/// heard, not what has merely been queued.
/// </summary>
public interface IAudioSink : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Prepares the sink for a stream. The sink may ask for a different rate or channel count than
    /// <paramref name="preferred"/> (a device has a mix format); the returned format is what every
    /// frame passed to <see cref="Write"/> must use.
    /// </summary>
    AudioFormat Open(AudioFormat preferred);

    /// <summary>
    /// Queues a frame, blocking while the sink's buffer is full: this back-pressure is what paces
    /// playback. The caller keeps ownership of the frame.
    /// </summary>
    void Write(AudioFrame frame, CancellationToken cancellationToken);

    /// <summary>Samples per channel heard since the last <see cref="Open"/> or <see cref="Flush"/>.</summary>
    long PlayedSamples { get; }

    /// <summary>Samples queued but not yet heard.</summary>
    long QueuedSamples { get; }

    /// <summary>True when playback runs at the speed of a real clock (false for a file writer).</summary>
    bool IsRealTime { get; }

    void Pause();

    void Resume();

    /// <summary>Discards queued audio and restarts the played count at zero, for a seek.</summary>
    void Flush();

    /// <summary>Blocks until everything queued has been heard, for the end of a stream.</summary>
    void Drain(CancellationToken cancellationToken);
}
