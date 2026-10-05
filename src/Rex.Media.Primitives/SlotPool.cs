namespace Rex.Media.Primitives;

/// <summary>
/// A fixed number of slots holding reusable objects, claimed and filled with compare-and-swap.
/// Taking and returning never allocate and never block; when every slot is full, a returned object
/// is simply dropped for the garbage collector.
/// </summary>
public sealed class SlotPool<T>
    where T : class
{
    private readonly T?[] _slots;

    public SlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new T?[capacity];
    }

    public int Capacity => _slots.Length;

    /// <summary>A pooled object, or null when the pool is empty.</summary>
    public T? Take()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var candidate = Volatile.Read(ref _slots[i]);
            if (candidate is not null && Interlocked.CompareExchange(ref _slots[i], null, candidate) == candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Offers <paramref name="item"/> back; false when every slot was full.</summary>
    public bool Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        for (var i = 0; i < _slots.Length; i++)
        {
            if (Interlocked.CompareExchange(ref _slots[i], item, null) is null)
            {
                return true;
            }
        }

        return false;
    }
}
