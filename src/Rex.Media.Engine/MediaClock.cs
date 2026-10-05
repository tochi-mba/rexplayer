using Rex.Media.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>The time everything is presented against (ADR-005). Video follows it; it never follows video.</summary>
public interface IMediaClock
{
    MediaTime Now { get; }
}

/// <summary>
/// The master clock while audio plays: the position of the sample being heard right now, from the
/// sink's played-sample count, relative to the timestamp the current run of audio started at.
/// </summary>
public sealed class AudioClock : IMediaClock
{
    private readonly IAudioSink _sink;
    private readonly object _gate = new();
    private MediaTime _anchor = MediaTime.Zero;
    private int _sampleRate = 48_000;

    public AudioClock(IAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    public MediaTime Now
    {
        get
        {
            lock (_gate)
            {
                return _anchor + MediaTime.FromSamples(_sink.PlayedSamples, _sampleRate);
            }
        }
    }

    /// <summary>Called after the sink is flushed or opened: the next sample heard is at <paramref name="anchor"/>.</summary>
    public void Rebase(MediaTime anchor, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        lock (_gate)
        {
            _anchor = anchor;
            _sampleRate = sampleRate;
        }
    }
}

/// <summary>
/// The clock when there is no audio (or the device is gone): real time from an anchor, stopped while
/// paused, scaled by the playback rate.
/// </summary>
public sealed class SystemClock : IMediaClock
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private MediaTime _anchor = MediaTime.Zero;
    private long _anchorTimestamp;
    private bool _running;
    private double _rate = 1.0;

    public SystemClock(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _anchorTimestamp = _time.GetTimestamp();
    }

    public MediaTime Now
    {
        get
        {
            lock (_gate)
            {
                return Current();
            }
        }
    }

    public double Rate
    {
        get
        {
            lock (_gate)
            {
                return _rate;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lock (_gate)
            {
                _anchor = Current();
                _anchorTimestamp = _time.GetTimestamp();
                _rate = value;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (!_running)
            {
                _anchorTimestamp = _time.GetTimestamp();
                _running = true;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _anchor = Current();
            _running = false;
        }
    }

    public void Rebase(MediaTime anchor)
    {
        lock (_gate)
        {
            _anchor = anchor;
            _anchorTimestamp = _time.GetTimestamp();
        }
    }

    private MediaTime Current()
    {
        if (!_running)
        {
            return _anchor;
        }

        var elapsed = _time.GetElapsedTime(_anchorTimestamp);
        return _anchor + new MediaTime((long)(elapsed.Ticks * _rate));
    }
}
