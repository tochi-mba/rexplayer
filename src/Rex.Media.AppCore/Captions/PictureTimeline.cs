namespace Rex.Media.AppCore.Captions;

/// <summary>
/// The pictures read ahead of what is playing (OSD-11), by time, for placing a cluster of words
/// clear of everything that matters for as long as it shows. Readings are kept for a stretch either
/// side of the playhead and forgotten beyond it, so a long film does not fill memory.
/// </summary>
public sealed class PictureTimeline
{
    /// <summary>How far behind the playhead readings are kept, in seconds.</summary>
    public const double KeptBehind = 10;

    private readonly SortedList<double, PictureSpace> _readings = [];
    private readonly object _gate = new();

    /// <summary>How many readings are held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _readings.Count;
            }
        }
    }

    /// <summary>The latest time read, in seconds; negative infinity before any.</summary>
    public double ReadTo
    {
        get
        {
            lock (_gate)
            {
                return _readings.Count > 0 ? _readings.Keys[^1] : double.NegativeInfinity;
            }
        }
    }

    /// <summary>Keeps the picture read at <paramref name="seconds"/>.</summary>
    public void Add(double seconds, PictureSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        lock (_gate)
        {
            _readings[seconds] = space;
        }
    }

    /// <summary>
    /// What matters from <paramref name="from"/> to <paramref name="to"/> seconds: every reading in
    /// the stretch, and the one just before it (the picture showing as it starts), taken together;
    /// null when nothing that early or within it has been read.
    /// </summary>
    public PictureSpace? Over(double from, double to)
    {
        lock (_gate)
        {
            var within = new List<PictureSpace>();
            PictureSpace? before = null;
            foreach (var (at, space) in _readings)
            {
                if (at <= from)
                {
                    before = space;
                }
                else if (at <= to)
                {
                    within.Add(space);
                }
            }

            if (before is not null)
            {
                within.Insert(0, before);
            }

            return within.Count == 0 ? null : within.Count == 1 ? within[0] : PictureSpace.Most(within);
        }
    }

    /// <summary>Forgets readings more than <see cref="KeptBehind"/> seconds before <paramref name="playhead"/>, keeping the latest of them.</summary>
    public void Forget(double playhead)
    {
        lock (_gate)
        {
            while (_readings.Count > 1 && _readings.Keys[1] < playhead - KeptBehind)
            {
                _readings.RemoveAt(0);
            }
        }
    }

    /// <summary>Forgets everything, as on a seek far away or a new file.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _readings.Clear();
        }
    }
}
