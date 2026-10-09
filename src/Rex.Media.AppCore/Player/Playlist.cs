using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>An entry in the playlist. A class, so the same file added twice is two entries.</summary>
public sealed class PlaylistItem(string location)
{
    /// <summary>A file path or a URL.</summary>
    public string Location { get; } = location;

    /// <summary>What the playlist shows until the media's own title is known.</summary>
    public string Title { get; set; } = TitleOf(location);

    /// <summary>Who made it, when a playlist says.</summary>
    public string? Artist { get; init; }

    /// <summary>Where in the file this item starts: zero, or a cue sheet's track in a whole-album file.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Where in the file this item ends, or null for the end of the file.</summary>
    public TimeSpan? End { get; init; }

    /// <summary>Whether this is a part of a file rather than all of it; its own title and artist then win over the file's.</summary>
    public bool IsPart => Start > TimeSpan.Zero || End is not null;

    /// <summary>
    /// The file name without its extension. Both separators count, whatever system this is: a
    /// playlist made on Windows still names its songs when it is played elsewhere.
    /// </summary>
    public static string TitleOf(string location)
    {
        var name = Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile ? uri.Segments[^1] : location[(location.LastIndexOfAny(['/', '\\']) + 1)..];
        var title = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(name));
        return title.Length > 0 ? title : location;
    }
}

/// <summary>
/// The list of things to play and the order they play in. The list (<see cref="Items"/>) is what
/// the user sees and rearranges; the play order is the same list, or with shuffle on a permutation
/// of it in which nothing plays twice until everything has played once. <see cref="PeekNext"/> says
/// what <see cref="Next"/> will give without moving, so the next item can be queued for a gapless
/// start before the current one ends.
/// </summary>
public sealed class Playlist(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly List<PlaylistItem> _items = [];
    private List<PlaylistItem> _order = [];
    private List<PlaylistItem>? _nextCycle;
    private int _position = -1;
    private bool _shuffle;

    /// <summary>Raised after any change to the list, the current item or the modes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<PlaylistItem> Items => _items;

    /// <summary>What is playing, or null before the first item and after the last.</summary>
    public PlaylistItem? Current { get; private set; }

    public int CurrentIndex => Current is null ? -1 : _items.IndexOf(Current);

    public RepeatMode Repeat
    {
        get;
        set
        {
            field = value;
            Raise();
        }
    }

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value)
            {
                return;
            }

            _shuffle = value;
            _nextCycle = null;
            if (value)
            {
                // The current item stays first; everything else plays in a fresh random order.
                _order = Current is null ? Shuffled(_items) : [Current, .. Shuffled(_items.Where(item => item != Current))];
                _position = Current is null ? -1 : 0;
            }
            else
            {
                Unshuffle();
            }

            Raise();
        }
    }

    /// <summary>Adds items to the end of the list; with shuffle on they join the part still to play at random places.</summary>
    public void Add(IEnumerable<PlaylistItem> items) => Insert(_items.Count, items);

    /// <summary>Inserts items into the list at <paramref name="index"/>.</summary>
    public void Insert(int index, IEnumerable<PlaylistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var added = items.ToList();
        _items.InsertRange(Math.Clamp(index, 0, _items.Count), added);
        _nextCycle = null;
        if (_shuffle)
        {
            foreach (var item in added)
            {
                _order.Insert(_random.Next(_position + 1, _order.Count + 1), item);
            }
        }
        else
        {
            Unshuffle();
        }

        Raise();
    }

    /// <summary>Empties the queue.</summary>
    public void Clear()
    {
        _items.Clear();
        _order.Clear();
        _nextCycle = null;
        _position = -1;
        Current = null;
        Raise();
    }

    /// <summary>
    /// Removes the item at <paramref name="index"/>. Removing what is playing leaves nothing current,
    /// but <see cref="Next"/> still continues with what would have followed it.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _nextCycle = null;
        var at = _order.IndexOf(item);
        _order.RemoveAt(at);
        if (at <= _position)
        {
            _position--;
        }

        if (item == Current)
        {
            Current = null;
        }

        Raise();
    }

    /// <summary>Puts <paramref name="item"/> in place of the one at <paramref name="index"/>, in its place in the play order too.</summary>
    public void Replace(int index, PlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var old = _items[index];
        _items[index] = item;
        _order[_order.IndexOf(old)] = item;
        _nextCycle = null;
        if (old == Current)
        {
            Current = item;
        }

        Raise();
    }

    /// <summary>Moves an item within the list (the play order follows unless shuffle is on).</summary>
    public void Move(int from, int to)
    {
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(Math.Clamp(to, 0, _items.Count), item);
        if (!_shuffle)
        {
            Unshuffle();
        }

        Raise();
    }

    /// <summary>Makes the item at <paramref name="index"/> current, as when the user picks it.</summary>
    public PlaylistItem JumpTo(int index)
    {
        var item = _items[index];
        if (_shuffle)
        {
            // Picked out of turn: it plays now and the rest of the shuffled order carries on after it.
            var at = _order.IndexOf(item);
            _order.RemoveAt(at);
            if (at <= _position)
            {
                _position--;
            }

            // After the end the position is past the last item, so the pick goes on the end.
            var insertAt = Math.Min(_position + 1, _order.Count);
            _order.Insert(insertAt, item);
            _position = insertAt;
        }
        else
        {
            _position = index;
        }

        _nextCycle = null;
        Current = item;
        Raise();
        return item;
    }

    /// <summary>
    /// What <see cref="Next"/> would give, without moving. <paramref name="automatic"/> is true when
    /// the current item ended by itself (then repeat-one plays it again), false when the user asked.
    /// </summary>
    public PlaylistItem? PeekNext(bool automatic)
    {
        if (automatic && Repeat == RepeatMode.One && Current is not null)
        {
            return Current;
        }

        if (_position + 1 < _order.Count)
        {
            return _order[_position + 1];
        }

        return Repeat != RepeatMode.Off && _order.Count > 0 ? NextCycle()[0] : null;
    }

    /// <summary>Moves to the next item and returns it, or returns null at the end of the queue.</summary>
    public PlaylistItem? Next(bool automatic)
    {
        if (automatic && Repeat == RepeatMode.One && Current is not null)
        {
            return Current;
        }

        if (_position + 1 < _order.Count)
        {
            _position++;
        }
        else if (Repeat != RepeatMode.Off && _order.Count > 0)
        {
            _order = NextCycle();
            _nextCycle = null;
            _position = 0;
        }
        else
        {
            _position = _order.Count;
            Current = null;
            Raise();
            return null;
        }

        Current = _order[_position];
        Raise();
        return Current;
    }

    /// <summary>
    /// Moves to the previous item and returns it. Before the first item, repeat-all wraps to the
    /// last; otherwise the first item stays (and starts again). Null only when the queue is empty.
    /// </summary>
    public PlaylistItem? Previous()
    {
        if (_order.Count == 0)
        {
            return null;
        }

        // With nothing current (its item was removed) the position is already the item before it.
        var target = Current is null && _position >= 0 && _position < _order.Count ? _position : _position - 1;
        _position = target >= 0 ? target
            : Repeat == RepeatMode.All ? _order.Count - 1
            : 0;
        _nextCycle = null;
        Current = _order[_position];
        Raise();
        return Current;
    }

    /// <summary>The order of the next pass through the queue, chosen once so a peek and the move agree.</summary>
    private List<PlaylistItem> NextCycle()
    {
        if (_nextCycle is null)
        {
            _nextCycle = _shuffle ? Shuffled(_items) : [.. _items];
            if (_shuffle && _nextCycle.Count > 1 && _nextCycle[0] == _order[^1])
            {
                // Never the same item twice in a row across the wrap.
                (_nextCycle[0], _nextCycle[^1]) = (_nextCycle[^1], _nextCycle[0]);
            }
        }

        return _nextCycle;
    }

    private void Unshuffle()
    {
        _order = [.. _items];
        _position = Current is null ? Math.Min(_position, _order.Count) : _order.IndexOf(Current);
    }

    private List<PlaylistItem> Shuffled(IEnumerable<PlaylistItem> items)
    {
        var list = items.ToList();
        _random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return list;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
