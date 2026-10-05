using System.Globalization;

namespace Rex.Media.Primitives;

/// <summary>
/// A point or a span on a media timeline in 100-nanosecond ticks, the unit Windows media APIs use.
/// Audio paths that need exact positions count samples instead and convert at the edges.
/// <see cref="Unknown"/> marks a timestamp a container did not give.
/// </summary>
public readonly record struct MediaTime : IComparable<MediaTime>
{
    public const long TicksPerSecond = 10_000_000;

    /// <summary>The timebase of a tick, for <see cref="Rational.Rescale"/>.</summary>
    public static readonly Rational Timebase = new(1, TicksPerSecond);

    public static readonly MediaTime Zero = new(0);

    public static readonly MediaTime Unknown = new(long.MinValue);

    public static readonly MediaTime MaxValue = new(long.MaxValue);

    public MediaTime(long ticks) => Ticks = ticks;

    public long Ticks { get; }

    public bool IsKnown => Ticks != long.MinValue;

    public double TotalSeconds => Ticks / (double)TicksPerSecond;

    public static MediaTime FromSeconds(double seconds) => new((long)Math.Round(seconds * TicksPerSecond, MidpointRounding.AwayFromZero));

    public static MediaTime FromMilliseconds(long milliseconds) => new(checked(milliseconds * 10_000));

    /// <summary>A count of units of <paramref name="timebase"/>, such as a 90 kHz PTS.</summary>
    public static MediaTime FromTimebase(long value, Rational timebase) => new(Rational.Rescale(value, timebase, Timebase));

    /// <summary>The start of sample <paramref name="samples"/> at <paramref name="sampleRate"/>.</summary>
    public static MediaTime FromSamples(long samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return FromTimebase(samples, new Rational(1, sampleRate));
    }

    public long ToTimebase(Rational timebase) => Rational.Rescale(Ticks, Timebase, timebase);

    /// <summary>The index of the sample that starts nearest to this time.</summary>
    public long ToSamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return ToTimebase(new Rational(1, sampleRate));
    }

    public TimeSpan ToTimeSpan() => new(Ticks);

    public int CompareTo(MediaTime other) => Ticks.CompareTo(other.Ticks);

    public static MediaTime operator +(MediaTime left, MediaTime right) => new(checked(left.Ticks + right.Ticks));

    public static MediaTime operator -(MediaTime left, MediaTime right) => new(checked(left.Ticks - right.Ticks));

    public static bool operator <(MediaTime left, MediaTime right) => left.Ticks < right.Ticks;

    public static bool operator >(MediaTime left, MediaTime right) => left.Ticks > right.Ticks;

    public static bool operator <=(MediaTime left, MediaTime right) => left.Ticks <= right.Ticks;

    public static bool operator >=(MediaTime left, MediaTime right) => left.Ticks >= right.Ticks;

    public static MediaTime Min(MediaTime left, MediaTime right) => left <= right ? left : right;

    public static MediaTime Max(MediaTime left, MediaTime right) => left >= right ? left : right;

    /// <summary>
    /// <c>h:mm:ss</c> past an hour, <c>m:ss</c> below it, with milliseconds when asked for. This is the
    /// form the seek bar, the OSD and the command line all show.
    /// </summary>
    public string ToClock(bool milliseconds = false)
    {
        if (!IsKnown)
        {
            return "--:--";
        }

        var negative = Ticks < 0;
        var span = TimeSpan.FromTicks(Math.Abs(Ticks));
        var hours = (long)span.TotalHours;
        var text = hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
        if (milliseconds)
        {
            text += string.Create(CultureInfo.InvariantCulture, $".{span.Milliseconds:000}");
        }

        return negative ? "-" + text : text;
    }

    /// <summary>
    /// Reads <c>h:mm:ss[.fff]</c>, <c>m:ss[.fff]</c> or plain seconds (<c>90</c>, <c>12.5</c>), the
    /// forms the go-to-time box and <c>--start</c> accept.
    /// </summary>
    public static bool TryParseClock(string? text, out MediaTime time)
    {
        time = Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double seconds = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            if (!isLast && value != Math.Floor(value))
            {
                return false;
            }

            if (i > 0 && value >= 60)
            {
                return false;
            }

            seconds = (seconds * 60) + value;
        }

        time = FromSeconds(seconds);
        return true;
    }

    public override string ToString() => IsKnown ? ToClock(milliseconds: true) : "unknown";
}
