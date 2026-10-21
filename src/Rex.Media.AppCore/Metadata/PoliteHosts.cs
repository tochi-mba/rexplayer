namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// How the metadata fetcher keeps to each free service's terms: one <see cref="RateGate"/> per host
/// for the services that ask for a pace (MusicBrainz about one request a second; Wikidata,
/// Wikipedia and Wikimedia Commons well under their limit for an identified client), and which
/// answers mean "slow down" (429 Too Many Requests, 503 Service Unavailable), so the caller
/// stops and lets <see cref="FetchPlan"/>'s backoff hold the whole queue. Not thread-safe; owned
/// by the one fetch delegate, which asks one request at a time.
/// </summary>
public sealed class PoliteHosts
{
    /// <summary>MusicBrainz's own rule: at most about one request a second.</summary>
    public static readonly TimeSpan MusicBrainzInterval = TimeSpan.FromSeconds(1.1);

    /// <summary>The pace kept with each Wikimedia host: one request a second, a fraction of what an identified client may send.</summary>
    public static readonly TimeSpan WikimediaInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, RateGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The gap kept between requests to <paramref name="uri"/>'s host, or null for a host that asks for none.</summary>
    public static TimeSpan? IntervalFor(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.Host;
        if (Within(host, "musicbrainz.org"))
        {
            return MusicBrainzInterval;
        }

        return Within(host, "wikidata.org") || Within(host, "wikipedia.org") || Within(host, "wikimedia.org") ? WikimediaInterval : null;
    }

    /// <summary>How long a request to <paramref name="uri"/> starting at <paramref name="now"/> must wait first; its slot is booked.</summary>
    public TimeSpan Delay(Uri uri, DateTimeOffset now)
    {
        if (IntervalFor(uri) is not { } interval)
        {
            return TimeSpan.Zero;
        }

        if (!_gates.TryGetValue(uri.Host, out var gate))
        {
            _gates[uri.Host] = gate = new RateGate(interval);
        }

        return gate.Delay(now);
    }

    /// <summary>Whether an answer's status asks the client to slow down, so the whole queue backs off rather than reading it as "nothing found".</summary>
    public static bool BacksOff(int status) => status is 429 or 503;

    private static bool Within(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
