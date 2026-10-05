namespace Rex.Media.Engine;

/// <summary>An item in a pipeline queue: a payload, or the end-of-stream marker, tagged with the seek generation.</summary>
internal readonly record struct QueueItem<T>(T? Payload, bool EndOfStream, long Generation)
    where T : class, IDisposable;

/// <summary>
/// A bounded single-producer, single-consumer queue between two pipeline threads. A full queue blocks
/// the producer, which is how back-pressure flows from the sink to the demuxer. Items from an older
/// seek generation are disposed rather than delivered, both when they are added and when they are
/// taken, so a seek never shows a stale frame.
/// </summary>
internal sealed class BoundedQueue<T> : IDisposable
    where T : class, IDisposable
{
    private readonly Queue<QueueItem<T>> _items = new();
    private readonly object _gate = new();
    private readonly int _capacity;
    private long _generation;
    private bool _closed;

    public BoundedQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    /// <summary>Adds an item, waiting while the queue is full. Returns false once the queue is closed.</summary>
    public bool Add(QueueItem<T> item, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Wake);
        lock (_gate)
        {
            while (_items.Count >= _capacity && !_closed && item.Generation >= _generation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(_gate);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_closed || item.Generation < _generation)
            {
                item.Payload?.Dispose();
                return !_closed;
            }

            _items.Enqueue(item);
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /// <summary>
    /// Takes the next item, waiting while there is none. Returns false once closed and empty. Every
    /// queued item is current: <see cref="Flush"/> empties the queue and <see cref="Add"/> refuses
    /// older generations, so nothing stale can be waiting here.
    /// </summary>
    public bool TryTake(out QueueItem<T> item, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Wake);
        lock (_gate)
        {
            while (_items.Count == 0 && !_closed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(_gate);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_items.Count == 0)
            {
                item = default;
                return false;
            }

            item = _items.Dequeue();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /// <summary>Starts a new generation: everything queued is stale and is disposed now.</summary>
    public void Flush(long generation)
    {
        lock (_gate)
        {
            _generation = Math.Max(_generation, generation);
            while (_items.Count > 0)
            {
                _items.Dequeue().Payload?.Dispose();
            }

            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Ends the queue: producers stop, the consumer drains what is left and then sees the end.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            while (_items.Count > 0)
            {
                _items.Dequeue().Payload?.Dispose();
            }

            Monitor.PulseAll(_gate);
        }
    }

    private void Wake()
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }
}
