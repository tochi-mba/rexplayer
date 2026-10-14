namespace Rex.Media.AppCore.Shell;

/// <summary>
/// When the position is worth telling Windows' media controls again. Windows moves its own timeline
/// on while it is told the media plays, so it needs telling only when that guess goes wrong: a seek,
/// a pause, a new length; and while playing about once a second, which keeps it honest without a
/// call for every one of the player's ten position reports a second. Asked on every report, so it
/// allocates nothing.
/// </summary>
public sealed class TimelineThrottle
{
    /// <summary>How often a playing position is told even when it is where Windows expects.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>How far from where Windows expects it the position may be before it counts as a jump.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    private bool _told;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _seekable;
    private bool _playing;
    private double _speed;
    private TimeSpan _at;

    /// <summary>
    /// Whether to tell Windows the position now, at <paramref name="now"/> on any steady clock; when
    /// true, it is taken as told.
    /// </summary>
    public bool ShouldPublish(TimeSpan position, TimeSpan duration, bool seekable, bool playing, double speed, TimeSpan now)
    {
        var publish = !_told || duration != _duration || seekable != _seekable || playing != _playing || speed != _speed
            || (duration > TimeSpan.Zero && (Drift(position, now) > Tolerance || (playing && now - _at >= Interval)));
        if (publish)
        {
            (_told, _position, _duration, _seekable, _playing, _speed, _at) = (true, position, duration, seekable, playing, speed, now);
        }

        return publish;
    }

    /// <summary>Forgets what was told, so the next report is told whatever it says: a new item has started.</summary>
    public void Reset() => _told = false;

    /// <summary>How far <paramref name="position"/> is from where Windows, moving on since the last report, expects it.</summary>
    private TimeSpan Drift(TimeSpan position, TimeSpan now)
    {
        var expected = _playing ? _position + ((now - _at) * _speed) : _position;
        return (position - expected).Duration();
    }
}
