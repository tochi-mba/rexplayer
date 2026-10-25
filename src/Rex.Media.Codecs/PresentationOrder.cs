namespace Rex.Media.Codecs;

/// <summary>
/// Puts decoded pictures into presentation order for a decoder that may hand them over in
/// decode order (B-frames come after the frames they sit between). Pictures are held, sorted by
/// time, until more than <see cref="Depth"/> are waiting; the earliest then goes out. At the end
/// of a stream the rest go out in order. Pictures without a time sort first, as they arrived.
/// </summary>
public sealed class PresentationOrder<T>
{
    private readonly Func<T, long> _time;
    private readonly List<T> _held = [];

    /// <param name="depth">How many pictures may wait for an earlier one: the deepest reordering a stream may use.</param>
    /// <param name="time">A picture's presentation time.</param>
    public PresentationOrder(int depth, Func<T, long> time)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentNullException.ThrowIfNull(time);
        Depth = depth;
        _time = time;
    }

    public int Depth { get; }

    public int Count => _held.Count;

    /// <summary>Takes a picture, keeping the held ones sorted (later arrivals with an equal time go after).</summary>
    public void Add(T picture)
    {
        var time = _time(picture);
        var at = _held.Count;
        while (at > 0 && _time(_held[at - 1]) > time)
        {
            at--;
        }

        _held.Insert(at, picture);
    }

    /// <summary>Hands over the earliest pictures while more than <see cref="Depth"/> are held.</summary>
    public void Release(ICollection<T> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        while (_held.Count > Depth)
        {
            output.Add(_held[0]);
            _held.RemoveAt(0);
        }
    }

    /// <summary>Hands over every held picture in order, for the end of a stream.</summary>
    public void Drain(ICollection<T> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var picture in _held)
        {
            output.Add(picture);
        }

        _held.Clear();
    }

    /// <summary>Forgets every held picture after a seek, giving each to <paramref name="discard"/> (to dispose it).</summary>
    public void Clear(Action<T> discard)
    {
        ArgumentNullException.ThrowIfNull(discard);
        foreach (var picture in _held)
        {
            discard(picture);
        }

        _held.Clear();
    }
}
