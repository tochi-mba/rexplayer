namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Keeps calls to one service at most one per interval, as MusicBrainz asks (about one request a
/// second): before each call, <see cref="Delay"/> says how long to wait first and books the slot.
/// Pure arithmetic over the clock the caller hands in; not thread-safe, owned by the one fetcher.
/// </summary>
public sealed class RateGate(TimeSpan interval)
{
    private DateTimeOffset _bookedUntil = DateTimeOffset.MinValue;

    /// <summary>The gap kept between calls.</summary>
    public TimeSpan Interval { get; } = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval));

    /// <summary>How long a call starting at <paramref name="now"/> must wait first; the slot after that wait is booked.</summary>
    public TimeSpan Delay(DateTimeOffset now)
    {
        var wait = _bookedUntil > now ? _bookedUntil - now : TimeSpan.Zero;
        _bookedUntil = now + wait + Interval;
        return wait;
    }
}
