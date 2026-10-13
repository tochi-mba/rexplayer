namespace Rex.Media.AppCore.Performance;

/// <summary>
/// How responsive a thread is (the window's, above all): a probe is posted to it every so often, one
/// at a time, and how late each runs is recorded here. A probe later than a frame
/// (<see cref="Hitch"/> is the threshold for one the user notices) means the thread was busy with
/// something else that long; the sum of that lateness over the time watched is how much of each
/// second the thread was blocked.
/// </summary>
public sealed class ThreadResponsiveness
{
    /// <summary>Lateness up to this is the ordinary wait for the thread's next turn, not blocking.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    /// <summary>A wait at least this long is a hitch a person notices.</summary>
    public static readonly TimeSpan Hitch = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private int _probes;
    private int _hitches;
    private TimeSpan _blocked;
    private TimeSpan _worst;

    /// <summary>A probe ran <paramref name="late"/> after it was posted.</summary>
    public void Answered(TimeSpan late)
    {
        late = late < TimeSpan.Zero ? TimeSpan.Zero : late;
        lock (_gate)
        {
            _probes++;
            _worst = late > _worst ? late : _worst;
            if (late > Frame)
            {
                _blocked += late;
            }

            if (late >= Hitch)
            {
                _hitches++;
            }
        }
    }

    /// <summary>Forgets every probe, as a measurement starts.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            (_probes, _hitches, _blocked, _worst) = (0, 0, TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    /// <summary>The figures since the last <see cref="Clear"/>, over <paramref name="watched"/> of watching.</summary>
    public ResponsivenessStats Snapshot(TimeSpan watched)
    {
        lock (_gate)
        {
            var seconds = watched.TotalSeconds;
            return new ResponsivenessStats(
                _probes,
                _hitches,
                _blocked.TotalMilliseconds,
                seconds > 0 ? _blocked.TotalMilliseconds / seconds : 0,
                _worst.TotalMilliseconds);
        }
    }
}

/// <summary>
/// A thread's responsiveness (see <see cref="ThreadResponsiveness"/>): how many probes ran, how many
/// were hitches, how long it was blocked in all and per second watched, and its longest wait.
/// </summary>
public readonly record struct ResponsivenessStats(int Probes, int Hitches, double BlockedMs, double BlockedMsPerSecond, double WorstMs);
