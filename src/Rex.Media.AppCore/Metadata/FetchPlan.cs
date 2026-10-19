namespace Rex.Media.AppCore.Metadata;

/// <summary>One lookup the library wants: what kind, and the name or path that identifies it.</summary>
public readonly record struct MetadataJob(MetadataWork Kind, string Key);

/// <summary>
/// What to fetch next. Jobs run one at a time in the order they were asked for, never while the
/// caller says playback could stutter, and a failure holds the whole queue back - a minute at
/// first, doubling to an hour - so a service that is down or refusing is left alone, and the queue
/// resumes where it left off when it answers again. Not thread-safe; the one background fetcher
/// owns it.
/// </summary>
public sealed class FetchPlan
{
    /// <summary>How long the first failure holds the queue back.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);

    /// <summary>The longest a run of failures holds the queue back.</summary>
    public static readonly TimeSpan LongestDelay = TimeSpan.FromHours(1);

    private readonly Queue<MetadataJob> _waiting = new();
    private readonly HashSet<MetadataJob> _known = [];
    private int _failures;
    private DateTimeOffset _heldUntil = DateTimeOffset.MinValue;

    /// <summary>How many jobs wait, the one handed out included until it is done or failed.</summary>
    public int Count => _known.Count;

    /// <summary>Puts a job at the back of the queue; false when it is already queued or handed out.</summary>
    public bool Add(MetadataJob job)
    {
        if (!_known.Add(job))
        {
            return false;
        }

        _waiting.Enqueue(job);
        return true;
    }

    /// <summary>When the queue can hand out work again; null when it can right now (or has none).</summary>
    public DateTimeOffset? HeldUntil(DateTimeOffset now) => _heldUntil > now ? _heldUntil : null;

    /// <summary>
    /// The next job to run, or null while there is none, playback could stutter, or failures hold
    /// the queue back. The caller reports how it went with <see cref="Done"/> or <see cref="Failed"/>.
    /// </summary>
    public MetadataJob? Next(DateTimeOffset now, bool playing = false) =>
        playing || _heldUntil > now || _waiting.Count == 0 ? null : _waiting.Dequeue();

    /// <summary>The job succeeded: the run of failures is over, and the job may be queued again one day (a refresh).</summary>
    public void Done(MetadataJob job)
    {
        _known.Remove(job);
        _failures = 0;
        _heldUntil = DateTimeOffset.MinValue;
    }

    /// <summary>The job failed: it goes to the back to try again, and the whole queue backs off.</summary>
    public void Failed(MetadataJob job, DateTimeOffset now)
    {
        _waiting.Enqueue(job);
        var delay = FirstDelay * Math.Pow(2, Math.Min(_failures, 10));
        _failures++;
        _heldUntil = now + (delay < LongestDelay ? delay : LongestDelay);
    }

    /// <summary>The job is no longer wanted (its switch turned off, its folder excluded): forgotten without touching the backoff.</summary>
    public void Drop(MetadataJob job)
    {
        if (_known.Remove(job))
        {
            var kept = _waiting.Where(waiting => waiting != job).ToList();
            _waiting.Clear();
            foreach (var waiting in kept)
            {
                _waiting.Enqueue(waiting);
            }
        }
    }
}
