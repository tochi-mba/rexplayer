using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Plays audio to nowhere at real-time speed: what the engine uses when there is no audio device, so
/// the clock still advances at the rate a listener would hear. The buffer holds 200 ms, the same
/// order as a shared-mode device, so back-pressure behaves like the real thing.
/// </summary>
public sealed class NullAudioSink : IAudioSink
{
    private readonly TimeProvider _clock;
    private readonly Action<TimeSpan, CancellationToken> _wait;
    private AudioFormat? _format;
    private long _written;
    private long _startTimestamp;
    private long _pausedAt = -1;
    private long _pausedTicks;
    private bool _started;

    /// <param name="clock">The time source; tests pass a manual one.</param>
    /// <param name="wait">How to wait for the clock; tests advance their manual clock instead of sleeping.</param>
    /// <param name="buffer">How much audio may be queued before writes block.</param>
    public NullAudioSink(TimeProvider? clock = null, Action<TimeSpan, CancellationToken>? wait = null, TimeSpan? buffer = null)
    {
        _clock = clock ?? TimeProvider.System;
        _wait = wait ?? ((span, token) => token.WaitHandle.WaitOne(span));
        Buffer = buffer ?? TimeSpan.FromMilliseconds(200);
    }

    public string Name => "No audio device";

    public TimeSpan Buffer { get; }

    public bool IsRealTime => true;

    public long PlayedSamples => Math.Min(_written, ElapsedSamples());

    public long QueuedSamples => _written - PlayedSamples;

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        _format = new AudioFormat(preferred.SampleRate, preferred.Channels, SampleFormat.F32, preferred.Layout);
        _pausedAt = -1;
        Restart();
        return _format;
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var format = _format ?? throw new InvalidOperationException("The sink is not open.");
        var limit = (long)(Buffer.TotalSeconds * format.SampleRate);
        while (_written - ElapsedSamples() > limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _wait(TimeSpan.FromMilliseconds(5), cancellationToken);
        }

        if (!_started)
        {
            // Like a device, the clock runs from the first audio, not from opening: whatever the
            // host does between the two (opening a window, say) is not time anyone heard.
            _started = true;
            _startTimestamp = _pausedAt >= 0 ? _pausedAt : _clock.GetTimestamp();
            _pausedTicks = 0;
        }

        _written += frame.SampleCount;
    }

    public void Pause()
    {
        if (_pausedAt < 0)
        {
            _pausedAt = _clock.GetTimestamp();
        }
    }

    public void Resume()
    {
        if (_pausedAt >= 0)
        {
            _pausedTicks += _clock.GetTimestamp() - _pausedAt;
            _pausedAt = -1;
        }
    }

    public void Flush() => Restart();

    public void Drain(CancellationToken cancellationToken)
    {
        while (ElapsedSamples() < _written)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _wait(TimeSpan.FromMilliseconds(5), cancellationToken);
        }
    }

    public void Dispose()
    {
    }

    private void Restart()
    {
        _written = 0;
        _started = false;
        _pausedTicks = 0;
    }

    private long ElapsedSamples()
    {
        if (_format is null || !_started)
        {
            return 0;
        }

        var now = _pausedAt >= 0 ? _pausedAt : _clock.GetTimestamp();
        var ticks = now - _startTimestamp - _pausedTicks;
        return Math.Max(0, (long)((Int128)ticks * _format.SampleRate / _clock.TimestampFrequency));
    }
}
