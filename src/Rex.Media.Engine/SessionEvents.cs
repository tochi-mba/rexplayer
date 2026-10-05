using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>Something that happened in a session, delivered in order on the event thread.</summary>
public abstract record SessionEvent;

public sealed record StateChangedEvent(SessionState From, SessionState To) : SessionEvent;

public sealed record MediaOpenedEvent(MediaInfo Info) : SessionEvent;

public sealed record TracksChangedEvent(int? AudioTrack, int? VideoTrack, int? SubtitleTrack) : SessionEvent;

public sealed record SeekCompletedEvent(MediaTime Position) : SessionEvent;

public sealed record EndedEvent : SessionEvent;

/// <summary>A track stopped playing (its decoder could not open or kept failing); the rest continue.</summary>
public sealed record TrackFailedEvent(int TrackId, string Reason) : SessionEvent;

/// <summary>The session cannot continue.</summary>
public sealed record ErrorEvent(string Message) : SessionEvent;

/// <summary>Coalesced: only the latest value matters, so a slow listener never falls behind.</summary>
public sealed record PositionEvent(MediaTime Position, MediaTime Duration) : SessionEvent;

/// <summary>Coalesced: counters for the stats overlay and Media Information.</summary>
public sealed record StatsEvent(SessionStats Stats) : SessionEvent;

/// <summary>Running counters for one session.</summary>
public sealed record SessionStats
{
    public long PacketsRead { get; init; }

    public long BytesRead { get; init; }

    public long AudioFramesDecoded { get; init; }

    public long AudioSamplesPlayed { get; init; }

    public long CorruptPackets { get; init; }

    public string? AudioDecoder { get; init; }
}

/// <summary>
/// Delivers events on one thread, in order (ADR-007). Discrete events are queued and never dropped;
/// position and stats are "latest value wins" slots, so a busy UI thread sees the newest position
/// rather than a backlog. The engine never blocks on a listener: posting only enqueues.
/// </summary>
public sealed class EventPump : IDisposable
{
    private readonly Queue<SessionEvent> _discrete = new();
    private readonly object _gate = new();
    private readonly Action<SessionEvent> _listener;
    private readonly Thread _thread;
    private PositionEvent? _position;
    private StatsEvent? _stats;
    private bool _stopping;

    public EventPump(Action<SessionEvent> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listener = listener;
        _thread = new Thread(Run) { IsBackground = true, Name = "rexplayer events" };
        _thread.Start();
    }

    public void Post(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case PositionEvent position:
                    _position = position;
                    break;
                case StatsEvent stats:
                    _stats = stats;
                    break;
                default:
                    _discrete.Enqueue(sessionEvent);
                    break;
            }

            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Delivers everything already posted, then stops the thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.Pulse(_gate);
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }
    }

    private void Run()
    {
        var batch = new List<SessionEvent>();
        while (true)
        {
            lock (_gate)
            {
                while (_discrete.Count == 0 && _position is null && _stats is null && !_stopping)
                {
                    Monitor.Wait(_gate);
                }

                batch.AddRange(_discrete);
                _discrete.Clear();
                if (_position is not null)
                {
                    batch.Add(_position);
                    _position = null;
                }

                if (_stats is not null)
                {
                    batch.Add(_stats);
                    _stats = null;
                }

                if (batch.Count == 0 && _stopping)
                {
                    return;
                }
            }

            foreach (var sessionEvent in batch)
            {
                _listener(sessionEvent);
            }

            batch.Clear();
        }
    }
}
