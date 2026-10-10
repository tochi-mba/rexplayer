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
/// sink's played-sample count, relative to the timestamp the current run of audio started at. At a
/// speed other than 1 each sample heard stands for that many samples of the media.
/// <para>
/// A gapless run hands from one item to the next inside one run of audio: the next item's first
/// sample is written behind the last one's tail, and is heard only once the device has played that
/// tail. <see cref="Continue"/> marks the place, so the clock moves to the new item's own time
/// exactly when its first sample is heard, and <see cref="TakeStarted"/> says which items began.
/// Each item's time can be asked for apart (<see cref="NowFor"/>): an item still to come is before
/// its start, one already over is long past its end, so pictures of each are shown at their moment.
/// </para>
/// </summary>
public sealed class AudioClock : IMediaClock
{
    /// <summary>How far off an item the clock has not been told of yet is: its pictures wait until it is.</summary>
    private static readonly MediaTime NotYet = MediaTime.FromSeconds(-3600);

    private readonly IAudioSink _sink;
    private readonly object _gate = new();
    private readonly Queue<Segment> _coming = new();
    // Old timestamps remain queryable while a video worker still owns that item, without keeping
    // every disposed item of an indefinitely running playlist alive.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Segment> _past = new();
    private readonly Queue<object> _started = new();
    private Segment _heard;
    private long _written;
    private int _sampleRate = 48_000;
    private double _speed = 1;

    /// <param name="sink">The device whose played samples are counted.</param>
    /// <param name="item">What plays first; it is not reported as started, being where the clock begins.</param>
    public AudioClock(IAudioSink sink, object? item = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _heard = new Segment(0, MediaTime.Zero, item);
    }

    /// <summary>The time of the item being heard.</summary>
    public MediaTime Now => NowFor(null);

    /// <summary>The item being heard, as given to <see cref="Rebase"/> or <see cref="Continue"/>.</summary>
    public object? Heard
    {
        get
        {
            lock (_gate)
            {
                Advance(_sink.PlayedSamples);
                return _heard.Item;
            }
        }
    }

    /// <summary>
    /// The time in <paramref name="item"/>'s own terms: negative while it is still to come (far off
    /// while its sound has not even been written), past its end once it is over.
    /// </summary>
    public MediaTime NowFor(object? item)
    {
        lock (_gate)
        {
            var played = _sink.PlayedSamples;
            Advance(played);
            var segment = item is null || ReferenceEquals(item, _heard.Item)
                ? _heard
                : _past.TryGetValue(item, out var past) ? past
                : _coming.FirstOrDefault(candidate => ReferenceEquals(candidate.Item, item));
            return segment is null ? NotYet : segment.Anchor + Scaled(played - segment.At);
        }
    }

    /// <summary>
    /// Called after the sink is flushed or opened: the next sample heard is at <paramref name="anchor"/>,
    /// of <paramref name="item"/> (null: the same item as before), and from there the media moves
    /// <paramref name="speed"/> times as fast as the sound is heard.
    /// </summary>
    public void Rebase(MediaTime anchor, int sampleRate, double speed = 1, object? item = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);
        lock (_gate)
        {
            _coming.Clear();
            _past.Clear();
            _started.Clear();
            item ??= _heard.Item;
            if (item is not null && !ReferenceEquals(item, _heard.Item))
            {
                _started.Enqueue(item);
            }

            _heard = new Segment(0, anchor, item);
            _written = 0;
            _sampleRate = sampleRate;
            _speed = speed;
        }
    }

    /// <summary>Counts samples just written to the sink, which places the next <see cref="Continue"/>.</summary>
    public void Wrote(long samples)
    {
        lock (_gate)
        {
            _written += samples;
        }
    }

    /// <summary>The next sample written starts <paramref name="item"/>, at <paramref name="anchor"/> of its own time.</summary>
    public void Continue(MediaTime anchor, object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            _coming.Enqueue(new Segment(_written, anchor, item));
        }
    }

    /// <summary>The items whose first sample has been heard since the last call, in order.</summary>
    public IReadOnlyList<object> TakeStarted()
    {
        lock (_gate)
        {
            Advance(_sink.PlayedSamples);
            var started = _started.ToArray();
            _started.Clear();
            return started;
        }
    }

    private void Advance(long played)
    {
        while (_coming.TryPeek(out var next) && next.At <= played)
        {
            _coming.Dequeue();
            if (_heard.Item is { } previous)
            {
                _past.AddOrUpdate(previous, _heard);
            }
            _heard = next;
            _started.Enqueue(next.Item!);
        }
    }

    private MediaTime Scaled(long samples)
    {
        var time = samples >= 0 ? MediaTime.FromSamples(samples, _sampleRate) : MediaTime.Zero - MediaTime.FromSamples(-samples, _sampleRate);
        return _speed == 1 ? time : new MediaTime((long)(time.Ticks * _speed));
    }

    private sealed record Segment(long At, MediaTime Anchor, object? Item);
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
