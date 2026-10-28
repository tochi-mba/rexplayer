namespace Rex.Media.AppCore.Library;

/// <summary>
/// The decoded pictures kept in memory, sized in bytes rather than counted: each picture costs
/// what its pixels take once decoded, and the least recently shown go first when the total passes
/// the ceiling. The window trims it further when the view changes (<see cref="TrimTo"/>), so a
/// library left behind does not keep hundreds of megabytes of artwork nobody is looking at.
/// </summary>
public sealed class PictureMemory<T> where T : class
{
    private readonly Dictionary<string, LinkedListNode<(string Key, T Value, long Bytes)>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Key, T Value, long Bytes)> _order = new();
    private long _ceiling;

    public PictureMemory(long ceiling) => _ceiling = Math.Max(0, ceiling);

    /// <summary>The most the pictures may take together; lowering it lets the oldest go at once.</summary>
    public long Ceiling
    {
        get => _ceiling;
        set
        {
            _ceiling = Math.Max(0, value);
            TrimTo(_ceiling);
        }
    }

    /// <summary>What the kept pictures take now, in bytes.</summary>
    public long Bytes { get; private set; }

    public int Count => _index.Count;

    /// <summary>The picture under <paramref name="key"/>, which becomes the most recently shown.</summary>
    public bool TryGet(string key, out T value)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>Keeps <paramref name="value"/> as the most recently shown; one larger than the ceiling is not kept.</summary>
    public void Add(string key, T value, long bytes)
    {
        Remove(key);
        bytes = Math.Max(1, bytes);
        if (bytes > _ceiling)
        {
            return;
        }

        _index[key] = _order.AddFirst((key, value, bytes));
        Bytes += bytes;
        TrimTo(_ceiling);
    }

    /// <summary>Lets the least recently shown pictures go until the rest take no more than <paramref name="bytes"/>.</summary>
    public void TrimTo(long bytes)
    {
        while (Bytes > Math.Max(0, bytes) && _order.Last is { } oldest)
        {
            Remove(oldest.Value.Key);
        }
    }

    public void Clear()
    {
        _index.Clear();
        _order.Clear();
        Bytes = 0;
    }

    private void Remove(string key)
    {
        if (_index.Remove(key, out var node))
        {
            _order.Remove(node);
            Bytes -= node.Value.Bytes;
        }
    }
}
