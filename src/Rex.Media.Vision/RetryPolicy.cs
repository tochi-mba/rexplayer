using System.Net;

namespace Rex.Media.Vision;

/// <summary>
/// When a failed download is tried again: a dropped connection or a busy server is worth another
/// attempt after a growing pause; a missing file or a refused request is not. Each retry resumes
/// from the bytes already on disk.
/// </summary>
public sealed record RetryPolicy(int Attempts, TimeSpan First, TimeSpan Longest)
{
    /// <summary>Four attempts, pausing 2, 4 and then 8 seconds.</summary>
    public static RetryPolicy Default { get; } = new(4, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

    /// <summary>The pause before attempt <paramref name="attempt"/> + 1, doubling each time up to <see cref="Longest"/>.</summary>
    public TimeSpan Delay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);
        var factor = Math.Pow(2, Math.Min(attempt - 1, 30));
        return TimeSpan.FromTicks((long)Math.Min(First.Ticks * factor, Longest.Ticks));
    }

    /// <summary>Whether attempt <paramref name="attempt"/>, which failed with <paramref name="error"/>, should be followed by another.</summary>
    public bool ShouldRetry(int attempt, Exception error) => attempt < Attempts && IsTransient(error);

    /// <summary>Whether a failure is the kind that goes away: the network, a timeout or a server under load.</summary>
    public static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        IOException or TimeoutException or TaskCanceledException => true,
        _ => false,
    };
}
