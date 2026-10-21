namespace Rex.Media.AppCore.Library;

/// <summary>
/// Which of the Audiobooks, Podcasts and Recordings views the library's sidebar lists: each only while
/// it has something to show. The first look shows what is there at once; after that a change waits
/// until it has held for <see cref="Settle"/> with no scan running, so a scan reading a folder of
/// recordings does not make a view blink in and out. A change the person made (moving a file) shows
/// at once. The order is always Audiobooks, Podcasts, Recordings, after the music views.
/// </summary>
public sealed class ListeningSources
{
    /// <summary>How long a change must hold, with no scan running, before the sidebar follows it.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    /// <summary>The views in the order the sidebar lists them.</summary>
    public static readonly IReadOnlyList<AudioShelf> Order = [AudioShelf.Audiobooks, AudioShelf.Podcasts, AudioShelf.Recordings];

    private HashSet<AudioShelf>? _shown;
    private HashSet<AudioShelf>? _pending;
    private DateTimeOffset _pendingSince;

    /// <summary>The views listed now, in <see cref="Order"/>.</summary>
    public IReadOnlyList<AudioShelf> Shown => [.. Order.Where(shelf => _shown?.Contains(shelf) == true)];

    /// <summary>When the sidebar should look again to settle a waiting change; null when nothing waits.</summary>
    public DateTimeOffset? NextLook => _pending is null ? null : _pendingSince + Settle;

    /// <summary>
    /// Takes what has something to show now (<paramref name="present"/>); true when the views listed
    /// changed. <paramref name="scanning"/> holds a change back; <paramref name="immediate"/> (the
    /// person's own move) lets it through at once.
    /// </summary>
    public bool Observe(IReadOnlySet<AudioShelf> present, bool scanning, DateTimeOffset now, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(present);
        var wanted = new HashSet<AudioShelf>(present.Where(Order.Contains));
        if (_shown is null || immediate)
        {
            var changed = _shown is null ? wanted.Count > 0 : !_shown.SetEquals(wanted);
            _shown = wanted;
            _pending = null;
            return changed;
        }

        if (_shown.SetEquals(wanted))
        {
            _pending = null;
            return false;
        }

        if (_pending is null || !_pending.SetEquals(wanted))
        {
            _pending = wanted;
            _pendingSince = now;
        }

        if (scanning)
        {
            // The clock starts again once the scan is over.
            _pendingSince = now;
            return false;
        }

        if (now - _pendingSince < Settle)
        {
            return false;
        }

        _shown = wanted;
        _pending = null;
        return true;
    }
}
