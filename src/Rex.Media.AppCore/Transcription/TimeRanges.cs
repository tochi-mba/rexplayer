namespace Rex.Media.AppCore.Transcription;

/// <summary>A stretch of a media file's timeline, from <see cref="Start"/> up to but not including <see cref="End"/>.</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;

    /// <summary>Whether <paramref name="time"/> falls in the stretch.</summary>
    public bool Contains(TimeSpan time) => time >= Start && time < End;
}

/// <summary>
/// The stretches of a timeline that are done (SUB-21): kept sorted and apart, touching or
/// overlapping stretches joined into one, so a file transcribed in any order is as many stretches
/// as it has holes, and every question is a binary search. Immutable: a change is a new set.
/// </summary>
public sealed class TimeRanges
{
    private readonly TimeRange[] _ranges;

    private TimeRanges(TimeRange[] ranges) => _ranges = ranges;

    /// <summary>Nothing done.</summary>
    public static TimeRanges Empty { get; } = new([]);

    /// <summary>The stretches, sorted and apart.</summary>
    public IReadOnlyList<TimeRange> Ranges => _ranges;

    /// <summary>How much of the timeline is done.</summary>
    public TimeSpan Total => _ranges.Aggregate(TimeSpan.Zero, (sum, range) => sum + range.Length);

    /// <summary>The set from any stretches, in any order; empty and backward ones are passed over.</summary>
    public static TimeRanges Of(IEnumerable<TimeRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        return ranges.Aggregate(Empty, (set, range) => set.Add(range));
    }

    /// <summary>The set with <paramref name="range"/> done too.</summary>
    public TimeRanges Add(TimeRange range)
    {
        if (range.End <= range.Start)
        {
            return this;
        }

        // Every stretch that touches the new one is swallowed by it.
        var first = FirstEndingAtOrAfter(range.Start);
        var last = first;
        var (start, end) = (range.Start, range.End);
        while (last < _ranges.Length && _ranges[last].Start <= end)
        {
            start = _ranges[last].Start < start ? _ranges[last].Start : start;
            end = _ranges[last].End > end ? _ranges[last].End : end;
            last++;
        }

        var joined = new TimeRange[_ranges.Length - (last - first) + 1];
        Array.Copy(_ranges, joined, first);
        joined[first] = new TimeRange(start, end);
        Array.Copy(_ranges, last, joined, first + 1, _ranges.Length - last);
        return new TimeRanges(joined);
    }

    /// <summary>The stretch holding <paramref name="time"/>, or null when it is not done.</summary>
    public TimeRange? At(TimeSpan time)
    {
        var index = FirstEndingAtOrAfter(time);
        return index < _ranges.Length && _ranges[index].Contains(time) ? _ranges[index] : null;
    }

    /// <summary>Whether all of <paramref name="range"/> is done.</summary>
    public bool Covers(TimeRange range) => range.End <= range.Start || (At(range.Start) is { } holding && holding.End >= range.End);

    /// <summary>The stretches of <paramref name="within"/> not done yet, in order.</summary>
    public IReadOnlyList<TimeRange> Gaps(TimeRange within)
    {
        var gaps = new List<TimeRange>();
        var from = within.Start;
        for (var i = FirstEndingAtOrAfter(within.Start); i < _ranges.Length && from < within.End; i++)
        {
            if (_ranges[i].Start > from)
            {
                gaps.Add(new TimeRange(from, _ranges[i].Start < within.End ? _ranges[i].Start : within.End));
            }

            from = _ranges[i].End > from ? _ranges[i].End : from;
        }

        if (from < within.End)
        {
            gaps.Add(new TimeRange(from, within.End));
        }

        return gaps;
    }

    /// <summary>The first stretch that ends after <paramref name="time"/> (one ending exactly at it touches it): a binary search.</summary>
    private int FirstEndingAtOrAfter(TimeSpan time)
    {
        int low = 0, high = _ranges.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_ranges[middle].End < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
