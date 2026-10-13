using System.Globalization;

namespace Rex.Media.AppCore.Updates;

/// <summary>How far a download has got: bytes done of the total (0 when unknown), and roughly how long is left.</summary>
public sealed record TransferProgress(long Done, long Total, TimeSpan? Left = null)
{
    /// <summary>The share done, from 0 to 1, or null when the total is not known.</summary>
    public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;

    /// <summary>Every byte is in: the file is being checked against its checksum.</summary>
    public bool Complete => Total > 0 && Done >= Total;

    /// <summary>"15%, 42 of 280 MB, about 1 minute left", or "42 MB so far" when the total is unknown.</summary>
    public string Text()
    {
        if (Total <= 0)
        {
            return $"{Megabytes(Done, Done)} MB so far";
        }

        // Whole percents counted in bytes, so it never says 100% before the last byte.
        var percent = Math.Clamp(Done * 100 / Total, 0, 100);
        var text = string.Create(CultureInfo.InvariantCulture, $"{percent}%, {Megabytes(Done, Total)} of {Megabytes(Total, Total)} MB");
        return TimeLeft(Left) is { } left ? $"{text}, {left}" : text;
    }

    /// <summary>A size in megabytes as people read it: "280 MB", or "4.2 MB" below ten.</summary>
    public static string Size(long bytes) => Megabytes(bytes, bytes) + " MB";

    /// <summary>"about 3 minutes left", in words a person would use; null when unknown.</summary>
    public static string? TimeLeft(TimeSpan? left) => left switch
    {
        null => null,
        { TotalSeconds: < 45 } => "less than a minute left",
        { TotalSeconds: < 90 } => "about 1 minute left",
        { TotalMinutes: < 59.5 } span => string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(span.TotalMinutes):0} minutes left"),
        { TotalMinutes: < 90 } => "about 1 hour left",
        { } span => string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(span.TotalHours):0} hours left"),
    };

    /// <summary><paramref name="bytes"/> in megabytes, with one decimal when <paramref name="scale"/> is under ten.</summary>
    private static string Megabytes(long bytes, long scale)
    {
        var megabytes = bytes / 1048576.0;
        return scale < 10 * 1048576L
            ? megabytes.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Floor(megabytes).ToString("0", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Turns a download's byte counts into <see cref="TransferProgress"/>, with a time left from a
/// smoothed speed: samples at least a second apart, each weighing <see cref="Smoothing"/>, so a
/// slow-down while something plays or a moment's stall moves the estimate gently.
/// </summary>
public sealed class TransferMeter
{
    /// <summary>The weight of the newest speed sample.</summary>
    public const double Smoothing = 0.3;

    /// <summary>The shortest time between two speed samples.</summary>
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);

    /// <summary>The longest time left worth saying.</summary>
    public static readonly TimeSpan LongestEstimate = TimeSpan.FromDays(1);

    private long _sampledBytes = -1;
    private TimeSpan _sampledAt;
    private double _bytesPerSecond;

    /// <summary>The progress at <paramref name="at"/> (any steady clock) with <paramref name="done"/> of <paramref name="total"/> bytes in.</summary>
    public TransferProgress Measure(long done, long total, TimeSpan at)
    {
        if (_sampledBytes < 0 || done < _sampledBytes || at < _sampledAt)
        {
            // The first count, or the download started again from the beginning: speed is measured afresh.
            (_sampledBytes, _sampledAt, _bytesPerSecond) = (done, at, 0);
        }
        else if (at - _sampledAt >= SampleEvery)
        {
            var speed = (done - _sampledBytes) / (at - _sampledAt).TotalSeconds;
            _bytesPerSecond = _bytesPerSecond == 0 ? speed : (Smoothing * speed) + ((1 - Smoothing) * _bytesPerSecond);
            (_sampledBytes, _sampledAt) = (done, at);
        }

        // A crawl that would take more than a day is no estimate worth showing.
        var seconds = total > done && _bytesPerSecond > 0 ? (total - done) / _bytesPerSecond : double.PositiveInfinity;
        return new TransferProgress(done, total, seconds <= LongestEstimate.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null);
    }
}
